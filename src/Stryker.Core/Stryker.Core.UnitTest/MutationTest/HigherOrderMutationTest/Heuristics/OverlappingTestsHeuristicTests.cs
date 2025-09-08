using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Heuristics;

/// <summary>
/// Unit tests for OverlappingTestsHeuristic to verify correct scoring based on covering test overlap.
/// This is crucial for SSHOM validation as overlapping covering tests indicate potential for shared killing tests.
/// </summary>
[TestClass]
public class OverlappingTestsHeuristicTests : TestBase
{
    private OverlappingTestsHeuristic _sut;

    [TestInitialize]
    public void Setup()
    {
        _sut = new OverlappingTestsHeuristic();
    }

    #region Basic Property Tests

    [TestMethod]
    public void Properties_ShouldHaveCorrectValues()
    {
        // Assert
        _sut.Name.ShouldBe("OverlappingTests");
        _sut.Weight.ShouldBe(1.0);
        _sut.IsFitnessScoringHeuristic.ShouldBeTrue();
        _sut.IsFilteringHeuristic.ShouldBeFalse(); // Default from base class
        _sut.IsSearchStrategyHeuristic.ShouldBeFalse(); // Default from base class
    }

    #endregion

    #region ScoreCandidate Tests

    [TestMethod]
    public void ScoreCandidate_WithNullCandidate_ShouldReturnZero()
    {
        // Act
        var score = _sut.ScoreCandidate(null);

        // Assert
        score.ShouldBe(0.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithSingleMutant_ShouldReturnZero()
    {
        // Arrange
        var mutant = CreateMutant(1, new[] { "Test1", "Test2" });
        var candidate = new List<IMutant> { mutant };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithEmptyCandidate_ShouldReturnZero()
    {
        // Arrange
        var candidate = new List<IMutant>();

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithMutantHavingNoCoveringTests_ShouldReturnZero()
    {
        // Arrange
        var mutant1 = CreateMutant(1, new[] { "Test1", "Test2" });
        var mutant2 = CreateMutant(2, new string[0]); // No covering tests
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithPerfectOverlap_ShouldReturnHighScore()
    {
        // Arrange - Both mutants covered by identical tests
        var sharedTests = new[] { "Test1", "Test2", "Test3" };
        var mutant1 = CreateMutant(1, sharedTests);
        var mutant2 = CreateMutant(2, sharedTests);
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.6); // Perfect overlap (Jaccard = 1.0) with reasonable size weight (0.6)
    }

    [TestMethod]
    public void ScoreCandidate_WithNoOverlap_ShouldReturnZero()
    {
        // Arrange - Mutants with completely different covering tests
        var mutant1 = CreateMutant(1, new[] { "Test1", "Test2" });
        var mutant2 = CreateMutant(2, new[] { "Test3", "Test4" });
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithPartialOverlap_ShouldReturnProportionalScore()
    {
        // Arrange - Mutants with some overlapping tests
        var mutant1 = CreateMutant(1, new[] { "Test1", "Test2", "Test3" });
        var mutant2 = CreateMutant(2, new[] { "Test2", "Test3", "Test4" });
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        // Intersection: {Test2, Test3} = 2 tests
        // Union: {Test1, Test2, Test3, Test4} = 4 tests
        // Jaccard similarity: 2/4 = 0.5
        // Size weight for 2 tests: 0.6
        // Expected score: 0.5 * 0.6 = 0.3
        score.ShouldBe(0.3);
    }

    [TestMethod]
    public void ScoreCandidate_WithSingleTestOverlap_ShouldReturnLowScore()
    {
        // Arrange - Mutants with minimal overlap (single test)
        var mutant1 = CreateMutant(1, new[] { "Test1", "Test2" });
        var mutant2 = CreateMutant(2, new[] { "Test2", "Test3" });
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        // Intersection: {Test2} = 1 test
        // Union: {Test1, Test2, Test3} = 3 tests
        // Jaccard similarity: 1/3 ≈ 0.333
        // Size weight for 1 test: 0.3
        // Expected score: 0.333 * 0.3 ≈ 0.1
        score.ShouldBe(0.1);
    }

    [TestMethod]
    public void ScoreCandidate_WithLargeOverlap_ShouldGetMaximumSizeWeight()
    {
        // Arrange - Mutants with large overlapping test sets
        var sharedTests = Enumerable.Range(1, 15).Select(i => $"Test{i}").ToArray();
        var mutant1Tests = sharedTests.Concat(new[] { "UniqueTest1", "UniqueTest2" }).ToArray();
        var mutant2Tests = sharedTests.Concat(new[] { "UniqueTest3", "UniqueTest4" }).ToArray();
        
        var mutant1 = CreateMutant(1, mutant1Tests);
        var mutant2 = CreateMutant(2, mutant2Tests);
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        // Intersection: 15 tests
        // Union: 19 tests (15 shared + 4 unique)
        // Jaccard similarity: 15/19 ≈ 0.789
        // Size weight for 15 tests: 1.0 (maximum)
        // Expected score: 0.789 * 1.0 ≈ 0.789
        score.ShouldBe(0.789);
    }

    [TestMethod]
    public void ScoreCandidate_WithThreeMutants_ShouldCalculateCorrectly()
    {
        // Arrange - Three mutants with various overlaps
        var mutant1 = CreateMutant(1, new[] { "Test1", "Test2", "Test3", "Test4" });
        var mutant2 = CreateMutant(2, new[] { "Test2", "Test3", "Test5" });
        var mutant3 = CreateMutant(3, new[] { "Test3", "Test6" });
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        // Intersection: {Test3} = 1 test (only Test3 is common to all)
        // Union: {Test1, Test2, Test3, Test4, Test5, Test6} = 6 tests
        // Jaccard similarity: 1/6 ≈ 0.167
        // Size weight for 1 test: 0.3
        // Expected score: 0.167 * 0.3 ≈ 0.05
        score.ShouldBe(0.05);
    }

    [TestMethod]
    public void ScoreCandidate_WithComplexRealWorldScenario_ShouldHandleCorrectly()
    {
        // Arrange - Simulate real-world scenario with integration and unit tests
        var mutant1 = CreateMutant(1, new[] { "IntegrationTest1", "UnitTest1", "UnitTest2", "UnitTest3" });
        var mutant2 = CreateMutant(2, new[] { "IntegrationTest1", "UnitTest1", "UnitTest4", "UnitTest5" });
        var mutant3 = CreateMutant(3, new[] { "IntegrationTest1", "UnitTest6" });
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };

        // Act
        var score = _sut.ScoreCandidate(candidate);

        // Assert
        // Intersection: {IntegrationTest1} = 1 test
        // Union: {IntegrationTest1, UnitTest1, UnitTest2, UnitTest3, UnitTest4, UnitTest5, UnitTest6} = 7 tests
        // Jaccard similarity: 1/7 ≈ 0.143
        // Size weight for 1 test: 0.3
        // Expected score: 0.143 * 0.3 ≈ 0.043
        score.ShouldBe(0.043);
    }

    #endregion

    #region ShouldFilterCandidate Tests

    [TestMethod]
    public void ShouldFilterCandidate_WithNullCandidate_ShouldReturnTrue()
    {
        // Act
        var shouldFilter = _sut.ShouldFilterCandidate(null);

        // Assert
        shouldFilter.ShouldBeTrue();
    }

    [TestMethod]
    public void ShouldFilterCandidate_WithSingleMutant_ShouldReturnTrue()
    {
        // Arrange
        var mutant = CreateMutant(1, new[] { "Test1" });
        var candidate = new List<IMutant> { mutant };

        // Act
        var shouldFilter = _sut.ShouldFilterCandidate(candidate);

        // Assert
        shouldFilter.ShouldBeTrue();
    }

    [TestMethod]
    public void ShouldFilterCandidate_WithNoOverlap_ShouldReturnTrue()
    {
        // Arrange
        var mutant1 = CreateMutant(1, new[] { "Test1", "Test2" });
        var mutant2 = CreateMutant(2, new[] { "Test3", "Test4" });
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var shouldFilter = _sut.ShouldFilterCandidate(candidate);

        // Assert
        shouldFilter.ShouldBeTrue(); // No overlap means no SSHOM potential
    }

    [TestMethod]
    public void ShouldFilterCandidate_WithOverlap_ShouldReturnFalse()
    {
        // Arrange
        var mutant1 = CreateMutant(1, new[] { "Test1", "Test2" });
        var mutant2 = CreateMutant(2, new[] { "Test2", "Test3" });
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var shouldFilter = _sut.ShouldFilterCandidate(candidate);

        // Assert
        shouldFilter.ShouldBeFalse(); // Has overlap, keep for potential SSHOM
    }

    [TestMethod]
    public void ShouldFilterCandidate_WithEmptyCoveringTests_ShouldReturnTrue()
    {
        // Arrange
        var mutant1 = CreateMutant(1, new[] { "Test1" });
        var mutant2 = CreateMutant(2, new string[0]); // No covering tests
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var shouldFilter = _sut.ShouldFilterCandidate(candidate);

        // Assert
        shouldFilter.ShouldBeTrue();
    }

    #endregion

    #region Analysis and Integration Tests

    [TestMethod]
    public void GetOverlapAnalysis_ShouldProvideDetailedInformation()
    {
        // Arrange
        var mutant1 = CreateMutant(1, new[] { "Test1", "Test2", "Test3" });
        var mutant2 = CreateMutant(2, new[] { "Test2", "Test3", "Test4" });
        var candidate = new List<IMutant> { mutant1, mutant2 };

        // Act
        var analysis = _sut.GetOverlapAnalysis(candidate);

        // Assert
        analysis.ShouldContain("Intersection: 2");
        analysis.ShouldContain("Union: 4");
        analysis.ShouldContain("Jaccard: 0,500");
        analysis.ShouldContain("Score: 0,300");
    }

    [TestMethod]
    public void GetOverlapAnalysis_WithInvalidCandidate_ShouldReturnErrorMessage()
    {
        // Arrange
        var candidate = new List<IMutant> { CreateMutant(1, new[] { "Test1" }) };

        // Act
        var analysis = _sut.GetOverlapAnalysis(candidate);

        // Assert
        analysis.ShouldBe("Invalid candidate for overlap analysis");
    }

    [TestMethod]
    public void ScoreCandidate_ShouldAlwaysReturnNormalizedScore()
    {
        // Arrange - Test various scenarios to ensure score is always between 0.0 and 1.0
        var testCases = new[]
        {
            // Perfect overlap, large size
            (Enumerable.Range(1, 20).Select(i => $"Test{i}").ToArray(), 
             Enumerable.Range(1, 20).Select(i => $"Test{i}").ToArray()),
            
            // No overlap
            (new[] { "Test1", "Test2" }, new[] { "Test3", "Test4" }),
            
            // Minimal overlap
            (new[] { "Test1" }, new[] { "Test1" }),
            
            // Partial overlap
            (new[] { "Test1", "Test2", "Test3" }, new[] { "Test2", "Test3", "Test4" }),
        };

        foreach (var (tests1, tests2) in testCases)
        {
            var mutant1 = CreateMutant(1, tests1);
            var mutant2 = CreateMutant(2, tests2);
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var score = _sut.ScoreCandidate(candidate);

            // Assert
            score.ShouldBeGreaterThanOrEqualTo(0.0);
            score.ShouldBeLessThanOrEqualTo(1.0);
        }
    }

    [TestMethod]
    public void ScoreCandidate_WithSSHOMPotentialScenarios_ShouldRankCorrectly()
    {
        // Arrange - Create scenarios with different SSHOM potential
        var highPotential = new List<IMutant>
        {
            CreateMutant(1, new[] { "IntegrationTest1", "UnitTest1", "UnitTest2", "UnitTest4" }),
            CreateMutant(2, new[] { "IntegrationTest1", "UnitTest1", "UnitTest3", "UnitTest4" })
        };

        var mediumPotential = new List<IMutant>
        {
            CreateMutant(3, new[] { "IntegrationTest1", "UnitTest2", "UnitTest4" }),
            CreateMutant(4, new[] { "IntegrationTest1", "UnitTest2", "UnitTest5" })
        };

        var lowPotential = new List<IMutant>
        {
            CreateMutant(5, new[] { "UnitTest5", "UnitTest6" }),
            CreateMutant(6, new[] { "UnitTest5" })
        };

        var noPotential = new List<IMutant>
        {
            CreateMutant(7, new[] { "UnitTest7" }),
            CreateMutant(8, new[] { "UnitTest8" })
        };

        // Act
        var highScore = _sut.ScoreCandidate(highPotential);
        var mediumScore = _sut.ScoreCandidate(mediumPotential);
        var lowScore = _sut.ScoreCandidate(lowPotential);
        var noScore = _sut.ScoreCandidate(noPotential);

        // Assert - Scores should be ranked according to SSHOM potential
        highScore.ShouldBeGreaterThan(mediumScore);
        mediumScore.ShouldBeGreaterThan(lowScore);
        lowScore.ShouldBeGreaterThan(noScore);
        noScore.ShouldBe(0.0);
    }

    [TestMethod]
    public void ScoreCandidate_ShouldConsiderBothOverlapRatioAndAbsoluteSize()
    {
        // Arrange - Two scenarios: high ratio with small size vs lower ratio with larger size
        var highRatioSmallSize = new List<IMutant>
        {
            CreateMutant(1, new[] { "Test1" }),
            CreateMutant(2, new[] { "Test1" }) // Perfect overlap but only 1 test
        };

        var lowerRatioLargerSize = new List<IMutant>
        {
            CreateMutant(3, new[] { "Test1", "Test2", "Test3", "Test4", "Test5" }),
            CreateMutant(4, new[] { "Test1", "Test2", "Test3", "Test6", "Test7" }) // 3 out of 7 unique tests
        };

        // Act
        var highRatioScore = _sut.ScoreCandidate(highRatioSmallSize);
        var lowerRatioScore = _sut.ScoreCandidate(lowerRatioLargerSize);

        // Assert
        // High ratio (1.0) but low size weight (0.3) = 0.3
        // Lower ratio (3/7 ≈ 0.43) but higher size weight (0.6) ≈ 0.26
        // The high ratio should still win, but the difference should be modest
        highRatioScore.ShouldBe(0.3, 0.01);
        lowerRatioScore.ShouldBe(0.26, 0.01);
        highRatioScore.ShouldBeGreaterThan(lowerRatioScore);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test mutant with specified covering tests
    /// </summary>
    private static Mutant CreateMutant(int id, string[] coveringTests)
    {
        return new Mutant
        {
            Id = id,
            ResultStatus = MutantStatus.Pending,
            CoveringTests = new TestIdentifierList(coveringTests),
            AssessingTests = new TestIdentifierList(coveringTests), // Assume same for simplicity
            KillingTests = TestIdentifierList.NoTest(),
            Mutation = new Mutation
            {
                DisplayName = $"Test Mutation {id}",
                Type = Mutator.Arithmetic,
                Description = $"Test mutation {id}"
            }
        };
    }

    #endregion
}
