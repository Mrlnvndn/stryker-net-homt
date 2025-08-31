using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;

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

            // Calculate assessing tests intersection
            var assessingTests = candidate[0].AssessingTests;
            
            // If first mutant has no assessing tests, the intersection will be empty
            if (assessingTests?.IsEmpty ?? true)
            {
                return true; // Filter out - no shared tests possible
            }
            
            // Calculate intersection of assessing tests from all constituent mutants
            for (var i = 1; i < candidate.Count; i++)
            {
                var mutantAssessingTests = candidate[i].AssessingTests;
                if (mutantAssessingTests?.IsEmpty ?? true)
                {
                    return true; // Filter out - one mutant has no tests, so intersection is empty
                }
                
                assessingTests = assessingTests.Intersect(mutantAssessingTests);
                
                // Early exit if intersection becomes empty
                if (assessingTests.IsEmpty)
                {
                    return true; // Filter out - no shared assessing tests
                }
            }

            return false; // Don't filter - candidate has non-empty assessing tests
        }

        public override List<List<IMutant>> SuggestNextCandidates(List<IMutant> currentCandidate, IReadOnlyCollection<IMutant> availableFOMs)
        {
            // This heuristic doesn't provide search guidance
            return [];
        }
    }
}
