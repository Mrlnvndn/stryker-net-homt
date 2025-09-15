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

        private readonly int _maxNeighbors;

        /// <summary>
        /// Random number generator for stochastic elements of search.
        /// </summary>
        private readonly Random _random = new();

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
            int maxIterations = 20,
            int candidatePoolSize = 100,
            int maxOrder = 4,
            int maxNeighbors = 15)
        {
            _options = options as StrykerOptions ?? new StrykerOptions();
            _maxIterations = maxIterations;
            // ENHANCEMENT: Scale candidate pool size based on available mutants
            _candidatePoolSize = Math.Max(candidatePoolSize, Math.Min(200, availableMutants.Count / 20));
            _maxOrder = maxOrder;
            _maxNeighbors = maxNeighbors;
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
        /// Uses deferred yielding to ensure globally optimal candidate selection.
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
            var (allCandidates, generatedCandidates, scoredCandidatePool) = InitializeSearchContext(heuristics, availableFOMs);

            if (scoredCandidatePool.Count == 0)
            {
                _logger.LogWarning("No viable SSHOM candidates found in starting population");
                return Enumerable.Empty<HigherOrderMutant>();
            }

            ProcessInitialCandidates(scoredCandidatePool, generatedCandidates, allCandidates);

            PerformSearchIterations(availableFOMs, scoredCandidatePool, generatedCandidates, allCandidates);

            return FinalizeResults(allCandidates);
        }

        /// <summary>
        /// Initializes the search context by registering heuristics, setting up data structures,
        /// and creating initial starting points.
        /// </summary>
        private (List<ScoredCandidate> allCandidates, HashSet<string> generatedCandidates, List<ScoredCandidate> scoredCandidatePool) 
            InitializeSearchContext(IReadOnlyList<IHOMHeuristic> heuristics, IReadOnlyCollection<IMutant> availableFOMs)
        {
            // Register additional heuristics
            foreach (var heuristic in heuristics)
            {
                _heuristicRegistry.RegisterHeuristic(heuristic);
            }

            // Track all candidates throughout the search process
            var allCandidates = new List<ScoredCandidate>();
            var generatedCandidates = new HashSet<string>(StringComparer.Ordinal);

            _logger.LogInformation("=== LocalSearchV2 Debug Information ===");
            _logger.LogInformation("Algorithm Configuration: maxIterations={MaxIterations}, candidatePoolSize={CandidatePoolSize}, maxOrder={MaxOrder}, maxNeighbors={MaxNeighbors}", 
                _maxIterations, _candidatePoolSize, _maxOrder, _maxNeighbors);
            _logger.LogInformation("Starting SSHOM-focused LocalSearchV2 with deferred yielding for {FOMCount} FOMs", availableFOMs.Count);

            // Initialize with SSHOM-targeted starting points
            var scoredCandidatePool = InitializeSSHOMTargetedStartingPoints(availableFOMs, _heuristicRegistry);

            _logger.LogInformation("Generated {StartingPoolSize} initial SSHOM-targeted candidates", scoredCandidatePool.Count);

            return (allCandidates, generatedCandidates, scoredCandidatePool);
        }

        /// <summary>
        /// Processes the initial candidate pool by applying quality filtering and adding qualifying candidates
        /// to the final collection.
        /// </summary>
        private void ProcessInitialCandidates(
            List<ScoredCandidate> scoredCandidatePool, 
            HashSet<string> generatedCandidates, 
            List<ScoredCandidate> allCandidates)
        {
            var initialQualityCount = 0;
            foreach (var scoredCandidate in scoredCandidatePool)
            {
                var candidateKey = GetCandidateKey(scoredCandidate.Candidate);
                if (generatedCandidates.Add(candidateKey))
                {
                    var passesQuality = ShouldYieldCandidate(scoredCandidate.Candidate, 0, _heuristicRegistry);
                    _logger.LogTrace("Initial candidate {Key} (score: {Score:F3}) - Quality check: {PassesQuality}", 
                        candidateKey, scoredCandidate.Score, passesQuality);
                    
                    if (passesQuality)
                    {
                        allCandidates.Add(scoredCandidate);
                        initialQualityCount++;
                    }
                }
            }

            _logger.LogInformation("Initial quality filtering: {InitialQualityCount}/{StartingPoolSize} candidates passed (threshold: >0.6)", 
                initialQualityCount, scoredCandidatePool.Count);
        }

        /// <summary>
        /// Performs the main search iterations, exploring neighborhoods and collecting qualifying candidates.
        /// </summary>
        private void PerformSearchIterations(
            IReadOnlyCollection<IMutant> availableFOMs,
            List<ScoredCandidate> scoredCandidatePool,
            HashSet<string> generatedCandidates,
            List<ScoredCandidate> allCandidates)
        {
            for (var iteration = 0; iteration < _maxIterations; iteration++)
            {
                if (scoredCandidatePool.Count == 0)
                {
                    _logger.LogInformation("No scorable SSHOM candidates at iteration {Iteration}", iteration);
                    break;
                }

                var newScoredCandidates = ExploreNeighborhoodsForIteration(iteration, scoredCandidatePool, availableFOMs);

                var iterationStats = ProcessIterationCandidates(newScoredCandidates, generatedCandidates, scoredCandidatePool, allCandidates, iteration);

                LogIterationResults(iteration, newScoredCandidates.Count, iterationStats);

                // EXPERIMENTAL: Disable pruning for first few iterations to allow more exploration
                // This should reduce duplicates and increase diversity
                if (iteration >= 3)  // Only prune after iteration 3
                {
                    PruneSearchPool(scoredCandidatePool);
                }
                else
                {
                    _logger.LogTrace("  Pool pruning skipped for iteration {Iteration} to encourage exploration (pool size: {PoolSize})", 
                        iteration, scoredCandidatePool.Count);
                }
            }

            _logger.LogInformation("Search completed: Collected {TotalCollected} quality candidates across {Iterations} iterations", 
                allCandidates.Count, _maxIterations);
        }

        /// <summary>
        /// Explores neighborhoods for a single iteration using exploitation and exploration strategies.
        /// </summary>
        private List<ScoredCandidate> ExploreNeighborhoodsForIteration(
            int iteration,
            List<ScoredCandidate> scoredCandidatePool,
            IReadOnlyCollection<IMutant> availableFOMs)
        {
            var newScoredCandidates = new List<ScoredCandidate>();

            // Exploitation: Explore neighborhoods of top SSHOM candidates
            var topScoredCandidates = scoredCandidatePool.Take(Math.Max(1, scoredCandidatePool.Count / 4));

            // Exploration: Explore neighborhoods of random remaining candidates
            var randomScoredCandidates = scoredCandidatePool.Skip(Math.Max(1, scoredCandidatePool.Count / 4))
                .OrderBy(x => _random.Next()).Take(scoredCandidatePool.Count / 4);

            var topCandidatesCount = topScoredCandidates.Count();
            
            _logger.LogInformation("Iteration {Iteration}: Exploring neighborhoods of {TopCount}/{PoolSize} top candidates", 
                iteration, topCandidatesCount, scoredCandidatePool.Count);

            // Process top candidates
            foreach (var scoredCandidate in topScoredCandidates)
            {
                var neighbors = ExploreSSHOMTargetedNeighborhood(scoredCandidate, availableFOMs, _heuristicRegistry);
                newScoredCandidates.AddRange(neighbors);
                
                _logger.LogDebug("  Candidate {Key} (score: {Score:F3}) generated {NeighborCount} neighbors", 
                    GetCandidateKey(scoredCandidate.Candidate), scoredCandidate.Score, neighbors.Count);
            }

            // Process random candidates
            foreach (var scoredCandidate in randomScoredCandidates)
            {
                var neighbors = ExploreSSHOMTargetedNeighborhood(scoredCandidate, availableFOMs, _heuristicRegistry);
                newScoredCandidates.AddRange(neighbors);

                _logger.LogInformation("  Candidate {Key} (score: {Score:F3}) generated {NeighborCount} neighbors",
                    GetCandidateKey(scoredCandidate.Candidate), scoredCandidate.Score, neighbors.Count);
            }

            return newScoredCandidates;
        }

        /// <summary>
        /// Processes candidates from a single iteration, applying filtering and collecting qualifying ones.
        /// </summary>
        private (int duplicates, int filteredByHeuristics, int filteredByQuality, int collected) ProcessIterationCandidates(
            List<ScoredCandidate> newScoredCandidates,
            HashSet<string> generatedCandidates,
            List<ScoredCandidate> scoredCandidatePool,
            List<ScoredCandidate> allCandidates,
            int iteration)
        {
            var candidatesAddedThisIteration = 0;
            var filteredByHeuristics = 0;
            var filteredByQuality = 0;
            var duplicates = 0;

            foreach (var newScoredCandidate in newScoredCandidates)
            {
                var candidateKey = GetCandidateKey(newScoredCandidate.Candidate);

                if (generatedCandidates.Add(candidateKey))
                {
                    scoredCandidatePool.Add(newScoredCandidate);

                    // PROGRESSIVE FILTERING: Be more lenient as iterations progress
                    var passesHeuristicFilter = !_heuristicRegistry.ShouldFilterCandidate(newScoredCandidate.Candidate);
                    var passesQualityFilter = ShouldYieldCandidate(newScoredCandidate.Candidate, iteration, _heuristicRegistry);
                    
                    // ADAPTIVE BYPASS: If we're getting too few candidates, bypass some filtering
                    var bypassHeuristicFilter = iteration >= 3 && candidatesAddedThisIteration == 0 && newScoredCandidate.Score > 0.5;
                    
                    if (!passesHeuristicFilter && !bypassHeuristicFilter)
                    {
                        filteredByHeuristics++;
                    }
                    else if (!passesQualityFilter)
                    {
                        filteredByQuality++;
                    }
                    else
                    {
                        allCandidates.Add(newScoredCandidate);
                        candidatesAddedThisIteration++;
                        
                        if (bypassHeuristicFilter)
                        {
                            _logger.LogInformation("  Bypassed heuristic filter for candidate {Key} (score: {Score:F3})", 
                                candidateKey, newScoredCandidate.Score);
                        }
                    }
                }
                else
                {
                    duplicates++;
                }
            }

            return (duplicates, filteredByHeuristics, filteredByQuality, candidatesAddedThisIteration);
        }

        /// <summary>
        /// Logs the results of an iteration with detailed statistics.
        /// </summary>
        private void LogIterationResults(
            int iteration,
            int totalNeighbors,
            (int duplicates, int filteredByHeuristics, int filteredByQuality, int collected) stats)
        {
            // Determine quality threshold for this iteration
            var qualityThreshold = iteration < _maxIterations / 3 ? 0.6 : 0.4;

            _logger.LogInformation("Iteration {Iteration}: Generated {NewCandidates} neighbors → {Duplicates} duplicates, {HeuristicFiltered} heuristic-filtered, {QualityFiltered} quality-filtered (>{Threshold:F1}), {QualityCandidates} collected",
                iteration, totalNeighbors, stats.duplicates, stats.filteredByHeuristics, stats.filteredByQuality, qualityThreshold, stats.collected);
        }

        /// <summary>
        /// Prunes the search pool to maintain manageable size by keeping only the top-scoring candidates.
        /// Enhanced with diversity preservation to prevent premature convergence.
        /// </summary>
        private void PruneSearchPool(List<ScoredCandidate> scoredCandidatePool)
        {
            var poolSizeBefore = scoredCandidatePool.Count;
            
            if (scoredCandidatePool.Count <= _candidatePoolSize)
            {
                // No pruning needed
                scoredCandidatePool.Sort((x, y) => y.Score.CompareTo(x.Score));
                return;
            }

            // Sort by score (descending)
            scoredCandidatePool.Sort((x, y) => y.Score.CompareTo(x.Score));
            
            // Strategy: Keep top 70% by score + 30% diverse candidates
            var topCandidatesCount = (int)(_candidatePoolSize * 0.7);
            var diverseCandidatesCount = _candidatePoolSize - topCandidatesCount;
            
            // Keep top performers
            var prunedPool = scoredCandidatePool.Take(topCandidatesCount).ToList();
            
            // Add diverse candidates from remaining pool
            var remainingCandidates = scoredCandidatePool.Skip(topCandidatesCount).ToList();
            var diverseCandidates = SelectDiverseCandidates(
                remainingCandidates, 
                prunedPool.Select(c => GetCandidateKey(c.Candidate)).ToHashSet(StringComparer.Ordinal), 
                diverseCandidatesCount);
            
            prunedPool.AddRange(diverseCandidates);
            
            // Replace the original pool
            scoredCandidatePool.Clear();
            scoredCandidatePool.AddRange(prunedPool);
            
            _logger.LogTrace("  Pool size: {Before} → {After} (pruned {Pruned}, kept {TopCount} top + {DiverseCount} diverse)", 
                poolSizeBefore, scoredCandidatePool.Count, poolSizeBefore - scoredCandidatePool.Count, 
                topCandidatesCount, diverseCandidates.Count);
        }

        /// <summary>
        /// Finalizes the search results by selecting the globally best candidates and creating the final output.
        /// </summary>
        private IEnumerable<HigherOrderMutant> FinalizeResults(List<ScoredCandidate> allCandidates)
        {
            // Global optimization: Select the absolute best candidates from all collected
            var selectedCandidates = SelectGloballyBestCandidates(allCandidates);

            _logger.LogInformation("LocalSearchV2 completed: Selected {SelectedCount} best candidates from {TotalCollected} total candidates across {Iterations} iterations",
                selectedCandidates.Count, allCandidates.Count, _maxIterations);

            // Debug final selection
            if (selectedCandidates.Count > 0)
            {
                var topScore = selectedCandidates.Max(c => c.Score);
                var bottomScore = selectedCandidates.Min(c => c.Score);
                _logger.LogInformation("Final candidates score range: {BottomScore:F3} - {TopScore:F3}", bottomScore, topScore);
            }

            _logger.LogInformation("=== End Debug Information ===");

            // Return the globally optimal candidates
            return selectedCandidates.Select(sc => new HigherOrderMutant(sc.Candidate, Name));
        }

        /// <summary>
        /// Selects the globally best candidates from all collected candidates using sophisticated ranking.
        /// Prioritizes score while ensuring diversity to avoid redundant similar candidates.
        /// </summary>
        /// <param name="allCandidates">All candidates collected during the search process</param>
        /// <returns>The globally best candidates for SSHOM potential</returns>
        private List<ScoredCandidate> SelectGloballyBestCandidates(List<ScoredCandidate> allCandidates)
        {
            _logger.LogDebug("=== Global Selection Debug ===");
            _logger.LogDebug("Input: {TotalCandidates} candidates, target pool size: {TargetSize}", 
                allCandidates.Count, _candidatePoolSize);

            if (allCandidates.Count <= _candidatePoolSize)
            {
                var result = allCandidates.OrderByDescending(c => c.Score).ToList();
                _logger.LogDebug("All candidates fit in pool: returning {Count} candidates", result.Count);
                return result;
            }

            var selectedCandidates = new List<ScoredCandidate>();
            var candidateKeys = new HashSet<string>(StringComparer.Ordinal);

            // Sort all candidates by score (descending)
            var sortedCandidates = allCandidates.OrderByDescending(c => c.Score).ToList();

            // Quality gate: Only consider candidates above a minimum threshold
            var qualityThreshold = sortedCandidates.Count > 10 
                ? sortedCandidates[Math.Min(9, sortedCandidates.Count - 1)].Score * 0.8  // Top 10 quality level
                : 0.4; // Fallback threshold

            var qualityCandidates = sortedCandidates.Where(c => c.Score >= qualityThreshold).ToList();
            
            _logger.LogDebug("Quality threshold: {Threshold:F3} → {QualityCount}/{TotalCount} candidates qualify", 
                qualityThreshold, qualityCandidates.Count, sortedCandidates.Count);

            if (qualityCandidates.Count == 0)
            {
                // Fallback to top candidates if quality gate is too strict
                qualityCandidates = sortedCandidates.Take(Math.Min(20, sortedCandidates.Count)).ToList();
                _logger.LogDebug("Quality gate too strict, using fallback: top {FallbackCount} candidates", qualityCandidates.Count);
            }

            // Phase 1: Take the highest scoring candidates (exploitation)
            var targetSize = Math.Min(_candidatePoolSize, qualityCandidates.Count);
            var topCandidates = (int)(targetSize * 0.8); // 80% exploitation
            
            foreach (var candidate in qualityCandidates.Take(topCandidates))
            {
                var candidateKey = GetCandidateKey(candidate.Candidate);
                if (candidateKeys.Add(candidateKey)) // Avoid exact duplicates
                {
                    selectedCandidates.Add(candidate);
                }
            }

            _logger.LogDebug("Exploitation phase: selected {ExploitationCount}/{TargetExploitation} candidates", 
                selectedCandidates.Count, topCandidates);

            // Phase 2: Add diverse candidates from remaining pool (exploration)
            var remainingSlots = targetSize - selectedCandidates.Count;
            if (remainingSlots > 0)
            {
                var remainingCandidates = qualityCandidates.Skip(topCandidates).ToList();
                var diverseCandidates = SelectDiverseCandidates(remainingCandidates, candidateKeys, remainingSlots);
                selectedCandidates.AddRange(diverseCandidates);
                
                _logger.LogDebug("Exploration phase: added {ExplorationCount} diverse candidates", diverseCandidates.Count);
            }

            _logger.LogDebug("Selected {SelectedCount} candidates from {QualityCount} quality candidates (threshold: {Threshold:F3}) out of {TotalCount} total",
                selectedCandidates.Count, qualityCandidates.Count, qualityThreshold, allCandidates.Count);
            
            _logger.LogDebug("=== End Global Selection Debug ===");

            return selectedCandidates;
        }

        /// <summary>
        /// Selects diverse candidates to avoid redundancy in the final selection.
        /// Uses simple diversity heuristics based on constituent mutant overlap.
        /// </summary>
        /// <param name="candidates">Remaining candidates to select from</param>
        /// <param name="existingKeys">Keys of already selected candidates</param>
        /// <param name="maxCount">Maximum number of diverse candidates to select</param>
        /// <returns>List of diverse candidates</returns>
        private List<ScoredCandidate> SelectDiverseCandidates(
            List<ScoredCandidate> candidates, 
            HashSet<string> existingKeys, 
            int maxCount)
        {
            var diverseSelection = new List<ScoredCandidate>();
            var selectedMutantIds = new HashSet<int>();

            foreach (var candidate in candidates)
            {
                if (diverseSelection.Count >= maxCount) break;

                var candidateKey = GetCandidateKey(candidate.Candidate);
                if (existingKeys.Contains(candidateKey)) continue;

                // Simple diversity check: prefer candidates with different constituent mutants
                var candidateMutantIds = candidate.Candidate.Select(m => m.Id).ToHashSet();
                var overlap = candidateMutantIds.Intersect(selectedMutantIds).Count();
                var overlapRatio = candidateMutantIds.Count > 0 ? (double)overlap / candidateMutantIds.Count : 1.0;

                // Accept candidates with low overlap (< 50%) or if we need to fill remaining slots
                if (overlapRatio < 0.5 || diverseSelection.Count < maxCount / 2)
                {
                    diverseSelection.Add(candidate);
                    existingKeys.Add(candidateKey);
                    foreach (var mutantId in candidateMutantIds)
                    {
                        selectedMutantIds.Add(mutantId);
                    }
                }
            }

            return diverseSelection;
        }

        /// <summary>
        /// Initializes starting points specifically targeting SSHOM potential.
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
            // This includes all aspects: overlapping tests, syntactic compatibility (via CodeLocationHeuristic), 
            // mutator types, coverage patterns
            var sshomTargetPairs = CreateSSHOMTargetPairs(killableFOMs, heuristicRegistry);
            initialCandidates.AddRange(sshomTargetPairs);

            _logger.LogDebug("Created {InitialCount} candidates before progressive filtering", initialCandidates.Count);

            // Phase 3: Apply progressive filtering with fallback protection using generic scoring
            var filtered = ApplyProgressiveFiltering(initialCandidates, heuristicRegistry);
            
            _logger.LogDebug("Progressive filtering: {InitialCount} → {FilteredCount} candidates", 
                initialCandidates.Count, filtered.Count);

            return filtered;
        }

        /// <summary>
        /// Creates pairs with maximum SSHOM potential based on heuristic evaluation.
        /// </summary>
        private List<ScoredCandidate> CreateSSHOMTargetPairs(List<IMutant> killableFOMs, HeuristicRegistry heuristicRegistry)
        {
            var scoredCandidates = new List<ScoredCandidate>();
            var totalCompatiblePairs = 0;

            for (var i = 0; i < killableFOMs.Count && scoredCandidates.Count < _candidatePoolSize * 2; i++) // Increased initial generation
            {
                var fom1 = killableFOMs[i];

                // Pre-calculate scores for all compatible FOMs
                var scoredCompatibleFOMs = killableFOMs.Skip(i + 1)
                    .Where(fom2 => !fom1.AssessingTests.Intersect(fom2.AssessingTests).IsEmpty)
                    .Select(fom2 => new ScoredCandidate([fom1, fom2], heuristicRegistry.ScoreCandidate([fom1, fom2])))
                    .ToList();

                totalCompatiblePairs += scoredCompatibleFOMs.Count;

                // Take top 5 most promising pairs (increased from 3)
                var topPairs = scoredCompatibleFOMs.OrderByDescending(sc => sc.Score).Take(5).ToList();
                scoredCandidates.AddRange(topPairs);

                if (i < 5) // Debug first few FOMs
                {
                    _logger.LogTrace("FOM {Index} (ID: {Id}): {CompatibleCount} compatible FOMs, selected top {SelectedCount} pairs", 
                        i, fom1.Id, scoredCompatibleFOMs.Count, topPairs.Count);
                }
            }

            _logger.LogDebug("Created {PairCount} SSHOM-targeted pairs from {TotalCompatible} compatible pairs across {FOMCount} FOMs", 
                scoredCandidates.Count, totalCompatiblePairs, killableFOMs.Count);

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

            // RELAXED FILTERING: If we lost too many candidates, be more permissive
            if (criticalFiltered.Count < Math.Max(5, candidates.Count / 2)) // Changed from /4 to /2
            {
                _logger.LogDebug("Critical filtering too aggressive ({FilteredCount}/{TotalCount}), using score-based selection", 
                    criticalFiltered.Count, candidates.Count);
                return candidates
                    .OrderByDescending(c => c.Score)
                    .Take(Math.Max(10, _candidatePoolSize / 2))
                    .ToList(); // Increased fallback size
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

            // Get heuristic suggestions first (most intelligent approach)

            var searchGuidanceHeuristics = heuristicRegistry.GetSearchGuidanceHeuristics();
            if (searchGuidanceHeuristics?.Any() == true)
            {               
                var suggestedNeighbors = heuristicRegistry.SuggestNextCandidates(candidate.Candidate, availableFOMs);
                _logger.LogInformation("Found {numberOfSuggestions} intelligently found neighbours", suggestedNeighbors.Count);
                foreach (var suggested in suggestedNeighbors)
                {
                    var score = heuristicRegistry.ScoreCandidate(suggested);
                    neighbors.Add(new ScoredCandidate(suggested, score));
                }
            }

            var currentIntersection = candidate.Candidate[0].AssessingTests;
            for (var i = 1; i < candidate.Candidate.Count; i++)
            {
                currentIntersection = currentIntersection.Intersect(candidate.Candidate[i].AssessingTests);
            }            

            var fittingCandidates = availableFOMs.Where(fom => !currentIntersection.Intersect(fom.AssessingTests).IsEmpty);

            var fittingCandidatesCount = fittingCandidates.Count();
            _logger.LogTrace("Candidate {Key}: {FittingCount}/{TotalCount} FOMs maintain test intersection",
                GetCandidateKey(candidate.Candidate), fittingCandidatesCount, availableFOMs.Count);

            if(fittingCandidatesCount == 0)
            {
                fittingCandidates = availableFOMs;
            }

            // If we don't have enough suggestions, perform systematic neighborhood exploration
            if (neighbors.Count < 5)
            {
                // Randomly choose exploration strategies to add diversity
                var availableStrategies = new List<Action>();

                // Addition strategy (if not at max order)
                if (candidate.Candidate.Count < _maxOrder)
                {
                    availableStrategies.Add(() => neighbors.AddRange(GenerateAdditionNeighbors(candidate, fittingCandidates, heuristicRegistry)));
                }

                // Removal strategy (if we have more than 2 mutants to maintain HOM validity)
                if (candidate.Candidate.Count > 2)
                {
                    availableStrategies.Add(() => neighbors.AddRange(GenerateRemovalNeighbors(candidate, heuristicRegistry)));
                }

                // Swap strategy (always available if there are FOMs to swap with)
                if (availableFOMs.Any(fom => !candidate.Candidate.Any(c => c.Id == fom.Id)))
                {
                    availableStrategies.Add(() => neighbors.AddRange(GenerateSwapNeighbors(candidate, fittingCandidates, heuristicRegistry)));
                }

                // Randomize strategy execution order for diversity
                var shuffledStrategies = availableStrategies.OrderBy(x => _random.Next()).ToList();

                // Execute strategies until we have enough neighbors or exhaust all strategies

                //get a random strategy from the availableStategies list and execute it
                foreach (var strategy in shuffledStrategies)
                {
                    strategy();
                    if (neighbors.Count >= 15)
                    {
                        break;
                    }
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
            IEnumerable<IMutant> availableFOMs,
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
            IEnumerable<IMutant> availableFOMs,
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
                return [.. items];
            }
            
            var result = new List<T>();
            var indices = new HashSet<int>();
            
            while (result.Count < count && indices.Count < items.Count)
            {
                int index = _random.Next(items.Count);
                if (indices.Add(index))
                {
                    result.Add(items[index]);
                }
            }
            
            return result;
        }

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
}
