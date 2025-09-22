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
        /// Random number generator for stochastic elements of search.
        /// </summary>
        private readonly Random _random;

        /// <summary>
        /// Registry for accessing and using heuristics.
        /// </summary>
        private readonly HeuristicRegistry _heuristicRegistry;

        /// <summary>
        /// The maximum number of iterations for local search.
        /// </summary>
        private readonly int _maxIterations = 10;

        /// <summary>
        /// The maximum size of the candidate pool to maintain.
        /// </summary>
        private readonly int _candidatePoolSize = 100;

        /// <summary>
        /// The maximum order (number of FOMs) to consider in HOMs.
        /// </summary>
        private readonly int _maxOrder = 4;

        /// <summary>
        /// Final selection cap per neighborhood.
        /// </summary>
        private readonly int _maxNeighbors = 10;
        
        /// <summary>
        /// Upper cap of generated neighbors before selection.
        /// </summary>
        private readonly int _neighborGenerationCap = 15;
        
        /// <summary>
        /// Top-K additions to consider.
        /// </summary>
        private readonly int _topAdditions = 5;
        
        /// <summary>
        /// Top-K removals to consider.
        /// </summary>
        private readonly int _topRemovals = 5;
        
        /// <summary>
        /// Top-K swaps to consider.
        /// </summary>
        private readonly int _topSwaps = 5;
        
        /// <summary>
        /// How many positions to try swapping.
        /// </summary>
        private readonly int _swapPositionsToTry = 2;
        
        /// <summary>
        /// Relative score threshold for removal.
        /// </summary>
        private readonly double _removalThresholdMultiplier = 0.7;
        
        /// <summary>
        /// Relative score threshold to keep a swap.
        /// </summary>
        private readonly double _swapMaintainThreshold = 0.8;
        
        /// <summary>
        /// Yield threshold in early iterations.
        /// </summary>
        private readonly double _qualityThresholdEarly = 0.6;
        
        /// <summary>
        /// Yield threshold in later iterations.
        /// </summary>
        private readonly double _qualityThresholdLate = 0.4;
        
        /// <summary>
        /// Proportion of top neighbors to keep before diversity.
        /// </summary>
        private readonly double _neighborExploitationRatio = 0.7;
        
        /// <summary>
        /// Fraction of pool used as "top" each iteration.
        /// </summary>
        private readonly double _topCandidateFraction = 0.5;
        
        /// <summary>
        /// Divisor for critical filter fallback.
        /// </summary>
        private readonly int _criticalFilterDivisor = 4;
        
        /// <summary>
        /// Minimum fallback kept candidates.
        /// </summary>
        private readonly int _fallbackTakeMin = 5;
        
        /// <summary>
        /// Divisor for fallback kept candidates.
        /// </summary>
        private readonly int _fallbackTakeDivisor = 4;
        
        /// <summary>
        /// Number of top pairs taken per seed FOM.
        /// </summary>
        private readonly int _initialPairsPerSeed = 3;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalSearchAlgorithmV2"/> class.
        /// </summary>
        /// <param name="mutationTestInput">Mutation test input for additional context.</param>
        /// <param name="heuristics">A list of heuristics to guide the search.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="availableMutants">Available mutants for selection.</param>
        public LocalSearchAlgorithmV2(
            MutationTestInput mutationTestInput,
            IList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            IReadOnlyCollection<IMutant> availableMutants)
        {
            _random = new Random();
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<LocalSearchAlgorithmV2>();

            // Initialize heuristic registry with all available heuristics but without default heuristics
            _heuristicRegistry = new HeuristicRegistry(availableMutants, options, mutationTestInput, registerAllHeuristics: false);

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
            var scoredCandidatePool = InitializeMultiOrderSSHOMStartingPoints(availableFOMs, _heuristicRegistry);

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
                var topCount = Math.Max(1, (int)Math.Round(scoredCandidatePool.Count * _topCandidateFraction));
                var topScoredCandidates = scoredCandidatePool.Take(topCount);
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

                // Take top-N most promising pairs
                scoredCandidates.AddRange(scoredCompatibleFOMs.OrderByDescending(sc => sc.Score).Take(_initialPairsPerSeed));
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
            if (criticalFiltered.Count < Math.Max(2, candidates.Count / _criticalFilterDivisor))
            {
                _logger.LogDebug("Critical filtering too aggressive, using score-based selection");
                return [.. candidates
                    .OrderByDescending(c => c.Score)
                    .Take(Math.Max(_fallbackTakeMin, _candidatePoolSize / _fallbackTakeDivisor))];
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
                if (neighbors.Count >= _neighborGenerationCap)
                {
                    break; // Generate more for better selection
                }
            }

            // Hybrid selection: Balance exploitation with exploration
            if (neighbors.Count > _maxNeighbors)
            {
                return SelectBestNeighborsWithDiversity(neighbors, _maxNeighbors);
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

            // Strategy: exploitation (best scores) + exploration (diversity)
            var exploitationCount = (int)(maxCount * _neighborExploitationRatio);
            exploitationCount = Math.Clamp(exploitationCount, 0, maxCount);
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
                    .Take(_topAdditions)
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
                .Take(_topSwaps)
                .ToList();

            // For each position in the candidate (limited)
            for (int i = 0; i < candidate.Candidate.Count && i < _swapPositionsToTry; i++)
            {
                var originalScore = candidate.Score;

                // For each FOM to swap with
                foreach (var fom in fomsToSwapWith)
                {
                    var newCandidate = new List<IMutant>(candidate.Candidate);
                    newCandidate[i] = fom;

                    var newScore = heuristicRegistry.ScoreCandidate(newCandidate);

                    // Only include swaps that improve or maintain the score (threshold)
                    if (newScore >= originalScore * _swapMaintainThreshold)
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
        private List<ScoredCandidate> GenerateRemovalNeighbors(
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

                // Take the best removals
                var bestRemovals = scoredRemovals
                    .OrderByDescending(x => x.Score)
                    .Where(x => x.Score >= candidate.Score * _removalThresholdMultiplier)
                    .Take(_topRemovals)
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
                return score > _qualityThresholdEarly;
            }

            // Later iterations: more lenient to ensure diversity
            return score > _qualityThresholdLate;
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

        /// <summary>
        /// Initialize starting points with diverse orders (2, 3, 4) targeting SSHOM potential.
        /// Uses multi-order generation with balanced distribution and intelligent clustering.
        /// </summary>
        private List<ScoredCandidate> InitializeMultiOrderSSHOMStartingPoints(
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var initialCandidates = new List<ScoredCandidate>();
            var fomList = availableFOMs.ToList();

            // Phase 1: Pre-filter and cluster FOMs for better compatibility
            var clusteredFOMs = PrefilterAndClusterFOMs(fomList, heuristicRegistry);
            
            if (clusteredFOMs.Count < 2)
            {
                _logger.LogWarning("Insufficient FOMs with test coverage for multi-order SSHOM generation");
                return initialCandidates;
            }

            // Phase 2: Generate candidates for each order with balanced distribution
            var order2Candidates = GenerateOrder2Candidates(clusteredFOMs, heuristicRegistry);
            var order3Candidates = GenerateOrder3Candidates(clusteredFOMs, heuristicRegistry);
            var order4Candidates = GenerateOrder4Candidates(clusteredFOMs, heuristicRegistry);

            _logger.LogDebug("Generated candidates: Order2={Order2Count}, Order3={Order3Count}, Order4={Order4Count}",
                order2Candidates.Count, order3Candidates.Count, order4Candidates.Count);

            // Phase 3: Merge and balance candidates across orders
            initialCandidates.AddRange(order2Candidates);
            initialCandidates.AddRange(order3Candidates);
            initialCandidates.AddRange(order4Candidates);

            // Phase 4: Apply progressive filtering with multi-order awareness
            return ApplyMultiOrderFiltering(initialCandidates, heuristicRegistry);
        }

        /// <summary>
        /// Pre-filters and clusters FOMs based on compatibility metrics for better HOM generation.
        /// </summary>
        private List<IMutant> PrefilterAndClusterFOMs(List<IMutant> fomList, HeuristicRegistry heuristicRegistry)
        {
            // Filter FOMs with potential to be killed
            var killableFOMs = fomList.Where(fom =>
                !fom.AssessingTests.IsEmpty &&
                fom.ResultStatus != MutantStatus.NoCoverage).ToList();

            _logger.LogDebug("Filtered {KillableCount}/{TotalCount} FOMs as potentially killable",
                killableFOMs.Count, fomList.Count);

            if (killableFOMs.Count < 2)
            {
                killableFOMs = fomList.Where(f => !f.AssessingTests.IsEmpty).ToList();
                _logger.LogDebug("Using fallback: {FallbackCount} FOMs with any coverage", killableFOMs.Count);
            }

            // Sort by heuristic compatibility for better clustering
            return killableFOMs.OrderByDescending(fom => 
                heuristicRegistry.ScoreCandidate([fom])).ToList();
        }

        /// <summary>
        /// Generates high-quality order-2 candidates using enhanced pairing strategies.
        /// </summary>
        private List<ScoredCandidate> GenerateOrder2Candidates(List<IMutant> killableFOMs, HeuristicRegistry heuristicRegistry)
        {
            var candidates = new List<ScoredCandidate>();
            var targetCount = Math.Min(_candidatePoolSize / 3, _initialPairsPerSeed * killableFOMs.Count / 2);

            for (var i = 0; i < killableFOMs.Count && candidates.Count < targetCount; i++)
            {
                var fom1 = killableFOMs[i];
                
                // Find compatible FOMs with overlapping tests
                var compatibleFOMs = killableFOMs.Skip(i + 1)
                    .Where(fom2 => !fom1.AssessingTests.Intersect(fom2.AssessingTests).IsEmpty)
                    .Take(Math.Min(10, killableFOMs.Count - i - 1)) // Limit to prevent combinatorial explosion
                    .ToList();

                foreach (var fom2 in compatibleFOMs)
                {
                    var candidate = new List<IMutant> { fom1, fom2 };
                    var score = heuristicRegistry.ScoreCandidate(candidate);
                    
                    if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                    {
                        candidates.Add(new ScoredCandidate(candidate, score));
                    }
                }
            }

            return candidates.OrderByDescending(c => c.Score).Take(targetCount).ToList();
        }

        /// <summary>
        /// Generates order-3 candidates by extending high-scoring pairs and using triplet heuristics.
        /// </summary>
        private List<ScoredCandidate> GenerateOrder3Candidates(List<IMutant> killableFOMs, HeuristicRegistry heuristicRegistry)
        {
            var candidates = new List<ScoredCandidate>();
            var targetCount = Math.Min(_candidatePoolSize / 3, 50);

            // Strategy 1: Extend best order-2 pairs
            var order2Base = GenerateOrder2Candidates(killableFOMs, heuristicRegistry).Take(20).ToList();
            
            foreach (var basePair in order2Base)
            {
                var existingIds = new HashSet<int>(basePair.Candidate.Select(m => m.Id));
                
                // Find FOMs that could extend this pair
                var extendingFOMs = killableFOMs
                    .Where(fom => !existingIds.Contains(fom.Id))
                    .Where(fom => basePair.Candidate.All(existing => 
                        !fom.AssessingTests.Intersect(existing.AssessingTests).IsEmpty))
                    .Take(5)
                    .ToList();

                foreach (var extendingFOM in extendingFOMs)
                {
                    var candidate = new List<IMutant>(basePair.Candidate) { extendingFOM };
                    var score = heuristicRegistry.ScoreCandidate(candidate);
                    
                    if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                    {
                        candidates.Add(new ScoredCandidate(candidate, score));
                    }
                }
            }

            // Strategy 2: Direct triplet generation using clustering
            var clusterSize = Math.Min(killableFOMs.Count, 15);
            for (var i = 0; i < clusterSize - 2 && candidates.Count < targetCount; i++)
            {
                for (var j = i + 1; j < clusterSize - 1 && candidates.Count < targetCount; j++)
                {
                    for (var k = j + 1; k < clusterSize && candidates.Count < targetCount; k++)
                    {
                        var candidate = new List<IMutant> { killableFOMs[i], killableFOMs[j], killableFOMs[k] };
                        
                        // Check if all three have overlapping tests
                        if (HasMutualTestOverlap(candidate))
                        {
                            var score = heuristicRegistry.ScoreCandidate(candidate);
                            
                            if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                            {
                                candidates.Add(new ScoredCandidate(candidate, score));
                            }
                        }
                    }
                }
            }

            return candidates.OrderByDescending(c => c.Score).Take(targetCount).ToList();
        }

        /// <summary>
        /// Generates order-4 candidates using advanced composition strategies.
        /// </summary>
        private List<ScoredCandidate> GenerateOrder4Candidates(List<IMutant> killableFOMs, HeuristicRegistry heuristicRegistry)
        {
            var candidates = new List<ScoredCandidate>();
            var targetCount = Math.Min(_candidatePoolSize / 3, 30);

            // Strategy 1: Extend best order-3 candidates
            var order3Base = GenerateOrder3Candidates(killableFOMs, heuristicRegistry).Take(15).ToList();
            
            foreach (var baseTriple in order3Base)
            {
                var existingIds = new HashSet<int>(baseTriple.Candidate.Select(m => m.Id));
                
                var extendingFOMs = killableFOMs
                    .Where(fom => !existingIds.Contains(fom.Id))
                    .Where(fom => baseTriple.Candidate.All(existing => 
                        !fom.AssessingTests.Intersect(existing.AssessingTests).IsEmpty))
                    .Take(3)
                    .ToList();

                foreach (var extendingFOM in extendingFOMs)
                {
                    var candidate = new List<IMutant>(baseTriple.Candidate) { extendingFOM };
                    var score = heuristicRegistry.ScoreCandidate(candidate);
                    
                    if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                    {
                        candidates.Add(new ScoredCandidate(candidate, score));
                    }
                }
            }

            // Strategy 2: Merge compatible pairs
            var order2Candidates = GenerateOrder2Candidates(killableFOMs, heuristicRegistry).Take(10).ToList();
            
            for (var i = 0; i < order2Candidates.Count - 1 && candidates.Count < targetCount; i++)
            {
                for (var j = i + 1; j < order2Candidates.Count && candidates.Count < targetCount; j++)
                {
                    var pair1 = order2Candidates[i].Candidate;
                    var pair2 = order2Candidates[j].Candidate;
                    
                    // Check if pairs don't overlap
                    if (!pair1.Any(m1 => pair2.Any(m2 => m1.Id == m2.Id)))
                    {
                        var candidate = new List<IMutant>(pair1);
                        candidate.AddRange(pair2);
                        
                        if (HasMutualTestOverlap(candidate))
                        {
                            var score = heuristicRegistry.ScoreCandidate(candidate);
                            
                            if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                            {
                                candidates.Add(new ScoredCandidate(candidate, score));
                            }
                        }
                    }
                }
            }

            return candidates.OrderByDescending(c => c.Score).Take(targetCount).ToList();
        }

        /// <summary>
        /// Checks if all mutants in a candidate have mutual test overlap.
        /// </summary>
        private static bool HasMutualTestOverlap(List<IMutant> candidate)
        {
            if (candidate.Count < 2) return true;
            
            var commonTests = candidate[0].AssessingTests;
            for (var i = 1; i < candidate.Count; i++)
            {
                commonTests = commonTests.Intersect(candidate[i].AssessingTests);
                if (commonTests.IsEmpty) return false;
            }
            
            return !commonTests.IsEmpty;
        }

        /// <summary>
        /// Applies multi-order aware filtering with balanced representation.
        /// </summary>
        private List<ScoredCandidate> ApplyMultiOrderFiltering(
            List<ScoredCandidate> candidates,
            HeuristicRegistry heuristicRegistry)
        {
            if (candidates.Count == 0) return candidates;

            // Group by order for balanced selection
            var candidatesByOrder = candidates.GroupBy(c => c.Candidate.Count).ToDictionary(g => g.Key, g => g.ToList());
            
            var filteredCandidates = new List<ScoredCandidate>();
            var targetPerOrder = _candidatePoolSize / Math.Max(candidatesByOrder.Count, 1);

            foreach (var (order, orderCandidates) in candidatesByOrder)
            {
                // Apply heuristic filtering
                var filtered = orderCandidates.Where(c => !heuristicRegistry.ShouldFilterCandidate(c.Candidate)).ToList();
                
                // If filtering is too aggressive, fall back to score-based selection
                if (filtered.Count < Math.Max(2, orderCandidates.Count / _criticalFilterDivisor))
                {
                    _logger.LogDebug("Critical filtering too aggressive for order {Order}, using score-based selection", order);
                    filtered = orderCandidates.OrderByDescending(c => c.Score)
                        .Take(Math.Max(_fallbackTakeMin, targetPerOrder))
                        .ToList();
                }
                else
                {
                    filtered = filtered.OrderByDescending(c => c.Score).Take(targetPerOrder).ToList();
                }
                
                filteredCandidates.AddRange(filtered);
                
                _logger.LogDebug("Order {Order}: Selected {Selected}/{Total} candidates", 
                    order, filtered.Count, orderCandidates.Count);
            }

            return filteredCandidates.OrderByDescending(c => c.Score).ToList();
        }
    }
}
