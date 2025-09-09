using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.Utilities.Logging;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms
{
    /// <summary>
    /// Implements a SSHOM-focused local search algorithm for finding Higher-Order Mutants (HOMs).
    /// This algorithm uses predictive heuristics to estimate the likelihood of creating 
    /// Strongly Subsuming Higher-Order Mutants (SSHOMs).
    /// </summary>
    public class LocalSearchAlgorithmV2 : IHOMSearchAlgorithm
    {
        private readonly ILogger<LocalSearchAlgorithmV2> _logger;

        /// <summary>
        /// Gets the name of the algorithm.
        /// </summary>
        public string Name => "LocalSearch";

        /// <summary>
        /// The maximum number of iterations for local search.
        /// </summary>
        private readonly int _maxIterations;

        /// <summary>
        /// The maximum size of the candidate pool to maintain.
        /// </summary>
        private readonly int _candidatePoolSize;

        /// <summary>
        /// The maximum order (number of FOMs) to consider in HOMs.
        /// </summary>
        private readonly int _maxOrder;

        /// <summary>
        /// Random number generator for stochastic elements of search.
        /// </summary>
        private readonly Random _random;

        /// <summary>
        /// Registry for accessing and using heuristics.
        /// </summary>
        private readonly HeuristicRegistry _heuristicRegistry;

        /// <summary>
        /// Options for Stryker configuration.
        /// </summary>
        private StrykerOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalSearchAlgorithmV2"/> class.
        /// </summary>
        /// <param name="mutationTestInput">Mutation test input for additional context.</param>
        /// <param name="heuristics">A list of heuristics to guide the search.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="availableMutants">Available mutants for selection.</param>
        /// <param name="maxIterations">Maximum number of iterations for the search.</param>
        /// <param name="candidatePoolSize">Size of the candidate pool to maintain.</param>
        /// <param name="maxOrder">Maximum order (number of FOMs) to consider.</param>
        public LocalSearchAlgorithmV2(
            MutationTestInput mutationTestInput,
            IList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            IReadOnlyCollection<IMutant> availableMutants,
            int maxIterations = 10,
            int candidatePoolSize = 50,
            int maxOrder = 4)
        {
            _options = options as StrykerOptions ?? new StrykerOptions();
            _maxIterations = maxIterations;
            _candidatePoolSize = candidatePoolSize;
            _maxOrder = maxOrder;
            _random = new Random();
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<LocalSearchAlgorithmV2>();

            // Initialize heuristic registry with all available heuristics but without default heuristics
            _heuristicRegistry = new HeuristicRegistry(availableMutants, options, mutationTestInput, registerAllHeuristics: false);

            // Register default heuristics with the correct max order limit
            _heuristicRegistry.RegisterHeuristic(new MaxSizeLimitHeuristic(maxOrder));

            // Register other default heuristics that don't need max order configuration
            _heuristicRegistry.RegisterHeuristic(new CodeLocationHeuristic());
            _heuristicRegistry.RegisterHeuristic(new EmptyAssessingTestsFilterHeuristic());
            _heuristicRegistry.RegisterHeuristic(new MutatorTypeHeuristic());
            _heuristicRegistry.RegisterHeuristic(new OverlappingTestsHeuristic());
            _heuristicRegistry.RegisterHeuristic(new SyntaxNodeConflictHeuristic());
            _heuristicRegistry.RegisterHeuristic(new WeakMutatorFilterHeuristic());

            // For backward compatibility, register the provided legacy heuristics if not null
            if (heuristics != null)
            {
                foreach (var heuristic in heuristics)
                {
                    _heuristicRegistry.RegisterHeuristic(heuristic);
                }
            }
        }

        /// <summary>
        /// Generates candidate Higher-Order Mutants (HOMs) using SSHOM-focused local search.
        /// </summary>
        /// <param name="availableFOMs">The pool of FOMs to select from.</param>
        /// <param name="heuristics">A list of heuristics that guide the search.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="input">Mutation test input for additional context.</param>
        /// <returns>An enumerable of HigherOrderMutant instances representing candidate HOMs.</returns>
        public IEnumerable<HigherOrderMutant> GenerateCandidates(
            IReadOnlyCollection<IMutant> availableFOMs,
            IReadOnlyList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            MutationTestInput input)
        {
            // Register additional heuristics
            foreach (var heuristic in heuristics)
            {
                _heuristicRegistry.RegisterHeuristic(heuristic);
            }

            var generatedCandidates = new HashSet<string>(StringComparer.Ordinal);
            var yieldedCount = 0;

            _logger.LogDebug("Starting SSHOM-focused LocalSearch with {FOMCount} FOMs", availableFOMs.Count);

            // Initialize with SSHOM-targeted starting points
            var candidatePool = InitializeSSHOMTargetedStartingPoints(availableFOMs, _heuristicRegistry);

            if (candidatePool.Count == 0)
            {
                _logger.LogWarning("No viable SSHOM candidates found in starting population");
                yield break;
            }

            _logger.LogDebug("Generated {StartingPoolSize} initial SSHOM-targeted candidates", candidatePool.Count);

            // Yield high-quality initial candidates
            foreach (var candidate in candidatePool.ToList()) // ToList to avoid modification during iteration
            {
                var candidateKey = GetCandidateKey(candidate);
                if (generatedCandidates.Add(candidateKey) &&
                    ShouldYieldCandidate(candidate, 0))
                {
                    yield return new HigherOrderMutant(candidate, Name);
                    yieldedCount++;
                }
            }

            // Adaptive search loop focusing on SSHOM potential
            for (var iteration = 0; iteration < _maxIterations && yieldedCount < _candidatePoolSize; iteration++)
            {
                var scoredCandidates = ScoreAndRankCandidatesForSSHOM(candidatePool);

                if (scoredCandidates.Count == 0)
                {
                    _logger.LogDebug("No scorable SSHOM candidates at iteration {Iteration}", iteration);
                    break;
                }

                var newCandidates = new List<List<IMutant>>();

                // Explore neighborhoods of top SSHOM candidates
                var topCandidates = scoredCandidates.Take(Math.Max(1, scoredCandidates.Count / 2));
                foreach (var scoredCandidate in topCandidates)
                {
                    var neighbors = ExploreSSHOMTargetedNeighborhood(scoredCandidate.Candidate, availableFOMs, _heuristicRegistry);
                    newCandidates.AddRange(neighbors);
                }

                // Process new candidates with adaptive filtering
                var survivingNewCandidates = 0;
                foreach (var candidate in newCandidates)
                {
                    var candidateKey = GetCandidateKey(candidate);

                    if (!generatedCandidates.Contains(candidateKey))
                    {
                        generatedCandidates.Add(candidateKey);
                        candidatePool.Add(candidate);

                        // Apply adaptive filtering based on iteration
                        if (ShouldYieldCandidate(candidate, iteration))
                        {
                            yield return new HigherOrderMutant(candidate, Name);
                            yieldedCount++;
                            survivingNewCandidates++;
                        }
                    }
                }

                _logger.LogTrace("Iteration {Iteration}: Generated {NewCandidates} new candidates, yielded {Surviving}",
                    iteration, newCandidates.Count, survivingNewCandidates);

                // Update candidate pool, maintaining diversity
                candidatePool = MaintainDiverseCandidatePool(candidatePool, scoredCandidates);
            }

            _logger.LogInformation("LocalSearch completed: Yielded {YieldedCount} SSHOM candidates in {Iterations} iterations",
                yieldedCount, Math.Min(_maxIterations, yieldedCount >= _candidatePoolSize ? yieldedCount : _maxIterations));
        }

        /// <summary>
        /// Initialize starting points specifically targeting SSHOM potential.
        /// Uses intelligent pairing based on test coverage overlap and mutator compatibility.
        /// </summary>
        private List<List<IMutant>> InitializeSSHOMTargetedStartingPoints(
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var initialCandidates = new List<List<IMutant>>();
            var fomList = availableFOMs.ToList();

            // Phase 1: Pre-filter FOMs that have potential to be killed
            var killableFOMs = fomList.Where(fom =>
                !fom.AssessingTests.IsEmpty &&           // Has test coverage
                fom.ResultStatus != MutantStatus.NoCoverage && // Not already marked as no coverage
                !IsWeakMutatorType(fom)).ToList();           // Not a weak mutator type

            _logger.LogDebug("Filtered {KillableCount}/{TotalCount} FOMs as potentially killable",
                killableFOMs.Count, fomList.Count);

            if (killableFOMs.Count < 2)
            {
                // Fallback to all FOMs with coverage
                killableFOMs = fomList.Where(f => !f.AssessingTests.IsEmpty).ToList();
                _logger.LogDebug("Using fallback: {FallbackCount} FOMs with any coverage", killableFOMs.Count);
            }

            if (killableFOMs.Count < 2)
            {
                _logger.LogWarning("Insufficient FOMs with test coverage for SSHOM generation");
                return initialCandidates;
            }

            // Phase 2: Create pairs with maximum SSHOM potential
            var sshomTargetPairs = CreateSSHOMTargetPairs(killableFOMs);
            initialCandidates.AddRange(sshomTargetPairs);

            // Phase 3: Add syntactically compatible pairs (same method/class)
            var syntacticPairs = CreateSyntacticallyCompatiblePairs(killableFOMs);
            initialCandidates.AddRange(syntacticPairs);

            // Phase 4: Add mutator-type compatible pairs
            var mutatorCompatiblePairs = CreateMutatorCompatiblePairs(killableFOMs);
            initialCandidates.AddRange(mutatorCompatiblePairs);

            // Phase 5: Apply progressive filtering with fallback protection
            return ApplyProgressiveFiltering(initialCandidates, heuristicRegistry);
        }

        /// <summary>
        /// Creates pairs with maximum SSHOM potential based on assessing test overlap and compatibility.
        /// </summary>
        private List<List<IMutant>> CreateSSHOMTargetPairs(List<IMutant> killableFOMs)
        {
            var pairs = new List<List<IMutant>>();

            for (var i = 0; i < killableFOMs.Count && pairs.Count < _candidatePoolSize; i++)
            {
                var fom1 = killableFOMs[i];

                // Find FOMs with overlapping assessing tests (prerequisite for shared killing tests)
                var compatibleFOMs = killableFOMs.Skip(i + 1)
                    .Where(fom2 => !fom1.AssessingTests.Intersect(fom2.AssessingTests).IsEmpty)
                    .OrderByDescending(fom2 => CalculateSSHOMPotential(fom1, fom2))
                    .Take(3) // Top 3 most promising partners
                    .ToList();

                foreach (var fom2 in compatibleFOMs)
                {
                    pairs.Add(new List<IMutant> { fom1, fom2 });
                }
            }

            _logger.LogDebug("Created {PairCount} SSHOM-targeted pairs", pairs.Count);
            return pairs;
        }

        /// <summary>
        /// Calculates the SSHOM potential of a pair of FOMs based on multiple factors.
        /// </summary>
        private double CalculateSSHOMPotential(IMutant fom1, IMutant fom2)
        {
            var intersection = fom1.AssessingTests.Intersect(fom2.AssessingTests);
            if (intersection.IsEmpty)
                return 0.0;

            var score = 0.0;

            // Higher score for larger intersection (more tests likely to kill both)
            score += Math.Min(1.0, intersection.Count / 10.0) * 0.4;

            // Bonus for same method/class (higher chance of similar killing patterns)
            if (AreSyntacticallyRelated(fom1, fom2))
                score += 0.3;

            // Bonus for compatible mutator types
            if (AreCompatibleMutatorTypes(fom1, fom2))
                score += 0.2;

            // Bonus for being "hard to kill" (more likely to create subsuming effect)
            if (IsHardToKill(fom1) && IsHardToKill(fom2))
                score += 0.1;

            return score;
        }

        /// <summary>
        /// Creates pairs of FOMs that are syntactically related (same method/class).
        /// </summary>
        private List<List<IMutant>> CreateSyntacticallyCompatiblePairs(List<IMutant> killableFOMs)
        {
            var pairs = new List<List<IMutant>>();
            var methodGroups = killableFOMs.GroupBy(GetMethodSignature).Where(g => g.Count() > 1);

            foreach (var group in methodGroups)
            {
                var groupFOMs = group.ToList();
                for (var i = 0; i < groupFOMs.Count - 1 && pairs.Count < _candidatePoolSize / 4; i++)
                {
                    for (var j = i + 1; j < groupFOMs.Count; j++)
                    {
                        if (!groupFOMs[i].AssessingTests.Intersect(groupFOMs[j].AssessingTests).IsEmpty)
                        {
                            pairs.Add([groupFOMs[i], groupFOMs[j]]);
                        }
                    }
                }
            }

            _logger.LogDebug("Created {PairCount} syntactically compatible pairs", pairs.Count);
            return pairs;
        }

        /// <summary>
        /// Creates pairs of FOMs with compatible mutator types.
        /// </summary>
        private List<List<IMutant>> CreateMutatorCompatiblePairs(List<IMutant> killableFOMs)
        {
            var pairs = new List<List<IMutant>>();
            var mutatorGroups = killableFOMs.GroupBy(GetMutatorType).Where(g => g.Count() > 1);

            foreach (var group in mutatorGroups.Take(3)) // Limit to top 3 mutator types
            {
                var groupFOMs = group.ToList();
                for (var i = 0; i < Math.Min(groupFOMs.Count - 1, 5) && pairs.Count < _candidatePoolSize / 4; i++)
                {
                    for (var j = i + 1; j < Math.Min(groupFOMs.Count, i + 4); j++)
                    {
                        if (!groupFOMs[i].AssessingTests.Intersect(groupFOMs[j].AssessingTests).IsEmpty)
                        {
                            pairs.Add(new List<IMutant> { groupFOMs[i], groupFOMs[j] });
                        }
                    }
                }
            }

            _logger.LogDebug("Created {PairCount} mutator-compatible pairs", pairs.Count);
            return pairs;
        }

        /// <summary>
        /// Applies progressive filtering with fallback protection to ensure viable population.
        /// </summary>
        private List<List<IMutant>> ApplyProgressiveFiltering(
            List<List<IMutant>> candidates,
            HeuristicRegistry heuristicRegistry)
        {
            if (candidates.Count == 0)
                return candidates;

            // Apply only critical filters that would cause runtime errors
            var criticalFiltered = candidates.Where(c => !_heuristicRegistry.ShouldFilterCandidate(c)).ToList();

            // If we lost too many candidates, fall back to best scored candidates
            if (criticalFiltered.Count < Math.Max(2, candidates.Count / 4))
            {
                _logger.LogDebug("Critical filtering too aggressive, using score-based selection");
                return candidates
                    .OrderByDescending(c => ScoreCandidateForSSHOMPotential(c))
                    .Take(Math.Max(5, _candidatePoolSize / 4))
                    .ToList();
            }

            return criticalFiltered.Take(_candidatePoolSize).ToList();
        }

        /// <summary>
        /// Explores neighborhood with SSHOM-targeted strategies.
        /// </summary>
        private List<List<IMutant>> ExploreSSHOMTargetedNeighborhood(
            List<IMutant> candidate,
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var neighbors = new List<List<IMutant>>();

            // Calculate current intersection to guide additions
            var currentIntersection = CalculateAssessingTestsIntersection(candidate);

            if (currentIntersection.IsEmpty)
                return neighbors; // No point exploring if no shared coverage

            // Strategy 1: Add FOMs that maintain or improve intersection
            var compatibleFOMs = availableFOMs
                .Where(fom => !candidate.Any(c => c.Id == fom.Id)) // Not already in candidate
                .Where(fom => !fom.AssessingTests.Intersect(currentIntersection).IsEmpty) // Maintains intersection
                .OrderByDescending(fom => ScoreFOMForAddition(fom, currentIntersection))
                .Take(5);

            foreach (var fom in compatibleFOMs)
            {
                if (candidate.Count < _maxOrder)
                {
                    var newCandidate = new List<IMutant>(candidate) { fom };
                    neighbors.Add(newCandidate);
                }
            }

            // Strategy 2: Replace FOMs with better alternatives (maintaining/improving intersection)
            for (var i = 0; i < candidate.Count && neighbors.Count < 10; i++)
            {
                var replacementCandidates = availableFOMs
                    .Where(fom => !candidate.Any(c => c.Id == fom.Id))
                    .Where(fom => WouldImproveIntersection(candidate, i, fom))
                    .Take(2);

                foreach (var replacement in replacementCandidates)
                {
                    var newCandidate = new List<IMutant>(candidate);
                    newCandidate[i] = replacement;
                    neighbors.Add(newCandidate);
                }
            }

            // Strategy 3: Use heuristic suggestions as backup
            if (neighbors.Count < 3)
            {
                var heuristicSuggestions = heuristicRegistry.SuggestNextCandidates(candidate, availableFOMs);
                neighbors.AddRange(heuristicSuggestions.Take(5 - neighbors.Count));
            }

            return neighbors;
        }

        /// <summary>
        /// Scores and ranks candidates specifically for SSHOM potential.
        /// </summary>
        private List<ScoredCandidate> ScoreAndRankCandidatesForSSHOM(List<List<IMutant>> candidates)
        {
            var scoredCandidates = new List<ScoredCandidate>();

            foreach (var candidate in candidates)
            {
                // Skip candidates that should be critically filtered
                if (_heuristicRegistry.ShouldFilterCandidate(candidate))
                {
                    continue;
                }

                var score = ScoreCandidateForSSHOMPotential(candidate);
                scoredCandidates.Add(new ScoredCandidate(candidate, score));
            }

            // Sort by SSHOM potential score (descending)
            return scoredCandidates
                .OrderByDescending(sc => sc.Score)
                .ToList();
        }

        /// <summary>
        /// Scores a candidate based on its estimated SSHOM potential.
        /// </summary>
        private double ScoreCandidateForSSHOMPotential(List<IMutant> candidate)
        {
            if (candidate.Count < 2)
                return 0.0;

            var intersection = CalculateAssessingTestsIntersection(candidate);
            if (intersection.IsEmpty)
                return 0.0;

            var score = 0.0;

            // Core SSHOM potential (40% weight)
            score += CalculateKillingPotential(candidate, intersection) * 0.4;

            // Syntactic compatibility (25% weight) 
            score += CalculateSyntacticCompatibility(candidate) * 0.25;

            // Mutator type compatibility (20% weight)
            score += CalculateMutatorTypeCompatibility(candidate) * 0.2;

            // Coverage diversity (15% weight) - prevent too narrow focus
            score += CalculateCoverageDiversity(candidate) * 0.15;

            return Math.Min(1.0, score);
        }

        /// <summary>
        /// Calculates the estimated killing potential of a candidate.
        /// </summary>
        private double CalculateKillingPotential(List<IMutant> candidate, ITestIdentifiers intersection)
        {
            var potentialScore = 0.0;

            // Larger intersection = higher chance of containing killing tests
            potentialScore += Math.Min(1.0, intersection.Count / 5.0) * 0.5;

            // FOMs in same method more likely to share killing tests
            if (candidate.All(fom => GetMethodSignature(fom) == GetMethodSignature(candidate[0])))
                potentialScore += 0.3;

            // Compatible mutation types more likely to create subsumption
            if (AreSubsumptionCompatibleMutations(candidate))
                potentialScore += 0.2;

            return Math.Min(1.0, potentialScore);
        }

        /// <summary>
        /// Determines if mutations are compatible for creating SSHOMs.
        /// </summary>
        private bool AreSubsumptionCompatibleMutations(List<IMutant> candidate)
        {
            var mutatorTypes = candidate.Select(GetMutatorType).ToHashSet();

            // Same mutator type often creates good SSHOMs
            if (mutatorTypes.Count == 1)
                return true;

            // Compatible mutator combinations
            if (mutatorTypes.Contains("Relational") && mutatorTypes.Contains("Equality"))
                return true;

            if (mutatorTypes.Contains("Arithmetic") && mutatorTypes.Contains("Assignment"))
                return true;

            if (mutatorTypes.Contains("Logical") && mutatorTypes.Contains("Boolean"))
                return true;

            return false;
        }

        /// <summary>
        /// Calculates syntactic compatibility score.
        /// </summary>
        private double CalculateSyntacticCompatibility(List<IMutant> candidate)
        {
            var methods = candidate.Select(GetMethodSignature).Distinct().Count();
            var classes = candidate.Select(GetClassName).Distinct().Count();

            // Reward same method > same class > different classes
            if (methods == 1)
                return 1.0; // All in same method
            if (classes == 1)
                return 0.6; // All in same class
            return 0.2; // Different classes
        }

        /// <summary>
        /// Calculates mutator type compatibility score.
        /// </summary>
        private double CalculateMutatorTypeCompatibility(List<IMutant> candidate)
        {
            var mutatorTypes = candidate.Select(GetMutatorType).ToHashSet();

            if (mutatorTypes.Count == 1)
                return 1.0; // Same type
            if (AreSubsumptionCompatibleMutations(candidate))
                return 0.8; // Compatible types
            return 0.3; // Mixed types
        }

        /// <summary>
        /// Calculates coverage diversity to prevent over-specialization.
        /// </summary>
        private double CalculateCoverageDiversity(List<IMutant> candidate)
        {
            var intersection = CalculateAssessingTestsIntersection(candidate);
            var union = candidate.Aggregate(candidate[0].AssessingTests,
                (current, fom) => current.Merge(fom.AssessingTests));

            if (union.Count == 0)
                return 0.0;

            // Balance between intersection size and diversity
            var intersectionRatio = (double)intersection.Count / union.Count;
            return 0.3 + (intersectionRatio * 0.7); // Prefer some diversity
        }

        /// <summary>
        /// Determines if a candidate should be yielded based on adaptive criteria.
        /// </summary>
        private bool ShouldYieldCandidate(List<IMutant> candidate, int iteration)
        {
            // Critical filters always apply
            if (_heuristicRegistry.ShouldFilterCandidate(candidate))
            {
                return false;
            }

            // Early iterations: stricter filtering for quality
            if (iteration < _maxIterations / 3)
            {
                return ScoreCandidateForSSHOMPotential(candidate) > 0.5;
            }

            // Later iterations: more lenient to ensure diversity
            return ScoreCandidateForSSHOMPotential(candidate) > 0.3;
        }

        /// <summary>
        /// Maintains a diverse candidate pool by balancing quality and diversity.
        /// </summary>
        private List<List<IMutant>> MaintainDiverseCandidatePool(
            List<List<IMutant>> candidatePool,
            List<ScoredCandidate> scoredCandidates)
        {
            var maintainedPool = new List<List<IMutant>>();

            // Keep top performers
            maintainedPool.AddRange(scoredCandidates.Take(_candidatePoolSize / 2).Select(sc => sc.Candidate));

            // Add diversity by including different mutator types and methods
            var remaining = candidatePool.Except(maintainedPool, new CandidateEqualityComparer()).ToList();
            var diverseCandidates = remaining
                .GroupBy(c => $"{GetMutatorType(c[0])}_{GetMethodSignature(c[0])}")
                .SelectMany(g => g.Take(2)) // Max 2 per group
                .Take(_candidatePoolSize - maintainedPool.Count);

            maintainedPool.AddRange(diverseCandidates);

            return maintainedPool.Take(_candidatePoolSize).ToList();
        }

        #region Helper Methods

        /// <summary>
        /// Calculates the intersection of assessing tests for a list of mutants.
        /// </summary>
        private static ITestIdentifiers CalculateAssessingTestsIntersection(List<IMutant> candidate)
        {
            if (candidate.Count == 0)
                return null;

            var intersection = candidate[0].AssessingTests;
            for (var i = 1; i < candidate.Count; i++)
            {
                intersection = intersection?.Intersect(candidate[i].AssessingTests);
                if (intersection?.IsEmpty == true)
                    break;
            }

            return intersection;
        }

        /// <summary>
        /// Scores a FOM for addition to maintain intersection quality.
        /// </summary>
        private double ScoreFOMForAddition(IMutant fom, ITestIdentifiers currentIntersection)
        {
            var newIntersection = currentIntersection.Intersect(fom.AssessingTests);
            if (newIntersection.IsEmpty)
                return 0.0;

            // Prefer FOMs that maintain large intersection
            var intersectionRatio = (double)newIntersection.Count / currentIntersection.Count;

            // Bonus for syntactic compatibility and mutator type
            var bonus = (IsGoodMutatorType(fom) ? 0.1 : 0.0) +
                        (IsHardToKill(fom) ? 0.1 : 0.0);

            return intersectionRatio + bonus;
        }

        /// <summary>
        /// Checks if replacing a mutant would improve the intersection.
        /// </summary>
        private static bool WouldImproveIntersection(List<IMutant> candidate, int index, IMutant replacement)
        {
            var tempCandidate = new List<IMutant>(candidate);
            tempCandidate[index] = replacement;

            var originalIntersection = CalculateAssessingTestsIntersection(candidate);
            var newIntersection = CalculateAssessingTestsIntersection(tempCandidate);

            return newIntersection != null && !newIntersection.IsEmpty &&
                   (originalIntersection?.IsEmpty == true || newIntersection.Count >= originalIntersection.Count);
        }

        // Placeholder helper methods - these would need proper implementation based on your mutant structure
        private bool IsWeakMutatorType(IMutant fom) => GetMutatorType(fom) == "StringEmpty";
        private bool AreSyntacticallyRelated(IMutant fom1, IMutant fom2) => GetMethodSignature(fom1) == GetMethodSignature(fom2);
        private bool AreCompatibleMutatorTypes(IMutant fom1, IMutant fom2) => GetMutatorType(fom1) == GetMutatorType(fom2);
        private bool IsHardToKill(IMutant fom) => fom.AssessingTests.Count <= 3; // Fewer tests = harder to kill
        private bool IsGoodMutatorType(IMutant fom) => GetMutatorType(fom) != "StringEmpty";

        private string GetMethodSignature(IMutant fom) => fom.Mutation?.Description ?? "Unknown";
        private string GetClassName(IMutant fom) => fom.Mutation?.Description?.Split('.').FirstOrDefault() ?? "Unknown";
        private string GetMutatorType(IMutant fom) => fom.Mutation?.Type.ToString() ?? "Unknown";

        /// <summary>
        /// Creates a unique key for a candidate HOM based on the IDs of its constituent mutants.
        /// </summary>
        private string GetCandidateKey(List<IMutant> candidate) =>
            string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id));

        #endregion

        /// <summary>
        /// Class to represent a candidate with its SSHOM potential score.
        /// </summary>
        private class ScoredCandidate
        {
            public List<IMutant> Candidate { get; }
            public double Score { get; }

            public ScoredCandidate(List<IMutant> candidate, double score)
            {
                Candidate = candidate;
                Score = score;
            }
        }

        /// <summary>
        /// Equality comparer for candidate lists based on constituent mutant IDs.
        /// </summary>
        private class CandidateEqualityComparer : IEqualityComparer<List<IMutant>>
        {
            public bool Equals(List<IMutant> x, List<IMutant> y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                if (x.Count != y.Count)
                {
                    return false;
                }

                var xIds = x.Select(m => m.Id).OrderBy(id => id).ToArray();
                var yIds = y.Select(m => m.Id).OrderBy(id => id).ToArray();

                return xIds.SequenceEqual(yIds);
            }

            public int GetHashCode(List<IMutant> obj)
            {
                if (obj is null)
                    return 0;
                return obj.Select(m => m.Id).OrderBy(id => id).Aggregate(0, (acc, id) => acc ^ id.GetHashCode());
            }
        }
    }

    /// <summary>
    /// SSHOM-specific heuristic for predicting likelihood of creating SSHOMs.
    /// </summary>
    public class SSHOMPredictionHeuristic : BaseHOMHeuristic
    {
        public override string Name => "SSHOMPrediction";
        public override double Weight => 2.0; // High weight - this is our main goal
        public override bool IsFitnessScoringHeuristic => true;

        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count < 2)
                return 0.0;

            // Basic SSHOM prediction based on static analysis
            var intersection = CalculateIntersection(candidate);
            if (intersection.IsEmpty)
                return 0.0;

            var score = 0.0;

            // Intersection size score (larger = better chance of killing tests)
            score += Math.Min(1.0, intersection.Count / 8.0) * 0.4;

            // Syntactic relatedness (same method/class)
            if (AreSameMethod(candidate))
                score += 0.3;
            else if (AreSameClass(candidate))
                score += 0.2;

            // Mutator type compatibility
            if (HaveCompatibleMutators(candidate))
                score += 0.2;

            // Coverage patterns (not too broad, not too narrow)
            var diversityScore = CalculateCoverageDiversity(candidate);
            score += diversityScore * 0.1;

            return Math.Min(1.0, score);
        }

        private static ITestIdentifiers CalculateIntersection(List<IMutant> candidate)
        {
            var intersection = candidate[0].AssessingTests;
            for (var i = 1; i < candidate.Count; i++)
            {
                intersection = intersection?.Intersect(candidate[i].AssessingTests);
                if (intersection?.IsEmpty == true)
                    break;
            }
            return intersection;
        }

        private bool AreSameMethod(List<IMutant> candidate) =>
            candidate.Select(m => m.Mutation?.Description).Distinct().Count() == 1;

        private bool AreSameClass(List<IMutant> candidate) =>
            candidate.Select(m => m.Mutation?.Description?.Split('.').FirstOrDefault()).Distinct().Count() == 1;

        private bool HaveCompatibleMutators(List<IMutant> candidate)
        {
            var types = candidate.Where(m => m.Mutation != null).Select(m => m.Mutation.Type.ToString()).Distinct().ToList();
            return types.Count <= 2; // Allow up to 2 different mutator types
        }

        private double CalculateCoverageDiversity(List<IMutant> candidate)
        {
            var intersection = CalculateIntersection(candidate);
            var union = candidate.Aggregate(candidate[0].AssessingTests,
                (current, fom) => current.Merge(fom.AssessingTests));

            if (union.Count == 0)
                return 0.0;

            var ratio = (double)intersection.Count / union.Count;
            return ratio > 0.1 && ratio < 0.9 ? 1.0 : 0.5; // Prefer balanced coverage
        }
    }
}
