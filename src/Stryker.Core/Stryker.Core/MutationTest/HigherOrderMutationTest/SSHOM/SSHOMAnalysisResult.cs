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
    public SSHOMAnalysisResult(int sshomCount, int totalCandidates, TimeSpan analysisTime, Dictionary<int, int> sshomsByOrder = null)
    {
        SSHOMCount = sshomCount;
        TotalCandidates = totalCandidates;
        AnalysisTime = analysisTime;
        SSHOMsByOrder = sshomsByOrder ?? new Dictionary<int, int>();
    }

    public int SSHOMCount { get; }
    public int AnalyzedCount { get; }
    public int TotalCandidates { get; }
    public TimeSpan AnalysisTime { get; }
    
    /// <summary>
    /// Dictionary mapping order (2, 3, 4, etc.) to the number of SSHOMs found for that order
    /// </summary>
    public Dictionary<int, int> SSHOMsByOrder { get; }
    
    public double SSHOMRate => AnalyzedCount > 0 ? (double)SSHOMCount / AnalyzedCount : 0.0;
    
    /// <summary>
    /// Gets the number of SSHOMs found for a specific order
    /// </summary>
    /// <param name="order">The order (2, 3, 4, etc.)</param>
    /// <returns>Number of SSHOMs for that order, or 0 if none found</returns>
    public int GetSSHOMCountForOrder(int order) => SSHOMsByOrder.GetValueOrDefault(order, 0);
    
    /// <summary>
    /// Gets a formatted string of SSHOM counts by order
    /// </summary>
    /// <returns>String like "2nd: 5, 3rd: 2, 4th: 1"</returns>
    public string GetSSHOMBreakdownString()
    {
        if (!SSHOMsByOrder.Any()) return "None";
        
        return string.Join(", ", SSHOMsByOrder.OrderBy(kvp => kvp.Key)
            .Select(kvp => $"{GetOrderSuffix(kvp.Key)}: {kvp.Value}"));
    }
    
    private static string GetOrderSuffix(int order) => order switch
    {
        2 => "2nd",
        3 => "3rd", 
        _ => $"{order}th"
    };
}
