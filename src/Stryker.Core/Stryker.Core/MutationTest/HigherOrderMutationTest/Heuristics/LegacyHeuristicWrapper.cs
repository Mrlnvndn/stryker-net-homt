using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using System.Collections.Generic;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Wrapper for legacy IHOMHeuristic implementations to be compatible with the new framework.
    /// </summary>
    public class LegacyHeuristicWrapper : IHOMHeuristic
    {
        private readonly IHOMHeuristic _legacyHeuristic;

        /// <summary>
        /// Gets the name of this heuristic.
        /// </summary>
        public string Name => $"Legacy_{_legacyHeuristic.GetType().Name}";

        /// <summary>
        /// Gets the weight of this heuristic in overall scoring.
        /// Legacy heuristics are given medium weight by default.
        /// </summary>
        public double Weight => 1.0;

        /// <summary>
        /// Legacy heuristics cannot guide search.
        /// </summary>
        public bool CanGuideSearch => false;

        /// <summary>
        /// Initializes a new instance of the <see cref="LegacyHeuristicWrapper"/> class.
        /// </summary>
        /// <param name="legacyHeuristic">The legacy heuristic to wrap.</param>
        public LegacyHeuristicWrapper(IHOMHeuristic legacyHeuristic)
        {
            _legacyHeuristic = legacyHeuristic;
        }

        /// <summary>
        /// Scores a candidate using the legacy heuristic's evaluation method.
        /// </summary>
        /// <param name="candidate">The candidate to score.</param>
        /// <returns>A score between 0.0 and 1.0.</returns>
        public double ScoreCandidate(List<IMutant> candidate)
        {
            // Legacy heuristics don't have a standardized scoring method
            // Return a neutral score
            return 0.5;
        }

        /// <summary>
        /// Legacy heuristics don't have a filtering capability.
        /// </summary>
        /// <param name="candidate">The candidate to filter.</param>
        /// <returns>Always false to avoid filtering legacy-compatible candidates.</returns>
        public bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            return false;
        }

        /// <summary>
        /// Legacy heuristics can't suggest next candidates.
        /// </summary>
        /// <param name="currentCandidate">The current candidate.</param>
        /// <param name="availableFOMs">Available FOMs to consider.</param>
        /// <returns>An empty list since legacy heuristics can't guide search.</returns>
        public List<List<IMutant>> SuggestNextCandidates(List<IMutant> currentCandidate, IReadOnlyCollection<IMutant> availableFOMs)
        {
            return new List<List<IMutant>>();
        }

        /// <summary>
        /// Initializes the heuristic with the provided context.
        /// </summary>
        /// <param name="availableFOMs">The available FOMs.</param>
        /// <param name="options">The Stryker options.</param>
        /// <param name="mutationTestInput">The mutation test input.</param>
        public void Initialize(IReadOnlyCollection<IMutant> availableFOMs, IStrykerOptions options, MutationTestInput mutationTestInput)
        {
            // Legacy heuristics don't have initialization methods
        }
    }
}
