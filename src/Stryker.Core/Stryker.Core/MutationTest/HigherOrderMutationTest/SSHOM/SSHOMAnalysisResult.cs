using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.SSHOM;

/// <summary>
/// Represents the result of SSHOM analysis
/// </summary>
public class SSHOMAnalysisResult
{
    public SSHOMAnalysisResult(int sshomCount, int analyzedCount, int totalCandidates, TimeSpan analysisTime)
    {
        SSHOMCount = sshomCount;
        AnalyzedCount = analyzedCount;
        TotalCandidates = totalCandidates;
        AnalysisTime = analysisTime;
    }

    public int SSHOMCount { get; }
    public int AnalyzedCount { get; }
    public int TotalCandidates { get; }
    public TimeSpan AnalysisTime { get; }
    public double SSHOMRate => AnalyzedCount > 0 ? (double)SSHOMCount / AnalyzedCount : 0.0;
}
