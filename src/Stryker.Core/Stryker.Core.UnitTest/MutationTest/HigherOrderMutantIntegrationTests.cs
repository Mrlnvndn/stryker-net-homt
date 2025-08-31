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
using Stryker.Core.MutationTest;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.MutationTest;

/// <summary>
/// Integration tests to verify Higher Order Mutants are processed correctly during mutation testing
/// with focus on the TestUpdateHandler and BuildMutantGroupsForTest integration
/// </summary>
[TestClass]
public class HigherOrderMutantIntegrationTests : TestBase
{
    #region MutationTestProcess Integration Tests

    [TestMethod]
    public void TestUpdateHandler_WithHigherOrderMutant_ShouldAnalyzeTestRunCorrectly()
    {
        // Arrange - Create a HOM and simulate test execution results
        var mutant1 = CreateMutant(1, MutantStatus.Pending, assessingTests: new[] { "Test1", "Test2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, assessingTests: new[] { "Test1", "Test3" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 100 };

        // HOM's assessing tests should be the intersection: only "Test1"
        hom.AssessingTests.GetIdentifiers().ShouldBe(new[] { "Test1" });

        var failedTests = new TestIdentifierList(new[] { "Test1" }); // This should kill the HOM
        var ranTests = new TestIdentifierList(new[] { "Test1", "Test2", "Test3" });
        var timedOutTests = TestIdentifierList.NoTest();
        var reportedMutants = new HashSet<IMutant>();

        // Simulate the TestUpdateHandler logic
        var testedMutants = new List<IMutant> { hom };

        // Act - Simulate what TestUpdateHandler does
        foreach (var mutant in testedMutants)
        {
            mutant.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);
        }

        // Assert
        hom.ResultStatus.ShouldBe(MutantStatus.Killed);
        hom.KillingTests.GetIdentifiers().ShouldContain("Test1");
    }

    [TestMethod]
    public void TestUpdateHandler_WithHOMWithNoAssessingTests_ShouldSurvive()
    {
        // Arrange - Create a HOM with constituent mutants that have no common assessing tests
        var mutant1 = CreateMutant(1, MutantStatus.Pending, assessingTests: new[] { "Test1", "Test2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, assessingTests: new[] { "Test3", "Test4" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 101 };

        // HOM's assessing tests should be empty (no intersection)
        hom.AssessingTests.IsEmpty.ShouldBeTrue();

        var failedTests = new TestIdentifierList(new[] { "Test1", "Test2", "Test3", "Test4" }); // All tests fail
        var ranTests = new TestIdentifierList(new[] { "Test1", "Test2", "Test3", "Test4" });
        var timedOutTests = TestIdentifierList.NoTest();

        // Act - Simulate what TestUpdateHandler does
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert - This is the core issue: HOM survives even though its constituent mutants would be killed
        hom.ResultStatus.ShouldBe(MutantStatus.Survived);
        hom.KillingTests.IsEmpty.ShouldBeTrue();
    }

    [TestMethod]
    public void BuildMutantGroupsForTest_WithHigherOrderMutants_ShouldGroupCorrectly()
    {
        // Arrange - Create HOMs with different assessing test requirements
        var mutant1 = CreateMutant(1, MutantStatus.Pending, assessingTests: new[] { "Test1" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, assessingTests: new[] { "Test2" });
        var mutant3 = CreateMutant(3, MutantStatus.Pending, assessingTests: new[] { "Test1", "Test2" });
        
        var hom1 = new HigherOrderMutant(new List<IMutant> { mutant1 }) { Id = 100 }; // Assessing: Test1
        var hom2 = new HigherOrderMutant(new List<IMutant> { mutant2 }) { Id = 101 }; // Assessing: Test2  
        var hom3 = new HigherOrderMutant(new List<IMutant> { mutant3 }) { Id = 102 }; // Assessing: Test1, Test2

        var mutants = new List<IMutant> { hom1, hom2, hom3 };

        // Simulate BuildMutantGroupsForTest logic
        var totalTestsCount = 3; // Test1, Test2, Test3

        // Act - Group mutants based on their assessing tests (like BuildMutantGroupsForTest does)
        var groups = GroupMutantsForTesting(mutants, totalTestsCount);

        // Assert - Verify grouping logic works correctly with HOMs
        groups.ShouldNotBeEmpty();
        
        // HOM1 (Test1) and HOM2 (Test2) should be groupable together since Test1 + Test2 ≤ 3
        // HOM3 (Test1, Test2) might be in a separate group or combined depending on the algorithm
        
        var totalMutantsInGroups = groups.SelectMany(g => g).Count();
        totalMutantsInGroups.ShouldBe(3); // All mutants should be included
    }

    [TestMethod]
    public void HigherOrderMutant_WithComplexAssessingTestScenario_ShouldProcessCorrectly()
    {
        // Arrange - Create a complex scenario with multiple HOMs having different assessing test patterns
        var mutantA = CreateMutant(1, MutantStatus.Pending, assessingTests: new[] { "IntegrationTest1", "UnitTest1", "UnitTest2" });
        var mutantB = CreateMutant(2, MutantStatus.Pending, assessingTests: new[] { "IntegrationTest1", "UnitTest1" });
        var mutantC = CreateMutant(3, MutantStatus.Pending, assessingTests: new[] { "IntegrationTest1", "UnitTest3" });

        var hom1 = new HigherOrderMutant(new List<IMutant> { mutantA, mutantB }) { Id = 200 }; // Intersection: IntegrationTest1, UnitTest1
        var hom2 = new HigherOrderMutant(new List<IMutant> { mutantA, mutantC }) { Id = 201 }; // Intersection: IntegrationTest1
        var hom3 = new HigherOrderMutant(new List<IMutant> { mutantB, mutantC }) { Id = 202 }; // Intersection: IntegrationTest1

        // Assert assessing tests calculations
        hom1.AssessingTests.GetIdentifiers().OrderBy(x => x).ShouldBe(new[] { "IntegrationTest1", "UnitTest1" });
        hom2.AssessingTests.GetIdentifiers().ShouldBe(new[] { "IntegrationTest1" });
        hom3.AssessingTests.GetIdentifiers().ShouldBe(new[] { "IntegrationTest1" });

        // Test different failure scenarios
        
        // Scenario 1: IntegrationTest1 fails - should kill all HOMs
        var failedTests1 = new TestIdentifierList(new[] { "IntegrationTest1" });
        var ranTests1 = new TestIdentifierList(new[] { "IntegrationTest1", "UnitTest1", "UnitTest2", "UnitTest3" });
        
        var hom1Copy = new HigherOrderMutant(new List<IMutant> { mutantA, mutantB }) { Id = 200 };
        var hom2Copy = new HigherOrderMutant(new List<IMutant> { mutantA, mutantC }) { Id = 201 };
        var hom3Copy = new HigherOrderMutant(new List<IMutant> { mutantB, mutantC }) { Id = 202 };

        hom1Copy.AnalyzeTestRun(failedTests1, ranTests1, TestIdentifierList.NoTest(), false);
        hom2Copy.AnalyzeTestRun(failedTests1, ranTests1, TestIdentifierList.NoTest(), false);
        hom3Copy.AnalyzeTestRun(failedTests1, ranTests1, TestIdentifierList.NoTest(), false);

        hom1Copy.ResultStatus.ShouldBe(MutantStatus.Killed);
        hom2Copy.ResultStatus.ShouldBe(MutantStatus.Killed);
        hom3Copy.ResultStatus.ShouldBe(MutantStatus.Killed);

        // Scenario 2: Only UnitTest1 fails - should kill only hom1
        var failedTests2 = new TestIdentifierList(new[] { "UnitTest1" });
        var ranTests2 = new TestIdentifierList(new[] { "IntegrationTest1", "UnitTest1", "UnitTest2", "UnitTest3" });
        
        hom1.AnalyzeTestRun(failedTests2, ranTests2, TestIdentifierList.NoTest(), false);
        hom2.AnalyzeTestRun(failedTests2, ranTests2, TestIdentifierList.NoTest(), false);
        hom3.AnalyzeTestRun(failedTests2, ranTests2, TestIdentifierList.NoTest(), false);

        hom1.ResultStatus.ShouldBe(MutantStatus.Killed); // UnitTest1 is in its assessing tests
        hom2.ResultStatus.ShouldBe(MutantStatus.Survived); // UnitTest1 is NOT in its assessing tests
        hom3.ResultStatus.ShouldBe(MutantStatus.Survived); // UnitTest1 is NOT in its assessing tests
    }

    [TestMethod]
    public void HigherOrderMutant_ComparedToRegularMutant_ShouldBehaveConsistently()
    {
        // Arrange - Create scenarios where HOM and regular mutant should behave identically
        var assessingTests = new[] { "CommonTest1", "CommonTest2" };
        
        // Regular mutant with specific assessing tests
        var regularMutant = CreateMutant(1, MutantStatus.Pending, assessingTests: assessingTests);
        
        // HOM that should have the same assessing tests (via intersection)
        var mutantA = CreateMutant(2, MutantStatus.Pending, assessingTests: new[] { "CommonTest1", "CommonTest2", "ExtraTest1" });
        var mutantB = CreateMutant(3, MutantStatus.Pending, assessingTests: new[] { "CommonTest1", "CommonTest2", "ExtraTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutantA, mutantB }) { Id = 300 };

        // Verify both have the same assessing tests
        regularMutant.AssessingTests.GetIdentifiers().OrderBy(x => x).ShouldBe(hom.AssessingTests.GetIdentifiers().OrderBy(x => x));

        // Test multiple scenarios
        var testScenarios = new[]
        {
            // Scenario 1: Test that kills both
            new { 
                FailedTests = new[] { "CommonTest1" }, 
                RanTests = new[] { "CommonTest1", "CommonTest2" },
                ExpectedStatus = MutantStatus.Killed
            },
            // Scenario 2: Test that doesn't affect either
            new { 
                FailedTests = new[] { "UnrelatedTest" }, 
                RanTests = new[] { "CommonTest1", "CommonTest2", "UnrelatedTest" },
                ExpectedStatus = MutantStatus.Survived
            }
        };

        foreach (var scenario in testScenarios)
        {
            // Reset status
            var regularMutantCopy = CreateMutant(1, MutantStatus.Pending, assessingTests: assessingTests);
            var homCopy = new HigherOrderMutant(new List<IMutant> { mutantA, mutantB }) { Id = 300 };

            var failedTests = new TestIdentifierList(scenario.FailedTests);
            var ranTests = new TestIdentifierList(scenario.RanTests);

            // Act
            regularMutantCopy.AnalyzeTestRun(failedTests, ranTests, TestIdentifierList.NoTest(), false);
            homCopy.AnalyzeTestRun(failedTests, ranTests, TestIdentifierList.NoTest(), false);

            // Assert - Both should have identical behavior
            regularMutantCopy.ResultStatus.ShouldBe(scenario.ExpectedStatus, $"Regular mutant failed for scenario with failed tests: {string.Join(",", scenario.FailedTests)}");
            homCopy.ResultStatus.ShouldBe(scenario.ExpectedStatus, $"HOM failed for scenario with failed tests: {string.Join(",", scenario.FailedTests)}");
            homCopy.ResultStatus.ShouldBe(regularMutantCopy.ResultStatus, $"HOM and regular mutant should have same status for scenario with failed tests: {string.Join(",", scenario.FailedTests)}");
        }
    }

    [TestMethod]
    public void HigherOrderMutant_WithNoAssessingTestsIntersection_DocumentsIssue()
    {
        // Arrange - This test documents the core issue where HOMs might not be killable
        var mutant1 = CreateMutant(1, MutantStatus.Pending, assessingTests: new[] { "TestA", "TestB" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, assessingTests: new[] { "TestC", "TestD" });
        var mutant3 = CreateMutant(3, MutantStatus.Pending, assessingTests: new[] { "TestE", "TestF" });

        // Create HOM with mutants that have no common assessing tests
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2, mutant3 }) { Id = 400 };

        // Assert the issue
        hom.AssessingTests.IsEmpty.ShouldBeTrue("HOM has no assessing tests due to empty intersection");

        // Even if all tests fail, the HOM cannot be killed
        var allTestsFailed = new TestIdentifierList(new[] { "TestA", "TestB", "TestC", "TestD", "TestE", "TestF" });
        var allTestsRan = new TestIdentifierList(new[] { "TestA", "TestB", "TestC", "TestD", "TestE", "TestF" });

        hom.AnalyzeTestRun(allTestsFailed, allTestsRan, TestIdentifierList.NoTest(), false);

        // This is the issue: HOM survives even when all its constituent mutants would be killed
        hom.ResultStatus.ShouldBe(MutantStatus.Survived, "HOM cannot be killed because it has no assessing tests");

        // Verify individual mutants would be killed
        mutant1.AnalyzeTestRun(allTestsFailed, allTestsRan, TestIdentifierList.NoTest(), false);
        mutant2.AnalyzeTestRun(allTestsFailed, allTestsRan, TestIdentifierList.NoTest(), false);
        mutant3.AnalyzeTestRun(allTestsFailed, allTestsRan, TestIdentifierList.NoTest(), false);

        mutant1.ResultStatus.ShouldBe(MutantStatus.Killed);
        mutant2.ResultStatus.ShouldBe(MutantStatus.Killed);
        mutant3.ResultStatus.ShouldBe(MutantStatus.Killed);
        
        // The inconsistency: all constituents are killed, but the HOM survives
        Console.WriteLine($"HOM Status: {hom.ResultStatus}, Constituent Statuses: {string.Join(", ", hom.ConstituentMutants.Select(m => m.ResultStatus))}");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test mutant with specified properties
    /// </summary>
    private static Mutant CreateMutant(int id, MutantStatus status, string[] assessingTests = null)
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

        // Set assessing tests - if null, default to EveryTest, otherwise use the provided array
        mutant.AssessingTests = assessingTests == null ? TestIdentifierList.EveryTest() : new TestIdentifierList(assessingTests);

        return mutant;
    }

    /// <summary>
    /// Simulates the BuildMutantGroupsForTest logic for testing purposes
    /// </summary>
    private List<List<IMutant>> GroupMutantsForTesting(List<IMutant> mutants, int totalTestsCount)
    {
        var groups = new List<List<IMutant>>();
        var mutantsToGroup = mutants.Where(m => !m.AssessingTests.IsEveryTest).OrderBy(m => m.AssessingTests.Count).ToList();

        // Handle mutants that need every test first (isolated testing)
        foreach (var mutant in mutants.Where(m => m.AssessingTests.IsEveryTest))
        {
            groups.Add(new List<IMutant> { mutant });
        }

        // Group remaining mutants
        while (mutantsToGroup.Count > 0)
        {
            var currentGroup = new List<IMutant> { mutantsToGroup[0] };
            var usedTests = mutantsToGroup[0].AssessingTests;
            mutantsToGroup.RemoveAt(0);

            for (int i = 0; i < mutantsToGroup.Count; i++)
            {
                var candidate = mutantsToGroup[i];
                
                // Check if adding this mutant would exceed total test count
                if (usedTests.Count + candidate.AssessingTests.Count > totalTestsCount)
                {
                    break;
                }

                // Check if this mutant's tests overlap with already used tests
                if (usedTests.ContainsAny(candidate.AssessingTests))
                {
                    continue;
                }

                // Add to group
                currentGroup.Add(candidate);
                usedTests = usedTests.Merge(candidate.AssessingTests);
                mutantsToGroup.RemoveAt(i);
                i--; // Adjust index after removal
            }

            groups.Add(currentGroup);
        }

        return groups;
    }

    /// <summary>
    /// Creates a mock ID provider that returns specified IDs in sequence
    /// </summary>
    private static Mock<IProvideId> CreateMockIdProvider(params int[] ids)
    {
        var mockIdProvider = new Mock<IProvideId>();
        var setupSequence = mockIdProvider.SetupSequence(x => x.NextId());

        foreach (var id in ids)
        {
            setupSequence = setupSequence.Returns(id);
        }

        return mockIdProvider;
    }
    #endregion
}
