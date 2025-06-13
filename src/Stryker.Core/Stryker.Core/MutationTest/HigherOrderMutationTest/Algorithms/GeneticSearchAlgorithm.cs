using System;
using System.Collections.Generic;
using System.Linq;
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
        
        private readonly Random _random = new();
        private readonly int _populationSize = 100;
        private readonly int _maxGenerations = 10;
        private readonly double _mutationRate = 0.1;
        private readonly double _crossoverRate = 0.7;
        private readonly int _eliteCount = 5;
        private readonly int _tournamentSize = 3;
        private readonly int _maxOrder = 4; // Maximum number of FOMs in a HOM

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
            // Convert the read-only collection to a list for efficient indexed access
            var fomList = availableFOMs.ToList();
            
            // Initialize a population of random candidate HOMs
            var population = InitializePopulation(fomList, _populationSize);
            
            // Track all generated candidates to avoid duplicates
            var allCandidates = new HashSet<string>(StringComparer.Ordinal);
            
            // Keep track of all unique candidates yielded to the caller
            var yieldedCandidates = new HashSet<string>(StringComparer.Ordinal);
            
            // Add initial population to tracking set
            foreach (var candidate in population)
            {
                allCandidates.Add(GetCandidateKey(candidate));
            }
            
            // Use the heuristics to score candidates
            var candidateScores = new Dictionary<string, double>();
            
            // Run genetic algorithm for several generations
            for (int generation = 0; generation < _maxGenerations; generation++)
            {
                // Score each candidate in the current population
                foreach (var candidate in population)
                {
                    var key = GetCandidateKey(candidate);
                    if (!candidateScores.ContainsKey(key))
                    {
                        candidateScores[key] = EvaluateCandidate(candidate, heuristics);
                    }
                }
                
                // Sort population by their scores
                population.Sort((a, b) => candidateScores[GetCandidateKey(b)]
                    .CompareTo(candidateScores[GetCandidateKey(a)]));
                
                // Keep track of the elite candidates
                var elites = population.Take(_eliteCount).ToList();
                
                // Create the next generation
                var nextGeneration = new List<List<IMutant>>();
                
                // Add elites directly to next generation
                nextGeneration.AddRange(elites);
                
                // Fill the rest of the next generation with offspring
                while (nextGeneration.Count < _populationSize)
                {
                    // Select parents using tournament selection
                    var parent1 = TournamentSelection(population, candidateScores);
                    var parent2 = TournamentSelection(population, candidateScores);
                    
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
                    
                    // Ensure the offspring is unique
                    string offspringKey = GetCandidateKey(offspring);
                    if (!allCandidates.Contains(offspringKey))
                    {
                        allCandidates.Add(offspringKey);
                        nextGeneration.Add(offspring);
                    }
                }
                
                // Update population for the next generation
                population = nextGeneration;
                
                // Yield the best candidates from this generation, but avoid duplicates
                foreach (var candidate in elites)
                {
                    string candidateKey = GetCandidateKey(candidate);
                    if (!yieldedCandidates.Contains(candidateKey))
                    {
                        yieldedCandidates.Add(candidateKey);
                        yield return candidate;
                    }
                }
            }
        }

        /// <summary>
        /// Creates an initial population of random HOM candidates.
        /// </summary>
        private List<List<IMutant>> InitializePopulation(List<IMutant> foms, int populationSize)
        {
            var population = new List<List<IMutant>>(populationSize);
            
            for (int i = 0; i < populationSize; i++)
            {
                // Random size between 2 and _maxOrder FOMs per candidate
                int candidateSize = _random.Next(2, Math.Min(_maxOrder + 1, foms.Count));
                var candidate = new List<IMutant>();
                
                // Select random FOMs without replacement
                var availableFoms = new List<IMutant>(foms);
                for (int j = 0; j < candidateSize && availableFoms.Count > 0; j++)
                {
                    int index = _random.Next(availableFoms.Count);
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
        public static double EvaluateCandidate(List<IMutant> candidate, IReadOnlyList<IHOMHeuristic> heuristics)
        {
            double score = 0;
            
            // Heuristics are not yet implemented with concrete scoring methods
            // For now, we'll use our own internal scoring logic
            // When actual heuristic implementations are available, they can be integrated here
            
            // Bonus factors:
            
            // 1. Diversity of mutation operators
            var mutationTypes = new HashSet<string>();
            foreach (var mutant in candidate)
            {
                // Store some identifier of the mutation type
                string mutationType = mutant.Mutation.Type.ToString();
                mutationTypes.Add(mutationType);
            }
            score += mutationTypes.Count * 0.5; // Bonus for diversity
            
            // 2. The set of killing tests should overlap but not be identical
            if (candidate.Count >= 2 && candidate.All(m => m.KillingTests != null && !m.KillingTests.IsEmpty))
            {
                // Get intersection of all killing tests
                var intersection = candidate[0].KillingTests;

                // If there are no overlapping tests, intersection will be null
                for (int i = 1; i < candidate.Count && intersection != null; i++)
                {
                    intersection = intersection.Intersect(candidate[i].KillingTests);
                }
                
                if (intersection != null) 
                {
                    // Calculate what percentage of each FOM's killing tests are in the intersection
                    double averageOverlap = 0;
                    foreach (var mutant in candidate)
                    {
                        // Since we don't have direct access to test count, approximate based on string representation
                        // In a real implementation, this would use proper test count methods
                        int totalTests = 1; // Default to 1 for calculation purposes
                        int overlapTests = 1;
                        // When ITestIdentifiers implements proper count methods, replace with actual counts
                        if (totalTests > 0)
                        {
                            double overlap = (double)overlapTests / totalTests;
                            averageOverlap += overlap;
                        }
                    }
                    
                    averageOverlap /= candidate.Count;
                    
                    // Prefer candidates where 25%-75% of tests overlap (bell curve centered at 50%)
                    double targetOverlap = 0.5;
                    double overlapScore = 1.0 - Math.Abs(averageOverlap - targetOverlap) * 2.0;
                    score += Math.Max(0, overlapScore) * 3.0; // Heavy weight on good overlap
                }
            }
            
            // Adjust score based on candidate size - prefer smaller HOMs when scores are similar
            score -= (candidate.Count - 2) * 0.1; // Small penalty for each additional FOM beyond 2
            
            return score;
        }

        /// <summary>
        /// Performs tournament selection to choose a candidate for breeding.
        /// </summary>
        private List<IMutant> TournamentSelection(
            List<List<IMutant>> population,
            Dictionary<string, double> candidateScores)
        {
            // Randomly select candidates for the tournament
            var tournament = new List<List<IMutant>>();
            for (int i = 0; i < _tournamentSize; i++)
            {
                int randomIndex = _random.Next(population.Count);
                tournament.Add(population[randomIndex]);
            }
            
            // Return the candidate with the highest score
            return tournament.OrderByDescending(c => candidateScores[GetCandidateKey(c)])
                .First();
        }

        /// <summary>
        /// Performs crossover between two parent candidates to create a child candidate.
        /// </summary>
        private List<IMutant> Crossover(List<IMutant> parent1, List<IMutant> parent2)
        {
            // Simple one-point crossover
            int crossoverPoint = _random.Next(Math.Min(parent1.Count, parent2.Count));
            
            var child = new List<IMutant>();
            // Take FOMs from parent1 up to crossover point
            child.AddRange(parent1.Take(crossoverPoint));
            
            // Add FOMs from parent2 after crossover point if not already in child
            var childIds = new HashSet<int>(child.Select(m => m.Id));
            foreach (var fom in parent2.Skip(crossoverPoint))
            {
                if (!childIds.Contains(fom.Id))
                {
                    child.Add(fom);
                    childIds.Add(fom.Id);
                }
            }
            
            // Ensure minimum size of 2
            if (child.Count < 2)
            {
                // Add more FOMs from either parent
                foreach (var fom in parent1.Concat(parent2))
                {
                    if (!childIds.Contains(fom.Id))
                    {
                        child.Add(fom);
                        childIds.Add(fom.Id);
                        if (child.Count >= 2) break;
                    }
                }
            }
            
            // Ensure maximum size
            if (child.Count > _maxOrder)
            {
                child = child.Take(_maxOrder).ToList();
            }
            
            return child;
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
            
            int operation = _random.Next(3);
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
                        
                        if (availableIds.Any())
                        {
                            int randomIndex = _random.Next(availableIds.Count);
                            candidate.Add(availableIds[randomIndex]);
                        }
                    }
                    break;
                    
                case 1: // Remove
                    if (candidate.Count > 2) // Keep minimum size of 2
                    {
                        int removeIndex = _random.Next(candidate.Count);
                        candidate.RemoveAt(removeIndex);
                    }
                    break;
                    
                case 2: // Replace
                    if (availableFOMs.Count > candidateIds.Count)
                    {
                        int replaceIndex = _random.Next(candidate.Count);
                        int oldId = candidate[replaceIndex].Id;
                        
                        // Find a FOM not in the candidate
                        var availableIds = availableFOMs
                            .Where(m => !candidateIds.Contains(m.Id) || m.Id == oldId)
                            .ToList();
                        
                        if (availableIds.Any())
                        {
                            int randomIndex = _random.Next(availableIds.Count);
                            candidate[replaceIndex] = availableIds[randomIndex];
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Creates a unique key for a candidate HOM based on the IDs of its constituent mutants.
        /// </summary>
        private string GetCandidateKey(List<IMutant> candidate)
        {
            return string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id));
        }
    }
}
