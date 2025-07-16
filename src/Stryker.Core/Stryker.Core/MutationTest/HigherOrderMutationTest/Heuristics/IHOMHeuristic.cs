using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest;
using System.Collections.Generic;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Interface for heuristics that can guide HOM search algorithms.
    /// Heuristics can be used for scoring candidates, filtering out invalid candidates,
    /// or providing search guidance for generating new candidates.
    /// </summary>
    public interface IHOMHeuristic
    {
        /// <summary>
        /// Gets the name of this heuristic.
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Gets the weight of this heuristic when used in scoring (higher values have more influence).
        /// </summary>
        double Weight { get; }
        
        /// <summary>
        /// Indicates whether this heuristic can guide the search process by suggesting candidates.
        /// </summary>
        bool IsSearchStrategyHeuristic { get; }
        
        /// <summary>
        /// Indicates whether this heuristic is used for filtering candidates.
        /// </summary>
        bool IsFilteringHeuristic { get; }
        
        /// <summary>
        /// Indicates whether this heuristic is used for fitness scoring of candidates.
        /// </summary>
        bool IsFitnessScoringHeuristic { get; }

        /// <summary>
        /// Initializes the heuristic with context data.
        /// </summary>
        /// <param name="availableFOMs">All available first-order mutants that can be used.</param>
        /// <param name="options">Stryker configuration options.</param>
        /// <param name="input">Mutation test input for additional context.</param>
        void Initialize(IReadOnlyCollection<IMutant> availableFOMs, IStrykerOptions options, MutationTestInput input);
        
        /// <summary>
        /// Scores a candidate HOM based on this heuristic's criteria.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>A score between 0.0 and 1.0, where higher values indicate better candidates.</returns>
        double ScoreCandidate(List<IMutant> candidate);
        
        /// <summary>
        /// Determines whether a candidate should be filtered out based on this heuristic's criteria.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>True if the candidate should be filtered out, false otherwise.</returns>
        bool ShouldFilterCandidate(List<IMutant> candidate);
        
        /// <summary>
        /// Suggests next candidates to explore based on the current candidate and available FOMs.
        /// This is used primarily by heuristics that guide the search process.
        /// </summary>
        /// <param name="currentCandidate">The current candidate HOM.</param>
        /// <param name="availableFOMs">Available first-order mutants to consider.</param>
        /// <returns>A list of suggested candidate HOMs to explore next.</returns>
        List<List<IMutant>> SuggestNextCandidates(List<IMutant> currentCandidate, IReadOnlyCollection<IMutant> availableFOMs);
    }
}