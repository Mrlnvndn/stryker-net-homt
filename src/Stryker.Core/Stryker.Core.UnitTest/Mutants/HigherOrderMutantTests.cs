using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.Mutants;

/// <summary>
/// Unit tests for HigherOrderMutant to verify it processes test results the same as regular Mutants
/// </summary>
[TestClass]
public class HigherOrderMutantTests : TestBase
{
    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithNullConstituentMutants_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new HigherOrderMutant(null));
    }

    [TestMethod]
    public void Constructor_WithEmptyConstituentMutants_ShouldCreateValidHOM()
    {
        // Arrange
        var emptyList = new List<IMutant>();

        // Act
        var hom = new HigherOrderMutant(emptyList);

        // Assert
        hom.ShouldNotBeNull();
        hom.ConstituentMutants.ShouldBeEmpty();
        hom.Order.ShouldBe(0);
        hom.ResultStatus.ShouldBe(MutantStatus.Pending);
    }

    [TestMethod]
    public void Constructor_WithValidConstituentMutants_ShouldInitializeCorrectly()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1", "Test2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2", "Test3" });
        var constituents = new List<IMutant> { mutant1, mutant2 };

        // Act
        var hom = new HigherOrderMutant(constituents, "TestAlgorithm");

        // Assert
        hom.ConstituentMutants.ShouldBe(constituents);
        hom.Order.ShouldBe(2);
        hom.AlgorithmUsed.ShouldBe("TestAlgorithm");
        hom.ResultStatus.ShouldBe(MutantStatus.Pending);
        hom.KillingTests.ShouldNotBeNull();
        hom.KillingTests.IsEmpty.ShouldBeTrue();
        hom.Mutation.ShouldNotBeNull();
        hom.Mutation.Type.ShouldBe(Mutator.HigherOrderMutant);
    }

    #endregion

    #region Display and Properties Tests

    [TestMethod]
    public void DisplayName_ShouldIncludeHOMIdAndConstituentIds()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 100 };

        // Act & Assert
        hom.DisplayName.ShouldBe("HOM-100: 1+2");
    }

    [TestMethod]
    public void MutantIdsString_ShouldJoinConstituentIds()
    {
        // Arrange
        var mutant1 = CreateMutant(5, MutantStatus.Pending, new[] { "Test1" });
        var mutant2 = CreateMutant(10, MutantStatus.Pending, new[] { "Test2" });
        var mutant3 = CreateMutant(15, MutantStatus.Pending, new[] { "Test3" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2, mutant3 });

        // Act & Assert
        hom.MutantIdsString.ShouldBe("5,10,15");
    }

    [TestMethod]
    [DataRow(MutantStatus.CompileError, false)]
    [DataRow(MutantStatus.Ignored, false)]
    [DataRow(MutantStatus.Killed, true)]
    [DataRow(MutantStatus.NoCoverage, true)]
    [DataRow(MutantStatus.Pending, true)]
    [DataRow(MutantStatus.Survived, true)]
    [DataRow(MutantStatus.Timeout, true)]
    public void CountForStats_ShouldWorkSameAsRegularMutant(MutantStatus status, bool expectedCount)
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1 })
        {
            ResultStatus = status
        };

        // Act & Assert
        hom.CountForStats.ShouldBe(expectedCount);
    }

    #endregion

    #region AssessingTests and CoveringTests Tests

    [TestMethod]
    public void AssessingTests_WithNoConstituents_ShouldReturnEveryTest()
    {
        // Arrange
        var hom = new HigherOrderMutant(new List<IMutant>());

        // Act
        var assessingTests = hom.AssessingTests;

        // Assert
        assessingTests.IsEveryTest.ShouldBeTrue();
    }

    [TestMethod]
    public void AssessingTests_WithSingleConstituent_ShouldReturnConstituentAssessingTests()
    {
        // Arrange
        var mutant = CreateMutant(1, MutantStatus.Pending, new[] { "Test1", "Test2" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant });

        // Act
        var assessingTests = hom.AssessingTests;

        // Assert
        assessingTests.GetIdentifiers().ShouldBe(new[] { "AssessTest1", "AssessTest2" });
    }

    [TestMethod]
    public void AssessingTests_WithMultipleConstituents_ShouldReturnIntersection()
    {
        // Arrange - Create mutants with overlapping assessing tests
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "CommonTest1", "CommonTest2", "UniqueTest1" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "CommonTest1", "CommonTest2", "UniqueTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        // Act
        var assessingTests = hom.AssessingTests;

        // Assert - Should only contain the intersection (common tests)
        var assessingTestIds = assessingTests.GetIdentifiers().ToArray();
        assessingTestIds.ShouldContain("CommonTest1");
        assessingTestIds.ShouldContain("CommonTest2");
        assessingTestIds.ShouldNotContain("UniqueTest1");
        assessingTestIds.ShouldNotContain("UniqueTest2");
    }

    [TestMethod]
    public void AssessingTests_WithNoCommonAssessingTests_ShouldReturnEmpty()
    {
        // Arrange - Create mutants with completely different assessing tests
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "AssessTest3", "AssessTest4" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        // Act
        var assessingTests = hom.AssessingTests;

        // Assert - Should be empty because no common assessing tests
        assessingTests.IsEmpty.ShouldBeTrue();
    }

    [TestMethod]
    public void CoveringTests_WithNoConstituents_ShouldReturnNoTest()
    {
        // Arrange
        var hom = new HigherOrderMutant(new List<IMutant>());

        // Act
        var coveringTests = hom.CoveringTests;

        // Assert
        coveringTests.IsEmpty.ShouldBeTrue();
    }

    [TestMethod]
    public void CoveringTests_WithMultipleConstituents_ShouldReturnUnion()
    {
        // Arrange - Create mutants with different covering tests
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "CoverTest1", "CommonCoverTest" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "CoverTest2", "CommonCoverTest" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        // Act
        var coveringTests = hom.CoveringTests;

        // Assert - Should contain the union (all covering tests)
        var coveringTestIds = coveringTests.GetIdentifiers().ToArray();
        coveringTestIds.ShouldContain("CoverTest1");
        coveringTestIds.ShouldContain("CoverTest2");
        coveringTestIds.ShouldContain("CommonCoverTest");
    }

    #endregion

    #region AnalyzeTestRun Tests - Core Issue Testing

    [TestMethod]
    public void AnalyzeTestRun_WithKillingTestInAssessingTests_ShouldMarkAsKilled()
    {
        // Arrange - HOM with common assessing tests
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "CommonTest1", "CommonTest2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "CommonTest1", "CommonTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 100 };

        var failedTests = new TestIdentifierList(new[] { "CommonTest1" });
        var ranTests = new TestIdentifierList(new[] { "CommonTest1", "CommonTest2", "OtherTest" });
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert
        hom.ResultStatus.ShouldBe(MutantStatus.Killed);
        hom.KillingTests.GetIdentifiers().ShouldContain("CommonTest1");
    }

    [TestMethod]
    public void AnalyzeTestRun_WithNoCommonAssessingTests_ShouldNotBeKilled()
    {
        // Arrange - HOM with no common assessing tests (intersection is empty)
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "AssessTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 101 };

        var failedTests = new TestIdentifierList(new[] { "AssessTest1", "AssessTest2" });
        var ranTests = new TestIdentifierList(new[] { "AssessTest1", "AssessTest2" });
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert - This is the CORE ISSUE: HOM cannot be killed if it has no assessing tests
        hom.ResultStatus.ShouldBe(MutantStatus.Survived); // It survives because AssessingTests is empty
        hom.AssessingTests.IsEmpty.ShouldBeTrue(); // This is the root cause
    }

    [TestMethod]
    public void AnalyzeTestRun_WithFailedTestNotInAssessingTests_ShouldSurvive()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        var failedTests = new TestIdentifierList(new[] { "UnrelatedTest" });
        var ranTests = new TestIdentifierList(new[] { "AssessTest1", "AssessTest2", "UnrelatedTest" });
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert
        hom.ResultStatus.ShouldBe(MutantStatus.Survived);
        hom.KillingTests.IsEmpty.ShouldBeTrue();
    }

    [TestMethod]
    public void AnalyzeTestRun_WithTimedOutTestInAssessingTests_ShouldMarkAsTimeout()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "AssessTest1" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        var failedTests = TestIdentifierList.NoTest();
        var ranTests = new TestIdentifierList(new[] { "AssessTest1" });
        var timedOutTests = new TestIdentifierList(new[] { "AssessTest1" });

        // Act
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert
        hom.ResultStatus.ShouldBe(MutantStatus.Timeout);
    }

    [TestMethod]
    public void AnalyzeTestRun_WithSessionTimeout_ShouldMarkAsTimeout()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1 });

        var failedTests = TestIdentifierList.NoTest();
        var ranTests = TestIdentifierList.NoTest();
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, true);

        // Assert
        hom.ResultStatus.ShouldBe(MutantStatus.Timeout);
    }

    [TestMethod]
    public void AnalyzeTestRun_WithAllTestsRanAndNoneFailedOrTimedOut_ShouldSurvive()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1 });

        var failedTests = TestIdentifierList.NoTest();
        var ranTests = TestIdentifierList.EveryTest();
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert
        hom.ResultStatus.ShouldBe(MutantStatus.Survived);
    }

    [TestMethod]
    public void AnalyzeTestRun_WithPartialTestsRanAndAssessingTestsIncluded_ShouldSurvive()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1 });

        var failedTests = TestIdentifierList.NoTest();
        var ranTests = new TestIdentifierList(new[] { "AssessTest1", "AssessTest2", "OtherTest" });
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert
        hom.ResultStatus.ShouldBe(MutantStatus.Survived);
    }

    [TestMethod]
    public void AnalyzeTestRun_ShouldBehaveSameAsRegularMutant_KilledScenario()
    {
        // Arrange - Create HOM and regular mutant with same assessing tests
        var assessingTests = new[] { "CommonTest1", "CommonTest2" };
        
        var regularMutant = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: assessingTests);
        var mutant1 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: assessingTests);
        var mutant2 = CreateMutant(3, MutantStatus.Pending, new[] { "Test3" }, assessingTests: assessingTests);
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        var failedTests = new TestIdentifierList(new[] { "CommonTest1" });
        var ranTests = new TestIdentifierList(new[] { "CommonTest1", "CommonTest2" });
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        regularMutant.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert - Both should have same result
        regularMutant.ResultStatus.ShouldBe(MutantStatus.Killed);
        hom.ResultStatus.ShouldBe(MutantStatus.Killed);
        regularMutant.KillingTests.GetIdentifiers().ShouldBe(hom.KillingTests.GetIdentifiers());
    }

    [TestMethod]
    public void AnalyzeTestRun_ShouldBehaveSameAsRegularMutant_SurvivedScenario()
    {
        // Arrange - Create HOM and regular mutant with same assessing tests
        var assessingTests = new[] { "CommonTest1", "CommonTest2" };
        
        var regularMutant = CreateMutant(1, MutantStatus.Pending, [] , assessingTests: assessingTests);
        var mutant1 = CreateMutant(2, MutantStatus.Pending, [ "Test2" ], assessingTests: assessingTests);
        var mutant2 = CreateMutant(3, MutantStatus.Pending, [ "Test3" ], assessingTests: assessingTests);
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        var failedTests = new TestIdentifierList(new[] { "UnrelatedTest" });
        var ranTests = new TestIdentifierList(new[] { "CommonTest1", "CommonTest2", "UnrelatedTest" });
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        regularMutant.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert - Both should have same result
        regularMutant.ResultStatus.ShouldBe(MutantStatus.Survived);
        hom.ResultStatus.ShouldBe(MutantStatus.Survived);
        regularMutant.KillingTests.IsEmpty.ShouldBeTrue();
        hom.KillingTests.IsEmpty.ShouldBeTrue();
    }

    [TestMethod]
    public void AnalyzeTestRun_ShouldBehaveSameAsRegularMutant_TimeoutScenario()
    {
        // Arrange - Create HOM and regular mutant with same assessing tests
        var assessingTests = new[] { "CommonTest1", "CommonTest2" };
        
        var regularMutant = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: assessingTests);
        var mutant1 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: assessingTests);
        var mutant2 = CreateMutant(3, MutantStatus.Pending, new[] { "Test3" }, assessingTests: assessingTests);
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        var failedTests = TestIdentifierList.NoTest();
        var ranTests = new TestIdentifierList(new[] { "CommonTest1", "CommonTest2" });
        var timedOutTests = new TestIdentifierList(new[] { "CommonTest1" });

        // Act
        regularMutant.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert - Both should have same result
        regularMutant.ResultStatus.ShouldBe(MutantStatus.Timeout);
        hom.ResultStatus.ShouldBe(MutantStatus.Timeout);
    }

    #endregion

    #region Integration Tests with BuildMutantGroupsForTest Compatibility

    [TestMethod]
    public void AssessingTests_ForBuildMutantGroupsForTest_ShouldReturnValidIdentifiers()
    {
        // Arrange - Test the BuildMutantGroupsForTest compatibility
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "AssessTest1", "AssessTest3" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        // Act
        var assessingTests = hom.AssessingTests;
        
        // Assert - Should be compatible with BuildMutantGroupsForTest logic
        assessingTests.ShouldNotBeNull();
        assessingTests.GetIdentifiers().ShouldNotBeNull();
        assessingTests.Count.ShouldBeGreaterThanOrEqualTo(0);
        
        // The intersection should only contain AssessTest1
        assessingTests.GetIdentifiers().ShouldBe(new[] { "AssessTest1" });
    }

    [TestMethod]
    public void AssessingTests_IsEveryTest_ShouldBehaveLikeRegularMutants()
    {
        // Arrange - Test when constituents have IsEveryTest = true
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: null); // Will default to EveryTest
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "AssessTest1" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        // Act
        var assessingTests = hom.AssessingTests;

        // Assert - Intersection with EveryTest should result in the other set
        assessingTests.GetIdentifiers().ShouldBe(new[] { "AssessTest1" });
        assessingTests.IsEveryTest.ShouldBeFalse();
    }

    #endregion

    #region Equals and ToString Tests

    [TestMethod]
    public void Equals_WithSameId_ShouldReturnTrue()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var hom1 = new HigherOrderMutant(new List<IMutant> { mutant1 }) { Id = 100 };
        var hom2 = new HigherOrderMutant(new List<IMutant> { mutant1 }) { Id = 100 };

        // Act & Assert
        hom1.Equals(hom2).ShouldBeTrue();
        hom1.GetHashCode().ShouldBe(hom2.GetHashCode());
    }

    [TestMethod]
    public void Equals_WithDifferentId_ShouldReturnFalse()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var hom1 = new HigherOrderMutant(new List<IMutant> { mutant1 }) { Id = 100 };
        var hom2 = new HigherOrderMutant(new List<IMutant> { mutant1 }) { Id = 101 };

        // Act & Assert
        hom1.Equals(hom2).ShouldBeFalse();
    }

    [TestMethod]
    public void ToString_ShouldIncludeIdOrderAndStatus()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 })
        {
            Id = 150,
            ResultStatus = MutantStatus.Killed
        };

        // Act
        var result = hom.ToString();

        // Assert
        result.ShouldBe("HOM-150: 1,2 (Order: 2, Status: Killed)");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test mutant with specified properties
    /// </summary>
    private static Mutant CreateMutant(int id, MutantStatus status, string[] killingTests, string[] assessingTests = null)
    {
        var mutant = new Mutant
        {
            Id = id,
            ResultStatus = status,
            KillingTests = new TestIdentifierList(killingTests),
            CoveringTests = new TestIdentifierList(killingTests),
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

    #endregion
}
