using Stryker.Abstractions;
using System.Collections.Generic;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that filters out candidates that exceed a maximum size limit.
    /// Based on research suggesting HOMs should be limited to 4 or fewer FOMs.
    /// This is primarily a FILTERING heuristic but also provides fitness scoring.
    /// </summary>
    public class MaxSizeLimitHeuristic : BaseHOMHeuristic
    {
        /// <summary>
        /// The maximum number of FOMs allowed in a HOM.
        /// </summary>
        private readonly int _maxSize;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="MaxSizeLimitHeuristic"/> class.
        /// </summary>
        /// <param name="maxSize">The maximum number of FOMs allowed in a HOM.</param>
        public MaxSizeLimitHeuristic(int maxSize = 4)
        {
            _maxSize = maxSize;
        }
        
        /// <summary>
        /// Gets the name of this heuristic.
        /// </summary>
        public override string Name => "MaxSizeLimit";
        
        /// <summary>
        /// Maximum size isn't primarily a scoring heuristic but a filter, so its weight in scoring is low.
        /// </summary>
        public override double Weight => 0.5;
        
        /// <summary>
        /// This is primarily a filtering heuristic.
        /// </summary>
        public override bool IsFilteringHeuristic => true;
        
        /// <summary>
        /// Also provides some fitness scoring with low weight.
        /// </summary>
        public override bool IsFitnessScoringHeuristic => true;
        
        /// <summary>
        /// Scores a candidate HOM based on its size relative to the maximum size.
        /// Smaller HOMs receive higher scores.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>A score between 0.0 and 1.0, with 1.0 for size 2 and decreasing scores for larger sizes.</returns>
        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count < 2)
            {
                return 0.0; // Invalid candidate
            }
            
            // Size 2 gets highest score, then diminishing returns
            if (candidate.Count <= _maxSize)
            {
                // Linear decrease from 1.0 for size 2 to 0.5 for max size
                return NormalizeScore(1.0 - ((candidate.Count - 2) * 0.5 / (_maxSize - 1)));
            }
            
            return 0.0; // Over max size
        }
        
        /// <summary>
        /// Filters out candidates that exceed the maximum size.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>True if the candidate exceeds the maximum size, false otherwise.</returns>
        public override bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            return candidate == null || candidate.Count < 2 || candidate.Count > _maxSize;
        }
    }
}
