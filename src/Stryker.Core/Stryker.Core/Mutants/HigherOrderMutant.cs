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
        if (constituentMutants == null) throw new ArgumentNullException(nameof(constituentMutants));
        AlgorithmUsed = algorithmUsed ?? "Unknown";
        CreatedAt = DateTime.Now;
        
        // Initialize IMutant properties with sensible defaults
        ResultStatus = MutantStatus.Pending;
        KillingTests = TestIdentifierList.NoTest();
        
        // Note: ConstituentMutants will be set later in CreateDeepCopies method
        // after the HOM gets its ID assigned
        OriginalConstituentMutants = constituentMutants.ToList(); // Store originals temporarily
        Mutation = CreateCombinedMutation();
        
        // CoveringTests and AssessingTests will be calculated lazily when first accessed
        // This ensures constituent mutants have proper test data from coverage analysis
    }
    
    /// <summary>
    /// Creates deep copies of the constituent mutants with composite IDs.
    /// This method should be called after the HOM ID is assigned.
    /// </summary>
    /// <param name="idProvider">Provider for generating new IDs</param>
    public void CreateDeepCopies(IProvideId idProvider)
    {
        // Create deep copies with composite IDs
        ConstituentMutants = new List<IMutant>();
        
        foreach (var originalMutant in OriginalConstituentMutants)
        {
            if (originalMutant is Mutant mutant)
            {
                // Generate composite ID: combine original FOM ID with HOM ID
                var compositeId = idProvider.NextId();
                var constituentCopy = mutant.CreateConstituentCopy(compositeId);
                ConstituentMutants.Add(constituentCopy);
            }
            else
            {
                // Fallback for non-Mutant implementations - just use the original
                // This shouldn't happen in practice, but provides safety
                ConstituentMutants.Add(originalMutant);
            }
        }
        
        // Clear the temporary storage
        OriginalConstituentMutants = null;
    }

    /// <summary>
    /// Temporary storage for original constituent mutants before deep copying
    /// </summary>
    private List<IMutant> OriginalConstituentMutants { get; set; }

    /// <summary>
    /// The constituent first-order mutants that make up this HOM (as deep copies).
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
    /// Gets a string representation of the constituent mutant IDs.
    /// </summary>
    public string ConstituentMutantIdsString => string.Join(",", ConstituentMutants.Select(m => m.Id));

    /// <summary>
    /// Gets a string representation of the original FOM IDs.
    /// </summary>
    public string OriginalConstituentMutantIdsString
    {
        get
        {            
            if (ConstituentMutants?.Any() != true)
                return "";
                
            return ConstituentMutants.OfType<Mutant>()
                .Select(m => m.OriginalFomId?.ToString() ?? m.Id.ToString())
                .DefaultIfEmpty()
                .Aggregate((a, b) => $"{a},{b}");
        }
    }

    /// <summary>
    /// Gets the original FOM IDs that should be used for MutantControl activation.
    /// These are the IDs that the injected mutation code expects.
    /// </summary>
    public List<int> GetOriginalConstituentMutantIdsForActivation()
    {
        if (ConstituentMutants?.Any() != true)
            return [];
            
        return [.. ConstituentMutants.OfType<Mutant>().Select(m => m.OriginalFomId ?? m.Id)];
    }



    /// <summary>
    /// Gets the order (size) of this HOM.
    /// </summary>
    public int Order => ConstituentMutants?.Count ?? OriginalConstituentMutants?.Count ?? 0;

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

    public string DisplayName => $"HOM-{Id}: {OriginalConstituentMutantIdsString}";

    /// <summary>
    /// Calculates covering tests from constituent mutants using union (all tests that cover any constituent).
    /// </summary>
    private ITestIdentifiers CalculateCoveringTestsFromConstituents()
    {
        if (ConstituentMutants?.Count == 0)
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
            result = result.Intersect(ConstituentMutants[i].AssessingTests);
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
        Description = $"Combination of {Order} mutations: {OriginalConstituentMutantIdsString}"
    };

    public void AnalyzeTestRun(ITestIdentifiers failedTests, ITestIdentifiers resultRanTests, ITestIdentifiers timedOutTests, bool sessionTimedOut)
    {
        // First, update the HOM's own status based on its AssessingTests
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

        // Also update constituent mutants (deep copies) with the test results
        // This ensures that the deep copies reflect the results from HOM testing
        // and remain independent from the original FOMs
        if (ConstituentMutants != null)
        {
            foreach (var constituent in ConstituentMutants)
            {
                // Update each constituent mutant with the same test results
                // This allows each constituent to have independent results from HOM testing
                // while keeping the original FOMs unaffected
                constituent.AnalyzeTestRun(failedTests, resultRanTests, timedOutTests, sessionTimedOut);
            }
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

    public override string ToString() => $"HOM-{Id}: {ConstituentMutantIdsString} (Order: {Order}, Status: {ResultStatus}, Original FOMs: {OriginalConstituentMutantIdsString})";
}
