using System;
using Stryker.Core.MutationTest.HigherOrderMutationTest.SSHOM;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest;

/// <summary>
/// Simple static collector for HOM/SSHOM metrics across the run.
/// This allows reporters to access HOM data after the run completes.
/// </summary>
public static class HomMetricsCollector
{
    public static HigherOrderMutation HomContext { get; set; }
    public static HOMGenerationResult GenerationResult { get; set; }
    public static SSHOMAnalysisResult SshomAnalysis { get; set; }
    public static TimeSpan TotalRunDuration { get; set; }
    public static int? RandomSeed { get; set; }

    public static void Reset()
    {
        HomContext = null;
        GenerationResult = null;
        SshomAnalysis = null;
        TotalRunDuration = TimeSpan.Zero;
        RandomSeed = null;
    }
}
