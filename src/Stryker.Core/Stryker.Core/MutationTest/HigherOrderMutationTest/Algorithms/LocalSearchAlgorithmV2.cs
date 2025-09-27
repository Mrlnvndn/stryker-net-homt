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
    /// Local search configuration (parity with GeneticSearchOptions).
    /// </summary>
    public record LocalSearchOptions(
        int MaxIterations = 10,
        int TargetCandidateCount = 500,
        int MaxOrder = 4,
        int MaxNeighbors = 10,
        int NeighborGenerationCap = 15,
        int TopAdditions = 5,
        int TopRemovals = 5,
        int TopSwaps = 5,
        int SwapPositionsToTry = 2,
        double RemovalThresholdMultiplier = 0.7,
        double SwapMaintainThreshold = 0.8,
        double QualityThresholdEarly = 0.6,
        double QualityThresholdLate = 0.4,
        double NeighborExploitationRatio = 0.7,
        double TopCandidateFraction = 0.35,
        double RandomCandidateFraction = 0.15,
        int CriticalFilterDivisor = 4,
        int FallbackTakeMin = 5,
        int FallbackTakeDivisor = 4,
        int InitialPairsPerSeed = 3,
        double FinalSelectionPercentage = 0.4,
        int MinFinalCandidates = 20,
        int MaxFinalCandidates = 500,
        double FinalSelectionFomFactor = 0.10,
        int? RandomSeed = null
    );

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

        // Options backing fields
        private readonly LocalSearchOptions _opts;

        private List<ScoredCandidate> _allCandidates;
        private int _availableFomsCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalSearchAlgorithmV2"/> class.
        /// </summary>
        /// <param name="mutationTestInput">Mutation test input for additional context.</param>
        /// <param name="heuristics">A list of heuristics to guide the search.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="availableMutants">Available mutants for selection.</param>
        /// <param name="registerAllHeuristics">Whether to register built-in heuristics like Genetic.</param>
        /// <param name="randomSeed">Optional seed for deterministic comparisons (overrides LocalSearchOptions.RandomSeed).</param>
        /// <param name="localOptions">Local search options to configure the algorithm.</param>
        public LocalSearchAlgorithmV2(
            MutationTestInput mutationTestInput,
            IList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            IReadOnlyCollection<IMutant> availableMutants,
            bool registerAllHeuristics = false,
            int? randomSeed = null,
            LocalSearchOptions localOptions = null)
        {
            _opts = localOptions ?? new LocalSearchOptions();
            var seed = randomSeed ?? _opts.RandomSeed;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<LocalSearchAlgorithmV2>();

            // Initialize heuristic registry with parity to Genetic via registerAllHeuristics flag
            _heuristicRegistry = new HeuristicRegistry(availableMutants, options, mutationTestInput, registerAllHeuristics);

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
        /// Collects all candidates during the search and yields only the best candidates at the end.
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

            // Track all generated candidates with their scores
            _allCandidates = new List<ScoredCandidate>();
            _availableFomsCount = availableFOMs?.Count ?? 0;

            var generatedCandidates = new HashSet<string>(StringComparer.Ordinal);

            _logger.LogDebug("Starting SSHOM-focused LocalSearchV2 with {FOMCount} FOMs", availableFOMs.Count);

            // Initialize with SSHOM-targeted starting points
            var scoredCandidatePool = InitializeMultiOrderSSHOMStartingPoints(availableFOMs, _heuristicRegistry).OrderByDescending(s => s.Score).ToList();

            if (scoredCandidatePool.Count == 0)
            {
                _logger.LogWarning("No viable SSHOM candidates found in starting population");
                yield break;
            }

            _logger.LogDebug("Generated {StartingPoolSize} initial SSHOM-targeted candidates", scoredCandidatePool.Count);

            // Collect initial candidates that pass quality checks
            foreach (var scoredCandidate in scoredCandidatePool.ToList()) 
            {
                var candidateKey = GetCandidateKey(scoredCandidate.Candidate);
                if (generatedCandidates.Add(candidateKey))
                {
                    _allCandidates.Add(scoredCandidate);
                }
            }

            // Search loop - always run for full iterations to explore the search space completely
            for (var iteration = 0; iteration < _opts.MaxIterations; iteration++)
            {
                if (scoredCandidatePool.Count == 0)
                {
                    _logger.LogDebug("No scorable SSHOM candidates at iteration {Iteration}", iteration);
                    break;
                }

                var newScoredCandidates = new List<ScoredCandidate>();

                // Explore neighborhoods of top SSHOM candidates
                var topCount = Math.Max(1, (int)Math.Round(scoredCandidatePool.Count * _opts.TopCandidateFraction));

                var randomCount = Math.Max(1, (int)Math.Round(scoredCandidatePool.Count * _opts.RandomCandidateFraction));

                var topScoredCandidates = scoredCandidatePool.Take(topCount);
                var randomScoredCandidates = scoredCandidatePool.Skip(topCount).OrderBy(_ => _random.Next()).Take(randomCount);

                foreach (var scoredCandidate in topScoredCandidates.Concat(randomScoredCandidates))
                {
                    var neighbors = ExploreSSHOMTargetedNeighborhood(scoredCandidate, availableFOMs, _heuristicRegistry);
                    newScoredCandidates.AddRange(neighbors);
                }

                // Collect new candidates that pass quality checks
                var survivingNewCandidates = 0;
                foreach (var newScoredCandidate in newScoredCandidates)
                {
                    var candidateKey = GetCandidateKey(newScoredCandidate.Candidate);

                    if (generatedCandidates.Add(candidateKey))
                    {
                        scoredCandidatePool.Add(newScoredCandidate);

                        // Collect candidates that pass filtering and quality checks
                        if (!_heuristicRegistry.ShouldFilterCandidate(newScoredCandidate.Candidate))
                        {
                            _allCandidates.Add(newScoredCandidate);
                            survivingNewCandidates++;
                        }
                    }
                }

                _logger.LogTrace("Iteration {Iteration}: Generated {NewCandidates} new candidates, collected {Surviving}",
                    iteration, newScoredCandidates.Count, survivingNewCandidates);

                // Rerank and prune the candidate pool to maintain manageable size during search
                scoredCandidatePool = [.. scoredCandidatePool.OrderByDescending(scp => scp.Score).Take(_opts.TargetCandidateCount)];
            }

            // Final selection: yield the best candidates using standardized selection
            var finalCandidates = StandardizedFinalSelection(_allCandidates);
            
            _logger.LogInformation("LocalSearchV2 completed: Collected {TotalCandidates} candidates, yielding top {YieldedCount} after {Iterations} iterations",
                _allCandidates.Count, finalCandidates.Count, _opts.MaxIterations);

            foreach (var candidate in finalCandidates)
            {
                yield return new HigherOrderMutant(candidate.Candidate, Name, predictedScore: candidate.Score);
            }
        }

        /// <summary>
        /// Standardized final selection logic shared with GeneticSearch for fair comparison.
        /// </summary>
        private List<ScoredCandidate> StandardizedFinalSelection(List<ScoredCandidate> allCandidates)
        {
            if (allCandidates.Count == 0)
            {
                return allCandidates;
            }

            var allUniqueCandidates = allCandidates
                .GroupBy(c => GetCandidateKey(c.Candidate))
                .Select(g => g.OrderByDescending(c => c.Score).First())
                .ToList();

            _logger.LogInformation(
                "LocalSearchV2: from {Total} candidates, {Unique} are unique",
                allCandidates.Count, allUniqueCandidates.Count);

            var sortedCandidates = allUniqueCandidates.OrderByDescending(c => c.Score).ToList();

            //var byPercentage = (int)Math.Ceiling(sortedCandidates.Count * _opts.FinalSelectionPercentage);
            var byFoms = (int)Math.Ceiling(_availableFomsCount * _opts.FinalSelectionFomFactor);

            var targetCount = Math.Max(_opts.MinFinalCandidates, byFoms);
            
            var finalCandidates = sortedCandidates.Take(targetCount).ToList();

            _logger.LogInformation(
                "LocalSearchV2: Selected {FinalCount} candidates from {TotalCount} collected using standardized selection. Lowest score: {lowestscore}, highers score: {highestScore}",
                finalCandidates.Count, sortedCandidates.Count, finalCandidates.Last().Score, finalCandidates.First().Score);

            return finalCandidates;
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

            for (var i = 0; i < killableFOMs.Count && scoredCandidates.Count < _opts.TargetCandidateCount; i++)
            {
                var fom1 = killableFOMs[i];

                // Pre-calculate scores for all compatible FOMs
                var scoredCompatibleFOMs = killableFOMs.Skip(i + 1)
                    .Where(fom2 => !fom1.AssessingTests.Intersect(fom2.AssessingTests).IsEmpty)
                    .Select(fom2 => new ScoredCandidate([fom1, fom2], heuristicRegistry.ScoreCandidate([fom1, fom2])));

                // Take top-N most promising pairs
                scoredCandidates.AddRange(scoredCompatibleFOMs.OrderByDescending(sc => sc.Score).Take(_opts.InitialPairsPerSeed));
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
            if (criticalFiltered.Count < Math.Max(2, candidates.Count / _opts.CriticalFilterDivisor))
            {
                _logger.LogDebug("Critical filtering too aggressive, using score-based selection");
                return [.. candidates
                    .OrderByDescending(c => c.Score)
                    .Take(Math.Max(_opts.FallbackTakeMin, _opts.TargetCandidateCount / _opts.FallbackTakeDivisor))];
            }

            return [.. criticalFiltered.Take(_opts.TargetCandidateCount)];
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

            availableStrategies.Add(() => neighbors.AddRange(GenerateAdditionNeighbors(candidate, availableFOMs, heuristicRegistry)));
            

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
                if (neighbors.Count >= _opts.NeighborGenerationCap)
                {
                    break;
                }
            }

            // Hybrid selection: Balance exploitation with exploration
            if (neighbors.Count > _opts.MaxNeighbors)
            {
                return SelectBestNeighborsWithDiversity(neighbors, _opts.MaxNeighbors);
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
            var exploitationCount = (int)(maxCount * _opts.NeighborExploitationRatio);
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
            if (candidate.Candidate.Count < _opts.MaxOrder)
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
                    .Take(_opts.TopAdditions)
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
                .Take(_opts.TopSwaps)
                .ToList();

            // For each position in the candidate (limited)
            for (int i = 0; i < candidate.Candidate.Count && i < _opts.SwapPositionsToTry; i++)
            {
                var originalScore = candidate.Score;

                // For each FOM to swap with
                foreach (var fom in fomsToSwapWith)
                {
                    var newCandidate = new List<IMutant>(candidate.Candidate);
                    newCandidate[i] = fom;

                    var newScore = heuristicRegistry.ScoreCandidate(newCandidate);

                    // Only include swaps that improve or maintain the score (threshold)
                    if (newScore >= originalScore * _opts.SwapMaintainThreshold)
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
                    .Where(x => x.Score >= candidate.Score * _opts.RemovalThresholdMultiplier)
                    .Take(_opts.TopRemovals)
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
        /// Initialize starting points with diverse orders (2, 3, 4) targeting SSHOM potential.
        /// Uses multi-order generation with balanced distribution and intelligent clustering based on available heuristics.
        /// </summary>
        private List<ScoredCandidate> InitializeMultiOrderSSHOMStartingPoints(
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var initialCandidates = new List<ScoredCandidate>();
            var fomList = availableFOMs.ToList();

            // Phase 2: Generate candidates for each order with balanced distribution using heuristics
            var order2Candidates = GenerateOrder2CandidatesUsingHeuristics([.. availableFOMs], heuristicRegistry);
            var order3Candidates = GenerateOrder3CandidatesUsingHeuristics([.. availableFOMs], heuristicRegistry);
            var order4Candidates = GenerateOrder4CandidatesUsingHeuristics([.. availableFOMs], heuristicRegistry);

            _logger.LogDebug("Generated candidates: Order2={Order2Count}, Order3={Order3Count}, Order4={Order4Count}",
                order2Candidates.Count, order3Candidates.Count, order4Candidates.Count);

            // Phase 3: Merge and balance candidates across orders
            initialCandidates.AddRange(order2Candidates);
            initialCandidates.AddRange(order3Candidates);
            initialCandidates.AddRange(order4Candidates);

            // Phase 4: Apply progressive filtering with heuristic awareness
            return ApplyProgressiveFiltering(initialCandidates, heuristicRegistry);
        }

        /// <summary>
        /// Pre-filters and clusters FOMs based on available heuristics rather than hard-coded logic.
        /// </summary>
        private List<IMutant> PrefilterAndClusterFOMsUsingHeuristics(List<IMutant> fomList, HeuristicRegistry heuristicRegistry)
        {
            // Start with all FOMs and let heuristics do the filtering
            var candidateFOMs = fomList.ToList();

            // If we have filtering heuristics, use them to pre-filter individual FOMs
            var filteringHeuristics = heuristicRegistry.GetFilteringHeuristics().ToList();
            if (filteringHeuristics.Count > 0)
            {
                candidateFOMs = [.. candidateFOMs.Where(fom => !heuristicRegistry.ShouldFilterCandidate([fom]))];
                
                _logger.LogDebug("Pre-filtered {FilteredCount}/{TotalCount} FOMs using available heuristics",
                    candidateFOMs.Count, fomList.Count);
            }

            // Sort by heuristic-based scoring for better clustering
            var scoringHeuristics = heuristicRegistry.GetScoringHeuristics().ToList();
            if (scoringHeuristics.Count > 0)
            {
                candidateFOMs = candidateFOMs.OrderByDescending(fom => 
                    heuristicRegistry.ScoreCandidate([fom])).ToList();
            }
            else
            {
                // Fallback: simple random ordering for diversity
                candidateFOMs = [.. candidateFOMs.OrderBy(x => _random.Next())];
            }

            return candidateFOMs;
        }

        /// <summary>
        /// Generates high-quality order-2 candidates using heuristic-based strategies.
        /// </summary>
        private List<ScoredCandidate> GenerateOrder2CandidatesUsingHeuristics(List<IMutant> candidateFOMs, HeuristicRegistry heuristicRegistry)
        {
            var candidates = new List<ScoredCandidate>();
            var targetCount = Math.Min(_opts.TargetCandidateCount / 3, _opts.InitialPairsPerSeed * candidateFOMs.Count / 2);

            // Generate pairs and score them using heuristics
            for (var i = 0; i < candidateFOMs.Count && candidates.Count < targetCount; i++)
            {
                var fom1 = candidateFOMs[i];
                
                // Try pairing with subsequent FOMs
                var pairingCandidates = candidateFOMs.Skip(i + 1)
                    .Take(Math.Min(10, candidateFOMs.Count - i - 1)) // Limit to prevent combinatorial explosion
                    .ToList();

                foreach (var fom2 in pairingCandidates)
                {
                    var candidate = new List<IMutant> { fom1, fom2 };
                    
                    // Use heuristics to determine if this pair should be considered
                    if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                    {
                        var score = heuristicRegistry.ScoreCandidate(candidate);
                        candidates.Add(new ScoredCandidate(candidate, score));
                    }
                }
            }

            return [.. candidates.OrderByDescending(c => c.Score).Take(targetCount)];
        }

        /// <summary>
        /// Generates order-3 candidates using heuristic-based extension and direct generation strategies.
        /// </summary>
        private List<ScoredCandidate> GenerateOrder3CandidatesUsingHeuristics(List<IMutant> candidateFOMs, HeuristicRegistry heuristicRegistry)
        {
            var candidates = new List<ScoredCandidate>();
            var targetCount = Math.Min(_opts.TargetCandidateCount / 3, 50);

            // Strategy 1: Extend best order-2 pairs using heuristics
            var order2Base = GenerateOrder2CandidatesUsingHeuristics(candidateFOMs, heuristicRegistry).Take(20).ToList();
            
            foreach (var basePair in order2Base)
            {
                var existingIds = new HashSet<int>(basePair.Candidate.Select(m => m.Id));
                
                // Find FOMs that could extend this pair
                var extendingFOMs = candidateFOMs
                    .Where(fom => !existingIds.Contains(fom.Id))
                    .Take(5)
                    .ToList();

                foreach (var extendingFOM in extendingFOMs)
                {
                    var candidate = new List<IMutant>(basePair.Candidate) { extendingFOM };
                    
                    if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                    {
                        var score = heuristicRegistry.ScoreCandidate(candidate);
                        candidates.Add(new ScoredCandidate(candidate, score));
                    }
                }
            }

            // Strategy 2: Direct triplet generation using heuristic-based clustering
            var clusterSize = Math.Min(candidateFOMs.Count, 15);
            for (var i = 0; i < clusterSize - 2 && candidates.Count < targetCount; i++)
            {
                for (var j = i + 1; j < clusterSize - 1 && candidates.Count < targetCount; j++)
                {
                    for (var k = j + 1; k < clusterSize && candidates.Count < targetCount; k++)
                    {
                        var candidate = new List<IMutant> { candidateFOMs[i], candidateFOMs[j], candidateFOMs[k] };
                        
                        if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                        {
                            var score = heuristicRegistry.ScoreCandidate(candidate);
                            candidates.Add(new ScoredCandidate(candidate, score));
                        }
                    }
                }
            }

            return candidates.OrderByDescending(c => c.Score).Take(targetCount).ToList();
        }

        /// <summary>
        /// Generates order-4 candidates using heuristic-based advanced composition strategies.
        /// </summary>
        private List<ScoredCandidate> GenerateOrder4CandidatesUsingHeuristics(List<IMutant> candidateFOMs, HeuristicRegistry heuristicRegistry)
        {
            var candidates = new List<ScoredCandidate>();
            var targetCount = Math.Min(_opts.TargetCandidateCount / 3, 30);

            // Strategy 1: Extend best order-3 candidates using heuristics
            var order3Base = GenerateOrder3CandidatesUsingHeuristics(candidateFOMs, heuristicRegistry).Take(15).ToList();
            
            foreach (var baseTriple in order3Base)
            {
                var existingIds = new HashSet<int>(baseTriple.Candidate.Select(m => m.Id));
                
                var extendingFOMs = candidateFOMs
                    .Where(fom => !existingIds.Contains(fom.Id))
                    .Take(3)
                    .ToList();

                foreach (var extendingFOM in extendingFOMs)
                {
                    var candidate = new List<IMutant>(baseTriple.Candidate) { extendingFOM };
                    
                    if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                    {
                        var score = heuristicRegistry.ScoreCandidate(candidate);
                        candidates.Add(new ScoredCandidate(candidate, score));
                    }
                }
            }

            // Strategy 2: Merge compatible pairs using heuristics
            var order2Candidates = GenerateOrder2CandidatesUsingHeuristics(candidateFOMs, heuristicRegistry).Take(10).ToList();
            
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
                        
                        if (!heuristicRegistry.ShouldFilterCandidate(candidate))
                        {
                            var score = heuristicRegistry.ScoreCandidate(candidate);
                            candidates.Add(new ScoredCandidate(candidate, score));
                        }
                    }
                }
            }

            return candidates.OrderByDescending(c => c.Score).Take(targetCount).ToList();
        }
    }
}
