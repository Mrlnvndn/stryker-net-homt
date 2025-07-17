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

        public override string Name => "MaxSizeLimit";
        
        public override double Weight => 0.5;

        public override bool IsFilteringHeuristic => true;
        
        public override bool IsFitnessScoringHeuristic => true;
        
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
        
        public override bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            return candidate == null || candidate.Count < 2 || candidate.Count > _maxSize;
        }
    }
}
