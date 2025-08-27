using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.Mutants;
using System.Collections.Generic;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Filters out Higher-Order Mutant candidates that have empty assessing tests.
    /// HOMs with empty assessing tests cannot be properly tested or killed, leading to 
    /// issues in the mutation testing process and duplicate warnings in VsTestRunner.
    /// This happens when constituent FOMs have no overlapping assessing tests (empty intersection).
    /// </summary>
    public class EmptyAssessingTestsFilterHeuristic : BaseHOMHeuristic
    {
        public override string Name => "EmptyAssessingTestsFilter";
        public override double Weight => 0.0; // Not used for scoring
        public override bool IsFilteringHeuristic => true;
        public override bool IsFitnessScoringHeuristic => false;
        public override bool IsSearchStrategyHeuristic => false;
        public override bool RequiresPreRun => false; // Can work with coverage analysis data

        public override void Initialize(IReadOnlyCollection<IMutant> availableFOMs, IStrykerOptions options, MutationTestInput input)
        {
            // No initialization needed for this heuristic
        }

        public override double ScoreCandidate(List<IMutant> candidate)
        {
            // Not used for scoring, but return 0 for invalid candidates
            return ShouldFilterCandidate(candidate) ? 0.0 : 1.0;
        }

        public override bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count < 2)
            {
                return true; // Filter out null or too small candidates
            }

            // Create a temporary HOM to calculate assessing tests intersection
            var tempHOM = new HigherOrderMutant(candidate);
            
            // Check if the HOM would have empty assessing tests
            var hasEmptyAssessingTests = tempHOM.AssessingTests?.IsEmpty ?? true;

            if (hasEmptyAssessingTests)
            {
                // Log for debugging - this helps identify problematic HOM candidates
                // Note: We don't have logger access in heuristics, but the filtering will prevent issues
                return true; // Filter out candidates with empty assessing tests
            }

            return false; // Don't filter - candidate is valid
        }

        public override List<List<IMutant>> SuggestNextCandidates(List<IMutant> currentCandidate, IReadOnlyCollection<IMutant> availableFOMs)
        {
            // This heuristic doesn't provide search guidance
            return [];
        }
    }
}
