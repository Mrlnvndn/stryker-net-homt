using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using System.Collections.Generic;
using System.Linq;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Registry for managing and configuring HOM heuristics.
    /// </summary>
    public class HeuristicRegistry
    {
        private readonly List<IHOMHeuristic> _registeredHeuristics = new();
        
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
        /// <param name="registerDefaultHeuristics">Whether to register the default heuristics.</param>
        public HeuristicRegistry(IReadOnlyCollection<IMutant> availableFOMs, IStrykerOptions options, MutationTestInput mutationTestInput, bool registerDefaultHeuristics = true)
        {
            // Register default heuristics with default weights
            if (registerDefaultHeuristics)
            {
                RegisterDefaultHeuristics();
            }

            // Initialize all registered heuristics
            foreach (var heuristic in _registeredHeuristics)
            {
                heuristic.Initialize(availableFOMs, options, mutationTestInput);
            }
        }

        /// <summary>
        /// Registers a new heuristic.
        /// </summary>
        /// <param name="heuristic">The heuristic to register.</param>
        public void RegisterHeuristic(IHOMHeuristic heuristic) => 
            _registeredHeuristics.Add(heuristic);

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
                totalScore += score * heuristic.Weight;
                totalWeight += heuristic.Weight;
            }
            
            return totalWeight > 0 ? totalScore / totalWeight : 0.5;
        }
        
        /// <summary>
        /// Determines whether a candidate HOM should be filtered out.
        /// </summary>
        /// <param name="candidate">The candidate HOM to check.</param>
        /// <returns>True if the candidate should be filtered out, false otherwise.</returns>
        public bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            // If any heuristic says filter, then filter
            return GetFilteringHeuristics().Any(h => h.ShouldFilterCandidate(candidate));
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
        private void RegisterDefaultHeuristics()
        {
            RegisterHeuristic(new MaxSizeLimitHeuristic());
            RegisterHeuristic(new CodeLocationHeuristic());
            RegisterHeuristic(new HardToKillHeuristic());
            RegisterHeuristic(new MutatorTypeHeuristic());
            RegisterHeuristic(new DependencyHeuristic());
            RegisterHeuristic(new SSHOMExpanderHeuristic());
            RegisterHeuristic(new WeakMutatorFilterHeuristic());
            RegisterHeuristic(new SyntaxNodeConflictHeuristic());
        }
    }
}
