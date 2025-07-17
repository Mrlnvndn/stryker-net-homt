using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;

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

        private StrykerOptions _options;
        private readonly Random _random;
        // contains the scores for candidates based on their key to avoid recalculating them
        private readonly Dictionary<string, double> _candidateScoreCache;
        private readonly Dictionary<int, string> _idToKeyCache;
        private List<IMutant> _bestCandidate;
        private readonly HeuristicRegistry _heuristicRegistry;
        private readonly int _populationSize = 100;
        private readonly int _maxGenerations = 10;
        private readonly double _mutationRate = 0.1;
        private readonly double _crossoverRate = 0.7;
        private readonly int _eliteCount = 5;
        private readonly int _tournamentSize = 3;
        private readonly int _maxOrder = 4; // Maximum number of FOMs in a HOM
        private readonly bool _enforceUniqueness = true;

        public GeneticSearchAlgorithm(
            MutationTestInput mutationTestInput,
            IHOMHeuristic heuristic,
            IStrykerOptions options,
            IReadOnlyCollection<IMutant> availableMutants,
            IMutantExecutor executor)
        {
            _options = options as StrykerOptions ?? new StrykerOptions();
            _random = new Random();
            _candidateScoreCache = new Dictionary<string, double>();
            _idToKeyCache = new Dictionary<int, string>();

            // Initialize heuristic registry with all available heuristics
            _heuristicRegistry = new HeuristicRegistry(availableMutants, options, mutationTestInput);

            // For backward compatibility, register the provided legacy heuristic if it's not null
            if (heuristic != null)
            {
                _heuristicRegistry.RegisterHeuristic(heuristic);
            }
        }

        /// <summary>
        /// Generates candidate Higher-Order Mutants (HOMs) using a genetic algorithm approach.
        /// </summary>
        /// <param name="availableFOMs">The pool of FOMs to select from.</param>
        /// <param name="heuristics">A list of heuristics that guide the search.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="input">Mutation test input for additional context.</param>
        /// <returns>An enumerable of lists, where each inner list represents a candidate HOM that might be a SSHOM.</returns>
        public IEnumerable<List<IMutant>> GenerateCandidates(
            IReadOnlyCollection<IMutant> availableFOMs,
            IReadOnlyList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            MutationTestInput input)
        {
            // Check if there are enough FOMs to create HOMs
            if (availableFOMs.Count < 2)
            {
                yield break; // Not enough FOMs to create any HOMs
            }

            // Initialize population and tracking structures
            var (FomList, InitialPopulation, AllCandidateKeys, YieldedCandidateKeys) = InitializeGeneticAlgorithm(availableFOMs);
            
            // Run the evolutionary algorithm and yield results
            foreach (var candidate in EvolvePopulationAcrossGenerations(
                FomList,
                InitialPopulation,
                AllCandidateKeys,
                YieldedCandidateKeys,
                availableFOMs,
                options,
                input))
            {
                yield return candidate;
            }
        }

        /// <summary>
        /// Runs the evolutionary process for the specified number of iterations.
        /// </summary>
        private IEnumerable<List<IMutant>> EvolvePopulationAcrossGenerations(
            List<IMutant> fomList,
            List<List<IMutant>> initialPopulation, 
            HashSet<string> allCandidateKeys, 
            HashSet<string> yieldedCandidateKeys,
            IReadOnlyCollection<IMutant> availableFOMs,
            IStrykerOptions options,
            MutationTestInput input)
        {
            var currentPopulation = initialPopulation;
            
            // Run genetic algorithm for several iterations
            for (var iteration = 0; iteration < _maxGenerations; iteration++)
            {
                // Process and sort the current population
                var (Population, Elites, CandidateKeys) = ProcessPopulation(
                    currentPopulation, 
                    allCandidateKeys);
                    
                // Create the next population with elites and offspring,
                // including heuristic suggestions
                var nextPopulation = EvolvePopulation(
                    Population, 
                    Elites,
                    CandidateKeys, 
                    fomList, 
                    allCandidateKeys,
                    availableFOMs,
                    _enforceUniqueness);

                // If we couldn't fill the next population with unique candidates and uniqueness is enforced, fill with duplicates
                if (_enforceUniqueness)
                {
                    BackfillPopulation(
                        nextPopulation,
                        Population,
                        CandidateKeys);
                }

                // Update the population with the new candidates
                currentPopulation = nextPopulation;

                // Yield the best candidates from this population
                foreach (var candidate in YieldEliteCandidates(
                    Elites, 
                    CandidateKeys, 
                    yieldedCandidateKeys))
                {
                    yield return candidate;
                }
            }
        }

        /// <summary>
        /// Processes the current population, scoring and sorting candidates.
        /// </summary>
        private (List<List<IMutant>> Population, List<List<IMutant>> Elites, Dictionary<List<IMutant>, string> CandidateKeys) ProcessPopulation(
            List<List<IMutant>> population, 
            HashSet<string> allCandidateKeys)
        {
            // Process and cache keys and scores for the current population
            var candidateKeyDict = ProcessPopulationCandidates(population, allCandidateKeys);

            // Sort population by their scores
            population.Sort((a, b) => _candidateScoreCache[candidateKeyDict[b]]
                .CompareTo(_candidateScoreCache[candidateKeyDict[a]]));

            // Keep track of the elite candidates
            var elites = population.Take(_eliteCount).ToList();
            
            return (population, elites, candidateKeyDict);
        }

        /// <summary>
        /// Fills the remaining population slots with the best existing candidates.
        /// </summary>
        private void BackfillPopulation(
            List<List<IMutant>> nextPopulation,
            List<List<IMutant>> currentPopulation,
            Dictionary<List<IMutant>, string> candidateKeys)
        {
            if (nextPopulation.Count < _populationSize)
            {
                foreach (var candidate in currentPopulation.OrderByDescending(
                    c => _candidateScoreCache[candidateKeys[c]]))
                {
                    if (nextPopulation.Count >= _populationSize)
                    {
                        break;
                    }

                    nextPopulation.Add(candidate);
                }
            }
        }

        // GenerateEnhancedPopulation removed - functionality now directly integrated into EvolvePopulation

        /// <summary>
        /// Initialize the genetic algorithm by setting up the initial population
        /// and tracking structures.
        /// </summary>
        /// <param name="availableFOMs">Available First Order Mutants</param>
        /// <returns>Initialization results containing all required structures</returns>
        private (List<IMutant> FomList, List<List<IMutant>> InitialPopulation, HashSet<string> AllCandidateKeys, HashSet<string> YieldedCandidateKeys) InitializeGeneticAlgorithm(IReadOnlyCollection<IMutant> availableFOMs)
        {
            // Clear any existing cache
            _candidateScoreCache.Clear();
            _idToKeyCache.Clear();

            // Convert the read-only collection to a list for efficient indexed access
            var fomList = availableFOMs.ToList();

            // Initialize a population of random candidate HOMs
            var population = InitializePopulation(fomList, _populationSize);

            // Track all generated candidates to avoid duplicates
            var allCandidates = new HashSet<string>(StringComparer.Ordinal);

            // Keep track of all unique candidates yielded to the caller
            var yieldedCandidates = new HashSet<string>(StringComparer.Ordinal);

            // Add initial population to tracking set and cache their keys
            foreach (var candidate in population)
            {
                var key = CalculateCandidateKey(candidate);
                _candidateScoreCache[key] = EvaluateCandidate(candidate);
                allCandidates.Add(key);
            }

            return (fomList, population, allCandidates, yieldedCandidates);
        }

        /// <summary>
        /// Creates an initial population of random HOM candidates.
        /// </summary>
        private List<List<IMutant>> InitializePopulation(List<IMutant> foms, int populationSize)
        {
            var population = new List<List<IMutant>>(populationSize);

            for (var i = 0; i < populationSize; i++)
            {
                // Random size between 2 and _maxOrder FOMs per candidate
                var candidateSize = _random.Next(2, Math.Min(_maxOrder + 1, foms.Count));
                var candidate = new List<IMutant>();

                // Select random FOMs without replacement
                var availableFoms = new List<IMutant>(foms);
                for (var j = 0; j < candidateSize && availableFoms.Count > 0; j++)
                {
                    var index = _random.Next(availableFoms.Count);
                    candidate.Add(availableFoms[index]);
                    availableFoms.RemoveAt(index);
                }

                if (candidate.Count >= 2) // Ensure minimum size of 2
                {
                    population.Add(candidate);
                }
                else
                {
                    i--; // Try again
                }
            }

            return population;
        }

        /// <summary>
        /// Evaluates a candidate HOM based on heuristics and other factors.
        /// </summary>
        protected double EvaluateCandidate(List<IMutant> candidate)
        {
            // Use heuristic registry to evaluate the candidate
            // This leverages all registered heuristics with appropriate weighting
            return _heuristicRegistry.ScoreCandidate(candidate);
        }

        /// <summary>
        /// Performs tournament selection to choose a candidate for breeding.
        /// </summary>
        private List<IMutant> TournamentSelection(
            List<List<IMutant>> population,
            Dictionary<List<IMutant>, string> candidateKeys)
        {
            // Randomly select candidates for the tournament
            var tournament = new List<List<IMutant>>();
            for (var i = 0; i < _tournamentSize; i++)
            {
                var randomIndex = _random.Next(population.Count);
                tournament.Add(population[randomIndex]);
            }

            // Return the candidate with the highest score
            return tournament.OrderByDescending(c => 
            {
                // Get the candidate's key
                var key = candidateKeys[c];
                
                // Ensure the key exists in the score cache
                if (!_candidateScoreCache.ContainsKey(key))
                {
                    // Calculate and cache the score if not already present
                    _candidateScoreCache[key] = EvaluateCandidate(c);
                }
                
                return _candidateScoreCache[key];
            }).First();
        }

        /// <summary>
        /// Performs crossover between two parent candidates to create a child candidate.
        /// Uses one-point crossover with duplicate prevention and size constraints.
        /// </summary>
        /// <param name="parent1">First parent candidate containing mutants</param>
        /// <param name="parent2">Second parent candidate containing mutants</param>
        /// <returns>A new child candidate with traits from both parents</returns>
        private List<IMutant> Crossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            if (parent1 == null || parent2 == null)
            {
                throw new ArgumentNullException(parent1 == null ? nameof(parent1) : nameof(parent2));
            }

            // Calculate the expected size to optimize initial capacity
            var expectedSize = Math.Min(Math.Max(parent1.Count, parent2.Count), _maxOrder);
            var child = new List<IMutant>(expectedSize);
            var childIds = new HashSet<int>(expectedSize);

            // Simple one-point crossover
            var crossoverPoint = _random.Next(Math.Min(parent1.Count, parent2.Count));

            // First phase: Take genes from first parent up to crossover point
            AddUniqueGenes(child, childIds, parent1.Take(crossoverPoint));

            // Second phase: Take genes from second parent after crossover point
            AddUniqueGenes(child, childIds, parent2.Skip(crossoverPoint));

            // Third phase: Ensure minimum size by adding any unique genes from either parent
            if (child.Count < 2)
            {
                EnsureMinimumSize(child, childIds, parent1.Concat(parent2), 2);
            }

            // Fourth phase: Ensure we don't exceed maximum size
            if (child.Count > _maxOrder)
            {
                // Trim to max size without creating a new list
                child.RemoveRange(_maxOrder, child.Count - _maxOrder);
            }

            return child;
        }

        /// <summary>
        /// Adds genes to a child candidate if they aren't already present.
        /// </summary>
        private static void AddUniqueGenes(List<IMutant> child, HashSet<int> childIds, IEnumerable<IMutant> genes)
        {
            foreach (var gene in genes)
            {
                if (childIds.Add(gene.Id)) // Returns true if added (not present before)
                {
                    child.Add(gene);
                }
            }
        }

        /// <summary>
        /// Ensures a child candidate has at least the specified minimum size.
        /// </summary>
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

        /// <summary>
        /// Mutates a candidate by randomly adding, removing, or replacing FOMs.
        /// </summary>
        private void Mutate(List<IMutant> candidate, List<IMutant> availableFOMs)
        {
            // Pick a random mutation operation:
            // 1. Add a new FOM
            // 2. Remove a random FOM
            // 3. Replace a FOM with another one

            var operation = _random.Next(3);
            var candidateIds = new HashSet<int>(candidate.Select(m => m.Id));

            switch (operation)
            {
                case 0: // Add
                    if (candidate.Count < _maxOrder && availableFOMs.Count > candidateIds.Count)
                    {
                        // Find a FOM not in the candidate
                        var availableIds = availableFOMs
                            .Where(m => !candidateIds.Contains(m.Id))
                            .ToList();

                        if (availableIds.Count > 0)
                        {
                            var randomIndex = _random.Next(availableIds.Count);
                            candidate.Add(availableIds[randomIndex]);
                        }
                    }
                    break;

                case 1: // Remove
                    if (candidate.Count > 2) // Keep minimum size of 2
                    {
                        var removeIndex = _random.Next(candidate.Count);
                        candidate.RemoveAt(removeIndex);
                    }
                    break;

                case 2: // Replace
                    if (availableFOMs.Count > candidateIds.Count)
                    {
                        var replaceIndex = _random.Next(candidate.Count);
                        var oldId = candidate[replaceIndex].Id;

                        // Find a FOM not in the candidate
                        var availableIds = availableFOMs
                            .Where(m => !candidateIds.Contains(m.Id) || m.Id == oldId)
                            .ToList();

                        if (availableIds.Count > 0)
                        {
                            var randomIndex = _random.Next(availableIds.Count);
                            candidate[replaceIndex] = availableIds[randomIndex];
                        }
                    }
                    break;
            }

            // Remove the candidate from the key cache since it's been mutated
            var key = GetCandidateKey(candidate);
            if (!string.IsNullOrEmpty(key))
            {
                _candidateScoreCache.Remove(key);
            }
        }

        /// <summary>
        /// Gets the cached key for a candidate or calculates it if not cached.
        /// </summary>
        private string GetCandidateKey(List<IMutant> candidate)
        {
            if (candidate == null)
            {
                return string.Empty;
            }
            
            // Check if we've already cached the key
            var hashCode = candidate.GetHashCode();
            if (_idToKeyCache.TryGetValue(hashCode, out string key))
            {
                return key;
            }

            // Calculate the key if not cached
            key = CalculateCandidateKey(candidate);
            _idToKeyCache[hashCode] = key;
            return key;
        }

        /// <summary>
        /// Calculates a unique key for a candidate based on its constituent mutant IDs.
        /// </summary>
        private static string CalculateCandidateKey(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0)
            {
                return string.Empty;
            }
            // Sort by ID to ensure consistency regardless of order
            var sortedIds = candidate.Select(m => m.Id).OrderBy(id => id).Select(id => id.ToString()).ToList();
            return string.Join("_", sortedIds);
        }
        
        // EvolvePopulationUnfiltered removed - functionality now integrated into EvolvePopulation

        /// <summary>
        /// Process all candidates in the population, caching their keys and scores
        /// </summary>
        /// <param name="population">The current population of HOMs</param>
        /// <param name="allCandidates">Set of all candidate keys to track uniqueness</param>
        /// <returns>Dictionary mapping candidates to their string keys</returns>
        private Dictionary<List<IMutant>, string> ProcessPopulationCandidates(
            List<List<IMutant>> population,
            HashSet<string> allCandidates)
        {
            var candidateKeys = new Dictionary<List<IMutant>, string>();
            foreach (var candidate in population)
            {
                // Skip candidates that should be filtered out according to heuristics
                if (_heuristicRegistry.ShouldFilterCandidate(candidate))
                {
                    continue;
                }

                // Ensure we have a cached key for this candidate
                if (!candidateKeys.ContainsKey(candidate))
                {
                    string key = GetCandidateKey(candidate);
                    candidateKeys[candidate] = key;

                    // Calculate score if not already cached
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

        // AddSuggestedCandidates removed - functionality now directly integrated into EvolvePopulation

        /// <summary>
        /// Filters and yields elite candidates, ensuring we don't yield duplicates
        /// </summary>
        /// <param name="elites">The elite candidates from the current generation</param>
        /// <param name="candidateKeys">Dictionary mapping candidates to their string keys</param>
        /// <param name="yieldedCandidates">Set of already yielded candidate keys</param>
        /// <returns>Unique elite candidates that have not been yielded yet</returns>
        private static IEnumerable<List<IMutant>> YieldEliteCandidates(
            List<List<IMutant>> elites,
            Dictionary<List<IMutant>, string> candidateKeys,
            HashSet<string> yieldedCandidates)
        {
            foreach (var candidate in elites)
            {
                var candidateKey = candidateKeys[candidate]; // Use cached key
                if (yieldedCandidates.Add(candidateKey))
                {
                    yield return candidate;
                }
            }
        }

        /// <summary>
        /// Creates the next population of HOMs using elite preservation, offspring generation, and heuristic suggestions.
        /// </summary>
        /// <param name="population">Current population of HOMs</param>
        /// <param name="elites">Elite candidates to preserve</param>
        /// <param name="candidateKeys">Dictionary mapping candidates to their string keys</param>
        /// <param name="fomList">List of available FOMs for mutation</param>
        /// <param name="allCandidates">Set of all candidate keys to track uniqueness</param>
        /// <param name="availableFOMs">Available FOMs for mutation and heuristic suggestions</param>
        /// <param name="enforceUniqueness">Whether to enforce uniqueness in the population (default: true)</param>
        /// <returns>A new population of candidate HOMs</returns>
        private List<List<IMutant>> EvolvePopulation(
            List<List<IMutant>> population, 
            List<List<IMutant>> elites, 
            Dictionary<List<IMutant>, string> candidateKeys, 
            List<IMutant> fomList, 
            HashSet<string> allCandidates,
            IReadOnlyCollection<IMutant> availableFOMs = null,
            bool enforceUniqueness = true)
        {
            var nextPopulation = new List<List<IMutant>>();

            // Add elites directly to next population
            nextPopulation.AddRange(elites);
            
            // Add heuristic-suggested candidates if available
            if (availableFOMs != null && population.Count > 0)
            {
                var suggestedCandidates = new List<List<IMutant>>();

                // Get suggestions for each candidate in the population
                foreach (var candidate in population)
                {
                    var suggestions = _heuristicRegistry.SuggestNextCandidates(candidate, availableFOMs);
                    foreach (var suggestion in suggestions)
                    {
                        // If enforcing uniqueness, check before adding
                        var suggestionKey = CalculateCandidateKey(suggestion);
                        if (!enforceUniqueness || allCandidates == null || allCandidates.Add(suggestionKey))
                        {
                            suggestedCandidates.Add(suggestion);
                            _candidateScoreCache[suggestionKey] = EvaluateCandidate(suggestion);
                        }
                    }
                }

                // Add limited number of suggested candidates to avoid explosion
                if (suggestedCandidates.Count > 0)
                {
                    // Randomize and limit the number of suggestions
                    var limitedSuggestions = suggestedCandidates
                        .OrderBy(_ => _random.Next())
                        .Take(Math.Min(5, suggestedCandidates.Count));
                        
                    nextPopulation.AddRange(limitedSuggestions);
                }
            }

            // Fill the rest of the next population with offspring
            var attempts = 0;
            var maxAttempts = enforceUniqueness ? _populationSize * 10 : _populationSize; // More attempts if enforcing uniqueness
            
            while (nextPopulation.Count < _populationSize && attempts < maxAttempts)
            {
                attempts++;

                // Select parents using tournament selection
                var parent1 = TournamentSelection(population, candidateKeys);
                var parent2 = TournamentSelection(population, candidateKeys);

                // Perform crossover with some probability
                List<IMutant> offspring;
                if (_random.NextDouble() < _crossoverRate)
                {
                    offspring = Crossover(parent1, parent2);
                }
                else
                {
                    // No crossover, just copy one parent
                    offspring = new List<IMutant>(parent1);
                }

                // Apply mutation with some probability
                if (_random.NextDouble() < _mutationRate)
                {
                    Mutate(offspring, fomList);
                }

                // Calculate and cache the offspring key
                var offspringKey = CalculateCandidateKey(offspring);
                _candidateScoreCache[offspringKey] = EvaluateCandidate(offspring);

                // Add offspring to next population, checking uniqueness if required
                if (!enforceUniqueness || allCandidates.Add(offspringKey))
                {
                    nextPopulation.Add(offspring);
                }
            }
            
            return nextPopulation;
        }

        /// <summary>
        /// Comparer for lists of mutants based on reference equality.
        /// </summary>
        private class CandidateEqualityComparer : IEqualityComparer<List<IMutant>>
        {
            public bool Equals(List<IMutant> x, List<IMutant> y) => ReferenceEquals(x, y);

            public int GetHashCode(List<IMutant> obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
