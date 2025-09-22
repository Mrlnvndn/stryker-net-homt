using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.MutationTest;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Registry for managing and configuring HOM heuristics.
    /// </summary>
    public class HeuristicRegistry
    {
        private readonly List<IHOMHeuristic> _registeredHeuristics = new();

        private readonly IReadOnlyCollection<IMutant> _availableFOMs;

        private readonly MutationTestInput _mutationTestInput;

        private IStrykerOptions _strykerOptions;

        /// <summary>
        /// Gets the registered heuristics.
        /// </summary>
        public IReadOnlyCollection<IHOMHeuristic> RegisteredHeuristics => _registeredHeuristics.AsReadOnly();

        /// <summary>
        /// Initializes a new instance of the <see cref="HeuristicRegistry"/> class with default heuristics.
        /// </summary>
        /// <param name="availableFOMs">The available first-order mutants.</param>
        /// <param name="options">The stryker options.</param>
        /// <param name="mutationTestInput">The mutation test input.</param>
        /// <param name="registerAllHeuristics">Whether to register the default heuristics.</param>
        public HeuristicRegistry(IReadOnlyCollection<IMutant> availableFOMs, IStrykerOptions options, MutationTestInput mutationTestInput, bool registerAllHeuristics = false)
        {
            _availableFOMs = availableFOMs ?? throw new ArgumentNullException(nameof(availableFOMs));
            _mutationTestInput = mutationTestInput ?? throw new ArgumentNullException(nameof(mutationTestInput));
            _strykerOptions = options ?? throw new ArgumentNullException(nameof(options));

            // Register default heuristics with default weights
            if (registerAllHeuristics)
            {
                RegisterAllHeuristics();
            }
        }

        /// <summary>
        /// Registers a new heuristic.
        /// </summary>
        /// <param name="heuristic">The heuristic to register.</param>
        public void RegisterHeuristic(IHOMHeuristic heuristic)
        {
            _registeredHeuristics.Add(heuristic);
            heuristic.Initialize(_availableFOMs, _strykerOptions, _mutationTestInput);
        }
        /// <summary>
        /// Gets all scoring heuristics.
        /// </summary>
        /// <returns>A collection of scoring heuristics.</returns>
        public IEnumerable<IHOMHeuristic> GetScoringHeuristics() => 
            _registeredHeuristics.Where(h => h.IsFitnessScoringHeuristic && h.Weight > 0);
        
        /// <summary>
        /// Gets all filtering heuristics.
        /// </summary>
        /// <returns>A collection of filtering heuristics.</returns>
        public IEnumerable<IHOMHeuristic> GetFilteringHeuristics() =>
            _registeredHeuristics.Where(h => h.IsFilteringHeuristic);

        /// <summary>
        /// Gets all search guidance heuristics.
        /// </summary>
        /// <returns>A collection of search guidance heuristics.</returns>
        public IEnumerable<IHOMHeuristic> GetSearchGuidanceHeuristics() =>
            _registeredHeuristics.Where(h => h.IsSearchStrategyHeuristic);

        /// <summary>
        /// Gets a specific heuristic by type.
        /// </summary>
        /// <typeparam name="T">The type of heuristic to find</typeparam>
        /// <returns>The heuristic instance, or null if not found</returns>
        public T GetHeuristic<T>() where T : class, IHOMHeuristic =>
            _registeredHeuristics.OfType<T>().FirstOrDefault();
            
        /// <summary>
        /// Scores a candidate HOM using all registered scoring heuristics.
        /// </summary>
        /// <param name="candidate">The candidate HOM to score.</param>
        /// <returns>A weighted score between 0.0 and 1.0.</returns>
        public double ScoreCandidate(List<IMutant> candidate)
        {
            var scoringHeuristics = GetScoringHeuristics().ToList();
            
            if (scoringHeuristics.Count == 0)
            {
                return 0.5; // Neutral score if no scoring heuristics
            }
            
            double totalScore = 0;
            double totalWeight = 0;
            
            foreach (var heuristic in scoringHeuristics)
            {
                double score = heuristic.ScoreCandidate(candidate);
                
                // Add NaN protection here to prevent propagation
                if (double.IsNaN(score) || double.IsInfinity(score))
                {
                    // Use reflection to get the actual type name for debugging
                    var heuristicTypeName = heuristic.GetType().Name;
                    Console.WriteLine($"WARNING: Heuristic {heuristicTypeName} returned {score} for candidate {string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id))} (size: {candidate.Count})");
                    
                    // Treat NaN/Infinity as 0 to prevent contamination
                    score = 0.0;
                }
                
                totalScore += score * heuristic.Weight;
                totalWeight += heuristic.Weight;
            }
            
            var finalScore = totalWeight > 0 ? totalScore / totalWeight : 0.5;
            
            // Final protection
            if (double.IsNaN(finalScore) || double.IsInfinity(finalScore))
            {
                Console.WriteLine($"ERROR: Final score is {finalScore} for candidate {string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id))} (size: {candidate.Count})");
                return 0.0; // Safe fallback
            }
            
            return finalScore;
        }

        /// <summary>
        /// Determines whether a candidate HOM should be filtered out.
        /// </summary>
        /// <param name="candidate">The candidate HOM to check.</param>
        /// <param name="relaxed">If true, uses more lenient filtering criteria.</param>
        /// <returns>True if the candidate should be filtered out, false otherwise.</returns>
        public bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            var filteringHeuristics = GetFilteringHeuristics();
            
            // If any heuristic says filter, then filter
            return filteringHeuristics.Any(h => h.ShouldFilterCandidate(candidate));
        }
        
        /// <summary>
        /// Gets suggested next candidate HOMs to explore.
        /// </summary>
        /// <param name="currentCandidate">The current candidate HOM.</param>
        /// <param name="availableFOMs">Available first-order mutants to consider.</param>
        /// <returns>A list of suggested candidate HOMs to explore next.</returns>
        public List<List<IMutant>> SuggestNextCandidates(List<IMutant> currentCandidate, IReadOnlyCollection<IMutant> availableFOMs)
        {
            var suggestions = new List<List<IMutant>>();
            
            foreach (var heuristic in GetSearchGuidanceHeuristics())
            {
                suggestions.AddRange(heuristic.SuggestNextCandidates(currentCandidate, availableFOMs));
            }
            
            return suggestions;
        }
        
        /// <summary>
        /// Registers the default set of heuristics.
        /// </summary>
        private void RegisterAllHeuristics()
        {
            RegisterHeuristic(new CodeLocationHeuristic());
            RegisterHeuristic(new EmptyAssessingTestsHeuristic());
            RegisterHeuristic(new HardToKillHeuristic());
            RegisterHeuristic(new MaxSizeLimitHeuristic());
            RegisterHeuristic(new MutatorTypeHeuristic());
            RegisterHeuristic(new OverlappingTestsHeuristic());
            RegisterHeuristic(new SSHOMExpanderHeuristic());
            RegisterHeuristic(new SyntaxNodeConflictHeuristic());
            RegisterHeuristic(new WeakMutatorFilterHeuristic());
        }
    }
}
