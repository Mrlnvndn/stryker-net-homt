using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.MutationTest;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Heuristics
{
    [TestClass]
    public class EmptyAssessingTestsFilterHeuristicTests : TestBase
    {
        private EmptyAssessingTestsFilterHeuristic _heuristic;
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<MutationTestInput> _inputMock;

        [TestInitialize]
        public void Setup()
        {
            _heuristic = new EmptyAssessingTestsFilterHeuristic();
            _optionsMock = new Mock<IStrykerOptions>();
            _inputMock = new Mock<MutationTestInput>();
        }

        [TestMethod]
        public void Name_ShouldReturnCorrectName()
        {
            // Assert
            _heuristic.Name.ShouldBe("EmptyAssessingTestsFilter");
        }

        [TestMethod]
        public void Properties_ShouldBeConfiguredCorrectly()
        {
            // Assert
            _heuristic.Weight.ShouldBe(0.0);
            _heuristic.IsFilteringHeuristic.ShouldBeTrue();
            _heuristic.IsFitnessScoringHeuristic.ShouldBeFalse();
            _heuristic.IsSearchStrategyHeuristic.ShouldBeFalse();
            _heuristic.RequiresPreRun.ShouldBeFalse();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithNullCandidate_ShouldReturnTrue()
        {
            // Act
            var result = _heuristic.ShouldFilterCandidate(null);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithSingleMutant_ShouldReturnTrue()
        {
            // Arrange
            var singleMutant = new List<IMutant> { CreateMockMutant(1, new[] { "Test1" }) };

            // Act
            var result = _heuristic.ShouldFilterCandidate(singleMutant);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithEmptyList_ShouldReturnTrue()
        {
            // Arrange
            var emptyList = new List<IMutant>();

            // Act
            var result = _heuristic.ShouldFilterCandidate(emptyList);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithHOMHavingEmptyAssessingTests_ShouldReturnTrue()
        {
            // Arrange - Create mutants with no overlapping assessing tests
            var mutant1 = CreateMockMutant(1, new[] { "TestA", "TestB" });
            var mutant2 = CreateMockMutant(2, new[] { "TestC", "TestD" });
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var result = _heuristic.ShouldFilterCandidate(candidate);

            // Assert  
            result.ShouldBeTrue("Should filter HOM with empty assessing tests (no test overlap)");
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithHOMHavingNonEmptyAssessingTests_ShouldReturnFalse()
        {
            // Arrange - Create mutants with overlapping assessing tests
            var mutant1 = CreateMockMutant(1, new[] { "Test1", "Test2" });
            var mutant2 = CreateMockMutant(2, new[] { "Test1", "Test3" }); // Test1 overlaps
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var result = _heuristic.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse("Should not filter HOM with non-empty assessing tests");
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithThreeMutantsHavingCommonTests_ShouldReturnFalse()
        {
            // Arrange - Create three mutants with one common test
            var mutant1 = CreateMockMutant(1, new[] { "Test1", "Test2", "Test3" });
            var mutant2 = CreateMockMutant(2, new[] { "Test1", "Test4" });
            var mutant3 = CreateMockMutant(3, new[] { "Test1", "Test5" });
            var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };

            // Act
            var result = _heuristic.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse("Should not filter HOM when all constituents share at least one test");
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithThreeMutantsHavingNoCommonTests_ShouldReturnTrue()
        {
            // Arrange - Create three mutants with no common test intersection
            var mutant1 = CreateMockMutant(1, new[] { "Test1", "Test2" });
            var mutant2 = CreateMockMutant(2, new[] { "Test3", "Test4" });  
            var mutant3 = CreateMockMutant(3, new[] { "Test5", "Test6" });
            var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };

            // Act
            var result = _heuristic.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue("Should filter HOM when constituents have no test intersection");
        }

        [TestMethod]
        public void ScoreCandidate_WithValidCandidate_ShouldReturnOne()
        {
            // Arrange
            var mutant1 = CreateMockMutant(1, new[] { "Test1", "Test2" });
            var mutant2 = CreateMockMutant(2, new[] { "Test1", "Test3" });
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var score = _heuristic.ScoreCandidate(candidate);

            // Assert
            score.ShouldBe(1.0);
        }

        [TestMethod]
        public void ScoreCandidate_WithFilterableCandidate_ShouldReturnZero()
        {
            // Arrange - Create mutants with no overlapping tests
            var mutant1 = CreateMockMutant(1, new[] { "TestA" });
            var mutant2 = CreateMockMutant(2, new[] { "TestB" });
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var score = _heuristic.ScoreCandidate(candidate);

            // Assert
            score.ShouldBe(0.0);
        }

        [TestMethod]
        public void SuggestNextCandidates_ShouldReturnEmptyList()
        {
            // Arrange
            var currentCandidate = new List<IMutant> { CreateMockMutant(1, new[] { "Test1" }) };
            var availableFOMs = new List<IMutant> { CreateMockMutant(2, new[] { "Test2" }) };

            // Act
            var suggestions = _heuristic.SuggestNextCandidates(currentCandidate, availableFOMs);

            // Assert
            suggestions.ShouldBeEmpty();
        }

        [TestMethod]
        public void Initialize_ShouldNotThrow()
        {
            // Arrange
            var availableFOMs = new List<IMutant> { CreateMockMutant(1, new[] { "Test1" }) };

            // Act & Assert
            Should.NotThrow(() => _heuristic.Initialize(availableFOMs, _optionsMock.Object, _inputMock.Object));
        }

        [TestMethod]
        public void ShouldFilterCandidate_PerformanceTest_ShouldBeEfficient()
        {
            // Arrange - Create a larger candidate to test performance
            var largeCandidateWithOverlap = new List<IMutant>();
            for (int i = 1; i <= 10; i++)
            {
                // All mutants share "CommonTest" to ensure they won't be filtered
                largeCandidateWithOverlap.Add(CreateMockMutant(i, new[] { "CommonTest", $"Test{i}" }));
            }

            var largeCandidateWithoutOverlap = new List<IMutant>();
            for (int i = 1; i <= 10; i++)
            {
                // Each mutant has unique tests to ensure filtering
                largeCandidateWithoutOverlap.Add(CreateMockMutant(i, new[] { $"UniqueTest{i}" }));
            }

            // Act & Assert - Should complete quickly without creating HigherOrderMutant objects
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var result1 = _heuristic.ShouldFilterCandidate(largeCandidateWithOverlap);
            var result2 = _heuristic.ShouldFilterCandidate(largeCandidateWithoutOverlap);
            
            stopwatch.Stop();

            // Assert results
            result1.ShouldBeFalse("Large candidate with overlap should not be filtered");
            result2.ShouldBeTrue("Large candidate without overlap should be filtered");
            
            // Performance assertion - should complete very quickly
            stopwatch.ElapsedMilliseconds.ShouldBeLessThan(10, "Heuristic should be very fast without creating temporary HOM objects");
        }

        private IMutant CreateMockMutant(int id, string[] assessingTests)
        {
            var mutantMock = new Mock<IMutant>();
            mutantMock.Setup(m => m.Id).Returns(id);
            mutantMock.Setup(m => m.ResultStatus).Returns(MutantStatus.Pending);
            
            // Create test identifiers
            var testIdentifiers = new TestIdentifierList(assessingTests);
            mutantMock.Setup(m => m.AssessingTests).Returns(testIdentifiers);
            mutantMock.Setup(m => m.CoveringTests).Returns(testIdentifiers);

            return mutantMock.Object;
        }
    }
}
