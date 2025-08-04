using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Abstractions.Testing;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.Mutants;

/// <summary>
/// Represents a Higher-Order Mutant that combines multiple first-order mutants.
/// </summary>
public class HigherOrderMutant : IMutant
{
    public HigherOrderMutant(List<IMutant> constituentMutants, string algorithmUsed = null)
    {
        ConstituentMutants = constituentMutants ?? throw new ArgumentNullException(nameof(constituentMutants));
        AlgorithmUsed = algorithmUsed ?? "Unknown";
        CreatedAt = DateTime.Now;
        
        // Initialize IMutant properties with sensible defaults
        ResultStatus = MutantStatus.Pending;
        CoveringTests = TestIdentifierList.NoTest();
        KillingTests = TestIdentifierList.NoTest();
        AssessingTests = TestIdentifierList.EveryTest();
    }

    /// <summary>
    /// The constituent first-order mutants that make up this HOM.
    /// </summary>
    public List<IMutant> ConstituentMutants { get; set; }

    /// <summary>
    /// The algorithm that generated this candidate.
    /// </summary>
    public string AlgorithmUsed { get; }

    /// <summary>
    /// When this candidate was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Whether this HOM has been tested.
    /// </summary>
    public bool HasBeenTested => KillingTests != null && ResultStatus != MutantStatus.Pending;

    /// <summary>
    /// Whether this HOM is a validated SSHOM.
    /// </summary>
    public bool IsValidatedSSHOM { get; set; }

    /// <summary>
    /// The result of SSHOM validation if performed.
    /// </summary>
    public string SSHOMValidationResult { get; set; }

    /// <summary>
    /// Gets a string representation of the mutant IDs for logging.
    /// </summary>
    public string MutantIdsString => string.Join(",", ConstituentMutants.Select(m => m.Id));

    /// <summary>
    /// Gets the order (size) of this HOM.
    /// </summary>
    public int Order => ConstituentMutants.Count;

    // IMutant implementation

    public int Id { get; set; }
    public Mutation Mutation { get; set; }
    public MutantStatus ResultStatus { get; set; }
    public string ResultStatusReason { get; set; }
    public ITestIdentifiers CoveringTests { get; set; }
    public ITestIdentifiers KillingTests { get; set; }
    public ITestIdentifiers AssessingTests { get; set; }
    public bool CountForStats => ResultStatus != MutantStatus.CompileError && ResultStatus != MutantStatus.Ignored;
    public bool IsStaticValue { get; set; }
    public bool MustBeTestedInIsolation { get; set; }

    public string DisplayName => $"HOM-{Id}: {string.Join("+", ConstituentMutants.Select(m => m.Id))}";

    public void AnalyzeTestRun(ITestIdentifiers failedTests, ITestIdentifiers resultRanTests, ITestIdentifiers timedOutTests, bool sessionTimedOut)
    {
        if (AssessingTests.ContainsAny(failedTests))
        {
            ResultStatus = MutantStatus.Killed;
            KillingTests = AssessingTests.Intersect(failedTests);
        }
        else if (AssessingTests.ContainsAny(timedOutTests) || sessionTimedOut)
        {
            ResultStatus = MutantStatus.Timeout;
        }
        else if (resultRanTests.IsEveryTest || !resultRanTests.IsEveryTest && AssessingTests.IsIncludedIn(resultRanTests))
        {
            ResultStatus = MutantStatus.Survived;
        }
    }

    public override bool Equals(object obj)
    {
        if (obj is HigherOrderMutant other)
        {
            return Id == other.Id;
        }
        return false;
    }

    public override int GetHashCode() => Id.GetHashCode();

    public override string ToString() => $"HOM-{Id}: {MutantIdsString} (Order: {Order}, Status: {ResultStatus})";
}
