using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;
using Stryker.TestRunner.VsTest;

namespace Stryker.TestRunner.VsTest.UnitTest;

/// <summary>
/// Tests specifically for the VsTestRunner dual-mode HOM handling.
/// Verifies that HigherOrderMutants are correctly handled with both HOM and constituent FOM IDs
/// in the mutantTestsMap for XML generation, supporting both status tracking and MutantControl activation.
/// </summary>
[TestClass]
public class VsTestRunnerHOMExpansionTests : VsTestMockingHelper
{
    [TestMethod]
    public void TestCases_WithRegularFOM_ShouldAddToMutantTestsMap()
    {
        // Arrange
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        var fom = CreateMutant(1, MutantStatus.Pending, new[] { "Test1", "Test2" });
        var mutants = new List<IMutant> { fom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act - Use reflection to call private TestCases method
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert
        mutantTestsMap.ShouldContainKey(1);
        mutantTestsMap[1].GetIdentifiers().ShouldBe(new[] { "Test1", "Test2" });
        testCases.ShouldContain("Test1");
        testCases.ShouldContain("Test2");
    }

    [TestMethod]
    public void TestCases_WithHigherOrderMutant_ShouldIncludeBothHOMAndConstituentFOMs()
    {
        // Arrange - Create a HOM with two constituent FOMs
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        var fom1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1", "Test2" });
        var fom2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test1", "Test3" });
        var hom = new HigherOrderMutant(new List<IMutant> { fom1, fom2 }) { Id = 100 };
        
        var mutants = new List<IMutant> { hom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert - Should contain BOTH HOM and constituent FOM IDs (dual-mode)
        mutantTestsMap.ShouldContainKey(100, "Should contain HOM ID for status tracking and SSHOM validation");
        mutantTestsMap.ShouldContainKey(1, "Should contain first FOM ID for MutantControl activation");
        mutantTestsMap.ShouldContainKey(2, "Should contain second FOM ID for MutantControl activation");
        
        // All should have the same tests (HOM's assessing tests)
        var expectedTests = hom.AssessingTests.GetIdentifiers().ToArray();
        mutantTestsMap[100].GetIdentifiers().ShouldBe(expectedTests, "HOM should have correct assessing tests");
        mutantTestsMap[1].GetIdentifiers().ShouldBe(expectedTests, "FOM 1 should inherit HOM's assessing tests");
        mutantTestsMap[2].GetIdentifiers().ShouldBe(expectedTests, "FOM 2 should inherit HOM's assessing tests");
        
        // Should have 3 total entries: 1 HOM + 2 constituent FOMs
        mutantTestsMap.Count.ShouldBe(3);

        // Test cases should include all assessing tests
        if (testCases != null && expectedTests.Length > 0)
        {
            foreach (var test in expectedTests)
            {
                testCases.ShouldContain(test);
            }
        }
    }

    [TestMethod]
    public void TestCases_WithMixedFOMAndHOM_ShouldHandleBothCorrectly()
    {
        // Arrange - Mix of regular FOM and HOM (as would happen in BuildMutantGroupsForTest)
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        var regularFom = CreateMutant(5, MutantStatus.Pending, new[] { "Test5" });
        
        var homConstituentFom1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1", "Test2" });
        var homConstituentFom2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test1", "Test3" });
        var hom = new HigherOrderMutant(new List<IMutant> { homConstituentFom1, homConstituentFom2 }) { Id = 100 };
        
        var mutants = new List<IMutant> { regularFom, hom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert - Verify dual-mode handling worked correctly
        // Regular FOM should be added normally
        mutantTestsMap.ShouldContainKey(5, "Should contain regular FOM");
        
        // HOM and its constituents should all be present (dual-mode)
        mutantTestsMap.ShouldContainKey(100, "Should contain HOM ID for status tracking");
        mutantTestsMap.ShouldContainKey(1, "Should contain first HOM constituent for MutantControl");
        mutantTestsMap.ShouldContainKey(2, "Should contain second HOM constituent for MutantControl");
        
        // We should have 4 entries: 1 regular FOM + 1 HOM + 2 constituent FOMs
        mutantTestsMap.Count.ShouldBe(4);
        
        Console.WriteLine($"SUCCESS: Mixed test with dual-mode HOM handling worked!");
        Console.WriteLine($"SUCCESS: MutantTestsMap keys: [{string.Join(", ", mutantTestsMap.Keys)}]");
    }

    [TestMethod]
    public void TestCases_WithHOMInNonCoverageMode_ShouldStillUseDualMode()
    {
        // Arrange - Test in non-coverage-based mode
        var options = new StrykerOptions
        {
            OptimizationMode = OptimizationModes.None
        };
        var mockVsTest = BuildVsTestRunnerPool(options, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);
        
        var fom1 = CreateMutant(1, MutantStatus.Pending);
        var fom2 = CreateMutant(2, MutantStatus.Pending);
        var hom = new HigherOrderMutant(new List<IMutant> { fom1, fom2 }) { Id = 200 };
        
        var mutants = new List<IMutant> { hom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert - Should use dual-mode even in non-coverage mode
        mutantTestsMap.ShouldContainKey(200, "Should contain HOM ID for status tracking");
        mutantTestsMap.ShouldContainKey(1, "Should contain first FOM ID for MutantControl");
        mutantTestsMap.ShouldContainKey(2, "Should contain second FOM ID for MutantControl");
        
        // In non-coverage mode, should assign EveryTest to all entries
        mutantTestsMap[200].IsEveryTest.ShouldBeTrue();
        mutantTestsMap[1].IsEveryTest.ShouldBeTrue();
        mutantTestsMap[2].IsEveryTest.ShouldBeTrue();
        
        testCases.ShouldBeNull(); // Non-coverage mode returns null for testCases
        mutantTestsMap.Count.ShouldBe(3); // HOM + 2 FOMs
    }

    [TestMethod]
    public void TestCases_WithLargeHOM_ShouldExpandAllConstituents()
    {
        // Arrange - Create HOM with multiple constituents
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        var constituents = new List<IMutant>
        {
            CreateMutant(10, MutantStatus.Pending, new[] { "TestA", "TestB", "TestC" }),
            CreateMutant(20, MutantStatus.Pending, new[] { "TestA", "TestB" }),
            CreateMutant(30, MutantStatus.Pending, new[] { "TestA", "TestC", "TestD" }),
            CreateMutant(40, MutantStatus.Pending, new[] { "TestA" })
        };
        
        var hom = new HigherOrderMutant(constituents) { Id = 500 };
        var mutants = new List<IMutant> { hom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert - Should contain HOM + all constituent FOMs
        mutantTestsMap.ShouldContainKey(500, "Should contain HOM ID");
        mutantTestsMap.ShouldContainKey(10, "Should contain constituent FOM 10");
        mutantTestsMap.ShouldContainKey(20, "Should contain constituent FOM 20");
        mutantTestsMap.ShouldContainKey(30, "Should contain constituent FOM 30");
        mutantTestsMap.ShouldContainKey(40, "Should contain constituent FOM 40");
        
        // All should have the HOM's assessing tests (intersection = "TestA")
        var expectedTests = new[] { "TestA" };
        mutantTestsMap[500].GetIdentifiers().ShouldBe(expectedTests);
        mutantTestsMap[10].GetIdentifiers().ShouldBe(expectedTests);
        mutantTestsMap[20].GetIdentifiers().ShouldBe(expectedTests);
        mutantTestsMap[30].GetIdentifiers().ShouldBe(expectedTests);
        mutantTestsMap[40].GetIdentifiers().ShouldBe(expectedTests);
        
        testCases.ShouldContain("TestA");
        mutantTestsMap.Count.ShouldBe(5); // 1 HOM + 4 FOMs
    }

    [TestMethod]
    public void TestCases_DualModeMutantControlCompatibility_ShouldSupportBothActivationAndTracking()
    {
        // Arrange - Simulate the end-to-end scenario
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        var fom1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var fom2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test1" });
        var hom = new HigherOrderMutant(new List<IMutant> { fom1, fom2 }) { Id = 300 };
        
        var mutants = new List<IMutant> { hom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act - Get the mutant test mapping that would be sent to CoverageCollector
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert - Simulate what happens in the dual-mode approach
        var activeSet = new HashSet<int>(mutantTestsMap.Keys);
        
        // MutantControl.IsActive() simulation - FOM IDs should be active
        var isActive1 = activeSet.Contains(1); // Should be true for mutation activation
        var isActive2 = activeSet.Contains(2); // Should be true for mutation activation
        var isActiveHOM = activeSet.Contains(300); // Should be true for status tracking
        
        isActive1.ShouldBeTrue("FOM 1 should be active for MutantControl activation");
        isActive2.ShouldBeTrue("FOM 2 should be active for MutantControl activation");
        isActiveHOM.ShouldBeTrue("HOM should be active for status tracking and SSHOM validation");
        
        // Verify dual-mode benefits
        mutantTestsMap.Count.ShouldBe(3, "Should have both HOM and FOM entries");
        
        Console.WriteLine($"SUCCESS: Dual-mode supports both MutantControl activation AND HOM status tracking!");
        Console.WriteLine($"SUCCESS: MutantTestsMap contains: [{string.Join(", ", mutantTestsMap.Keys)}]");
        Console.WriteLine($"SUCCESS: FOM IDs {fom1.Id},{fom2.Id} enable MutantControl.IsActive()");
        Console.WriteLine($"SUCCESS: HOM ID {hom.Id} enables status tracking and SSHOM validation");
    }

    [TestMethod]
    public void TestCases_DualModeComprehensiveValidation()
    {
        // Arrange - Create complex scenario with multiple HOMs
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        // Regular FOM
        var regularFom = CreateMutant(99, MutantStatus.Pending, new[] { "TestRegular" });
        
        // HOM 1 with 2 constituents
        var hom1Fom1 = CreateMutant(1, MutantStatus.Pending, new[] { "TestA", "TestB" });
        var hom1Fom2 = CreateMutant(2, MutantStatus.Pending, new[] { "TestA", "TestC" });
        var hom1 = new HigherOrderMutant(new List<IMutant> { hom1Fom1, hom1Fom2 }) { Id = 100 };
        
        // HOM 2 with 3 constituents  
        var hom2Fom1 = CreateMutant(3, MutantStatus.Pending, new[] { "TestX" });
        var hom2Fom2 = CreateMutant(4, MutantStatus.Pending, new[] { "TestX" });
        var hom2Fom3 = CreateMutant(5, MutantStatus.Pending, new[] { "TestX" });
        var hom2 = new HigherOrderMutant(new List<IMutant> { hom2Fom1, hom2Fom2, hom2Fom3 }) { Id = 200 };
        
        var mutants = new List<IMutant> { regularFom, hom1, hom2 };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert - Comprehensive validation
        // Regular FOM
        mutantTestsMap.ShouldContainKey(99, "Should contain regular FOM");
        
        // HOM 1 and its constituents (dual-mode)
        mutantTestsMap.ShouldContainKey(100, "Should contain HOM 1 for status tracking");
        mutantTestsMap.ShouldContainKey(1, "Should contain HOM 1 constituent 1 for activation");
        mutantTestsMap.ShouldContainKey(2, "Should contain HOM 1 constituent 2 for activation");
        
        // HOM 2 and its constituents (dual-mode)
        mutantTestsMap.ShouldContainKey(200, "Should contain HOM 2 for status tracking");
        mutantTestsMap.ShouldContainKey(3, "Should contain HOM 2 constituent 1 for activation");
        mutantTestsMap.ShouldContainKey(4, "Should contain HOM 2 constituent 2 for activation");
        mutantTestsMap.ShouldContainKey(5, "Should contain HOM 2 constituent 3 for activation");
        
        // Total: 1 regular + 2 HOMs + 5 constituent FOMs = 8 entries
        mutantTestsMap.Count.ShouldBe(8);
        
        // Verify test assignments
        mutantTestsMap[99].GetIdentifiers().ShouldBe(new[] { "TestRegular" });
        mutantTestsMap[100].GetIdentifiers().ShouldBe(new[] { "TestA" }); // HOM 1 intersection
        mutantTestsMap[200].GetIdentifiers().ShouldBe(new[] { "TestX" }); // HOM 2 intersection
        
        Console.WriteLine("✓ SUCCESS: Comprehensive dual-mode validation passed!");
        Console.WriteLine($"✓ Total entries: {mutantTestsMap.Count} (1 regular + 2 HOMs + 5 constituent FOMs)");
        Console.WriteLine($"✓ All mutant IDs: [{string.Join(", ", mutantTestsMap.Keys.OrderBy(x => x))}]");
    }

    #region Helper Methods

    private Mutant CreateMutant(int id, MutantStatus status, string[] assessingTests = null)
    {
        var mutant = new Mutant
        {
            Id = id,
            ResultStatus = status,
            KillingTests = TestIdentifierList.NoTest(),
            CoveringTests = TestIdentifierList.NoTest(),
            Mutation = new Mutation
            {
                DisplayName = $"Test Mutation {id}",
                Type = Mutator.Arithmetic,
                Description = $"Test mutation {id}"
            }
        };

        mutant.AssessingTests = assessingTests == null 
            ? TestIdentifierList.EveryTest() 
            : new TestIdentifierList(assessingTests);

        return mutant;
    }

    private ICollection<string> CallTestCasesMethod(VsTestRunner runner, IReadOnlyList<IMutant> mutants, Dictionary<int, ITestIdentifiers> mutantTestsMap)
    {
        // Use reflection to call the private TestCases method
        var method = typeof(VsTestRunner).GetMethod("TestCases", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        method.ShouldNotBeNull("TestCases method should exist");
        
        var result = method.Invoke(runner, new object[] { mutants, mutantTestsMap });
        return result as ICollection<string>;
    }

    #endregion
}
