using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest;
using System;
using System.Collections.Generic;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Base class for Higher-Order Mutant (HOM) heuristics that provides default implementations
    /// for common functionality.
    /// 
    /// HOM heuristics can be categorized into three primary types:
    /// 1. Filtering heuristics - Used to eliminate candidates that don't meet specific criteria
    /// 2. Fitness scoring heuristics - Used to evaluate and rank candidates
    /// 3. Search strategy heuristics - Used to guide the search process by suggesting new candidates
    /// 
    /// A heuristic can belong to multiple categories. For example, a heuristic might both filter
    /// candidates and provide a fitness score.
    /// </summary>
    public abstract class BaseHOMHeuristic : IHOMHeuristic
    {
        /// <summary>
        /// Gets the name of this heuristic.
        /// </summary>
        public abstract string Name { get; }
        
        /// <summary>
        /// Gets the weight of this heuristic when used in scoring (higher values have more influence).
        /// Default weight is 1.0.
        /// </summary>
        public virtual double Weight => 1.0;
        
        /// <summary>
        /// Indicates whether this heuristic can guide the search process by suggesting candidates.
        /// Default is false.
        /// </summary>
        public virtual bool IsSearchStrategyHeuristic => false;
        
        /// <summary>
        /// Indicates whether this heuristic is used for filtering candidates.
        /// Default is false.
        /// </summary>
        public virtual bool IsFilteringHeuristic => false;
        
        /// <summary>
        /// Indicates whether this heuristic is used for fitness scoring of candidates.
        /// Default is true if Weight > 0.
        /// </summary>
        public virtual bool IsFitnessScoringHeuristic => Weight > 0;

        /// <summary>
        /// Indicates wether this heuristic requires a pre-run to gather context data like what tests kill it.
        /// </summary>
        public virtual bool RequiresPreRun => false;

        /// <summary>
        /// Protected properties to store data used by the heuristic.
        /// </summary>
        protected IReadOnlyCollection<IMutant> AvailableFOMs { get; private set; }
        protected IStrykerOptions Options { get; private set; }
        protected MutationTestInput Input { get; private set; }
        
        /// <summary>
        /// Initializes the heuristic with context data.
        /// </summary>
        public virtual void Initialize(IReadOnlyCollection<IMutant> availableFOMs, IStrykerOptions options, MutationTestInput input)
        {
            AvailableFOMs = availableFOMs ?? throw new ArgumentNullException(nameof(availableFOMs));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Input = input ?? throw new ArgumentNullException(nameof(input));
            
            OnInitialized();
        }
        
        /// <summary>
        /// Called after the heuristic is initialized. Override to perform additional initialization.
        /// </summary>
        protected virtual void OnInitialized() { }
        
        /// <summary>
        /// Scores a candidate HOM based on this heuristic's criteria.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>A score between 0.0 and 1.0, where higher values indicate better candidates.</returns>
        public abstract double ScoreCandidate(List<IMutant> candidate);
        
        /// <summary>
        /// Determines whether a candidate should be filtered out based on this heuristic's criteria.
        /// Default implementation returns false (no filtering).
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>True if the candidate should be filtered out, false otherwise.</returns>
        public virtual bool ShouldFilterCandidate(List<IMutant> candidate) => false;
        
        /// <summary>
        /// Suggests next candidates to explore based on the current candidate and available FOMs.
        /// Default implementation returns an empty list.
        /// </summary>
        /// <param name="currentCandidate">The current candidate HOM.</param>
        /// <param name="availableFOMs">Available first-order mutants to consider.</param>
        /// <returns>A list of suggested candidate HOMs to explore next.</returns>
        public virtual List<List<IMutant>> SuggestNextCandidates(List<IMutant> currentCandidate, IReadOnlyCollection<IMutant> availableFOMs)
        {
            return new List<List<IMutant>>();
        }
        
        /// <summary>
        /// Ensures the score is within the valid range of 0.0 to 1.0.
        /// </summary>
        protected double NormalizeScore(double score) => Math.Max(0.0, Math.Min(1.0, score));
    }
}
