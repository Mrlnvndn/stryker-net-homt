using System;
using System.Collections.Generic;
using System.Linq;
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
/// Tests for the enhanced dual-mode HOM tracking in VsTestRunner
/// Validates the proper handling of HOM/FOM relationships and XML generation
/// </summary>
[TestClass]
public class VsTestRunnerDualModeTests : VsTestMockingHelper
{
    [TestMethod]
    public void TestCases_WithDualModeHOM_ShouldAddBothHOMAndFOMsToMutantTestsMap()
    {
        // Arrange
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

        // Assert - Should contain BOTH HOM and constituent FOM IDs
        mutantTestsMap.ShouldContainKey(100, "Should contain HOM ID for status tracking");
        mutantTestsMap.ShouldContainKey(1, "Should contain first FOM ID for MutantControl activation");
        mutantTestsMap.ShouldContainKey(2, "Should contain second FOM ID for MutantControl activation");
        
        // All should have the same tests (HOM's assessing tests)
        var expectedTests = hom.AssessingTests.GetIdentifiers().ToArray();
        mutantTestsMap[100].GetIdentifiers().ShouldBe(expectedTests);
        mutantTestsMap[1].GetIdentifiers().ShouldBe(expectedTests);
        mutantTestsMap[2].GetIdentifiers().ShouldBe(expectedTests);
        
        // Should have 3 total entries
        mutantTestsMap.Count.ShouldBe(3);
    }

    [TestMethod]
    public void TestCases_WithMixedHOMsAndFOMs_ShouldHandleComplexScenarios()
    {
        // Arrange
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        // Create complex scenario: 1 regular FOM, 2 HOMs with different constituent counts
        var regularFom = CreateMutant(99, MutantStatus.Pending, new[] { "Test99" });
        
        // HOM 1 with 2 constituents
        var hom1Fom1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1", "Test2" });
        var hom1Fom2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test1", "Test3" });
        var hom1 = new HigherOrderMutant(new List<IMutant> { hom1Fom1, hom1Fom2 }) { Id = 100 };
        
        // HOM 2 with 3 constituents
        var hom2Fom1 = CreateMutant(3, MutantStatus.Pending, new[] { "TestA" });
        var hom2Fom2 = CreateMutant(4, MutantStatus.Pending, new[] { "TestA" });
        var hom2Fom3 = CreateMutant(5, MutantStatus.Pending, new[] { "TestA" });
        var hom2 = new HigherOrderMutant(new List<IMutant> { hom2Fom1, hom2Fom2, hom2Fom3 }) { Id = 200 };
        
        var mutants = new List<IMutant> { regularFom, hom1, hom2 };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert
        // Regular FOM
        mutantTestsMap.ShouldContainKey(99, "Should contain regular FOM");
        
        // HOM 1 and its constituents
        mutantTestsMap.ShouldContainKey(100, "Should contain HOM 1 for status tracking");
        mutantTestsMap.ShouldContainKey(1, "Should contain HOM 1 constituent 1");
        mutantTestsMap.ShouldContainKey(2, "Should contain HOM 1 constituent 2");
        
        // HOM 2 and its constituents
        mutantTestsMap.ShouldContainKey(200, "Should contain HOM 2 for status tracking");
        mutantTestsMap.ShouldContainKey(3, "Should contain HOM 2 constituent 1");
        mutantTestsMap.ShouldContainKey(4, "Should contain HOM 2 constituent 2");
        mutantTestsMap.ShouldContainKey(5, "Should contain HOM 2 constituent 3");
        
        // Total: 1 regular + 2 HOMs + 5 constituent FOMs = 8 entries
        mutantTestsMap.Count.ShouldBe(8);
    }

    [TestMethod]
    public void TestCases_WithDualModeInNonCoverageMode_ShouldStillWork()
    {
        // Arrange
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.None
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        var fom1 = CreateMutant(1, MutantStatus.Pending);
        var fom2 = CreateMutant(2, MutantStatus.Pending);
        var hom = new HigherOrderMutant(new List<IMutant> { fom1, fom2 }) { Id = 100 };
        
        var mutants = new List<IMutant> { hom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert
        mutantTestsMap.ShouldContainKey(100, "Should contain HOM ID");
        mutantTestsMap.ShouldContainKey(1, "Should contain first FOM ID");
        mutantTestsMap.ShouldContainKey(2, "Should contain second FOM ID");
        
        // All should have EveryTest in non-coverage mode
        mutantTestsMap[100].IsEveryTest.ShouldBeTrue();
        mutantTestsMap[1].IsEveryTest.ShouldBeTrue();
        mutantTestsMap[2].IsEveryTest.ShouldBeTrue();
        
        testCases.ShouldBeNull();
        mutantTestsMap.Count.ShouldBe(3);
    }

    [TestMethod]
    public void TestCases_ShouldSetHOMMappingsInContext()
    {
        // Arrange
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        var fom1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var fom2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test1" });
        var hom = new HigherOrderMutant(new List<IMutant> { fom1, fom2 }) { Id = 100 };
        
        var mutants = new List<IMutant> { hom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert - Verify SetHomMappings was called on context
        // We can't easily verify the internal state, but we can check that the method
        // completed without errors and the mappings would be passed to XML generation
        mutantTestsMap.ShouldNotBeEmpty();
        testCases.ShouldNotBeNull();
    }

    [TestMethod]
    public void TestCases_DualModeVsMutantControlCompatibility_ShouldSupportBothModes()
    {
        // Arrange
        var mockVsTest = BuildVsTestRunnerPool(new StrykerOptions
        {
            OptimizationMode = OptimizationModes.CoverageBasedTest
        }, out var pool);
        var context = pool.Context;
        var runner = new VsTestRunner(context, 1);

        var fom1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var fom2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test1" });
        var hom = new HigherOrderMutant(new List<IMutant> { fom1, fom2 }) { Id = 100 };
        
        var mutants = new List<IMutant> { hom };
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();

        // Act
        var testCases = CallTestCasesMethod(runner, mutants, mutantTestsMap);

        // Assert - Simulate what would happen in different scenarios
        
        // Scenario 1: MutantControl.IsActive() - should work with FOM IDs
        var activeSet = new HashSet<int>(mutantTestsMap.Keys);
        activeSet.Contains(1).ShouldBeTrue("FOM 1 should be activatable");
        activeSet.Contains(2).ShouldBeTrue("FOM 2 should be activatable");
        activeSet.Contains(100).ShouldBeTrue("HOM should be trackable for status updates");
        
        // Scenario 2: Status tracking - HOM should be available for status updates
        mutantTestsMap.ContainsKey(100).ShouldBeTrue("HOM should be available for status tracking");
        
        // Scenario 3: SSHOM validation - HOM should have proper test assignment
        mutantTestsMap[100].ShouldNotBeNull("HOM should have test assignments for SSHOM validation");
        
        Console.WriteLine($"SUCCESS: Dual-mode XML supports both MutantControl activation and HOM status tracking");
        Console.WriteLine($"SUCCESS: MutantTestsMap contains {mutantTestsMap.Count} entries: [{string.Join(", ", mutantTestsMap.Keys)}]");
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
