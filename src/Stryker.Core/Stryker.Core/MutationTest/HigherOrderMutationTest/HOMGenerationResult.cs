using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Core.Mutants;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest;

/// <summary>
/// Result of Higher-Order Mutant generation with essential metadata.
/// Contains only the data needed for subsequent planning to avoid recomputation.
/// </summary>
public class HOMGenerationResult(
    IReadOnlyList<HigherOrderMutant> homCandidates,
    IReadOnlyList<IMutant> missingFirstOrderMutants,
    bool isPreTestRun,
    string algorithmUsed,
    int heuristicsUsed,
    TimeSpan generationTime,
    IReadOnlyList<HOMGenerationAlgorithmStats> algorithmStats = null)
{
    /// <summary>
    /// Generated higher-order mutant candidates (HOMs).
    /// </summary>
    public IReadOnlyList<HigherOrderMutant> HomCandidates { get; } = homCandidates ?? Array.Empty<HigherOrderMutant>();

    /// <summary>
    /// Original FOMs that are not represented in any HOM (non-constituent FOMs).
    /// In validate mode this will usually be empty because all FOMs are kept anyway.
    /// </summary>
    public IReadOnlyList<IMutant> MissingFirstOrderMutants { get; } = missingFirstOrderMutants ?? Array.Empty<IMutant>();

    /// <summary>
    /// Whether this was a pre-test generation run.
    /// </summary>
    public bool IsPreTestRun { get; } = isPreTestRun;

    /// <summary>
    /// Name of the algorithm(s) used to produce the candidates.
    /// </summary>
    public string AlgorithmUsed { get; } = algorithmUsed ?? "Unknown";

    /// <summary>
    /// Number of heuristics applied during generation.
    /// </summary>
    public int HeuristicsUsed { get; } = heuristicsUsed;

    /// <summary>
    /// Total number of HOM candidates produced.
    /// </summary>
    public int CandidatesGenerated => HomCandidates.Count;

    /// <summary>
    /// Time spent generating HOM candidates.
    /// </summary>
    public TimeSpan GenerationTime { get; } = generationTime;

    /// <summary>
    /// Per-algorithm generation stats (kept, duplicates, filtered, etc.).
    /// </summary>
    public IReadOnlyList<HOMGenerationAlgorithmStats> AlgorithmStats { get; } = algorithmStats ?? Array.Empty<HOMGenerationAlgorithmStats>();

    /// <summary>
    /// Number of unique FOMs that appear in at least one HOM.
    /// </summary>
    public int UniqueFomsInHoms => HomCandidates.SelectMany(h => h.ConstituentMutants).Select(m => m.Id).Distinct().Count();
}
