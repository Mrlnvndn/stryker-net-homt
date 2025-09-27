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
    public HigherOrderMutant(List<IMutant> constituentMutants, string algorithmUsed = null, double? predictedScore = null)
    {
        ConstituentMutants = constituentMutants ?? throw new ArgumentNullException(nameof(constituentMutants));
        AlgorithmUsed = algorithmUsed ?? "Unknown";
        CreatedAt = DateTime.Now;
        PredictedScore = predictedScore;

        // Initialize IMutant properties with sensible defaults
        ResultStatus = MutantStatus.Pending;
        KillingTests = TestIdentifierList.NoTest();
        Mutation = CreateCombinedMutation();

        // CoveringTests and AssessingTests will be calculated lazily when first accessed
        // This ensures constituent mutants have proper test data from coverage analysis
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
    /// Optional predicted/heuristic score assigned by the generating algorithm.
    /// </summary>
    public double? PredictedScore { get; set; }

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
    public string ConstituentMutantIdsString => string.Join(",", ConstituentMutants.Select(m => m.Id));

    /// <summary>
    /// Gets the order (size) of this HOM.
    /// </summary>
    public int Order => ConstituentMutants.Count;

    // IMutant implementation
    public int Id { get; set; }
    public Mutation Mutation { get; set; }
    public MutantStatus ResultStatus { get; set; }
    public string ResultStatusReason { get; set; }
    private ITestIdentifiers _coveringTests;
    private ITestIdentifiers _assessingTests;
    public ITestIdentifiers CoveringTests
    {
        get => _coveringTests ??= CalculateCoveringTestsFromConstituents();
        set => _coveringTests = value;
    }

    public ITestIdentifiers AssessingTests
    {
        get => _assessingTests ??= CalculateAssessingTestsFromConstituents();
        set => _assessingTests = value;
    }
    public ITestIdentifiers KillingTests { get; set; }
    public bool CountForStats => ResultStatus != MutantStatus.CompileError && ResultStatus != MutantStatus.Ignored;
    public bool IsStaticValue { get; set; }
    public bool MustBeTestedInIsolation { get; set; }

    public string DisplayName => $"HOM-{Id}: {string.Join("+", ConstituentMutants.Select(m => m.Id))}";

    /// <summary>
    /// Calculates covering tests from constituent mutants using union (all tests that cover any constituent).
    /// </summary>
    private ITestIdentifiers CalculateCoveringTestsFromConstituents()
    {
        if (!ConstituentMutants.Any())
        {
            return TestIdentifierList.NoTest();
        }
        // Start with the first mutant's covering tests
        var result = ConstituentMutants[0].CoveringTests;

        // Union with all other constituent mutants' covering tests
        for (var i = 1; i < ConstituentMutants.Count; i++)
        {
            result = result.Merge(ConstituentMutants[i].CoveringTests);
        }

        return result;
    }

    /// <summary>
    /// Calculates assessing tests from constituent mutants using intersection (only tests that assess all constituents).
    /// This is critical for BuildMutantGroupsForTest compatibility - it determines which tests need to run for this HOM.
    /// </summary>
    private ITestIdentifiers CalculateAssessingTestsFromConstituents()
    {
        if (!ConstituentMutants.Any())
            return TestIdentifierList.EveryTest();

        // Start with the first mutant's assessing tests
        var result = ConstituentMutants[0].AssessingTests;

        // Intersect with all other constituent mutants' assessing tests
        // Only tests that assess ALL constituent mutants can properly assess the HOM
        for (var i = 1; i < ConstituentMutants.Count; i++)
        {
            result = result?.Intersect(ConstituentMutants[i].AssessingTests);
        }

        return result;
    }

    /// <summary>
    /// Creates a combined mutation description for this HOM.
    /// </summary>
    private Mutation CreateCombinedMutation() => new()
    {
        DisplayName = $"Higher-Order Mutation (Order {Order})",
        Type = Mutator.HigherOrderMutant, // Use the new specific mutator type for HOMs
        Description = $"Combination of {Order} mutations: {string.Join(", ", ConstituentMutants.Select(m => m.Id))}"
    };

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
        else if (resultRanTests.IsEveryTest || (!resultRanTests.IsEveryTest && AssessingTests.IsIncludedIn(resultRanTests)))
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

    public override string ToString() => $"HOM-{Id}: {ConstituentMutantIdsString} (Order: {Order}, Status: {ResultStatus})";
}
