using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Heuristics
{
    [TestClass]
    public class WeakMutatorFilterHeuristicTests : TestBase
    {
        private WeakMutatorFilterHeuristic _sut;
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<MutationTestInput> _inputMock;

        [TestInitialize]
        public void Setup()
        {
            _sut = new WeakMutatorFilterHeuristic();
            _optionsMock = new Mock<IStrykerOptions>();
            _inputMock = new Mock<MutationTestInput>();
        }

        [TestMethod]
        public void Name_ShouldReturnExpectedName()
        {
            // Act & Assert
            _sut.Name.ShouldBe("WeakMutatorFilter");
        }

        [TestMethod]
        public void Properties_ShouldBeConfiguredCorrectly()
        {
            // Act & Assert
            _sut.IsFilteringHeuristic.ShouldBeTrue();
            _sut.IsFitnessScoringHeuristic.ShouldBeTrue();
            _sut.IsSearchStrategyHeuristic.ShouldBeFalse();
        }

        [TestMethod]
        public void Constructor_WithDefaultThreshold_ShouldSetCorrectThreshold()
        {
            // Arrange & Act
            var heuristic = new WeakMutatorFilterHeuristic();

            // Assert - Default threshold should be 30.0
            var mutants = new List<IMutant> { CreateMockMutant(1, Mutator.String, MutantStatus.Killed) };
            heuristic.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            // For a mutator type with 100% MSI (all killed), it should not be filtered
            var candidate = new List<IMutant> { CreateMockMutant(2, Mutator.String, MutantStatus.Killed) };
            var shouldFilter = heuristic.ShouldFilterCandidate(candidate);
            shouldFilter.ShouldBeFalse();
        }

        [TestMethod]
        public void Constructor_WithCustomThreshold_ShouldSetCustomThreshold()
        {
            // Arrange & Act
            var heuristic = new WeakMutatorFilterHeuristic(50.0);

            // Create mutants with low MSI (25%)
            var mutants = new List<IMutant>
            {
                CreateMockMutant(1, Mutator.String, MutantStatus.Killed),
                CreateMockMutant(2, Mutator.String, MutantStatus.Survived),
                CreateMockMutant(3, Mutator.String, MutantStatus.Survived),
                CreateMockMutant(4, Mutator.String, MutantStatus.Survived)
            };
            heuristic.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            // Assert - With threshold 50.0 and MSI 25.0, should filter
            var candidate = new List<IMutant> { CreateMockMutant(5, Mutator.String, MutantStatus.Survived) };
            var shouldFilter = heuristic.ShouldFilterCandidate(candidate);
            shouldFilter.ShouldBeTrue();
        }

        [TestMethod]
        public void OnInitialized_WithDifferentMutatorTypes_ShouldCalculateCorrectMSI()
        {
            // Arrange - Create mutants with different MSI rates
            var mutants = new List<IMutant>
            {
                // String mutator: 1 killed out of 2 = 50% MSI
                CreateMockMutant(1, Mutator.String, MutantStatus.Killed),
                CreateMockMutant(2, Mutator.String, MutantStatus.Survived),
                
                // Arithmetic mutator: 3 killed out of 3 = 100% MSI
                CreateMockMutant(3, Mutator.Arithmetic, MutantStatus.Killed),
                CreateMockMutant(4, Mutator.Arithmetic, MutantStatus.Killed),
                CreateMockMutant(5, Mutator.Arithmetic, MutantStatus.Killed),
                
                // Boolean mutator: 0 killed out of 1 = 0% MSI
                CreateMockMutant(6, Mutator.Boolean, MutantStatus.Survived)
            };

            // Act
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            // Assert - String mutator candidate should score 0.5 (50% MSI normalized)
            var stringCandidate = new List<IMutant> { CreateMockMutant(7, Mutator.String, MutantStatus.Survived) };
            var stringScore = _sut.ScoreCandidate(stringCandidate);
            stringScore.ShouldBe(0.5, tolerance: 0.01);

            // Assert - Arithmetic mutator candidate should score 1.0 (100% MSI normalized)
            var arithmeticCandidate = new List<IMutant> { CreateMockMutant(8, Mutator.Arithmetic, MutantStatus.Killed) };
            var arithmeticScore = _sut.ScoreCandidate(arithmeticCandidate);
            arithmeticScore.ShouldBe(1.0, tolerance: 0.01);

            // Assert - Boolean mutator candidate should score 0.0 (0% MSI normalized)
            var booleanCandidate = new List<IMutant> { CreateMockMutant(9, Mutator.Boolean, MutantStatus.Survived) };
            var booleanScore = _sut.ScoreCandidate(booleanCandidate);
            booleanScore.ShouldBe(0.0, tolerance: 0.01);
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithNullCandidate_ShouldReturnTrue()
        {
            // Act
            var result = _sut.ShouldFilterCandidate(null);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithEmptyCandidate_ShouldReturnTrue()
        {
            // Arrange
            var candidate = new List<IMutant>();

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMutantBelowThreshold_ShouldReturnTrue()
        {
            // Arrange - Create mutants with MSI below threshold (30%)
            var mutants = new List<IMutant>
            {
                CreateMockMutant(1, Mutator.String, MutantStatus.Killed),     // 1 killed
                CreateMockMutant(2, Mutator.String, MutantStatus.Survived),  // 3 survived = 25% MSI
                CreateMockMutant(3, Mutator.String, MutantStatus.Survived),
                CreateMockMutant(4, Mutator.String, MutantStatus.Survived)
            };
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            var candidate = new List<IMutant> { CreateMockMutant(5, Mutator.String, MutantStatus.Survived) };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMutantAboveThreshold_ShouldReturnFalse()
        {
            // Arrange - Create mutants with MSI above threshold (30%)
            var mutants = new List<IMutant>
            {
                CreateMockMutant(1, Mutator.String, MutantStatus.Killed),   // 2 killed
                CreateMockMutant(2, Mutator.String, MutantStatus.Killed),   // 1 survived = 66.7% MSI
                CreateMockMutant(3, Mutator.String, MutantStatus.Survived)
            };
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            var candidate = new List<IMutant> { CreateMockMutant(4, Mutator.String, MutantStatus.Killed) };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMixedMutantTypes_ShouldFilterIfAnyBelowThreshold()
        {
            // Arrange
            var mutants = new List<IMutant>
            {
                // String mutator: 25% MSI (below threshold)
                CreateMockMutant(1, Mutator.String, MutantStatus.Killed),
                CreateMockMutant(2, Mutator.String, MutantStatus.Survived),
                CreateMockMutant(3, Mutator.String, MutantStatus.Survived),
                CreateMockMutant(4, Mutator.String, MutantStatus.Survived),
                
                // Arithmetic mutator: 100% MSI (above threshold)
                CreateMockMutant(5, Mutator.Arithmetic, MutantStatus.Killed),
                CreateMockMutant(6, Mutator.Arithmetic, MutantStatus.Killed)
            };
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            var candidate = new List<IMutant>
            {
                CreateMockMutant(7, Mutator.String, MutantStatus.Survived),      // Below threshold
                CreateMockMutant(8, Mutator.Arithmetic, MutantStatus.Killed)     // Above threshold
            };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue(); // Should filter because String mutator is below threshold
        }

        [TestMethod]
        public void ScoreCandidate_WithNullCandidate_ShouldReturnZero()
        {
            // Act
            var result = _sut.ScoreCandidate(null);

            // Assert
            result.ShouldBe(0.0);
        }

        [TestMethod]
        public void ScoreCandidate_WithEmptyCandidate_ShouldReturnZero()
        {
            // Arrange
            var candidate = new List<IMutant>();

            // Act
            var result = _sut.ScoreCandidate(candidate);

            // Assert
            result.ShouldBe(0.0);
        }

        [TestMethod]
        public void ScoreCandidate_WithMixedMutantTypes_ShouldReturnAverageScore()
        {
            // Arrange
            var mutants = new List<IMutant>
            {
                // String mutator: 50% MSI
                CreateMockMutant(1, Mutator.String, MutantStatus.Killed),
                CreateMockMutant(2, Mutator.String, MutantStatus.Survived),
                
                // Arithmetic mutator: 100% MSI
                CreateMockMutant(3, Mutator.Arithmetic, MutantStatus.Killed)
            };
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            var candidate = new List<IMutant>
            {
                CreateMockMutant(4, Mutator.String, MutantStatus.Survived),      // Score: 0.5
                CreateMockMutant(5, Mutator.Arithmetic, MutantStatus.Killed)     // Score: 1.0
            };

            // Act
            var result = _sut.ScoreCandidate(candidate);

            // Assert
            result.ShouldBe(0.75, tolerance: 0.01); // (0.5 + 1.0) / 2 = 0.75
        }

        [TestMethod]
        public void ScoreCandidate_WithUnknownMutatorType_ShouldReturnNeutralScore()
        {
            // Arrange - Initialize with no mutants so no statistics are available
            _sut.Initialize(new List<IMutant>(), _optionsMock.Object, _inputMock.Object);

            var candidate = new List<IMutant> { CreateMockMutant(1, Mutator.String, MutantStatus.Survived) };

            // Act
            var result = _sut.ScoreCandidate(candidate);

            // Assert
            result.ShouldBe(0.5); // Neutral score for unknown mutator type
        }

        [TestMethod]
        public void OnInitialized_WithEmptyMutantList_ShouldHandleGracefully()
        {
            // Arrange
            var mutants = new List<IMutant>();

            // Act & Assert - Should not throw
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            // Verify behavior with empty statistics
            var candidate = new List<IMutant> { CreateMockMutant(1, Mutator.String, MutantStatus.Survived) };
            var score = _sut.ScoreCandidate(candidate);
            score.ShouldBe(0.5); // Default neutral score
        }

        [TestMethod]
        public void OnInitialized_WithSingleMutatorType_AllKilled_ShouldCalculate100PercentMSI()
        {
            // Arrange
            var mutants = new List<IMutant>
            {
                CreateMockMutant(1, Mutator.String, MutantStatus.Killed),
                CreateMockMutant(2, Mutator.String, MutantStatus.Killed),
                CreateMockMutant(3, Mutator.String, MutantStatus.Killed)
            };

            // Act
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            // Assert
            var candidate = new List<IMutant> { CreateMockMutant(4, Mutator.String, MutantStatus.Killed) };
            var score = _sut.ScoreCandidate(candidate);
            score.ShouldBe(1.0); // 100% MSI = score of 1.0
        }

        [TestMethod]
        public void OnInitialized_WithSingleMutatorType_AllSurvived_ShouldCalculateZeroPercentMSI()
        {
            // Arrange
            var mutants = new List<IMutant>
            {
                CreateMockMutant(1, Mutator.String, MutantStatus.Survived),
                CreateMockMutant(2, Mutator.String, MutantStatus.Survived),
                CreateMockMutant(3, Mutator.String, MutantStatus.Survived)
            };

            // Act
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            // Assert
            var candidate = new List<IMutant> { CreateMockMutant(4, Mutator.String, MutantStatus.Survived) };
            var score = _sut.ScoreCandidate(candidate);
            score.ShouldBe(0.0); // 0% MSI = score of 0.0
        }

        [TestMethod]
        public void OnInitialized_WithNonKilledOrSurvivedStatuses_ShouldIgnoreThemInMSICalculation()
        {
            // Arrange
            var mutants = new List<IMutant>
            {
                CreateMockMutant(1, Mutator.String, MutantStatus.Killed),    // Should count
                CreateMockMutant(2, Mutator.String, MutantStatus.Survived),  // Should count
                CreateMockMutant(3, Mutator.String, MutantStatus.Timeout),   // Should count as survived (not killed)
                CreateMockMutant(4, Mutator.String, MutantStatus.NoCoverage) // Should count as survived (not killed)
            };

            // Act
            _sut.Initialize(mutants, _optionsMock.Object, _inputMock.Object);

            // Assert - MSI should be 25% (1 killed out of 4 total mutants)
            var candidate = new List<IMutant> { CreateMockMutant(5, Mutator.String, MutantStatus.Killed) };
            var score = _sut.ScoreCandidate(candidate);
            score.ShouldBe(0.25, tolerance: 0.01);
        }

        private IMutant CreateMockMutant(int id, Mutator mutatorType, MutantStatus status)
        {
            var mockMutant = new Mock<IMutant>();
            mockMutant.Setup(m => m.Id).Returns(id);
            mockMutant.Setup(m => m.ResultStatus).Returns(status);
            
            var mockMutation = new Mutation
            {
                Type = mutatorType,
                DisplayName = $"Test mutation {id}"
            };
            mockMutant.Setup(m => m.Mutation).Returns(mockMutation);

            return mockMutant.Object;
        }
    }
}
