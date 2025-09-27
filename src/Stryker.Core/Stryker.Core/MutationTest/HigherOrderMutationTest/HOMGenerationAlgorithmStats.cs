using System;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest;

/// <summary>
/// Per-algorithm statistics captured during HOM candidate generation.
/// Mirrors the information logged by CreateCandidateHOMs for reporting.
/// </summary>
public class HOMGenerationAlgorithmStats
{
    public string AlgorithmName { get; init; }
    public int RawCandidates { get; init; }
    public int Kept { get; init; }
    public int DuplicateWithinAlgorithm { get; init; }
    public int DuplicateAcrossAlgorithms { get; init; }
    public int FilteredEmptyAssessing { get; init; }
    public int FilteredInvalidOrder { get; init; }

    // New: score metrics over the algorithm's produced candidates
    public double? HighestScore { get; init; }
    public double? LowestScore { get; init; }
    public double? AverageScore { get; init; }
    public double? MedianScore { get; init; }
}
