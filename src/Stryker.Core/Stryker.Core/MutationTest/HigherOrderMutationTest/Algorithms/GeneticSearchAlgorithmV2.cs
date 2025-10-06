using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.Utilities.Logging;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms
{
    /// <summary>
    /// Genetic search configuration.
    /// </summary>
    public record GeneticSearchOptions(
        int PopulationSize = 500,
        int MaxGenerations = 80,
        int EliteCount = 12,
        double MutationRate = 0.35,
        double CrossoverRate = 0.65,
        int TournamentSize = 2,
        double DiversityWeight = 0.2,
        double ParentSimilarityThreshold = 0.7,
        int ParentReselectionTries = 8,
        double ScoreMaintenanceThreshold = 0.45,
        double ImmigrantMinRatio = 0.05,
        double ImmigrantMaxRatio = 0.10,
        int MaxOrder = 4,
        double FinalSelectionFomFactor = 0.10,
        double HeuristicPopulationRatio = 0.3,
        double DiversePopulationRatio = 0.5,
        int MaxNeighbourhoodLimit = 20,
        int MinFinalCandidates = 20,
        int? RandomSeed = null
    );

    /// <summary>
    /// Implements a genetic algorithm approach for finding Higher-Order Mutants (HOMs).
    /// Uses evolutionary principles of selection, crossover, and mutation to evolve
    /// a population of candidate HOMs toward potentially being SSHOMs.
    /// </summary>
    public class GeneticSearchAlgorithm : IHOMSearchAlgorithm
    {
        public string Name => "GeneticSearch";

        private readonly Random _random;

        private List<IMutant> _bestCandidates;
        private readonly HeuristicRegistry _heuristicRegistry;
        private readonly GeneticSearchOptions _opts;
        private readonly ILogger<GeneticSearchAlgorithm> _logger;
        private readonly bool _earlyFiltering = false;
        private List<ScoredCandidate> _allCandidates;
        // Tracks genotype signatures for everything recorded in _allCandidates
        private HashSet<string> _allCandidateSignatures;
        // Track available FOM count for scaled final selection
        private int _availableFomsCount;

        public GeneticSearchAlgorithm(
            MutationTestInput mutationTestInput,
            IEnumerable<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            IReadOnlyCollection<IMutant> availableMutants,
            bool registerAllHeuristics = false,
            bool earlyFiltering = false,
            GeneticSearchOptions geneticOptions = null)
        {
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<GeneticSearchAlgorithm>();
            _earlyFiltering = earlyFiltering;
            _opts = geneticOptions ?? new GeneticSearchOptions();
            _random = _opts.RandomSeed.HasValue ? new Random(_opts.RandomSeed.Value) : new Random();
            _heuristicRegistry = new HeuristicRegistry(availableMutants, options, mutationTestInput, registerAllHeuristics);

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
        /// Collects all candidates during the search and yields only the best candidates at the end.
        /// </summary>
        public IEnumerable<HigherOrderMutant> GenerateCandidates(
            IReadOnlyCollection<IMutant> availableFOMs,
            IReadOnlyList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            MutationTestInput input)
        {
            // Register any additional heuristics provided (for backward compatibility)
            RegisterAdditionalHeuristics(heuristics);

            // remember FOM count for scaled final selection
            _availableFomsCount = availableFOMs?.Count ?? 0;

            _logger.LogInformation(
                "GeneticSearch: Starting candidate generation with {FOMCount} FOMs | pop={Population} iters={Iterations} mut={MutationRate:P0} cross={CrossoverRate:P0} elite={EliteCount}",
                availableFOMs.Count, _opts.PopulationSize, _opts.MaxGenerations, _opts.MutationRate, _opts.CrossoverRate, _opts.EliteCount);

            if (!ValidateInputs(availableFOMs))
            {
                yield break;
            }

            // 1) Initialize population (list of candidate genomes)
            var population = InitializePopulation([.. availableFOMs], _opts.PopulationSize);

            // Collect elites over time for standardized final selection
            _allCandidates = new List<ScoredCandidate>();
            _allCandidateSignatures = new HashSet<string>(StringComparer.Ordinal);

            // Configurable mutation rate (adaptive)
            var currentMutationRate = _opts.MutationRate;

            // 2) Evolutionary loop
            for (var generation = 0; generation < _opts.MaxGenerations; generation++)
            {
                // Evaluate current population
                var scoredPopulation = ScorePopulation(population);
                if (scoredPopulation.Count == 0)
                {
                    _logger.LogWarning("GeneticSearch: No viable population at generation {Gen}", generation);
                    break;
                }

                // Sort by fitness
                scoredPopulation.Sort((a, b) => b.Score.CompareTo(a.Score));

                // Track best for logging
                var bestThisGen = scoredPopulation.First();
                UpdateBestCandidate(bestThisGen.Candidate, bestThisGen.Score);

                // Elite selection with genotype de-duplication
                var elites = SelectUniqueElites(scoredPopulation, _opts.EliteCount);

                // Add elites to global collection (as-is; final selection will deduplicate)
                var elitesToRecord = scoredPopulation.Take(Math.Min(_opts.EliteCount, scoredPopulation.Count)).ToList();
                _allCandidates.AddRange(elitesToRecord);
                foreach (var sc in elitesToRecord)
                {
                    var sig = GetGenotypeSignature(sc.Candidate);
                    _allCandidateSignatures.Add(sig);
                }

                // Collect ALL other unique candidates from the remainder of the generation to increase unique genotypes
                CollectDiversePerGeneration(scoredPopulation, elites.Count, generation);

                LogGenerationMetrics(generation, scoredPopulation, elites);

                // Reproduction: build next generation
                var nextPopulation = new List<List<IMutant>>(capacity: _opts.PopulationSize);
                nextPopulation.AddRange(elites);

                // Fill rest of population
                var attempts = 0;
                var maxAttempts = _opts.PopulationSize * 10; // safeguard
                while (nextPopulation.Count < _opts.PopulationSize && attempts < maxAttempts)
                {
                    attempts++;
                    var parent1 = TournamentSelection(population);
                    var parent2 = SelectDissimilarParent(population, parent1);
                    if (parent1.Count == 0 || parent2.Count == 0)
                    {
                        continue;
                    }

                    List<IMutant> offspring;
                    if (_random.NextDouble() < _opts.CrossoverRate)
                    {
                        offspring = Crossover(parent1, parent2);
                    }
                    else
                    {
                        offspring = new List<IMutant>(parent1);
                    }

                    if (_random.NextDouble() < currentMutationRate)
                    {
                        Mutate(offspring, [.. availableFOMs]);
                    }

                    if (_earlyFiltering && _heuristicRegistry.ShouldFilterCandidate(offspring))
                    {
                        continue;
                    }

                    nextPopulation.Add(offspring);
                }

                // Backfill from current population if needed (ensure uniqueness)
                if (nextPopulation.Count < _opts.PopulationSize)
                {
                    var sigs = new HashSet<string>(nextPopulation.Select(GetGenotypeSignature));
                    foreach (var sc in scoredPopulation)
                    {
                        if (nextPopulation.Count >= _opts.PopulationSize)
                        {
                            break;
                        }

                        var sig = GetGenotypeSignature(sc.Candidate);
                        if (sigs.Add(sig))
                        {
                            nextPopulation.Add(sc.Candidate);
                        }
                    }
                }

                // Apply per-generation deduplication and immigrants via helper (with adaptive schedule)
                population = ApplyPerGenerationDedupAndImmigrants(nextPopulation, availableFOMs, scoredPopulation, generation, ref currentMutationRate);
            }

            // Final evaluation and selection
            var finalScored = ScorePopulation(population);
            _allCandidates.AddRange(finalScored);
            var finalCandidates = StandardizedFinalSelection(_allCandidates);

            _logger.LogInformation(
                "GeneticSearch: Completed. Collected {TotalCandidates} candidates, yielding top {YieldedCount} after {Generations} generations",
                _allCandidates.Count, finalCandidates.Count, _opts.MaxGenerations);

            // Final output de-duplication by genotype signature
            var yieldedSigs = new HashSet<string>();
            foreach (var candidate in finalCandidates)
            {
                var sig = GetGenotypeSignature(candidate.Candidate);
                if (yieldedSigs.Add(sig))
                {
                    yield return new HigherOrderMutant(candidate.Candidate, Name, predictedScore: candidate.Score);
                }
            }

            LogSearchCompletion();
        }

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

        private bool ValidateInputs(IReadOnlyCollection<IMutant> availableFOMs)
        {
            if (availableFOMs.Count < 2)
            {
                _logger.LogInformation("GeneticSearch: Not enough FOMs (need >=2). Aborting.");
                return false;
            }
            return true;
        }

        private void LogSearchCompletion()
        {
            var bestScore = _bestCandidates == null ? 0 : _heuristicRegistry.ScoreCandidate(_bestCandidates);
            _logger.LogInformation("GeneticSearch: Finished evolutionary loop. Best score: {BestScore:F4}", bestScore);
        }

        private void LogGenerationMetrics(
            int generation,
            List<ScoredCandidate> scoredPopulation,
            List<List<IMutant>> elites)
        {
            var bestScore = scoredPopulation.Count > 0 ? scoredPopulation[0].Score : 0.0;
            var avgScore = scoredPopulation.Count > 0 ? scoredPopulation.Average(s => s.Score) : 0.0;
            var medianScore = scoredPopulation.Count > 0 ? scoredPopulation.Select(s => s.Score).OrderBy(s => s).ElementAt(scoredPopulation.Count / 2) : 0.0;

            _logger.LogDebug(
                "GeneticSearch: Generation {Generation} | pop={Pop} elites={EliteCount} best={Best:F4} avg={Avg:F4} median={Median:F4}",
                generation, scoredPopulation.Count, elites.Count, bestScore, avgScore, medianScore);
        }

        private void UpdateBestCandidate(List<IMutant> candidate, double score)
        {
            if (candidate == null)
                return;
            if (_bestCandidates == null)
            {
                _bestCandidates = candidate;
                return;
            }

            var currentBest = _heuristicRegistry.ScoreCandidate(_bestCandidates);
            if (score > currentBest)
            {
                _bestCandidates = candidate;
            }
        }

        private List<ScoredCandidate> StandardizedFinalSelection(List<ScoredCandidate> allCandidates)
        {
            if (allCandidates.Count == 0)
            {
                return allCandidates;
            }


            //filter allCandidates to only unique candidates by genotype signature
            var allUniqueCandidates = allCandidates
                .GroupBy(c => GetGenotypeSignature(c.Candidate))
                .Select(g => g.OrderByDescending(c => c.Score).First())
                .ToList();

            _logger.LogInformation("GeneticSearch: from {allCandidates} candidates, {uniqueCandidates} are unique", allCandidates.Count, allUniqueCandidates.Count);


            var sortedCandidates = allUniqueCandidates
                .OrderByDescending(c => c.Score)
                .ToList();

            // Calculate target count using percentage of unique candidates
            //var byPercentage = (int)Math.Ceiling(sortedCandidates.Count * _finalSelectionPercentage);
            // Scale with number of available FOMs (factor from options)
            var byFoms = (int)Math.Ceiling(_availableFomsCount * _opts.FinalSelectionFomFactor);

            // Keep proportion with no hard upper cap; enforce only minimum floor
            var targetCount = Math.Max(_opts.MinFinalCandidates, byFoms);

            var finalCandidates = sortedCandidates.Take(targetCount).ToList();

            _logger.LogInformation(
                "GeneticSearch: Selected {FinalCount} candidates from {TotalCount} collected using standardized selection. Lowest score: {lowestscore}, highers score: {highestScore}",
                finalCandidates.Count, sortedCandidates.Count, finalCandidates.Last().Score, finalCandidates.First().Score);

            return finalCandidates;
        }

        // Initializes a population of genomes (candidates)
        private List<List<IMutant>> InitializePopulation(List<IMutant> foms, int populationSize)
        {
            var population = new List<List<IMutant>>(populationSize);
            var retries = 0;
            var maxRetries = populationSize * 50;

            // 1) Heuristic-guided candidates
            var heuristicCount = (int)(populationSize * _opts.HeuristicPopulationRatio);
            var heuristicCandidates = GenerateHeuristicGuidedCandidates(foms, heuristicCount);
            foreach (var candidate in heuristicCandidates)
            {
                if (!_earlyFiltering || !_heuristicRegistry.ShouldFilterCandidate(candidate))
                {
                    population.Add(candidate);
                }
                if (population.Count >= populationSize)
                    break;
            }

            // 2) Diverse seeds
            var seedCount = (int)(populationSize * _opts.DiversePopulationRatio);
            var diverseSeeds = GenerateDiverseSeeds(foms, seedCount);
            foreach (var seed in diverseSeeds)
            {
                if (!_earlyFiltering || !_heuristicRegistry.ShouldFilterCandidate(seed))
                {
                    population.Add(seed);
                }
                if (population.Count >= populationSize)
                    break;
            }

            // 3) Random compatible candidates
            while (population.Count < populationSize && retries < maxRetries)
            {
                retries++;
                var candidate = GenerateCompatibleCandidate(foms);
                if (candidate.Count >= 2 && (!_earlyFiltering || !_heuristicRegistry.ShouldFilterCandidate(candidate)))
                {
                    population.Add(candidate);
                }
            }

            _logger.LogInformation(
                "GeneticSearch: Initial population size {PopSize} after {Retries} retries (Heuristic: {HeuristicCount}, Diverse: {DiverseCount}, Random: {RandomCount})",
                population.Count,
                retries,
                Math.Min(heuristicCandidates.Count, heuristicCount),
                diverseSeeds.Count,
                Math.Max(0, population.Count - Math.Min(heuristicCandidates.Count, heuristicCount) - diverseSeeds.Count));

            return population;
        }

        // Score a whole population
        private List<ScoredCandidate> ScorePopulation(List<List<IMutant>> population)
        {
            var scored = new List<ScoredCandidate>(population.Count);
            foreach (var candidate in population)
            {
                if (_earlyFiltering && _heuristicRegistry.ShouldFilterCandidate(candidate))
                {
                    continue;
                }
                var score = _heuristicRegistry.ScoreCandidate(candidate);
                scored.Add(new ScoredCandidate(candidate, score));
            }
            return scored;
        }

        private List<List<IMutant>> GenerateDiverseSeeds(List<IMutant> foms, int targetCount)
        {
            var seeds = new List<List<IMutant>>();

            // Prefer FOMs with assessing tests
            var fomsWithTests = foms.Where(f => f.AssessingTests != null && !f.AssessingTests.IsEmpty).ToList();
            if (fomsWithTests.Count < 2)
                fomsWithTests = foms;

            var usedMutants = new HashSet<int>();
            var pairsGenerated = 0;
            var maxPairs = Math.Max(1, targetCount / 2);

            for (var i = 0; i < fomsWithTests.Count && pairsGenerated < maxPairs; i++)
            {
                var fom1 = fomsWithTests[i];
                if (usedMutants.Contains(fom1.Id))
                    continue;

                for (var j = i + 1; j < fomsWithTests.Count; j++)
                {
                    var fom2 = fomsWithTests[j];
                    if (usedMutants.Contains(fom2.Id))
                        continue;

                    var candidate = new List<IMutant> { fom1, fom2 };
                    seeds.Add(candidate);
                    usedMutants.Add(fom1.Id);
                    usedMutants.Add(fom2.Id);
                    pairsGenerated++;
                    break;
                }
            }

            // Random pairs for remaining
            while (seeds.Count < targetCount)
            {
                if (foms.Count < 2)
                    break;
                var m1 = foms[_random.Next(foms.Count)];
                var m2 = foms[_random.Next(foms.Count)];
                if (m1.Id == m2.Id)
                    continue;
                seeds.Add(new List<IMutant> { m1, m2 });
            }

            // Optional: upgrade some pairs to triplets for additional early diversity
            UpgradeSeedsToTriplets(seeds, foms, Math.Min(seeds.Count / 3, targetCount), _opts.MaxOrder);

            return seeds;
        }

        private void UpgradeSeedsToTriplets(List<List<IMutant>> seeds, List<IMutant> foms, int upgradeTarget, int maxOrder)
        {
            var upgraded = 0;
            var fomsList = foms.ToList();
            foreach (var seed in seeds.OrderBy(_ => _random.Next()))
            {
                if (upgraded >= upgradeTarget)
                    break;
                if (seed.Count >= maxOrder)
                    continue;

                var currentIds = seed.Select(m => m.Id).ToHashSet();
                var pool = fomsList.Where(m => !currentIds.Contains(m.Id)).ToList();
                if (pool.Count == 0)
                    continue;
                var add = pool[_random.Next(pool.Count)];
                var candidate = new List<IMutant>(seed) { add };
                if (!_earlyFiltering || !_heuristicRegistry.ShouldFilterCandidate(candidate))
                {
                    seed.Add(add);
                    upgraded++;
                }
            }
        }

        private List<IMutant> GenerateCompatibleCandidate(List<IMutant> foms)
        {
            var candidateSize = _random.Next(2, Math.Min(_opts.MaxOrder + 1, Math.Max(3, foms.Count)));

            // Try a random pair as a start
            if (foms.Count >= 2)
            {
                var startingPair = new List<IMutant> { foms[_random.Next(foms.Count)], foms[_random.Next(foms.Count)] };
                if (!_heuristicRegistry.ShouldFilterCandidate(startingPair))
                {
                    return startingPair;
                }
            }

            return GenerateRandomCompatibleCandidate(foms, candidateSize);
        }

        private List<IMutant> GenerateRandomCompatibleCandidate(List<IMutant> foms, int candidateSize)
        {
            var candidate = new List<IMutant>();
            var fomsWithTests = foms.Where(f => f.AssessingTests != null && !f.AssessingTests.IsEmpty).ToList();
            if (fomsWithTests.Count == 0)
                fomsWithTests = foms;

            var seedFom = fomsWithTests[_random.Next(fomsWithTests.Count)];
            candidate.Add(seedFom);

            var available = foms.Where(f => f.Id != seedFom.Id).ToList();
            while (candidate.Count < candidateSize && available.Count > 0)
            {
                var idx = _random.Next(available.Count);
                candidate.Add(available[idx]);
                available.RemoveAt(idx);
            }

            return candidate;
        }

        private List<IMutant> TournamentSelection(List<List<IMutant>> population)
        {
            if (population.Count == 0)
                return new List<IMutant>();

            var tournament = new List<List<IMutant>>();
            var tSize = Math.Min(_opts.TournamentSize, population.Count);
            var indices = new HashSet<int>();
            while (tournament.Count < tSize)
            {
                var idx = _random.Next(population.Count);
                if (indices.Add(idx))
                    tournament.Add(population[idx]);
            }

            // Choose winner by fitness + diversity bonus (Jaccard-based)
            return tournament
                .OrderByDescending(c => _heuristicRegistry.ScoreCandidate(c) + CalculateDiversityBonus(c, tournament))
                .First();
        }

        private double CalculateDiversityBonus(List<IMutant> candidate, List<List<IMutant>> tournament)
        {
            var ids = candidate.Select(m => m.Id).ToHashSet();
            double total = 0;
            var count = 0;
            foreach (var other in tournament)
            {
                if (ReferenceEquals(candidate, other))
                    continue;
                var otherIds = other.Select(m => m.Id).ToHashSet();
                var inter = ids.Intersect(otherIds).Count();
                var uni = ids.Union(otherIds).Count();
                var sim = uni > 0 ? (double)inter / uni : 0;
                total += sim;
                count++;
            }
            var avgSim = count > 0 ? total / count : 0;
            return (1.0 - avgSim) * _opts.DiversityWeight;
        }

        private List<IMutant> SelectDissimilarParent(List<List<IMutant>> population, List<IMutant> parent1)
        {
            if (population.Count == 0)
                return new List<IMutant>();

            var tries = 0;
            List<IMutant> chosen = null;
            while (tries < _opts.ParentReselectionTries)
            {
                tries++;
                var candidate = TournamentSelection(population);
                if (candidate.Count == 0)
                    continue;
                var sim = Jaccard(parent1, candidate);
                chosen = candidate;
                if (sim <= _opts.ParentSimilarityThreshold)
                {
                    break;
                }
            }
            return chosen ?? TournamentSelection(population);
        }

        private List<IMutant> Crossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            if (parent1 == null || parent2 == null)
            {
                throw new ArgumentNullException(parent1 == null ? nameof(parent1) : nameof(parent2));
            }

            return _random.Next(3) switch
            {
                0 => SinglePointCrossover(parent1, parent2),
                1 => UniformCrossover(parent1, parent2),
                2 => FeatureBasedCrossover(parent1, parent2),
                _ => SinglePointCrossover(parent1, parent2)
            };
        }

        private List<IMutant> SinglePointCrossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            var expectedSize = Math.Min(Math.Max(parent1.Count, parent2.Count), _opts.MaxOrder);
            var child = new List<IMutant>(expectedSize);
            var childIds = new HashSet<int>(expectedSize);
            var crossoverPoint = _random.Next(Math.Min(parent1.Count, parent2.Count));
            AddUniqueGenes(child, childIds, parent1.Take(crossoverPoint));
            AddUniqueGenes(child, childIds, parent2.Skip(crossoverPoint));
            if (child.Count < 2)
            {
                EnsureMinimumSize(child, childIds, parent1.Concat(parent2), 2);
            }
            if (child.Count > _opts.MaxOrder)
            {
                child.RemoveRange(_opts.MaxOrder, child.Count - _opts.MaxOrder);
            }
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
                if (child.Count >= _opts.MaxOrder)
                    break;
            }
            if (child.Count < 2)
            {
                EnsureMinimumSize(child, childIds, allGenes, 2);
            }
            return child;
        }

        private List<IMutant> FeatureBasedCrossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            var allGenes = parent1.Concat(parent2).ToList();
            var child = new List<IMutant>();
            var childIds = new HashSet<int>();
            foreach (var gene in allGenes.OrderBy(_ => _random.Next()))
            {
                if (childIds.Add(gene.Id))
                {
                    var tmp = new List<IMutant>(child) { gene };
                    if (child.Count == 0 || _heuristicRegistry.ScoreCandidate(tmp) >= _heuristicRegistry.ScoreCandidate(child) * _opts.ScoreMaintenanceThreshold)
                    {
                        child.Add(gene);
                    }
                }
                if (child.Count >= _opts.MaxOrder)
                    break;
            }
            if (child.Count < 2)
            {
                EnsureMinimumSize(child, childIds, parent1.Concat(parent2), 2);
            }
            return child;
        }

        private void Mutate(List<IMutant> candidate, List<IMutant> availableFOMs)
        {
            if (candidate.Count == 0)
                return;
            var op = _random.Next(4); // add, remove, replace, swap
            var candidateIds = new HashSet<int>(candidate.Select(m => m.Id));
            switch (op)
            {
                case 0: // add
                    if (candidate.Count < _opts.MaxOrder)
                    {
                        var pool = availableFOMs.Where(m => !candidateIds.Contains(m.Id)).ToList();
                        if (pool.Count > 0)
                        {
                            candidate.Add(pool[_random.Next(pool.Count)]);
                        }
                    }
                    break;
                case 1: // remove
                    if (candidate.Count > 2)
                    {
                        candidate.RemoveAt(_random.Next(candidate.Count));
                    }
                    break;
                case 2: // replace (avoid no-op and duplicates)
                    if (candidate.Count > 0)
                    {
                        var idx = _random.Next(candidate.Count);
                        var currentId = candidate[idx].Id;
                        var allowed = availableFOMs.Where(m => m.Id != currentId && !candidateIds.Contains(m.Id)).ToList();
                        if (allowed.Count > 0)
                        {
                            var replacement = allowed[_random.Next(allowed.Count)];
                            candidate[idx] = replacement;
                        }
                    }
                    break;
                case 3: // swap two positions
                    if (candidate.Count >= 2)
                    {
                        var i = _random.Next(candidate.Count);
                        var j = _random.Next(candidate.Count);
                        if (i == j)
                        {
                            j = (j + 1) % candidate.Count;
                        }
                        (candidate[i], candidate[j]) = (candidate[j], candidate[i]);
                    }
                    break;
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
                    if (child.Count >= minimumSize)
                        break;
                }
            }
        }

        // Helpers: genotype signature, deduplication, novelty, and immigrants
        private static string GetGenotypeSignature(IReadOnlyList<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var id in candidate.Select(m => m.Id).Distinct().OrderBy(id => id))
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(id);
            }
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash);
        }

        private static double Jaccard(IReadOnlyList<IMutant> a, IReadOnlyList<IMutant> b)
        {
            if (a == null || b == null || a.Count == 0 && b.Count == 0) return 0.0;
            var ai = a.Select(m => m.Id).ToHashSet();
            var bi = b.Select(m => m.Id).ToHashSet();
            var inter = ai.Intersect(bi).Count();
            var uni = ai.Union(bi).Count();
            return uni == 0 ? 0.0 : (double)inter / uni;
        }

        private static double EstimateNovelty(IReadOnlyList<IMutant> c, IEnumerable<IReadOnlyList<IMutant>> reference)
        {
            var maxSim = 0.0;
            foreach (var r in reference)
            {
                var sim = Jaccard(c, r);
                if (sim > maxSim) maxSim = sim;
                if (maxSim >= 1.0) break;
            }
            return 1.0 - maxSim;
        }

        private List<List<IMutant>> DeduplicateByGenotype(List<List<IMutant>> population, out int removedCount, out HashSet<string> signatures)
        {
            signatures = new HashSet<string>();
            var unique = new List<List<IMutant>>(population.Count);
            foreach (var cand in population)
            {
                var sig = GetGenotypeSignature(cand);
                if (signatures.Add(sig))
                {
                    unique.Add(cand);
                }
            }
            removedCount = population.Count - unique.Count;
            return unique;
        }

        private int IntroduceImmigrants(
            List<List<IMutant>> population,
            List<IMutant> availableFOMs,
            HashSet<string> existingSignatures,
            int targetImmigrants)
        {
            if (targetImmigrants <= 0) return 0;
            var added = 0;
            var attempts = 0;
            var maxAttempts = Math.Max(100, targetImmigrants * 50);

            // reference sample for novelty (limit size)
            var reference = population.Count <= 64
                ? population.Cast<IReadOnlyList<IMutant>>().ToList()
                : population.OrderBy(_ => _random.Next()).Take(64).Cast<IReadOnlyList<IMutant>>().ToList();

            while (added < targetImmigrants && population.Count < _opts.PopulationSize && attempts < maxAttempts)
            {
                attempts++;
                // generate k candidates and pick the most novel
                var k = 4;
                List<IMutant> best = null;
                var bestNovelty = double.NegativeInfinity;
                for (var i = 0; i < k; i++)
                {
                    var immigrant = GenerateCompatibleCandidate(availableFOMs);
                    if (_earlyFiltering && _heuristicRegistry.ShouldFilterCandidate(immigrant))
                    {
                        continue;
                    }
                    var novelty = EstimateNovelty(immigrant, reference);
                    if (novelty > bestNovelty)
                    {
                        best = immigrant;
                        bestNovelty = novelty;
                    }
                }

                if (best == null)
                {
                    continue;
                }

                var sig = GetGenotypeSignature(best);
                if (existingSignatures.Add(sig))
                {
                    population.Add(best);
                    reference.Add(best);
                    added++;
                }
            }
            return added;
        }

        // Elite de-duplication
        private List<List<IMutant>> SelectUniqueElites(List<ScoredCandidate> scoredPopulation, int eliteCount)
        {
            var result = new List<List<IMutant>>(eliteCount);
            var seen = new HashSet<string>();
            foreach (var sc in scoredPopulation)
            {
                var sig = GetGenotypeSignature(sc.Candidate);
                if (seen.Add(sig))
                {
                    result.Add(sc.Candidate);
                    if (result.Count >= eliteCount)
                        break;
                }
            }
            return result;
        }

        // Per-generation deduplication and immigrant injection with adaptive schedule
        private List<List<IMutant>> ApplyPerGenerationDedupAndImmigrants(
            List<List<IMutant>> nextPopulation,
            IReadOnlyCollection<IMutant> availableFOMs,
            List<ScoredCandidate> scoredPopulation,
            int generation,
            ref double currentMutationRate)
        {
            var deduped = DeduplicateByGenotype(nextPopulation, out var removedDuplicates, out var existingSignatures);

            // Always inject immigrants within configured floor range, independent of removed duplicates
            var minImmigrants = (int)Math.Ceiling(_opts.PopulationSize * _opts.ImmigrantMinRatio);
            var maxImmigrants = (int)Math.Ceiling(_opts.PopulationSize * _opts.ImmigrantMaxRatio);
            var immigrantTarget = _random.Next(minImmigrants, maxImmigrants + 1);

            // Adaptive tweak: if many duplicates, increase mutation a bit and push immigrants to max
            var duplicateRatio = (double)removedDuplicates / Math.Max(1, nextPopulation.Count);
            if (duplicateRatio > 0.25)
            {
                currentMutationRate = Math.Min(currentMutationRate + 0.05, 0.9);
                immigrantTarget = maxImmigrants;
            }

            var immigrantsAdded = IntroduceImmigrants(deduped, [.. availableFOMs], existingSignatures, immigrantTarget);

            // If still under target size, backfill uniquely from best of current scoredPopulation
            if (deduped.Count < _opts.PopulationSize)
            {
                foreach (var sc in scoredPopulation)
                {
                    if (deduped.Count >= _opts.PopulationSize)
                        break;
                    var sig = GetGenotypeSignature(sc.Candidate);
                    if (existingSignatures.Add(sig))
                    {
                        deduped.Add(sc.Candidate);
                    }
                }
            }

            // Final guard: try random unique immigrants to fill to exact size
            var attempts = 0;
            var maxAttempts = _opts.PopulationSize * 10; // safeguard
            while (deduped.Count < _opts.PopulationSize && attempts < maxAttempts)
            {
                attempts++;
                var immigrant = GenerateCompatibleCandidate([.. availableFOMs]);
                if (_earlyFiltering && _heuristicRegistry.ShouldFilterCandidate(immigrant))
                {
                    continue;
                }
                var sig = GetGenotypeSignature(immigrant);
                if (existingSignatures.Add(sig))
                {
                    deduped.Add(immigrant);
                }
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var avgJaccard = deduped.Count > 1 ? deduped.Take(32).Average(c => Jaccard(c, deduped[_random.Next(deduped.Count)])) : 0.0;
                _logger.LogDebug(
                    "GeneticSearch: Gen {Generation} | unique={Unique} removedDup={Removed} immigrants={Immigrants} mutRate={Mut:F2} avgJaccard~={AvgJac:F3}",
                    generation, deduped.Count, removedDuplicates, immigrantsAdded, currentMutationRate, avgJaccard);
            }

            return deduped;
        }

        private class CandidateEqualityComparer : IEqualityComparer<List<IMutant>>
        {
            public bool Equals(List<IMutant> x, List<IMutant> y) => ReferenceEquals(x, y);
            public int GetHashCode(List<IMutant> obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private List<List<IMutant>> GenerateHeuristicGuidedCandidates(List<IMutant> foms, int targetCount)
        {
            var candidates = new List<List<IMutant>>();
            if (targetCount <= 0 || foms.Count < 2)
            {
                return candidates;
            }

            var filteredFoms = foms.Where(f => !_heuristicRegistry.ShouldFilterCandidate(new List<IMutant> { f })).ToList();
            if (filteredFoms.Count < 2)
            {
                filteredFoms = foms;
            }

            var scoredPairs = new List<ScoredCandidate>();
            var maxPairs = Math.Max(1, targetCount * 5);
            for (var i = 0; i < filteredFoms.Count && scoredPairs.Count < maxPairs; i++)
            {
                var maxJForI = Math.Min(filteredFoms.Count, i + _opts.MaxNeighbourhoodLimit);
                for (var j = i + 1; j < maxJForI && scoredPairs.Count < maxPairs; j++)
                {
                    var candidate = new List<IMutant> { filteredFoms[i], filteredFoms[j] };
                    if (_earlyFiltering && _heuristicRegistry.ShouldFilterCandidate(candidate))
                        continue;
                    var score = _heuristicRegistry.ScoreCandidate(candidate);
                    scoredPairs.Add(new ScoredCandidate(candidate, score));
                }
            }

            candidates.AddRange(scoredPairs
                .OrderByDescending(p => p.Score)
                .Take(targetCount)
                .Select(p => p.Candidate));

            return candidates;
        }

        // Collect ALL unique candidates from the scored population into _allCandidates using a cached signature set.
        private void CollectDiversePerGeneration(List<ScoredCandidate> scoredPopulation, int eliteCount, int generation)
        {
            if (scoredPopulation.Count == 0)
            {
                return;
            }

            var added = 0;
            foreach (var sc in scoredPopulation)
            {
                var sig = GetGenotypeSignature(sc.Candidate);
                if (_allCandidateSignatures.Add(sig))
                {
                    _allCandidates.Add(sc);
                    added++;
                }
            }

            if (added > 0)
            {
                _logger.LogDebug("GeneticSearch: Gen {Generation} | collected {Added} additional unique candidates", generation, added);
            }
        }
    }
}
