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
    /// 
    /// The algorithm leverages the HeuristicRegistry for all scoring, filtering, and search decisions,
    /// making it fully configurable while maintaining smart SSHOM-targeted behaviors.
    /// </summary>
    public class LocalSearchAlgorithmV2 : IHOMSearchAlgorithm
    {
        private readonly ILogger<LocalSearchAlgorithmV2> _logger;

        /// <summary>
        /// Gets the name of the algorithm.
        /// </summary>
        public string Name => "LocalSearchV2";

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
            int candidatePoolSize = 100,
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

            // Register the provided heuristics if not null
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

            // Track generated candidates to avoid duplicates
            var generatedCandidates = new HashSet<string>(StringComparer.Ordinal);
            var yieldedCount = 0;

            _logger.LogDebug("Starting SSHOM-focused LocalSearchV2 with {FOMCount} FOMs", availableFOMs.Count);

            // Initialize with SSHOM-targeted starting points
            var scoredCandidatePool = InitializeSSHOMTargetedStartingPoints(availableFOMs, _heuristicRegistry);

            if (scoredCandidatePool.Count == 0)
            {
                _logger.LogWarning("No viable SSHOM candidates found in starting population");
                yield break;
            }

            _logger.LogDebug("Generated {StartingPoolSize} initial SSHOM-targeted candidates", scoredCandidatePool.Count);

            // Yield high-quality initial candidates
            foreach (var scoredCandidate in scoredCandidatePool.ToList()) // ToList to avoid modification during iteration
            {
                var candidateKey = GetCandidateKey(scoredCandidate.Candidate);
                if (generatedCandidates.Add(candidateKey) &&
                    ShouldYieldCandidate(scoredCandidate.Candidate, 0, _heuristicRegistry))
                {
                    yield return new HigherOrderMutant(scoredCandidate.Candidate, Name);
                    yieldedCount++;
                }
            }

            // Adaptive search loop focusing on SSHOM potential
            for (var iteration = 0; iteration < _maxIterations && yieldedCount < _candidatePoolSize; iteration++)
            {
                if (scoredCandidatePool.Count == 0)
                {
                    _logger.LogDebug("No scorable SSHOM candidates at iteration {Iteration}", iteration);
                    break;
                }

                var newScoredCandidates = new List<ScoredCandidate>();

                // Explore neighborhoods of top SSHOM candidates
                var topScoredCandidates = scoredCandidatePool.Take(Math.Max(1, scoredCandidatePool.Count / 2));
                foreach (var scoredCandidate in topScoredCandidates)
                {
                    var neighbors = ExploreSSHOMTargetedNeighborhood(scoredCandidate, availableFOMs, _heuristicRegistry);
                    newScoredCandidates.AddRange(neighbors);
                }

                // Process new candidates with adaptive filtering
                var survivingNewCandidates = 0;
                foreach (var newScoredCandidate in newScoredCandidates)
                {
                    var candidateKey = GetCandidateKey(newScoredCandidate.Candidate);

                    if (generatedCandidates.Add(candidateKey))
                    {
                        scoredCandidatePool.Add(newScoredCandidate);

                        // Apply adaptive filtering based on iteration using generic scoring
                        if (!_heuristicRegistry.ShouldFilterCandidate(newScoredCandidate.Candidate) &&
                            ShouldYieldCandidate(newScoredCandidate.Candidate, iteration, _heuristicRegistry))
                        {
                            yield return new HigherOrderMutant(newScoredCandidate.Candidate, Name);
                            yieldedCount++;
                            survivingNewCandidates++;
                        }
                    }
                }

                _logger.LogTrace("Iteration {Iteration}: Generated {NewCandidates} new candidates, yielded {Surviving}",
                    iteration, newScoredCandidates.Count, survivingNewCandidates);

                // Rerank and prune the candidate pool to maintain manageable size
                scoredCandidatePool = scoredCandidatePool.OrderByDescending(scp => scp.Score).Take(_candidatePoolSize).ToList();
            }

            _logger.LogInformation("LocalSearchV2 completed: Yielded {YieldedCount} SSHOM candidates in {Iterations} iterations",
                yieldedCount, Math.Min(_maxIterations, yieldedCount >= _candidatePoolSize ? yieldedCount : _maxIterations));
        }

        /// <summary>
        /// Initialize starting points specifically targeting SSHOM potential.
        /// Uses intelligent pairing based on heuristic-guided evaluation.
        /// </summary>
        private List<ScoredCandidate> InitializeSSHOMTargetedStartingPoints(
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var initialCandidates = new List<ScoredCandidate>();
            var fomList = availableFOMs.ToList();

            // Phase 1: Pre-filter FOMs that have potential to be killed
            var killableFOMs = fomList.Where(fom =>
                !fom.AssessingTests.IsEmpty &&           // Has test coverage
                fom.ResultStatus != MutantStatus.NoCoverage).ToList(); // Not already marked as no coverage

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

            // Phase 2: Create pairs with maximum SSHOM potential using comprehensive heuristic scoring
            // This now includes all aspects: overlapping tests, syntactic compatibility (via CodeLocationHeuristic), 
            // mutator types, etc. - making separate syntactic pairing redundant
            var sshomTargetPairs = CreateSSHOMTargetPairs(killableFOMs, heuristicRegistry);
            initialCandidates.AddRange(sshomTargetPairs);

            // Phase 3: Apply progressive filtering with fallback protection using generic scoring
            return ApplyProgressiveFiltering(initialCandidates, heuristicRegistry);
        }

        /// <summary>
        /// Creates pairs with maximum SSHOM potential based on heuristic evaluation.
        /// </summary>
        private List<ScoredCandidate> CreateSSHOMTargetPairs(List<IMutant> killableFOMs, HeuristicRegistry heuristicRegistry)
        {
            var scoredCandidates = new List<ScoredCandidate>();

            for (var i = 0; i < killableFOMs.Count && scoredCandidates.Count < _candidatePoolSize; i++)
            {
                var fom1 = killableFOMs[i];

                // Pre-calculate scores for all compatible FOMs
                var scoredCompatibleFOMs = killableFOMs.Skip(i + 1)
                    .Where(fom2 => !fom1.AssessingTests.Intersect(fom2.AssessingTests).IsEmpty)
                    .Select(fom2 => new ScoredCandidate([fom1, fom2], heuristicRegistry.ScoreCandidate([fom1, fom2])));

                // Take top 3 most promising pairs
                scoredCandidates.AddRange(scoredCompatibleFOMs.OrderByDescending(sc => sc.Score).Take(3));
            }

            _logger.LogDebug("Created {PairCount} SSHOM-targeted pairs", scoredCandidates.Count);
            return scoredCandidates;
        }

        /// <summary>
        /// Applies progressive filtering with fallback protection to ensure viable population.
        /// </summary>
        private List<ScoredCandidate> ApplyProgressiveFiltering(
            List<ScoredCandidate> candidates,
            HeuristicRegistry heuristicRegistry)
        {
            if (candidates.Count == 0)
            {
                return candidates;
            }

            // Apply heuristic-based filtering
            var criticalFiltered = candidates.Where(c => !heuristicRegistry.ShouldFilterCandidate(c.Candidate)).ToList();

            // If we lost too many candidates, fall back to best scored candidates using generic scoring
            if (criticalFiltered.Count < Math.Max(2, candidates.Count / 4))
            {
                _logger.LogDebug("Critical filtering too aggressive, using score-based selection");
                return [.. candidates
                    .OrderByDescending(c => c.Score)
                    .Take(Math.Max(5, _candidatePoolSize / 4))];
            }

            return [.. criticalFiltered.Take(_candidatePoolSize)];
        }

        /// <summary>
        /// Explores neighborhood with SSHOM-targeted strategies using generic heuristic methods.
        /// Uses a hybrid approach that balances exploitation (high scores) with exploration (diversity).
        /// </summary>
        private List<ScoredCandidate> ExploreSSHOMTargetedNeighborhood(
            ScoredCandidate candidate,
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var neighbors = new List<ScoredCandidate>();


            // Randomly choose exploration strategies to add diversity
            var availableStrategies = new List<Action>();

            // Addition strategy (if not at max order)
            if (candidate.Candidate.Count < _maxOrder)
            {
                availableStrategies.Add(() => neighbors.AddRange(GenerateAdditionNeighbors(candidate, availableFOMs, heuristicRegistry)));
            }

            // Removal strategy (if we have more than 2 mutants to maintain HOM validity)
            if (candidate.Candidate.Count > 2)
            {
                availableStrategies.Add(() => neighbors.AddRange(GenerateRemovalNeighbors(candidate, heuristicRegistry)));
            }

            // Swap strategy (always available if there are FOMs to swap with)
            if (availableFOMs.Any(fom => !candidate.Candidate.Any(c => c.Id == fom.Id)))
            {
                availableStrategies.Add(() => neighbors.AddRange(GenerateSwapNeighbors(candidate, availableFOMs, heuristicRegistry)));
            }

            // Randomize strategy execution order for diversity
            var shuffledStrategies = availableStrategies.OrderBy(x => _random.Next()).ToList();
                
            // Execute strategies until we have enough neighbors or exhaust all strategies
            foreach (var strategy in shuffledStrategies)
            {
                strategy();
                if (neighbors.Count >= 15)
                {
                    break; // Generate more for better selection
                }
            }            

            // Hybrid selection: Balance exploitation with exploration
            const int MaxNeighbors = 10;
            if (neighbors.Count > MaxNeighbors)
            {
                //return TakeRandomSample(neighbors, MaxNeighbors);
                return SelectBestNeighborsWithDiversity(neighbors, MaxNeighbors);
            }

            return neighbors;
        }

        /// <summary>
        /// Selects the best neighbors using a hybrid approach that balances exploitation and exploration.
        /// Takes top-scoring candidates while ensuring diversity to avoid local optima.
        /// </summary>
        private List<ScoredCandidate> SelectBestNeighborsWithDiversity(List<ScoredCandidate> neighbors, int maxCount)
        {
            if (neighbors.Count <= maxCount)
            {
                return neighbors;
            }

            var selected = new List<ScoredCandidate>();
            
            // Sort neighbors by score (descending)
            var sortedNeighbors = neighbors.OrderByDescending(n => n.Score).ToList();
            
            // Strategy: 70% exploitation (best scores) + 30% exploration (diversity)
            var exploitationCount = (int)(maxCount * 0.7);
            var explorationCount = maxCount - exploitationCount;
            
            // Phase 1: Take top scoring candidates (exploitation)
            selected.AddRange(sortedNeighbors.Take(exploitationCount));
            
            // Phase 2: Add diverse candidates from remaining pool (exploration)
            var remainingNeighbors = sortedNeighbors.Skip(exploitationCount).ToList();
            if (remainingNeighbors.Count > 0)
            {
                var diverseSelection = TakeRandomSample(remainingNeighbors, explorationCount);
                selected.AddRange(diverseSelection);
            }
            
            _logger.LogTrace("Selected {ExploitationCount} top-scoring + {ExplorationCount} diverse neighbors from {TotalCount}",
                exploitationCount, explorationCount, neighbors.Count);
            
            return selected;
        }

        /// <summary>
        /// Generate neighbors by adding one FOM to the candidate using heuristic scoring.
        /// </summary>
        private List<ScoredCandidate> GenerateAdditionNeighbors(
            ScoredCandidate candidate,
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var neighbors = new List<ScoredCandidate>();

            // Only add if we're below max order
            if (candidate.Candidate.Count < _maxOrder)
            {
                // Create a set of existing FOM IDs for quick lookup
                var existingFomIds = new HashSet<int>(candidate.Candidate.Select(m => m.Id));

                // Score potential additions and take the best ones
                var scoredAdditions = availableFOMs
                    .Where(m => !existingFomIds.Contains(m.Id))
                    .Select(fom => {
                        var newCandidate = new List<IMutant>(candidate.Candidate) { fom };
                        var score = heuristicRegistry.ScoreCandidate(newCandidate);
                        return new ScoredCandidate(newCandidate, score);
                    })
                    .OrderByDescending(x => x.Score)
                    .Take(5) // Top 5 most promising additions
                    .ToList();

                neighbors.AddRange(scoredAdditions);
            }

            return neighbors;
        }

        /// <summary>
        /// Generate neighbors by swapping one FOM in the candidate with another using heuristic scoring.
        /// </summary>
        private List<ScoredCandidate> GenerateSwapNeighbors(
            ScoredCandidate candidate,
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var neighbors = new List<ScoredCandidate>();

            // Create a set of existing FOM IDs for quick lookup
            var existingFomIds = new HashSet<int>(candidate.Candidate.Select(m => m.Id));

            // Take a scored sample of FOMs to try swapping with
            var fomsToSwapWith = availableFOMs
                .Where(m => !existingFomIds.Contains(m.Id))
                .OrderBy(x => _random.Next()) // Random sampling for diversity
                .Take(5)
                .ToList();

            // For each position in the candidate
            for (int i = 0; i < candidate.Candidate.Count && i < 2; i++) // Limit to first 2 positions for efficiency
            {
                var originalScore = candidate.Score;

                // For each FOM to swap with
                foreach (var fom in fomsToSwapWith)
                {
                    var newCandidate = new List<IMutant>(candidate.Candidate);
                    newCandidate[i] = fom;

                    var newScore = heuristicRegistry.ScoreCandidate(newCandidate);

                    // Only include swaps that improve or maintain the score
                    if (newScore >= originalScore * 0.8) // Allow small degradation for diversity
                    {
                        neighbors.Add(new ScoredCandidate(newCandidate, newScore));
                    }
                }
            }

            return neighbors;
        }

        /// <summary>
        /// Generate neighbors by removing one FOM from the candidate using heuristic scoring.
        /// Only removes if the resulting candidate would still be a valid HOM (>= 2 mutants).
        /// </summary>
        private static List<ScoredCandidate> GenerateRemovalNeighbors(
            ScoredCandidate candidate,
            HeuristicRegistry heuristicRegistry)
        {
            var neighbors = new List<ScoredCandidate>();

            // Only remove if we have more than 2 mutants (to maintain HOM validity)
            if (candidate.Candidate.Count > 2)
            {
                // Score potential removals and take the best ones
                var scoredRemovals = new List<ScoredCandidate>();
                
                // Try removing each mutant and score the resulting candidate
                for (var i = 0; i < candidate.Candidate.Count; i++)
                {
                    var newCandidate = new List<IMutant>(candidate.Candidate);
                    newCandidate.RemoveAt(i);

                    var score = heuristicRegistry.ScoreCandidate(newCandidate);
                    scoredRemovals.Add(new ScoredCandidate(newCandidate, score));
                }

                // Take the best removals (similar to addition neighbors taking top 5)
                var bestRemovals = scoredRemovals
                    .OrderByDescending(x => x.Score)
                    .Where(x => x.Score >= candidate.Score * 0.7) // More lenient threshold for removal
                    .Take(5) 
                    .ToList();

                neighbors.AddRange(bestRemovals);
            }

            return neighbors;
        }



        /// <summary>
        /// Score and rank candidates using the heuristic registry (same as original LocalSearchAlgorithm).
        /// </summary>
        private static List<ScoredCandidate> ScoreAndRankCandidates(
            List<List<IMutant>> candidates, 
            HeuristicRegistry heuristicRegistry)
        {
            var scoredCandidates = new List<ScoredCandidate>();
            
            foreach (var candidate in candidates)
            {
                // Skip candidates that should be filtered
                if (heuristicRegistry.ShouldFilterCandidate(candidate))
                {
                    continue;
                }
                
                var score = heuristicRegistry.ScoreCandidate(candidate);
                scoredCandidates.Add(new ScoredCandidate(candidate, score));
            }
            
            // Sort by score (descending)
            return [.. scoredCandidates.OrderByDescending(sc => sc.Score)];
        }

        /// <summary>
        /// Determines if a candidate should be yielded based on adaptive criteria using generic scoring.
        /// </summary>
        private bool ShouldYieldCandidate(List<IMutant> candidate, int iteration, HeuristicRegistry heuristicRegistry)
        {
            // Use generic heuristic-based scoring for yielding decisions
            var score = heuristicRegistry.ScoreCandidate(candidate);

            // Early iterations: stricter filtering for quality
            if (iteration < _maxIterations / 3)
            {
                return score > 0.6;
            }

            // Later iterations: more lenient to ensure diversity
            return score > 0.4;
        }

        /// <summary>
        /// Take a random sample from a list (same as original LocalSearchAlgorithm).
        /// </summary>
        private List<T> TakeRandomSample<T>(List<T> items, int count)
        {
            if (items.Count <= count)
            {
                return new List<T>(items);
            }
            
            var result = new List<T>();
            var indices = new HashSet<int>();
            
            while (result.Count < count && indices.Count < items.Count)
            {
                int index = _random.Next(items.Count);
                if (!indices.Contains(index))
                {
                    indices.Add(index);
                    result.Add(items[index]);
                }
            }
            
            return result;
        }

        /// <summary>
        /// Get simple method signature for basic grouping.
        /// </summary>
        private string GetMethodSignature(IMutant fom) => fom.Mutation?.Description ?? "Unknown";

        /// <summary>
        /// Creates a unique key for a candidate HOM based on the IDs of its constituent mutants.
        /// </summary>
        private string GetCandidateKey(List<IMutant> candidate) =>
            string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id));

        /// <summary>
        /// Class to represent a candidate with its score (same as original LocalSearchAlgorithm).
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
    }

    /// <summary>
    /// SSHOM-specific heuristic for predicting likelihood of creating SSHOMs.
    /// This heuristic provides additional SSHOM-specific scoring beyond the standard heuristics.
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
            if (intersection?.IsEmpty != false)
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

            if (union?.Count == 0)
                return 0.0;

            var ratio = (double)intersection.Count / union.Count;
            return ratio > 0.1 && ratio < 0.9 ? 1.0 : 0.5; // Prefer balanced coverage
        }
    }
}
