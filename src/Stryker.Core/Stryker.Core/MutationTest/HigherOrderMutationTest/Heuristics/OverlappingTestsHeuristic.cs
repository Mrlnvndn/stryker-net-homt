using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;

/// <summary>
/// Heuristic that evaluates HOM candidates based on overlapping covering tests.
/// 
/// This heuristic is crucial for SSHOM detection because:
/// 1. SSHOMs are defined by having a proper subset of killing tests compared to their constituents
/// 2. Higher overlap in covering tests increases the likelihood of shared killing tests
/// 3. Candidates with good covering test overlap are more likely to become valid SSHOMs
/// 
/// The scoring favors candidates where:
/// - Constituent mutants have high overlap in their covering tests
/// - The intersection of covering tests is substantial
/// - The potential for SSHOM validation is maximized
/// </summary>
public class OverlappingTestsHeuristic : BaseHOMHeuristic
{
    public override string Name => "OverlappingTests";

    public override double Weight => 1.0;

    public override bool IsFitnessScoringHeuristic => true;

    /// <summary>
    /// Scores a candidate HOM based on the overlap of covering tests among its constituent mutants.
    /// 
    /// The scoring algorithm:
    /// 1. Calculates the intersection of covering tests across all constituent mutants
    /// 2. Calculates the union of covering tests across all constituent mutants  
    /// 3. Uses Jaccard similarity coefficient: |intersection| / |union|
    /// 4. Applies weighting based on the absolute size of the intersection
    /// 
    /// Higher scores indicate better SSHOM potential.
    /// </summary>
    /// <param name="candidate">The candidate HOM to evaluate</param>
    /// <returns>A score between 0.0 and 1.0, where higher values indicate better overlap</returns>
    public override double ScoreCandidate(List<IMutant> candidate)
    {
        if (candidate == null || candidate.Count < 2)
        {
            return 0.0; // Cannot be a HOM with less than 2 mutants
        }

        // Get covering tests for each constituent mutant
        var coveringTestSets = candidate.Select(m => m.CoveringTests).ToList();
        
        // Handle edge cases
        if (coveringTestSets.Any(tests => tests == null || tests.IsEmpty))
        {
            return 0.0; // If any mutant has no covering tests, no overlap is possible
        }

        // Calculate intersection (tests that cover ALL constituent mutants)
        var intersection = coveringTestSets[0];
        for (var i = 1; i < coveringTestSets.Count; i++)
        {
            intersection = intersection.Intersect(coveringTestSets[i]);
        }

        // If no intersection, score is 0
        if (intersection.IsEmpty)
        {
            return 0.0;
        }

        // Calculate union (all tests that cover ANY constituent mutant and in turn the tests which cover the HOM)
        var union = coveringTestSets[0];
        for (var i = 1; i < coveringTestSets.Count; i++)
        {
            union = union.Merge(coveringTestSets[i]);
        }

        // Calculate Jaccard similarity coefficient
        var intersectionSize = intersection.GetIdentifiers().Count();
        var unionSize = union.GetIdentifiers().Count();
        
        if (unionSize == 0)
        {
            return 0.0;
        }

        var jaccardSimilarity = (double)intersectionSize / unionSize;

        // Apply size weighting - favor candidates with larger absolute intersections
        // This helps prioritize candidates that not only have good relative overlap
        // but also sufficient absolute coverage for meaningful SSHOM validation
        var sizeWeight = CalculateSizeWeight(intersectionSize);
        
        // Combine Jaccard similarity with size weighting
        var finalScore = jaccardSimilarity * sizeWeight;

        return NormalizeScore(finalScore);
    }

    /// <summary>
    /// Calculates a weighting factor based on the absolute size of the intersection.
    /// This helps prioritize candidates with meaningful test coverage over those with
    /// high relative overlap but very small absolute numbers.
    /// </summary>
    /// <param name="intersectionSize">The number of tests in the intersection</param>
    /// <returns>A weight factor between 0.1 and 1.0</returns>
    private static double CalculateSizeWeight(int intersectionSize) => intersectionSize switch
    {
        0 => 0.0,
        1 => 0.3, // Single test overlap is weak for SSHOM validation
        <= 3 => 0.6, // Small but reasonable overlap
        <= 10 => 0.8, // Good overlap size
        _ => 1.0 // Excellent overlap size
    };

    /// <summary>
    /// Provides additional filtering based on minimum overlap requirements.
    /// Can be enabled to filter out candidates with insufficient covering test overlap.
    /// </summary>
    /// <param name="candidate">The candidate HOM to evaluate</param>
    /// <returns>True if the candidate should be filtered out due to insufficient overlap</returns>
    public override bool ShouldFilterCandidate(List<IMutant> candidate)
    {
        // Optional: Enable filtering for candidates with no covering test overlap
        // This can be useful to avoid creating HOMs that have no potential for SSHOM validation
        
        if (candidate == null || candidate.Count < 2)
        {
            return true; // Filter out invalid candidates
        }

        // Check if there's any covering test overlap at all
        var coveringTestSets = candidate.Select(m => m.CoveringTests).ToList();
        
        if (coveringTestSets.Any(tests => tests == null || tests.IsEmpty))
        {
            return true; // Filter out if any mutant has no covering tests
        }

        // Calculate intersection
        var intersection = coveringTestSets[0];
        for (var i = 1; i < coveringTestSets.Count; i++)
        {
            intersection = intersection.Intersect(coveringTestSets[i]);
        }

        // Filter out candidates with no overlapping covering tests
        // These have zero potential for becoming SSHOMs
        return intersection.IsEmpty;
    }

    /// <summary>
    /// For debugging and analysis - provides detailed overlap information
    /// </summary>
    public string GetOverlapAnalysis(List<IMutant> candidate)
    {
        if (candidate == null || candidate.Count < 2)
        {
            return "Invalid candidate for overlap analysis";
        }

        var coveringTestSets = candidate.Select(m => m.CoveringTests).ToList();
        
        var intersection = coveringTestSets[0];
        for (var i = 1; i < coveringTestSets.Count; i++)
        {
            intersection = intersection.Intersect(coveringTestSets[i]);
        }

        var union = coveringTestSets[0];
        for (var i = 1; i < coveringTestSets.Count; i++)
        {
            union = union.Merge(coveringTestSets[i]);
        }

        var intersectionSize = intersection.GetIdentifiers().Count();
        var unionSize = union.GetIdentifiers().Count();
        var jaccardSimilarity = unionSize > 0 ? (double)intersectionSize / unionSize : 0.0;

        return $"Overlap Analysis - Intersection: {intersectionSize}, Union: {unionSize}, " +
               $"Jaccard: {jaccardSimilarity:F3}, Score: {ScoreCandidate(candidate):F3}";
    }
}
