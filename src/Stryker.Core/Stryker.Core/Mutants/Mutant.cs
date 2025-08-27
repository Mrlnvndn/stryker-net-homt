using Stryker.Abstractions;
using Stryker.Abstractions.Testing;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.Mutants;

/// <summary>
/// Represents a single mutation on domain level
/// </summary>
public class Mutant : IMutant
{
    public int Id { get; set; }

    public Mutation Mutation { get; set; }

    public MutantStatus ResultStatus { get; set; }

    public ITestIdentifiers CoveringTests { get; set; } = TestIdentifierList.NoTest();

    public ITestIdentifiers KillingTests { get; set; } = TestIdentifierList.NoTest();

    public ITestIdentifiers AssessingTests { get; set; } = TestIdentifierList.EveryTest();

    public string ResultStatusReason { get; set; }

    public bool CountForStats => ResultStatus != MutantStatus.CompileError && ResultStatus != MutantStatus.Ignored;

    public bool IsStaticValue { get; set; }

    public bool MustBeTestedInIsolation { get; set; }

    public string DisplayName => $"{Id}: {Mutation?.DisplayName}";

    /// <summary>
    /// The original FOM ID before becoming a constituent of a HOM. 
    /// This is used for tracing and debugging purposes.
    /// </summary>
    public int? OriginalFomId { get; set; }

    /// <summary>
    /// Creates a deep copy of this mutant as a constituent for a Higher-Order Mutant.
    /// The new mutant gets a composite ID combining the original FOM ID with the HOM ID.
    /// </summary>
    /// <param name="newId">The new composite ID for this constituent mutant</param>
    /// <param name="assessingTests">The assessing tests of the HOM</param>
    /// <returns>A deep copy of this mutant with the composite ID</returns>
    public Mutant CreateConstituentCopy(int newId)
    {
        return new Mutant
        {
            Id = newId,
            OriginalFomId = this.Id, // Preserve the original FOM ID
            Mutation = this.Mutation, // Mutation objects are immutable, so shallow copy is fine
            ResultStatus = MutantStatus.Pending, // Start fresh for HOM testing
            CoveringTests = this.CoveringTests, // These are immutable collections
            AssessingTests = this.AssessingTests, // These are immutable collections  
            KillingTests = TestIdentifierList.NoTest(), // Start fresh for HOM testing
            ResultStatusReason = null, // Start fresh for HOM testing
            IsStaticValue = this.IsStaticValue,
            MustBeTestedInIsolation = this.MustBeTestedInIsolation
        };
    }

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
}
