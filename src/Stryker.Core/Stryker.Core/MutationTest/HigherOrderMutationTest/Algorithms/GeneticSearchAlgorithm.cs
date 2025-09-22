using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.Utilities.Logging;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms
{
    /// <summary>
    /// Implements a genetic algorithm approach for finding Higher-Order Mutants (HOMs).
    /// Uses evolutionary principles of selection, crossover, and mutation to evolve
    /// a population of candidate HOMs toward potentially being SSHOMs.
    /// </summary>
    public class GeneticSearchAlgorithm : IHOMSearchAlgorithm
    {
        public string Name => "GeneticSearch";

        /// <summary>
        /// Random number generator for stochastic operations.
        /// </summary>
        private readonly Random _random;
        
        /// <summary>
        /// Contains the scores for candidates based on their key to avoid recalculating them.
        /// </summary>
        private readonly Dictionary<string, double> _candidateScoreCache;
        
        /// <summary>
        /// Cache mapping hash codes to candidate keys for performance optimization.
        /// </summary>
        private readonly Dictionary<int, string> _idToKeyCache;
        
        /// <summary>
        /// The best candidate found during the search process.
        /// </summary>
        private List<IMutant> _bestCandidates;
        
        /// <summary>
        /// Registry for accessing and using heuristics.
        /// </summary>
        private readonly HeuristicRegistry _heuristicRegistry;
        
        /// <summary>
        /// Size of the population to maintain during evolution.
        /// </summary>
        private readonly int _populationSize = 500;
        
        /// <summary>
        /// Maximum number of generations to evolve.
        /// </summary>
        private readonly int _maxGenerations = 80;
        
        /// <summary>
        /// Probability of mutation occurring for each candidate.
        /// </summary>
        private readonly double _mutationRate = 0.25;
        
        /// <summary>
        /// Probability of crossover occurring between parents.
        /// </summary>
        private readonly double _crossoverRate = 0.75;
        
        /// <summary>
        /// Number of elite candidates to carry over to next generation.
        /// </summary>
        private readonly int _eliteCount = 12;
        
        /// <summary>
        /// Size of tournament selection pool.
        /// </summary>
        private readonly int _tournamentSize = 3;
        
        /// <summary>
        /// Maximum number of FOMs in a HOM.
        /// </summary>
        private readonly int _maxOrder = 4;
        
        /// <summary>
        /// Whether to enforce uniqueness in the population.
        /// </summary>
        private readonly bool _enforceUniqueness = true;
        
        /// <summary>
        /// Logger for genetic search operations.
        /// </summary>
        private readonly ILogger<GeneticSearchAlgorithm> _logger;
        
        /// <summary>
        /// Whether to apply early filtering during population processing.
        /// </summary>
        private readonly bool _earlyFiltering = false;

        /// <summary>
        /// Ratio of population generated using heuristic guidance.
        /// </summary>
        private readonly double _heuristicPopulationRatio = 0.3;
        
        /// <summary>
        /// Ratio of population generated using diverse seeding.
        /// </summary>
        private readonly double _diversePopulationRatio = 0.5;
        
        /// <summary>
        /// Ratio of candidates selected for exploitation vs exploration.
        /// </summary>
        private readonly double _exploitationRatio = 0.4;
        
        /// <summary>
        /// Quality threshold ratio for candidate selection.
        /// </summary>
        private readonly double _qualityThresholdRatio = 0.6;
        
        /// <summary>
        /// Minimum number of candidates required for quality threshold calculation.
        /// </summary>
        private readonly int _qualityThresholdMinCandidates = 3;
        
        /// <summary>
        /// Maximum number of suggestions to take per starting pair.
        /// </summary>
        private readonly int _maxSuggestionsPerPair = 4;
        
        /// <summary>
        /// Maximum neighborhood limit for candidate generation.
        /// </summary>
        private readonly int _maxNeighbourhoodLimit = 20;
        
        /// <summary>
        /// Score maintenance threshold for feature-based crossover.
        /// </summary>
        private readonly double _scoreMaintenanceThreshold = 0.5;

        public GeneticSearchAlgorithm(
            MutationTestInput mutationTestInput,
            IEnumerable<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            IReadOnlyCollection<IMutant> availableMutants,
            bool registerAllHeuristics =false,
            bool earlyFiltering = false)
        {
            _random = new Random();
            _candidateScoreCache = new Dictionary<string, double>();
            _idToKeyCache = new Dictionary<int, string>();
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<GeneticSearchAlgorithm>();
            _earlyFiltering = earlyFiltering;
            // Initialize heuristic registry with all available heuristics
            _heuristicRegistry = new HeuristicRegistry(availableMutants, options, mutationTestInput, registerAllHeuristics);

            // For backward compatibility, register the provided legacy heuristic if it's not null
            if (heuristics != null)
            {
                foreach (var heuristic in heuristics)
                {
                    _heuristicRegistry.RegisterHeuristic(heuristic);
                }
            }
        }

        /// <summary>
        /// Generates candidate Higher-Order Mutants (HOMs) using a genetic algorithm approach.
        /// </summary>
        /// <param name="availableFOMs">The pool of FOMs to select from.</param>
        /// <param name="heuristics">Legacy parameter - heuristics are managed through the registry.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="input">Mutation test input for additional context.</param>
        /// <returns>An enumerable of HigherOrderMutant instances representing candidate HOMs.</returns>
        public IEnumerable<HigherOrderMutant> GenerateCandidates(
            IReadOnlyCollection<IMutant> availableFOMs,
            IReadOnlyList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            MutationTestInput input)
        {
            // Register any additional heuristics provided (for backward compatibility)
            RegisterAdditionalHeuristics(heuristics);

            _logger.LogInformation("GeneticSearch: Starting candidate generation with {FOMCount} FOMs | pop={Population} gens={Generations} mut={MutationRate:P0} cross={CrossoverRate:P0} elite={EliteCount}",
                availableFOMs.Count, _populationSize, _maxGenerations, _mutationRate, _crossoverRate, _eliteCount);

            if (!ValidateInputs(availableFOMs))
            {
                yield break;
            }

            var context = InitializeGeneticAlgorithm(availableFOMs);
            if (!ValidateInitialization(context))
            {
                yield break;
            }

            foreach (var candidate in ExecuteGeneticSearch(context, availableFOMs))
            {
                var hom = new HigherOrderMutant(candidate, Name);
                _logger.LogTrace("GeneticSearch: Yielding candidate HOM (order {Order}) with genes [{Genes}]", 
                    hom.Order, string.Join(",", candidate.Select(m => m.Id)));
                yield return hom;
            }

            LogSearchCompletion(context);
        }

        /// <summary>
        /// Registers additional heuristics for backward compatibility.
        /// </summary>
        private void RegisterAdditionalHeuristics(IReadOnlyList<IHOMHeuristic> heuristics)
        {
            if (heuristics?.Any() == true)
            {
                foreach (var heuristic in heuristics)
                {
                    _heuristicRegistry.RegisterHeuristic(heuristic);
                }
                _logger.LogDebug("GeneticSearch: Registered {Count} additional heuristics", heuristics.Count);
            }
        }

        /// <summary>
        /// Validates the input parameters for the genetic search.
        /// </summary>
        private bool ValidateInputs(IReadOnlyCollection<IMutant> availableFOMs)
        {
            if (availableFOMs.Count < 2)
            {
                _logger.LogInformation("GeneticSearch: Not enough FOMs (need >=2). Aborting.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Validates the initialization was successful.
        /// </summary>
        private bool ValidateInitialization(GeneticSearchContext context)
        {
            _logger.LogInformation("GeneticSearch: Initial population created with {PopSize} candidates (distinct keys {KeyCount}).", 
                context.Population.Count, context.AllCandidateKeys.Count);
            
            if (context.Population.Count == 0)
            {
                _logger.LogWarning("GeneticSearch: Initial population is empty after filtering. No candidates to evolve.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Logs the completion of the genetic search.
        /// </summary>
        private void LogSearchCompletion(GeneticSearchContext context)
        {
            var bestScore = _bestCandidates == null ? 0 : _candidateScoreCache.GetValueOrDefault(CalculateCandidateKey(_bestCandidates));
            _logger.LogInformation("GeneticSearch: Finished evolutionary loop. Total distinct candidates explored: {TotalDistinct} | Best score: {BestScore:F4}",
                context.AllCandidateKeys.Count, bestScore);
        }

        /// <summary>
        /// Context object to encapsulate genetic search state.
        /// </summary>
        private class GeneticSearchContext
        {
            public List<IMutant> FomList { get; set; }
            public List<List<IMutant>> Population { get; set; }
            public HashSet<string> AllCandidateKeys { get; set; }
            public HashSet<string> YieldedCandidateKeys { get; set; }
        }

        /// <summary>
        /// Executes the main genetic search algorithm.
        /// </summary>
        private IEnumerable<List<IMutant>> ExecuteGeneticSearch(
            GeneticSearchContext context, 
            IReadOnlyCollection<IMutant> availableFOMs)
        {
            var allEliteCandidates = new List<ScoredEliteCandidate>();
            var currentPopulation = context.Population;

            for (var generation = 0; generation < _maxGenerations; generation++)
            {
                if (!ProcessGeneration(ref currentPopulation, context.AllCandidateKeys, allEliteCandidates, 
                    context.FomList, availableFOMs, generation))
                {
                    break;
                }
            }

            return SelectGloballyBestElites(allEliteCandidates, context.YieldedCandidateKeys);
        }

        /// <summary>
        /// Processes a single generation of the genetic algorithm.
        /// </summary>
        private bool ProcessGeneration(
            ref List<List<IMutant>> currentPopulation,
            HashSet<string> allCandidateKeys,
            List<ScoredEliteCandidate> allEliteCandidates,
            List<IMutant> fomList,
            IReadOnlyCollection<IMutant> availableFOMs,
            int generation)
        {
            if (currentPopulation.Count == 0)
            {
                _logger.LogWarning("GeneticSearch: Population empty at generation {Generation}. Stopping early.", generation);
                return false;
            }

            var (processedPopulation, elites, candidateKeys) = ProcessPopulation(currentPopulation, allCandidateKeys);
            
            if (processedPopulation.Count == 0 || elites.Count == 0)
            {
                _logger.LogWarning("GeneticSearch: Population or elites became empty at generation {Generation}. Stopping early.", generation);
                return false;
            }

            LogGenerationMetrics(generation, processedPopulation, elites, candidateKeys, allCandidateKeys);
            CollectEliteCandidates(elites, candidateKeys, allEliteCandidates, generation);

            currentPopulation = EvolvePopulation(processedPopulation, elites, candidateKeys, fomList, 
                allCandidateKeys, availableFOMs, _enforceUniqueness, generation);

            if (_enforceUniqueness && currentPopulation.Count < _populationSize)
            {
                BackfillPopulation(currentPopulation, processedPopulation, candidateKeys);
            }

            return currentPopulation.Count > 0;
        }

        /// <summary>
        /// Logs metrics for the current generation.
        /// </summary>
        private void LogGenerationMetrics(
            int generation,
            List<List<IMutant>> population,
            List<List<IMutant>> elites,
            Dictionary<List<IMutant>, string> candidateKeys,
            HashSet<string> allCandidateKeys)
        {
            var eliteScores = elites.Select(e => _candidateScoreCache[candidateKeys[e]]).ToList();
            var bestScore = eliteScores.FirstOrDefault();
            var avgScore = eliteScores.Count > 0 ? eliteScores.Average() : 0;
            
            UpdateBestCandidate(elites.FirstOrDefault(), bestScore);

            _logger.LogDebug("GeneticSearch: Generation {Generation} | pop={Pop} elites={EliteCount} best={Best:F4} avgElite={Avg:F4} distinctKeys={Distinct}",
                generation, population.Count, elites.Count, bestScore, avgScore, allCandidateKeys.Count);
        }

        /// <summary>
        /// Updates the best candidate if a better one is found.
        /// </summary>
        private void UpdateBestCandidate(List<IMutant> candidate, double score)
        {
            if (candidate == null) return;
            
            if (_bestCandidates == null || score > _candidateScoreCache.GetValueOrDefault(CalculateCandidateKey(_bestCandidates)))
            {
                _bestCandidates = candidate;
            }
        }

        /// <summary>
        /// Collects elite candidates for deferred selection.
        /// </summary>
        private void CollectEliteCandidates(
            List<List<IMutant>> elites,
            Dictionary<List<IMutant>, string> candidateKeys,
            List<ScoredEliteCandidate> allEliteCandidates,
            int generation)
        {
            foreach (var elite in elites)
            {
                var score = _candidateScoreCache[candidateKeys[elite]];
                allEliteCandidates.Add(new ScoredEliteCandidate(elite, score, generation));
            }
        }

        /// <summary>
        /// Selects globally best elite candidates using sophisticated ranking and diversity control.
        /// OPTIMIZED: Dramatically increased target count and more lenient thresholds to generate many more HOMs
        /// </summary>
        private List<List<IMutant>> SelectGloballyBestElites(
            List<ScoredEliteCandidate> allEliteCandidates, 
            HashSet<string> yieldedCandidateKeys)
        {
            if (allEliteCandidates.Count == 0)
            {
                return new List<List<IMutant>>();
            }

            var selectedCandidates = new List<List<IMutant>>();
            // MAJOR IMPROVEMENT: Remove the /2 division that severely limited output
            var baseTargetCount = _eliteCount * _maxGenerations;
            var targetCount = Math.Min(baseTargetCount, allEliteCandidates.Count);
            
            // Scale up target based on diversity - more diverse = more candidates
            var diversityBonus = Math.Min(allEliteCandidates.Count / 10, baseTargetCount / 2);
            targetCount = Math.Min(targetCount + diversityBonus, allEliteCandidates.Count);

            // Remove duplicates by key
            var uniqueCandidates = allEliteCandidates
                .GroupBy(c => CalculateCandidateKey(c.Candidate))
                .Select(g => g.OrderByDescending(c => c.Score).First()) // Best score for each unique candidate
                .OrderByDescending(c => c.Score)
                .ToList();

            _logger.LogInformation("GeneticSearch: Deferred selection from {Total} elites → {Unique} unique candidates → targeting {Target} (was limited to {Original})",
                allEliteCandidates.Count, uniqueCandidates.Count, targetCount, baseTargetCount / 2);

            // Much more lenient quality threshold
            var qualityThreshold = uniqueCandidates.Count > _qualityThresholdMinCandidates 
                ? uniqueCandidates[Math.Min(29, uniqueCandidates.Count - 1)].Score * _qualityThresholdRatio // Use 30th percentile instead of 10th
                : 0.3; // Lower fallback threshold

            var qualityCandidates = uniqueCandidates.Where(c => c.Score >= qualityThreshold).ToList();
            
            // More lenient fallback - take more candidates
            if (qualityCandidates.Count == 0)
            {
                qualityCandidates = uniqueCandidates.Take(Math.Min(targetCount * 2, uniqueCandidates.Count)).ToList();
            }

            // Phase 1: Exploitation (best scores) - reduced ratio for more exploration
            var exploitationCount = (int)(targetCount * _exploitationRatio);
            var topCandidates = qualityCandidates.Take(exploitationCount);
            
            foreach (var candidate in topCandidates)
            {
                var candidateKey = CalculateCandidateKey(candidate.Candidate);
                if (yieldedCandidateKeys.Add(candidateKey))
                {
                    selectedCandidates.Add(candidate.Candidate);
                }
            }

            // Phase 2: Exploration (diversity) - much more aggressive
            var explorationCount = targetCount - selectedCandidates.Count;
            if (explorationCount > 0)
            {
                var remainingCandidates = qualityCandidates.Skip(exploitationCount).ToList();
                var diverseCandidates = SelectDiverseElites(remainingCandidates, yieldedCandidateKeys, explorationCount);
                selectedCandidates.AddRange(diverseCandidates);
            }

            // Phase 3: Final push - if we still have room, be even more lenient
            var finalPushCount = Math.Min(targetCount - selectedCandidates.Count, uniqueCandidates.Count - selectedCandidates.Count);
            if (finalPushCount > 0)
            {
                var remainingUnique = uniqueCandidates
                    .Where(c => !yieldedCandidateKeys.Contains(CalculateCandidateKey(c.Candidate)))
                    .Take(finalPushCount);
                    
                foreach (var candidate in remainingUnique)
                {
                    var candidateKey = CalculateCandidateKey(candidate.Candidate);
                    if (yieldedCandidateKeys.Add(candidateKey))
                    {
                        selectedCandidates.Add(candidate.Candidate);
                    }
                }
            }

            _logger.LogInformation("GeneticSearch: Selected {Selected} candidates via deferred yield (exploitation: {Exploitation}, exploration: {Exploration}, final: {Final})",
                selectedCandidates.Count, exploitationCount, selectedCandidates.Count - exploitationCount - finalPushCount, finalPushCount);

            return selectedCandidates;
        }

        /// <summary>
        /// Selects diverse elite candidates to avoid similar HOMs in final output.
        /// OPTIMIZED: More lenient diversity criteria to accept more candidates
        /// </summary>
        private List<List<IMutant>> SelectDiverseElites(
            List<ScoredEliteCandidate> candidates,
            HashSet<string> yieldedCandidateKeys,
            int maxCount)
        {
            var selected = new List<List<IMutant>>();
            var selectedMutantIds = new HashSet<int>();

            foreach (var candidate in candidates.OrderByDescending(c => c.Score))
            {
                if (selected.Count >= maxCount) break;

                var candidateKey = CalculateCandidateKey(candidate.Candidate);
                if (yieldedCandidateKeys.Contains(candidateKey)) continue;

                // More lenient diversity check: prefer candidates with different constituent mutants
                var candidateMutantIds = candidate.Candidate.Select(m => m.Id).ToHashSet();
                var overlap = candidateMutantIds.Intersect(selectedMutantIds).Count();
                var overlapRatio = candidateMutantIds.Count > 0 ? (double)overlap / candidateMutantIds.Count : 1.0;

                // Accept if overlap is not too high OR we need to fill remaining slots - much more lenient acceptance
                if (overlapRatio < 0.7 || selected.Count < maxCount * 0.8) // Increased from 0.5 and 0.5
                {
                    yieldedCandidateKeys.Add(candidateKey);
                    selected.Add(candidate.Candidate);
                    foreach (var mutantId in candidateMutantIds)
                    {
                        selectedMutantIds.Add(mutantId);
                    }
                }
            }

            return selected;
        }

        /// <summary>
        /// Represents a scored elite candidate with generation context.
        /// </summary>
        private class ScoredEliteCandidate
        {
            public List<IMutant> Candidate { get; }
            public double Score { get; }
            public int Generation { get; }

            public ScoredEliteCandidate(List<IMutant> candidate, double score, int generation)
            {
                Candidate = candidate;
                Score = score;
                Generation = generation;
            }
        }

        private (List<List<IMutant>> Population, List<List<IMutant>> Elites, Dictionary<List<IMutant>, string> CandidateKeys) ProcessPopulation(
            List<List<IMutant>> population,
            HashSet<string> allCandidateKeys)
        {
            var before = population.Count;
            var candidateKeyDict = ProcessPopulationCandidates(population, allCandidateKeys);
            
            // Safety check: if all candidates were filtered out, this is a problem
            if (population.Count == 0)
            {
                _logger.LogError("GeneticSearch: All {Before} candidates were filtered out during population processing. This indicates overly restrictive filtering.", before);
                return (population, new List<List<IMutant>>(), candidateKeyDict);
            }
            
            population.Sort((a, b) => _candidateScoreCache[candidateKeyDict[b]]
                .CompareTo(_candidateScoreCache[candidateKeyDict[a]]));
            var elites = population.Take(_eliteCount).ToList();
            var removed = before - population.Count;
            if (removed > 0)
            {
                _logger.LogTrace("GeneticSearch: Filtered {Removed} candidates this generation (remaining {Remaining}).", removed, population.Count);
            }
            return (population, elites, candidateKeyDict);
        }

        private void BackfillPopulation(
            List<List<IMutant>> nextPopulation,
            List<List<IMutant>> currentPopulation,
            Dictionary<List<IMutant>, string> candidateKeys)
        {
            if (nextPopulation.Count < _populationSize)
            {
                foreach (var candidate in currentPopulation.OrderByDescending(c => _candidateScoreCache[candidateKeys[c]]))
                {
                    if (nextPopulation.Count >= _populationSize)
                    {
                        break;
                    }
                    nextPopulation.Add(candidate);
                }
                _logger.LogTrace("GeneticSearch: Backfilled population to {Count} (target {Target}).", nextPopulation.Count, _populationSize);
            }
        }

        private GeneticSearchContext InitializeGeneticAlgorithm(IReadOnlyCollection<IMutant> availableFOMs)
        {
            _candidateScoreCache.Clear();
            _idToKeyCache.Clear();
            
            var fomList = availableFOMs.ToList();
            var population = InitializePopulation(fomList, _populationSize);
            var allCandidates = new HashSet<string>(StringComparer.Ordinal);
            var yieldedCandidates = new HashSet<string>(StringComparer.Ordinal);
            
            // Pre-score all initial candidates
            foreach (var candidate in population)
            {
                var key = CalculateCandidateKey(candidate);
                _candidateScoreCache[key] = EvaluateCandidate(candidate);
                allCandidates.Add(key);
            }
            
            return new GeneticSearchContext
            {
                FomList = fomList,
                Population = population,
                AllCandidateKeys = allCandidates,
                YieldedCandidateKeys = yieldedCandidates
            };
        }

        private List<List<IMutant>> InitializePopulation(List<IMutant> foms, int populationSize)
        {
            var population = new List<List<IMutant>>(populationSize);
            var uniqueKeys = new HashSet<string>();
            var retries = 0;
            var maxRetries = populationSize * 50;
            
            // Strategy 1: Heuristic-guided candidates (most intelligent approach)
            var heuristicCount = (int)(populationSize * _heuristicPopulationRatio);
            var heuristicCandidates = GenerateHeuristicGuidedCandidates(foms, heuristicCount);
            foreach (var candidate in heuristicCandidates)
            {
                var candidateKey = CalculateCandidateKey(candidate);
                if (uniqueKeys.Add(candidateKey) && (!_earlyFiltering || !_heuristicRegistry.ShouldFilterCandidate(candidate)))
                {
                    population.Add(candidate);
                }
                if (population.Count >= populationSize) break;
            }
            
            // Strategy 2: Diverse seeded initialization (simple diversity)
            var seedCount = (int)(populationSize * _diversePopulationRatio);
            var diverseSeeds = GenerateDiverseSeeds(foms, seedCount, uniqueKeys);
            population.AddRange(diverseSeeds);
            
            // Strategy 3: Random with compatibility enforcement (fallback)
            while (population.Count < populationSize && retries < maxRetries)
            {
                retries++;
                var candidate = GenerateCompatibleCandidate(foms);
                
                if (candidate.Count >= 2)
                {
                    var candidateKey = CalculateCandidateKey(candidate);
                    if (uniqueKeys.Add(candidateKey))
                    {
                        if (!_earlyFiltering || !_heuristicRegistry.ShouldFilterCandidate(candidate))
                        {
                            population.Add(candidate);
                        }
                    }
                }
                
                if (retries % 500 == 0)
                {
                    _logger.LogTrace("GeneticSearch: {RetryCount} retries, population at {Pop}, unique keys: {Unique}", 
                        retries, population.Count, uniqueKeys.Count);
                }
            }
            
            _logger.LogInformation("GeneticSearch: Initial population: {PopSize} candidates, {UniqueKeys} unique keys after {Retries} retries (Heuristic: {HeuristicCount}, Diverse: {DiverseCount}, Random: {RandomCount})", 
                population.Count, uniqueKeys.Count, retries, 
                Math.Min(heuristicCandidates.Count, heuristicCount),
                diverseSeeds.Count,
                population.Count - Math.Min(heuristicCandidates.Count, heuristicCount) - diverseSeeds.Count);
            return population;
        }

        /// <summary>
        /// Generate diverse initial seeds for the population to enhance genetic diversity
        /// Uses simple diversity strategies without duplicating heuristic-guided generation
        /// </summary>
        private List<List<IMutant>> GenerateDiverseSeeds(List<IMutant> foms, int targetCount, HashSet<string> uniqueKeys)
        {
            var seeds = new List<List<IMutant>>();
            
            // Strategy 1: Random diverse pairs with test coverage preference
            var fomsWithTests = foms.Where(f => f.AssessingTests != null && !f.AssessingTests.IsEmpty).ToList();
            if (fomsWithTests.Count < 2) fomsWithTests = foms; // Fallback
            
            var usedMutants = new HashSet<int>();
            var pairsGenerated = 0;
            var maxPairs = targetCount / 2;
            
            // Create pairs from FOMs with test coverage, ensuring no reuse
            for (var i = 0; i < fomsWithTests.Count && pairsGenerated < maxPairs && usedMutants.Count < fomsWithTests.Count - 1; i++)
            {
                var fom1 = fomsWithTests[i];
                if (usedMutants.Contains(fom1.Id)) continue;
                
                // Find a compatible FOM that hasn't been used
                for (var j = i + 1; j < fomsWithTests.Count; j++)
                {
                    var fom2 = fomsWithTests[j];
                    if (usedMutants.Contains(fom2.Id)) continue;
                    
                    var candidate = new List<IMutant> { fom1, fom2 };
                    var key = CalculateCandidateKey(candidate);
                    
                    if (uniqueKeys.Add(key))
                    {
                        seeds.Add(candidate);
                        usedMutants.Add(fom1.Id);
                        usedMutants.Add(fom2.Id);
                        pairsGenerated++;
                        break;
                    }
                }
            }
            
            // Strategy 2: Random diverse pairs from remaining FOMs
            var remainingSlots = targetCount - seeds.Count;
            if (remainingSlots > 0)
            {
                seeds.AddRange(GenerateRandomDiversePairs(foms, remainingSlots, uniqueKeys));
            }
            
            return seeds;
        }

        /// <summary>
        /// Generate random diverse pairs as fallback when heuristic guidance is insufficient
        /// </summary>
        private List<List<IMutant>> GenerateRandomDiversePairs(List<IMutant> foms, int targetCount, HashSet<string> uniqueKeys)
        {
            var seeds = new List<List<IMutant>>();
            var usedMutants = new HashSet<int>();
            
            for (var i = 0; i < targetCount && usedMutants.Count < foms.Count - 1; i++)
            {
                var availableFoms = foms.Where(f => !usedMutants.Contains(f.Id)).ToList();
                if (availableFoms.Count < 2) break;
                
                var fom1 = availableFoms[_random.Next(availableFoms.Count)];
                usedMutants.Add(fom1.Id);
                
                var fom2 = availableFoms.Where(f => f.Id != fom1.Id).OrderBy(_ => _random.Next()).First();
                usedMutants.Add(fom2.Id);
                
                var candidate = new List<IMutant> { fom1, fom2 };
                var key = CalculateCandidateKey(candidate);
                if (uniqueKeys.Add(key))
                {
                    seeds.Add(candidate);
                }
            }
            
            return seeds;
        }

        /// <summary>
        /// Generate candidates using heuristic guidance rather than pure randomness
        /// </summary>
        private List<List<IMutant>> GenerateHeuristicGuidedCandidates(List<IMutant> foms, int targetCount)
        {
            var candidates = new List<List<IMutant>>();
            
            // First, try to get suggestions from search guidance heuristics if available
            var searchGuidanceHeuristics = _heuristicRegistry.GetSearchGuidanceHeuristics();
            if (searchGuidanceHeuristics?.Any() == true)
            {
                // Start with basic pairs and let heuristics suggest expansions
                var startingPairs = CreateBasicStartingPairs(foms, Math.Min(targetCount / 4, _maxNeighbourhoodLimit));
                
                foreach (var startingPair in startingPairs)
                {
                    var suggestions = _heuristicRegistry.SuggestNextCandidates(startingPair, foms);
                    candidates.AddRange(suggestions.Take(_maxSuggestionsPerPair));
                    
                    if (candidates.Count >= targetCount)
                    {
                        break;
                    }
                }
            }
            
            // If we don't have enough candidates from heuristic guidance, create scored pairs
            if (candidates.Count < targetCount)
            {
                var scoredPairs = CreateScoredCandidatePairs(foms, targetCount - candidates.Count);
                candidates.AddRange(scoredPairs);
            }
            
            return candidates.Take(targetCount).ToList();
        }

        /// <summary>
        /// Create basic starting pairs for heuristic expansion
        /// </summary>
        private List<List<IMutant>> CreateBasicStartingPairs(List<IMutant> foms, int pairCount)
        {
            var pairs = new List<List<IMutant>>();
            var fomsWithTests = foms.Where(f => f.AssessingTests != null && !f.AssessingTests.IsEmpty).ToList();
            
            if (fomsWithTests.Count < 2)
            {
                fomsWithTests = foms; // Fallback to all FOMs
            }
            
            for (var i = 0; i < Math.Min(pairCount, fomsWithTests.Count) && i < fomsWithTests.Count - 1; i++)
            {
                var fom1 = fomsWithTests[i];
                var fom2 = fomsWithTests[i + 1];
                pairs.Add(new List<IMutant> { fom1, fom2 });
            }
            
            return pairs;
        }

        /// <summary>
        /// Create candidate pairs based on heuristic scoring
        /// OPTIMIZED: Evaluate more pairs and be less restrictive in filtering
        /// </summary>
        private List<List<IMutant>> CreateScoredCandidatePairs(List<IMutant> foms, int targetCount)
        {
            var fomsWithTests = foms.Where(f => f.AssessingTests != null && !f.AssessingTests.IsEmpty).ToList();
            if (fomsWithTests.Count < 2)
            {
                fomsWithTests = foms; // Fallback
            }
            
            // Create pairs and score them using the heuristic registry
            var scoredPairs = new List<(List<IMutant> candidate, double score)>();
            var maxPairsToEvaluate = targetCount * 5; // Increased from 3 to evaluate more pairs
            
            for (var i = 0; i < fomsWithTests.Count && scoredPairs.Count < maxPairsToEvaluate; i++)
            {
                var maxJForI = Math.Min(fomsWithTests.Count, i + _maxNeighbourhoodLimit);
                for (var j = i + 1; j < maxJForI && scoredPairs.Count < maxPairsToEvaluate; j++)
                {
                    var candidate = new List<IMutant> { fomsWithTests[i], fomsWithTests[j] };
                    
                    // Only skip if heuristics strongly filter this candidate
                    if (_heuristicRegistry.ShouldFilterCandidate(candidate))
                    {
                        continue;
                    }
                    
                    var score = _heuristicRegistry.ScoreCandidate(candidate);
                    scoredPairs.Add((candidate, score));
                }
            }
            
            // Return more pairs - take top scoring ones but be less restrictive
            return scoredPairs
                .OrderByDescending(p => p.score)
                .Take(targetCount)
                .Select(p => p.candidate)
                .ToList();
        }

        /// <summary>
        /// Generate a candidate using heuristic guidance for compatibility assessment
        /// </summary>
        private List<IMutant> GenerateCompatibleCandidate(List<IMutant> foms)
        {
            var candidateSize = _random.Next(2, Math.Min(_maxOrder + 1, foms.Count + 1));
            
            // Try heuristic-guided generation first
            if (foms.Count >= 2)
            {
                var startingPair = new List<IMutant> { foms[_random.Next(foms.Count)], foms[_random.Next(foms.Count)] };
                
                // Let heuristics suggest expansions if we need more mutants
                if (candidateSize > 2)
                {
                    var suggestions = _heuristicRegistry.SuggestNextCandidates(startingPair, foms);
                    var bestSuggestion = suggestions
                        .Where(s => s.Count == candidateSize)
                        .OrderByDescending(s => _heuristicRegistry.ScoreCandidate(s))
                        .FirstOrDefault();
                    
                    if (bestSuggestion != null)
                    {
                        return bestSuggestion;
                    }
                }
                
                // Score the starting pair using heuristics
                if (!_heuristicRegistry.ShouldFilterCandidate(startingPair))
                {
                    return startingPair;
                }
            }
            
            // Fallback to original logic if heuristic guidance fails
            return GenerateRandomCompatibleCandidate(foms, candidateSize);
        }

        /// <summary>
        /// Fallback method for generating candidates when heuristic guidance is insufficient
        /// </summary>
        private List<IMutant> GenerateRandomCompatibleCandidate(List<IMutant> foms, int candidateSize)
        {
            var candidate = new List<IMutant>();
            
            // Start with a random FOM that has test coverage
            var fomsWithTests = foms.Where(f => f.AssessingTests != null && !f.AssessingTests.IsEmpty).ToList();
            if (fomsWithTests.Count == 0) fomsWithTests = foms; // fallback
            
            var seedFom = fomsWithTests[_random.Next(fomsWithTests.Count)];
            candidate.Add(seedFom);
            
            // Add compatible FOMs preferentially
            var availableFoms = foms.Where(f => f != seedFom).ToList();
            
            for (var j = 1; j < candidateSize && availableFoms.Count > 0; j++)
            {
                // Prefer FOMs with overlapping tests
                var compatibleFoms = availableFoms
                    .Where(f => f.AssessingTests != null && candidate[0].AssessingTests != null && 
                               f.AssessingTests.ContainsAny(candidate[0].AssessingTests))
                    .ToList();
                
                List<IMutant> selectionPool = compatibleFoms.Count > 0 ? compatibleFoms : availableFoms;
                var selectedIndex = _random.Next(selectionPool.Count);
                var selectedFom = selectionPool[selectedIndex];
                
                candidate.Add(selectedFom);
                availableFoms.Remove(selectedFom);
            }
            
            return candidate;
        }

        /// <summary>
        /// Calculates adaptive mutation rate based on population diversity
        /// </summary>
        private double CalculateAdaptiveMutationRate(HashSet<string> allCandidateKeys, int generation)
        {
            var baseMutationRate = _mutationRate;
            
            // Calculate diversity metric (unique candidates vs total processed)
            var diversityRatio = allCandidateKeys.Count / (double)(_populationSize * (generation + 1));
            
            // Increase mutation rate when diversity is low
            if (diversityRatio < 0.3) // Less than 30% unique
            {
                return Math.Min(baseMutationRate * 2.0, 0.5); // Double rate, cap at 50%
            }
            else if (diversityRatio < 0.5) // Less than 50% unique
            {
                return baseMutationRate * 1.5;
            }
            
            return baseMutationRate;
        }

        private List<List<IMutant>> EvolvePopulation(
            List<List<IMutant>> population,
            List<List<IMutant>> elites,
            Dictionary<List<IMutant>, string> candidateKeys,
            List<IMutant> fomList,
            HashSet<string> allCandidates,
            IReadOnlyCollection<IMutant> availableFOMs = null,
            bool enforceUniqueness = true,
            int generation = 0)
        {
            var nextPopulation = new List<List<IMutant>>();
            nextPopulation.AddRange(elites);
            var suggestionCount = 0;
            
            // Calculate adaptive mutation rate based on diversity
            var adaptiveMutationRate = CalculateAdaptiveMutationRate(allCandidates, generation);
            
            if (availableFOMs != null && population.Count > 0)
            {
                var suggestedCandidates = new List<List<IMutant>>();
                foreach (var candidate in population)
                {
                    var suggestions = _heuristicRegistry.SuggestNextCandidates(candidate, availableFOMs);
                    foreach (var suggestion in suggestions)
                    {
                        if (_earlyFiltering && _heuristicRegistry.ShouldFilterCandidate(suggestion))
                        {
                            continue;
                        }

                        var suggestionKey = CalculateCandidateKey(suggestion);
                        if (!enforceUniqueness || allCandidates == null || allCandidates.Add(suggestionKey))
                        {
                            suggestedCandidates.Add(suggestion);
                            _candidateScoreCache[suggestionKey] = EvaluateCandidate(suggestion);
                        }
                    }
                }
                if (suggestedCandidates.Count > 0)
                {
                    var limitedSuggestions = suggestedCandidates.OrderBy(_ => _random.Next()).Take(Math.Min(5, suggestedCandidates.Count));
                    var list = limitedSuggestions.ToList();
                    suggestionCount = list.Count;
                    nextPopulation.AddRange(list);
                }
            }
            var attempts = 0;
            var maxAttempts = enforceUniqueness ? _populationSize * 10 : _populationSize;
            while (nextPopulation.Count < _populationSize && attempts < maxAttempts)
            {
                attempts++;
                if (population.Count == 0) break; // safeguard
                var parent1 = TournamentSelection(population, candidateKeys);
                var parent2 = TournamentSelection(population, candidateKeys);
                List<IMutant> offspring;
                if (_random.NextDouble() < _crossoverRate)
                {
                    offspring = Crossover(parent1, parent2);
                }
                else
                {
                    offspring = new List<IMutant>(parent1);
                }
                if (_random.NextDouble() < adaptiveMutationRate) // Use adaptive rate
                {
                    Mutate(offspring, fomList);
                }
                if (_earlyFiltering && _heuristicRegistry.ShouldFilterCandidate(offspring))
                {
                    continue;
                }
                var offspringKey = CalculateCandidateKey(offspring);
                _candidateScoreCache[offspringKey] = EvaluateCandidate(offspring);
                if (!enforceUniqueness || allCandidates.Add(offspringKey))
                {
                    nextPopulation.Add(offspring);
                }
            }
            if (nextPopulation.Count < _populationSize)
            {
                _logger.LogTrace("GeneticSearch: Evolution ended with population {Current}/{Target} (attempts={Attempts}, suggestions={Suggestions}, mutRate={MutRate:P1}).", 
                    nextPopulation.Count, _populationSize, attempts, suggestionCount, adaptiveMutationRate);
            }
            return nextPopulation;
        }

        protected double EvaluateCandidate(List<IMutant> candidate) => _heuristicRegistry.ScoreCandidate(candidate);

        private List<IMutant> TournamentSelection(
            List<List<IMutant>> population,
            Dictionary<List<IMutant>, string> candidateKeys)
        {
            if (population.Count == 0)
            {
                _logger.LogWarning("GeneticSearch: Tournament selection requested with empty population.");
                return new List<IMutant>();
            }
            
            var tournament = new List<List<IMutant>>();
            var tournamentSize = Math.Min(_tournamentSize, population.Count);
            
            // Diversity-aware tournament selection
            var selectedIndices = new HashSet<int>();
            var maxAttempts = tournamentSize * 3; // Allow some retries to find diverse candidates
            var attempts = 0;
            
            while (tournament.Count < tournamentSize && attempts < maxAttempts)
            {
                var randomIndex = _random.Next(population.Count);
                attempts++;
                
                // Accept if not already selected, or if we're running out of attempts
                if (selectedIndices.Add(randomIndex) || attempts > maxAttempts - tournamentSize)
                {
                    tournament.Add(population[randomIndex]);
                }
            }
            
            // Select winner based on fitness, but with diversity bonus
            return tournament.OrderByDescending(c =>
            {
                var key = candidateKeys[c];
                if (!_candidateScoreCache.ContainsKey(key))
                {
                    _candidateScoreCache[key] = EvaluateCandidate(c);
                }
                
                var fitness = _candidateScoreCache[key];
                
                // Apply diversity bonus: penalize candidates that are too similar to others in tournament
                var diversityBonus = CalculateDiversityBonus(c, tournament, candidateKeys);
                
                return fitness + diversityBonus;
            }).First();
        }

        /// <summary>
        /// Calculates a diversity bonus to encourage selection of unique candidates.
        /// </summary>
        private double CalculateDiversityBonus(List<IMutant> candidate, List<List<IMutant>> tournament, Dictionary<List<IMutant>, string> candidateKeys)
        {
            var candidateKey = candidateKeys[candidate];
            var candidateMutantIds = candidate.Select(m => m.Id).ToHashSet();
            
            double totalSimilarity = 0.0;
            int comparisons = 0;
            
            foreach (var other in tournament)
            {
                if (ReferenceEquals(candidate, other)) continue;
                
                var otherMutantIds = other.Select(m => m.Id).ToHashSet();
                var intersection = candidateMutantIds.Intersect(otherMutantIds).Count();
                var union = candidateMutantIds.Union(otherMutantIds).Count();
                
                // Jaccard similarity: intersection / union
                var similarity = union > 0 ? (double)intersection / union : 0.0;
                totalSimilarity += similarity;
                comparisons++;
            }
            
            var avgSimilarity = comparisons > 0 ? totalSimilarity / comparisons : 0.0;
            
            // Higher diversity bonus for more unique candidates (lower similarity)
            return (1.0 - avgSimilarity) * 0.1; // 10% bonus for completely unique candidates
        }

        private List<IMutant> Crossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            if (parent1 == null || parent2 == null)
            {
                throw new ArgumentNullException(parent1 == null ? nameof(parent1) : nameof(parent2));
            }
            
            // Use diverse crossover strategies
            var crossoverStrategy = _random.Next(3);
            List<IMutant> child;
            
            switch (crossoverStrategy)
            {
                case 0: // Single-point crossover (existing)
                    child = SinglePointCrossover(parent1, parent2);
                    break;
                case 1: // Uniform crossover
                    child = UniformCrossover(parent1, parent2);
                    break;
                case 2: // Feature-based crossover
                    child = FeatureBasedCrossover(parent1, parent2);
                    break;
                default:
                    child = SinglePointCrossover(parent1, parent2);
                    break;
            }
            
            // Ensure diversity by avoiding too much similarity to parents
            var parent1Key = CalculateCandidateKey(parent1);
            var parent2Key = CalculateCandidateKey(parent2);
            var childKey = CalculateCandidateKey(child);
            
            if (childKey == parent1Key || childKey == parent2Key)
            {
                // Force mutation to create diversity
                Mutate(child, parent1.Concat(parent2).Distinct().ToList());
            }
            
            return child;
        }

        private List<IMutant> SinglePointCrossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            var expectedSize = Math.Min(Math.Max(parent1.Count, parent2.Count), _maxOrder);
            var child = new List<IMutant>(expectedSize);
            var childIds = new HashSet<int>(expectedSize);
            var crossoverPoint = _random.Next(Math.Min(parent1.Count, parent2.Count));
            AddUniqueGenes(child, childIds, parent1.Take(crossoverPoint));
            AddUniqueGenes(child, childIds, parent2.Skip(crossoverPoint));
            if (child.Count < 2)
            {
                EnsureMinimumSize(child, childIds, parent1.Concat(parent2), 2);
            }
            if (child.Count > _maxOrder)
            {
                child.RemoveRange(_maxOrder, child.Count - _maxOrder);
            }
            _logger.LogTrace("GeneticSearch: Crossover produced child size={Size} (p1={P1} p2={P2} point={Point}).", child.Count, parent1.Count, parent2.Count, crossoverPoint);
            return child;
        }

        private List<IMutant> UniformCrossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            var child = new List<IMutant>();
            var childIds = new HashSet<int>();
            var allGenes = parent1.Concat(parent2).ToList();
            
            foreach (var gene in allGenes)
            {
                if (_random.NextDouble() < 0.5 && childIds.Add(gene.Id))
                {
                    child.Add(gene);
                }
                if (child.Count >= _maxOrder) break;
            }
            
            if (child.Count < 2)
            {
                EnsureMinimumSize(child, childIds, allGenes, 2);
            }
            
            return child;
        }

        private List<IMutant> FeatureBasedCrossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            // Use heuristic guidance for feature-based crossover instead of hardcoded mutator type grouping
            var allGenes = parent1.Concat(parent2).ToList();
            
            // Try to get suggestions from heuristics for combining these parents
            var parentCombination = parent1.Concat(parent2).Distinct().ToList();
            var suggestions = _heuristicRegistry.SuggestNextCandidates(parent1, parentCombination);
            
            // If we have heuristic suggestions, use the best scoring one that fits our constraints
            var validSuggestion = suggestions
                .Where(s => s.Count >= 2 && s.Count <= _maxOrder)
                .OrderByDescending(s => _heuristicRegistry.ScoreCandidate(s))
                .FirstOrDefault();
            
            if (validSuggestion != null)
            {
                return validSuggestion;
            }
            
            // Fallback to selecting genes based on heuristic scoring
            var child = new List<IMutant>();
            var childIds = new HashSet<int>();
            
            foreach (var gene in allGenes.OrderBy(_ => _random.Next()))
            {
                if (childIds.Add(gene.Id))
                {
                    var tempChild = new List<IMutant>(child) { gene };
                    
                    // Only add if it improves or maintains the score (using threshold)
                    if (child.Count == 0 || 
                        _heuristicRegistry.ScoreCandidate(tempChild) >= _heuristicRegistry.ScoreCandidate(child) * _scoreMaintenanceThreshold)
                    {
                        child.Add(gene);
                    }
                }
                if (child.Count >= _maxOrder) break;
            }
            
            if (child.Count < 2)
            {
                EnsureMinimumSize(child, childIds, parent1.Concat(parent2), 2);
            }
            
            return child;
        }

        private static string CalculateCandidateKey(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0) return string.Empty;
            var sortedIds = candidate.Select(m => m.Id).OrderBy(id => id).Select(id => id.ToString()).ToList();
            return string.Join("_", sortedIds);
        }

        private void Mutate(List<IMutant> candidate, List<IMutant> availableFOMs)
        {
            if (candidate.Count == 0) return;
            var operation = _random.Next(3);
            var before = candidate.Count;
            var candidateIds = new HashSet<int>(candidate.Select(m => m.Id));
            switch (operation)
            {
                case 0:
                    if (candidate.Count < _maxOrder && availableFOMs.Count > candidateIds.Count)
                    {
                        var availableIds = availableFOMs.Where(m => !candidateIds.Contains(m.Id)).ToList();
                        if (availableIds.Count > 0)
                        {
                            var randomIndex = _random.Next(availableIds.Count);
                            candidate.Add(availableIds[randomIndex]);
                        }
                    }
                    break;
                case 1:
                    if (candidate.Count > 2)
                    {
                        var removeIndex = _random.Next(candidate.Count);
                        candidate.RemoveAt(removeIndex);
                    }
                    break;
                case 2:
                    if (availableFOMs.Count > candidateIds.Count)
                    {
                        var replaceIndex = _random.Next(candidate.Count);
                        var oldId = candidate[replaceIndex].Id;
                        var availableIds = availableFOMs.Where(m => !candidateIds.Contains(m.Id) || m.Id == oldId).ToList();
                        if (availableIds.Count > 0)
                        {
                            var randomIndex = _random.Next(availableIds.Count);
                            candidate[replaceIndex] = availableIds[randomIndex];
                        }
                    }
                    break;
            }
            var key = GetCandidateKey(candidate);
            if (!string.IsNullOrEmpty(key))
            {
                _candidateScoreCache.Remove(key);
            }
            _logger.LogTrace("GeneticSearch: Mutation op={Op} size {Before}->{After}", operation, before, candidate.Count);
        }

        private string GetCandidateKey(List<IMutant> candidate)
        {
            if (candidate == null) return string.Empty;
            var hashCode = candidate.GetHashCode();
            if (_idToKeyCache.TryGetValue(hashCode, out var key)) return key;
            key = CalculateCandidateKey(candidate);
            _idToKeyCache[hashCode] = key;
            return key;
        }

        private Dictionary<List<IMutant>, string> ProcessPopulationCandidates(
            List<List<IMutant>> population,
            HashSet<string> allCandidates)
        {
            var candidateKeys = new Dictionary<List<IMutant>, string>();
            for (var i = population.Count - 1; i >= 0; i--)
            {
                var candidate = population[i];
                if (_earlyFiltering && _heuristicRegistry.ShouldFilterCandidate(candidate))
                {
                    population.RemoveAt(i);
                    continue;
                }
                if (!candidateKeys.ContainsKey(candidate))
                {
                    var key = GetCandidateKey(candidate);
                    candidateKeys[candidate] = key;
                    if (!_candidateScoreCache.ContainsKey(key))
                    {
                        var score = EvaluateCandidate(candidate);
                        _candidateScoreCache[key] = score;
                        allCandidates.Add(key);
                    }
                }
            }
            return candidateKeys;
        }

        private static IEnumerable<List<IMutant>> YieldEliteCandidates(
            List<List<IMutant>> elites,
            Dictionary<List<IMutant>, string> candidateKeys,
            HashSet<string> yieldedCandidates)
        {
            foreach (var candidate in elites)
            {
                var candidateKey = candidateKeys[candidate];
                if (yieldedCandidates.Add(candidateKey))
                {
                    yield return candidate;
                }
            }
        }

        private static void AddUniqueGenes(List<IMutant> child, HashSet<int> childIds, IEnumerable<IMutant> genes)
        {
            foreach (var gene in genes)
            {
                if (childIds.Add(gene.Id))
                {
                    child.Add(gene);
                }
            }
        }

        private static void EnsureMinimumSize(List<IMutant> child, HashSet<int> childIds, IEnumerable<IMutant> potentialGenes, int minimumSize)
        {
            foreach (var gene in potentialGenes)
            {
                if (childIds.Add(gene.Id))
                {
                    child.Add(gene);
                    if (child.Count >= minimumSize) break;
                }
            }
        }

        private class CandidateEqualityComparer : IEqualityComparer<List<IMutant>>
        {
            public bool Equals(List<IMutant> x, List<IMutant> y) => ReferenceEquals(x, y);
            public int GetHashCode(List<IMutant> obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
