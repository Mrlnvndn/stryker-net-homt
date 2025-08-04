using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms
{
    /// <summary>
    /// Implements a local search algorithm for finding Higher-Order Mutants (HOMs).
    /// This algorithm explores neighborhoods of promising candidates to find Strongly Subsuming HOMs.
    /// </summary>
    public class LocalSearchAlgorithm : IHOMSearchAlgorithm
    {
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
        /// Initializes a new instance of the <see cref="LocalSearchAlgorithm"/> class.
        /// </summary>
        /// <param name="mutationTestInput">Mutation test input for additional context.</param>
        /// <param name="heuristic">A legacy heuristic to guide the search.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="reporter">Reporter for timeout heuristic information.</param>
        /// <param name="availableMutants">Available mutants for selection.</param>
        /// <param name="executor">Executor for running mutants.</param>
        /// <param name="maxIterations">Maximum number of iterations for the search.</param>
        /// <param name="candidatePoolSize">Size of the candidate pool to maintain.</param>
        /// <param name="maxOrder">Maximum order (number of FOMs) to consider.</param>
        public LocalSearchAlgorithm(
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

            // Initialize heuristic registry with all available heuristics but without default heuristics
            _heuristicRegistry = new HeuristicRegistry(availableMutants, options, mutationTestInput, registerDefaultHeuristics: false);
            
            // Register default heuristics with the correct max order limit
            _heuristicRegistry.RegisterHeuristic(new MaxSizeLimitHeuristic(maxOrder));
            
            // Register other default heuristics that don't need max order configuration
            _heuristicRegistry.RegisterHeuristic(new CodeLocationHeuristic());
            _heuristicRegistry.RegisterHeuristic(new HardToKillHeuristic());
            _heuristicRegistry.RegisterHeuristic(new MutatorTypeHeuristic());
            _heuristicRegistry.RegisterHeuristic(new WeakMutatorFilterHeuristic());
            _heuristicRegistry.RegisterHeuristic(new SyntaxNodeConflictHeuristic());
            
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
        /// Generates candidate Higher-Order Mutants (HOMs) using local search.
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
            
            // Initialize the candidate pool with promising starting points
            var candidatePool = InitializeStartingPoints(availableFOMs, _heuristicRegistry);
            
            // Score initial candidates
            var scoredCandidates = ScoreAndRankCandidates(candidatePool, _heuristicRegistry);

            // Filter initial candidates
            candidatePool.RemoveAll(_heuristicRegistry.ShouldFilterCandidate);

            // Main local search loop
            for (var iteration = 0; iteration < _maxIterations; iteration++)
            {
                var newCandidates = new List<List<IMutant>>();
                
                // Explore neighborhood of each candidate in the pool
                foreach (var candidate in scoredCandidates)
                {
                    // Generate neighborhood using heuristic suggestions
                    var neighbors = ExploreNeighborhood(candidate.Candidate, availableFOMs, _heuristicRegistry);
                    newCandidates.AddRange(neighbors);
                }
                
                // Add new candidates to the pool, removing duplicates
                foreach (var candidate in newCandidates)
                {
                    var candidateKey = GetCandidateKey(candidate);
                    
                    if (!generatedCandidates.Contains(candidateKey))
                    {
                        generatedCandidates.Add(candidateKey);
                        
                        // Only yield if the candidate is not filtered by heuristics
                        if (!_heuristicRegistry.ShouldFilterCandidate(candidate))
                        {
                            // Create HigherOrderMutant instance from the candidate list
                            var higherOrderMutant = new HigherOrderMutant(candidate, Name);
                            yield return higherOrderMutant;
                        }
                        
                        candidatePool.Add(candidate);
                    }
                }
                
                // Rerank and prune the candidate pool to maintain manageable size
                scoredCandidates = [.. ScoreAndRankCandidates(candidatePool, _heuristicRegistry).Take(_candidatePoolSize)];
                
                // Update candidate pool with the best candidates
                candidatePool = [.. scoredCandidates.Select(sc => sc.Candidate)];
            }
        }

        /// <summary>
        /// Initialize the starting points for local search.
        /// </summary>
        /// <param name="availableFOMs">Available FOMs to select from.</param>
        /// <param name="heuristicRegistry">Registry of heuristics.</param>
        /// <returns>Initial set of candidates to explore.</returns>
        private List<List<IMutant>> InitializeStartingPoints(
            IReadOnlyCollection<IMutant> availableFOMs, 
            HeuristicRegistry heuristicRegistry)
        {
            var initialCandidates = new List<List<IMutant>>();
            var fomList = availableFOMs.ToList();
            
            // Create some random pairs as starting points
            const int randomPairsCount = 10;
            for (int i = 0; i < randomPairsCount && i < fomList.Count; i++)
            {
                var fom1 = fomList[_random.Next(fomList.Count)];
                var fom2 = fomList[_random.Next(fomList.Count)];
                
                // Ensure we have two different FOMs
                while (fom1.Id == fom2.Id && fomList.Count > 1)
                {
                    fom2 = fomList[_random.Next(fomList.Count)];
                }
                
                initialCandidates.Add(new List<IMutant> { fom1, fom2 });
            }
            
            // Use heuristic suggestions as additional starting points
            foreach (var fom in fomList.Take(5)) // Take a few FOMs as seed candidates
            {
                var suggestedCandidates = heuristicRegistry.SuggestNextCandidates(
                    new List<IMutant> { fom }, 
                    availableFOMs);
                
                initialCandidates.AddRange(suggestedCandidates);
                
                // Limit the number of initial candidates
                if (initialCandidates.Count >= _candidatePoolSize)
                {
                    break;
                }
            }
            
            return initialCandidates.Take(_candidatePoolSize).ToList();
        }
        
        /// <summary>
        /// Explore the neighborhood of a candidate by adding, removing, or swapping FOMs.
        /// </summary>
        /// <param name="candidate">The candidate HOM to explore.</param>
        /// <param name="availableFOMs">Available FOMs to select from.</param>
        /// <param name="heuristicRegistry">Registry of heuristics.</param>
        /// <returns>List of neighboring candidates.</returns>
        private List<List<IMutant>> ExploreNeighborhood(
            List<IMutant> candidate,
            IReadOnlyCollection<IMutant> availableFOMs,
            HeuristicRegistry heuristicRegistry)
        {
            var neighbors = new List<List<IMutant>>();
            
            // Get heuristic suggestions first (most intelligent approach)
            var suggestedNeighbors = heuristicRegistry.SuggestNextCandidates(candidate, availableFOMs);
            neighbors.AddRange(suggestedNeighbors);
            
            // If we don't have enough suggestions, perform systematic neighborhood exploration
            if (neighbors.Count < 5)
            {
                if (candidate.Count < _maxOrder)
                {
                    neighbors.AddRange(GenerateAdditionNeighbors(candidate, availableFOMs));
                }
                neighbors.AddRange(GenerateRemovalNeighbors(candidate));
                neighbors.AddRange(GenerateSwapNeighbors(candidate, availableFOMs));
            }
            
            // Take a random sample if we have too many neighbors
            const int maxNeighbors = 10;
            if (neighbors.Count > maxNeighbors)
            {
                return TakeRandomSample(neighbors, maxNeighbors);
            }
            
            return neighbors;
        }
        
        /// <summary>
        /// Generate neighbors by adding one FOM to the candidate.
        /// </summary>
        /// <param name="candidate">The candidate HOM.</param>
        /// <param name="availableFOMs">Available FOMs to select from.</param>
        /// <returns>List of new candidates with one FOM added.</returns>
        private List<List<IMutant>> GenerateAdditionNeighbors(
            List<IMutant> candidate, 
            IReadOnlyCollection<IMutant> availableFOMs)
        {
            var neighbors = new List<List<IMutant>>();
            
            // Only add if we're below max order
            if (candidate.Count < _maxOrder)
            {
                // Create a set of existing FOM IDs for quick lookup
                var existingFomIds = new HashSet<int>(candidate.Select(m => m.Id));
                
                // Take a random sample of FOMs to add
                const int maxAdditions = 5;
                var fomsToTry = TakeRandomSample(
                    availableFOMs.Where(m => !existingFomIds.Contains(m.Id)).ToList(), 
                    maxAdditions);
                
                foreach (var fom in fomsToTry)
                {
                    var newCandidate = new List<IMutant>(candidate) { fom };
                    neighbors.Add(newCandidate);
                }
            }
            
            return neighbors;
        }
        
        /// <summary>
        /// Generate neighbors by removing one FOM from the candidate.
        /// </summary>
        /// <param name="candidate">The candidate HOM.</param>
        /// <returns>List of new candidates with one FOM removed.</returns>
        private static List<List<IMutant>> GenerateRemovalNeighbors(List<IMutant> candidate)
        {
            var neighbors = new List<List<IMutant>>();
            
            // If candidate is of order 2 or below, do NOT generate any removal neighbors
            if (candidate.Count <= 2)
            {
                return neighbors;
            }

            for (var i = 0; i < candidate.Count; i++)
            {
                var newCandidate = new List<IMutant>(candidate);
                newCandidate.RemoveAt(i);
                neighbors.Add(newCandidate);            
            }            
            
            return neighbors;
        }
        
        /// <summary>
        /// Generate neighbors by swapping one FOM in the candidate with another from available FOMs.
        /// </summary>
        /// <param name="candidate">The candidate HOM.</param>
        /// <param name="availableFOMs">Available FOMs to select from.</param>
        /// <returns>List of new candidates with one FOM swapped.</returns>
        private List<List<IMutant>> GenerateSwapNeighbors(
            List<IMutant> candidate, 
            IReadOnlyCollection<IMutant> availableFOMs)
        {
            var neighbors = new List<List<IMutant>>();
            
            // Create a set of existing FOM IDs for quick lookup
            var existingFomIds = new HashSet<int>(candidate.Select(m => m.Id));
            
            // Take a random sample of FOMs to try swapping with
            const int maxSwaps = 3;
            var fomsToSwapWith = TakeRandomSample(
                availableFOMs.Where(m => !existingFomIds.Contains(m.Id)).ToList(), 
                maxSwaps);
            
            // For each position in the candidate
            for (int i = 0; i < candidate.Count && i < 2; i++) // Limit to first 2 positions for efficiency
            {
                // For each FOM to swap with
                foreach (var fom in fomsToSwapWith)
                {
                    var newCandidate = new List<IMutant>(candidate);
                    newCandidate[i] = fom;
                    neighbors.Add(newCandidate);
                }
            }
            
            return neighbors;
        }
        
        /// <summary>
        /// Score and rank candidates using the heuristic registry.
        /// </summary>
        /// <param name="candidates">List of candidate HOMs.</param>
        /// <param name="heuristicRegistry">Registry of heuristics.</param>
        /// <returns>Scored and ranked candidates.</returns>
        private List<ScoredCandidate> ScoreAndRankCandidates(
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
                
                double score = heuristicRegistry.ScoreCandidate(candidate);
                scoredCandidates.Add(new ScoredCandidate(candidate, score));
            }
            
            // Sort by score (descending)
            return scoredCandidates
                .OrderByDescending(sc => sc.Score)
                .ToList();
        }
        
        /// <summary>
        /// Take a random sample from a list.
        /// </summary>
        /// <typeparam name="T">Type of items in the list.</typeparam>
        /// <param name="items">List of items.</param>
        /// <param name="count">Number of items to sample.</param>
        /// <returns>Random sample of the specified count.</returns>
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
        /// Creates a unique key for a candidate HOM based on the IDs of its constituent mutants.
        /// </summary>
        /// <param name="candidate">The candidate HOM.</param>
        /// <returns>A string that uniquely identifies the candidate.</returns>
        private string GetCandidateKey(List<IMutant> candidate)
        {
            return string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id));
        }
        
        /// <summary>
        /// Class to represent a candidate with its score.
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
