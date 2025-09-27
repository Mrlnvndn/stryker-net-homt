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
        private readonly int _maxSize = 4;

        public override string Name => "MaxSizeLimit";
        
        public override double Weight => 0.5;

        public override bool IsFilteringHeuristic => true;
        
        public override bool IsFitnessScoringHeuristic => true;
        
        public override double ScoreCandidate(List<IMutant> candidate) => ShouldFilterCandidate(candidate) ? 0.0 : 1.0;

        public override bool ShouldFilterCandidate(List<IMutant> candidate) => candidate == null || candidate.Count > _maxSize;
    }
}
