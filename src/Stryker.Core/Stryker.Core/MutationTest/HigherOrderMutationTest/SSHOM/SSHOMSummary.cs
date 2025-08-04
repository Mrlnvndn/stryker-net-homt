using System.Collections.Generic;
using Stryker.Core.Mutants;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.SSHOM;

/// <summary>
/// Summary information about SSHOM analysis
/// </summary>
public class SSHOMSummary
{
    public int TotalSSHOMs { get; set; }
    public int TotalTestedCandidates { get; set; }
    public int TotalCreatedCandidates { get; set; }
    public IEnumerable<HigherOrderMutant> SSHOMCandidates { get; set; }
    public double SSHOMValidationRate { get; set; }
}
