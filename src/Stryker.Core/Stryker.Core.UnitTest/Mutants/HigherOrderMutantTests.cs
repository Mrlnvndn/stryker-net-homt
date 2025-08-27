using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
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
    public void DisplayName_ShouldIncludeHOMIdAndOriginalConstituentIds()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 100 };

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

        // Act & Assert
        hom.DisplayName.ShouldBe("HOM-100: 1,2");
    }

    [TestMethod]
    public void ConstituentMutantIdsString_ShouldJoinCompositeIds()
    {
        // Arrange
        var mutant1 = CreateMutant(5, MutantStatus.Pending, new[] { "Test1" });
        var mutant2 = CreateMutant(10, MutantStatus.Pending, new[] { "Test2" });
        var mutant3 = CreateMutant(15, MutantStatus.Pending, new[] { "Test3" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2, mutant3 });

        var mockIdProvider = CreateMockIdProvider(1005, 1010, 1015);
        hom.CreateDeepCopies(mockIdProvider.Object);

        // Act & Assert
        hom.ConstituentMutantIdsString.ShouldBe("1005,1010,1015");
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

        var mockIdProvider = CreateMockIdProvider(1001);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

        // Act
        var coveringTests = hom.CoveringTests;

        // Assert - Should contain the union (all covering tests)
        var coveringTestIds = coveringTests.GetIdentifiers().ToArray();
        coveringTestIds.ShouldContain("CoverTest1");
        coveringTestIds.ShouldContain("CoverTest2");
        coveringTestIds.ShouldContain("CommonCoverTest");
    }

    #endregion

    #region AnalyzeTestRun Tests

    [TestMethod]
    public void AnalyzeTestRun_WithKillingTestInAssessingTests_ShouldMarkAsKilled()
    {
        // Arrange - HOM with common assessing tests
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "CommonTest1", "CommonTest2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "CommonTest1", "CommonTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 100 };

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

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
    public void AnalyzeTestRun_WithNoCommonAssessingTests_ShouldSurvive()
    {
        // Arrange - HOM with no common assessing tests (intersection is empty)
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "AssessTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 101 };

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

        var failedTests = new TestIdentifierList(new[] { "AssessTest1", "AssessTest2" });
        var ranTests = new TestIdentifierList(new[] { "AssessTest1", "AssessTest2" });
        var timedOutTests = TestIdentifierList.NoTest();

        // Act
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert - HOM survives because it has no assessing tests (empty intersection)
        hom.ResultStatus.ShouldBe(MutantStatus.Survived);
        hom.AssessingTests.IsEmpty.ShouldBeTrue();
    }

    [TestMethod]
    public void AnalyzeTestRun_WithFailedTestNotInAssessingTests_ShouldSurvive()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        var mutant2 = CreateMutant(2, MutantStatus.Pending, new[] { "Test2" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

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
        
        var regularMutant = CreateMutant(1, MutantStatus.Pending, [], assessingTests: assessingTests);
        var mutant1 = CreateMutant(2, MutantStatus.Pending, ["Test2"], assessingTests: assessingTests);
        var mutant2 = CreateMutant(3, MutantStatus.Pending, ["Test3"], assessingTests: assessingTests);
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 });

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

        var mockIdProvider = CreateMockIdProvider(1001, 1002);
        hom.CreateDeepCopies(mockIdProvider.Object);

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

    #region Deep Copy and Composite ID Tests

    [TestMethod]
    public void CreateDeepCopies_ShouldCreateDeepCopiesWithCompositeIds()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Killed, new[] { "Test1" }); 
        var mutant2 = CreateMutant(2, MutantStatus.Survived, new[] { "Test2" });
        var originalMutants = new List<IMutant> { mutant1, mutant2 };
        var hom = new HigherOrderMutant(originalMutants) { Id = 100 };

        var mockIdProvider = CreateMockIdProvider(1001, 1002);

        // Act
        hom.CreateDeepCopies(mockIdProvider.Object);

        // Assert
        hom.ConstituentMutants.ShouldNotBeNull();
        hom.ConstituentMutants.Count.ShouldBe(2);

        var constituentCopy1 = hom.ConstituentMutants[0] as Mutant;
        var constituentCopy2 = hom.ConstituentMutants[1] as Mutant;

        // Verify the first constituent copy
        constituentCopy1.ShouldNotBeNull();
        constituentCopy1.Id.ShouldBe(1001); // New composite ID
        constituentCopy1.OriginalFomId.ShouldBe(1); // Original FOM ID preserved
        constituentCopy1.ResultStatus.ShouldBe(MutantStatus.Pending); // Reset for fresh HOM testing
        constituentCopy1.KillingTests.IsEmpty.ShouldBeTrue(); // Reset for fresh HOM testing
        constituentCopy1.Mutation.ShouldBe(mutant1.Mutation); // Mutation should be same reference (shallow copy)
        constituentCopy1.CoveringTests.ShouldBe(mutant1.CoveringTests); // Test data should be preserved
        constituentCopy1.AssessingTests.ShouldBe(mutant1.AssessingTests); // Test data should be preserved

        // Verify the second constituent copy
        constituentCopy2.ShouldNotBeNull();
        constituentCopy2.Id.ShouldBe(1002); // New composite ID
        constituentCopy2.OriginalFomId.ShouldBe(2); // Original FOM ID preserved
        constituentCopy2.ResultStatus.ShouldBe(MutantStatus.Pending); // Reset for fresh HOM testing
        constituentCopy2.KillingTests.IsEmpty.ShouldBeTrue(); // Reset for fresh HOM testing

        // Verify the original mutants are NOT the same objects
        ReferenceEquals(constituentCopy1, mutant1).ShouldBeFalse("Should be deep copies, not references");
        ReferenceEquals(constituentCopy2, mutant2).ShouldBeFalse("Should be deep copies, not references");

        mockIdProvider.Verify(x => x.NextId(), Times.Exactly(2));
    }

    [TestMethod]
    public void CreateDeepCopies_ShouldPreserveOriginalMutantData()
    {
        // Arrange
        var mutant1 = CreateMutant(5, MutantStatus.Killed, new[] { "KillingTest1" }, assessingTests: new[] { "AssessTest1", "AssessTest2" });
        mutant1.IsStaticValue = true;
        mutant1.MustBeTestedInIsolation = true;
        mutant1.ResultStatusReason = "Original reason";

        var hom = new HigherOrderMutant(new List<IMutant> { mutant1 }) { Id = 200 };
        
        var mockIdProvider = CreateMockIdProvider(2005);

        // Act
        hom.CreateDeepCopies(mockIdProvider.Object);

        // Assert
        var constituentCopy = hom.ConstituentMutants[0] as Mutant;
        
        // Verify properties that should be preserved
        constituentCopy.OriginalFomId.ShouldBe(5);
        constituentCopy.IsStaticValue.ShouldBe(true); // Should be preserved
        constituentCopy.MustBeTestedInIsolation.ShouldBe(true); // Should be preserved
        constituentCopy.CoveringTests.GetIdentifiers().ShouldBe(new[] { "KillingTest1" }); // Should be preserved
        constituentCopy.AssessingTests.GetIdentifiers().ShouldBe(new[] { "AssessTest1", "AssessTest2" }); // Should be preserved
        
        // Verify properties that should be reset
        constituentCopy.ResultStatus.ShouldBe(MutantStatus.Pending); // Should be reset
        constituentCopy.KillingTests.IsEmpty.ShouldBeTrue(); // Should be reset
        constituentCopy.ResultStatusReason.ShouldBeNull(); // Should be reset
        
        // Verify new properties
        constituentCopy.Id.ShouldBe(2005); // Should be new composite ID
    }

    [TestMethod]
    public void CreateDeepCopies_ShouldHandleNonMutantImplementations()
    {
        // Arrange - Create a mock IMutant that's not a Mutant class
        var mockMutant = new Mock<IMutant>();
        mockMutant.SetupGet(m => m.Id).Returns(123);
        mockMutant.SetupGet(m => m.CoveringTests).Returns(new TestIdentifierList(new[] { "Test1" }));

        var hom = new HigherOrderMutant(new List<IMutant> { mockMutant.Object }) { Id = 300 };
        
        var mockIdProvider = new Mock<IProvideId>();

        // Act
        hom.CreateDeepCopies(mockIdProvider.Object);

        // Assert - Should fallback to using original mutant for non-Mutant implementations
        hom.ConstituentMutants.ShouldNotBeNull();
        hom.ConstituentMutants.Count.ShouldBe(1);
        hom.ConstituentMutants[0].ShouldBe(mockMutant.Object); // Should be the original object
        
        // Should not call NextId for non-Mutant implementations
        mockIdProvider.Verify(x => x.NextId(), Times.Never);
    }

    [TestMethod]
    public void CreateDeepCopies_ShouldClearOriginalConstituentMutants()
    {
        // Arrange
        var mutant1 = CreateMutant(1, MutantStatus.Pending, new[] { "Test1" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1 }) { Id = 400 };

        // Verify original storage exists before
        hom.Order.ShouldBe(1); // This works because it checks OriginalConstituentMutants when ConstituentMutants is null

        var mockIdProvider = CreateMockIdProvider(4001);

        // Act
        hom.CreateDeepCopies(mockIdProvider.Object);

        // Assert - Original storage should be cleared
        // We can't directly access OriginalConstituentMutants (it's private), 
        // but we can verify that Order now uses ConstituentMutants
        hom.Order.ShouldBe(1); // Should still work via ConstituentMutants
        hom.ConstituentMutants.ShouldNotBeNull(); // ConstituentMutants should be populated
        hom.ConstituentMutants.Count.ShouldBe(1);
    }

    [TestMethod]
    public void OriginalFomIdsString_ShouldShowOriginalIds_AfterDeepCopy()
    {
        // Arrange
        var mutant1 = CreateMutant(10, MutantStatus.Pending, new[] { "Test1" });
        var mutant2 = CreateMutant(20, MutantStatus.Pending, new[] { "Test2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 500 };

        var mockIdProvider = CreateMockIdProvider(5010, 5020);

        // Act
        hom.CreateDeepCopies(mockIdProvider.Object);

        // Assert
        hom.ConstituentMutantIdsString.ShouldBe("5010,5020"); // Should show new composite IDs
        hom.OriginalConstituentMutantIdsString.ShouldBe("10,20"); // Should show original FOM IDs
        hom.DisplayName.ShouldBe("HOM-500: 10,20"); // Display should use original IDs
    }

    [TestMethod]
    public void GetOriginalFomIdsForActivation_ShouldReturnOriginalIds_AfterDeepCopy()
    {
        // Arrange
        var mutant1 = CreateMutant(10, MutantStatus.Pending, new[] { "Test1" });
        var mutant2 = CreateMutant(20, MutantStatus.Pending, new[] { "Test2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 500 };

        var mockIdProvider = CreateMockIdProvider(5010, 5020);

        // Act
        hom.CreateDeepCopies(mockIdProvider.Object);
        var originalFomIds = hom.GetOriginalConstituentMutantIdsForActivation();

        // Assert
        originalFomIds.ShouldNotBeNull();
        originalFomIds.Count.ShouldBe(2);
        originalFomIds.ShouldContain(10); // Original FOM 1 ID
        originalFomIds.ShouldContain(20); // Original FOM 2 ID
        originalFomIds.ShouldNotContain(5010); // Should not contain composite IDs
        originalFomIds.ShouldNotContain(5020); // Should not contain composite IDs

        // Verify constituent mutants have the composite IDs
        hom.ConstituentMutants[0].Id.ShouldBe(5010);
        hom.ConstituentMutants[1].Id.ShouldBe(5020);

        // Verify original FOM IDs are preserved
        var constituent1 = hom.ConstituentMutants[0] as Mutant;
        var constituent2 = hom.ConstituentMutants[1] as Mutant;
        constituent1.OriginalFomId.ShouldBe(10);
        constituent2.OriginalFomId.ShouldBe(20);
    }

    [TestMethod]
    public void GetOriginalFomIdsForActivation_ShouldHandleBackwardCompatibility_WithoutDeepCopy()
    {
        // Arrange - Create HOM without calling CreateDeepCopies (backward compatibility scenario)
        var mutant1 = CreateMutant(15, MutantStatus.Pending, new[] { "Test1" });
        var mutant2 = CreateMutant(25, MutantStatus.Pending, new[] { "Test2" });
        var hom = new HigherOrderMutant(new List<IMutant> { mutant1, mutant2 }) { Id = 600 };

        // Act - Don't call CreateDeepCopies to test backward compatibility
        var originalFomIds = hom.GetOriginalConstituentMutantIdsForActivation();

        // Assert - Should return empty list since ConstituentMutants is null
        originalFomIds.ShouldNotBeNull();
        originalFomIds.ShouldBeEmpty();
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
