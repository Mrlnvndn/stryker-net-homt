using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Heuristics
{
    [TestClass]
    public class HeuristicRegistryTests
    {
        private HeuristicRegistry _sut;
        private List<Mock<IHOMHeuristic>> _heuristicMocks;
        private List<IMutant> _mockFOMs;
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<MutationTestInput> _inputMock;

        [TestInitialize]
        public void Setup()
        {
            // Create test FOMs
            _mockFOMs = CreateTestMutants(5);

            // Setup options and input mocks
            _optionsMock = new Mock<IStrykerOptions>();
            _inputMock = new Mock<MutationTestInput>();

            // Create heuristic mocks
            _heuristicMocks = new List<Mock<IHOMHeuristic>>();
            
            // Create SUT using custom constructor that doesn't register default heuristics
            _sut = new HeuristicRegistry(_mockFOMs, _optionsMock.Object, _inputMock.Object, false);
        }

        [TestMethod]
        public void ScoreCandidate_WithWeightedHeuristics_ShouldReturnWeightedAverage()
        {
            // Arrange
            var candidate = new List<IMutant> { _mockFOMs[0], _mockFOMs[1] };
            
            // Create mocks with different weights and scores
            var heuristic1 = CreateHeuristicMock("Heuristic1", 2.0, 0.5, false, true);
            var heuristic2 = CreateHeuristicMock("Heuristic2", 1.0, 1.0, false, true);
            var heuristic3 = CreateHeuristicMock("Heuristic3", 0.0, 0.0, false, false); // Zero weight, should be ignored
            
            _sut.RegisterHeuristic(heuristic1.Object);
            _sut.RegisterHeuristic(heuristic2.Object);
            _sut.RegisterHeuristic(heuristic3.Object);
            
            // Expected: (0.5 * 2.0 + 1.0 * 1.0) / (2.0 + 1.0) = 0.6667
            var expectedScore = (0.5 * 2.0 + 1.0 * 1.0) / (2.0 + 1.0);

            // Act
            var result = _sut.ScoreCandidate(candidate);

            // Assert
            result.ShouldBeInRange(expectedScore - 0.001, expectedScore + 0.001);
            heuristic1.Verify(h => h.ScoreCandidate(candidate), Times.Once);
            heuristic2.Verify(h => h.ScoreCandidate(candidate), Times.Once);
            heuristic3.Verify(h => h.ScoreCandidate(candidate), Times.Never);
        }

        [TestMethod]
        public void ScoreCandidate_WithNoScoringHeuristics_ShouldReturnNeutralScore()
        {
            // Arrange
            var candidate = new List<IMutant> { _mockFOMs[0], _mockFOMs[1] };
            
            // All heuristics have zero weight, so none will be used for scoring
            var heuristic1 = CreateHeuristicMock("Heuristic1", 0.0, 0.5, false, true);
            var heuristic2 = CreateHeuristicMock("Heuristic2", 0.0, 1.0, false, true);
            
            _sut.RegisterHeuristic(heuristic1.Object);
            _sut.RegisterHeuristic(heuristic2.Object);

            // Act
            var result = _sut.ScoreCandidate(candidate);

            // Assert
            result.ShouldBe(0.5); // Neutral score
            heuristic1.Verify(h => h.ScoreCandidate(candidate), Times.Never);
            heuristic2.Verify(h => h.ScoreCandidate(candidate), Times.Never);
        }

        [TestMethod]
        public void ShouldFilterCandidate_WhenAnyHeuristicFilters_ShouldReturnTrue()
        {
            // Arrange
            var candidate = new List<IMutant> { _mockFOMs[0], _mockFOMs[1] };
            
            var heuristic1 = CreateHeuristicMock("Heuristic1", 1.0, 0.5, false, true);
            var heuristic2 = CreateHeuristicMock("Heuristic2", 1.0, 0.2, true, true); // This one filters
            var heuristic3 = CreateHeuristicMock("Heuristic3", 1.0, 0.7, false, true);
            
            _sut.RegisterHeuristic(heuristic1.Object);
            _sut.RegisterHeuristic(heuristic2.Object);
            _sut.RegisterHeuristic(heuristic3.Object);

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue();
            heuristic1.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
            heuristic2.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
            heuristic3.Verify(h => h.ShouldFilterCandidate(candidate), Times.AtMostOnce); // May stop early due to short-circuiting
        }

        [TestMethod]
        public void ShouldFilterCandidate_WhenNoHeuristicFilters_ShouldReturnFalse()
        {
            // Arrange
            var candidate = new List<IMutant> { _mockFOMs[0], _mockFOMs[1] };
            
            var heuristic1 = CreateHeuristicMock("Heuristic1", 1.0, 0.5, false, true);
            var heuristic2 = CreateHeuristicMock("Heuristic2", 1.0, 0.2, false, true);
            
            _sut.RegisterHeuristic(heuristic1.Object);
            _sut.RegisterHeuristic(heuristic2.Object);

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse();
            heuristic1.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
            heuristic2.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
        }

        [TestMethod]
        public void SuggestNextCandidates_ShouldAggregateAllHeuristicSuggestions()
        {
            // Arrange
            var currentCandidate = new List<IMutant> { _mockFOMs[0], _mockFOMs[1] };
            var availableFOMs = _mockFOMs;
            
            var suggestion1 = new List<IMutant> { _mockFOMs[0], _mockFOMs[2] };
            var suggestion2 = new List<IMutant> { _mockFOMs[1], _mockFOMs[3] };
            var suggestion3 = new List<IMutant> { _mockFOMs[2], _mockFOMs[4] };
            
            var heuristic1 = new Mock<IHOMHeuristic>();
            heuristic1.Setup(h => h.CanGuideSearch).Returns(true);
            heuristic1.Setup(h => h.SuggestNextCandidates(currentCandidate, availableFOMs))
                .Returns(new List<List<IMutant>> { suggestion1 });
            
            var heuristic2 = new Mock<IHOMHeuristic>();
            heuristic2.Setup(h => h.CanGuideSearch).Returns(true);
            heuristic2.Setup(h => h.SuggestNextCandidates(currentCandidate, availableFOMs))
                .Returns(new List<List<IMutant>> { suggestion2, suggestion3 });
                
            var heuristic3 = new Mock<IHOMHeuristic>();
            heuristic3.Setup(h => h.CanGuideSearch).Returns(false); // This one cannot guide search
            
            _sut.RegisterHeuristic(heuristic1.Object);
            _sut.RegisterHeuristic(heuristic2.Object);
            _sut.RegisterHeuristic(heuristic3.Object);

            // Act
            var result = _sut.SuggestNextCandidates(currentCandidate, availableFOMs);

            // Assert
            result.Count.ShouldBe(3); // All 3 suggestions combined
            result.ShouldContain(suggestion1);
            result.ShouldContain(suggestion2);
            result.ShouldContain(suggestion3);
            
            heuristic1.Verify(h => h.SuggestNextCandidates(currentCandidate, availableFOMs), Times.Once);
            heuristic2.Verify(h => h.SuggestNextCandidates(currentCandidate, availableFOMs), Times.Once);
            heuristic3.Verify(h => h.SuggestNextCandidates(It.IsAny<List<IMutant>>(), It.IsAny<IReadOnlyCollection<IMutant>>()), Times.Never);
        }

        [TestMethod]
        public void RegisterDefaultHeuristics_ShouldRegisterAllExpectedHeuristics()
        {
            // Arrange - create a fresh registry that will register default heuristics
            var registry = new HeuristicRegistry(_mockFOMs, _optionsMock.Object, _inputMock.Object);
            
            // Act - DefaultHeuristics are registered in the constructor
            var registeredHeuristics = registry.RegisteredHeuristics;
            
            // Assert
            registeredHeuristics.Count.ShouldBeGreaterThanOrEqualTo(7); // At least 7 default heuristics
            
            // Check if at least one heuristic of each expected type exists
            // Note: We're assuming these types exist - if they don't, the test will fail
            var heuristicTypes = registeredHeuristics.Select(h => h.GetType().Name).ToList();
            heuristicTypes.ShouldContain("MaxSizeLimitHeuristic");
            heuristicTypes.ShouldContain("CodeLocationHeuristic");
            heuristicTypes.ShouldContain("HardToKillHeuristic");
            heuristicTypes.ShouldContain("MutatorTypeHeuristic");
            heuristicTypes.ShouldContain("DependencyHeuristic");
            heuristicTypes.ShouldContain("SSHOMExpanderHeuristic");
            heuristicTypes.ShouldContain("WeakMutatorFilterHeuristic");
        }

        #region Helper Methods

        private List<IMutant> CreateTestMutants(int count)
        {
            var mutants = new List<IMutant>();
            for (int i = 0; i < count; i++)
            {
                mutants.Add(CreateMockMutant(i));
            }
            return mutants;
        }

        private IMutant CreateMockMutant(int id)
        {
            // Create mock test identifiers
            var testIdentifiers = CreateMockTestIdentifiers(new[] { $"Test{id}", $"Test{id+1}" });
            
            // Create a mock for the mutation
            var mutation = CreateMockMutation($"File{id % 3 + 1}.cs");
            
            // Create a mock mutant
            var mutant = new Mock<IMutant>();
            mutant.Setup(m => m.Id).Returns(id);
            mutant.Setup(m => m.Mutation).Returns(mutation);
            mutant.Setup(m => m.KillingTests).Returns(testIdentifiers);
            
            return mutant.Object;
        }

        private Mutation CreateMockMutation(string filePath)
        {
            // Create a simple mutation without mocking SyntaxNode
            var mutation = new Mutation
            {
                DisplayName = $"Test Mutation {filePath}",
                Type = Mutator.Block,
                Description = "Test Description"
            };
            
            return mutation;
        }
        
        private ITestIdentifiers CreateMockTestIdentifiers(string[] testNames)
        {
            var testIdentifiersMock = new Mock<ITestIdentifiers>();
            
            testIdentifiersMock.Setup(ti => ti.IsEmpty).Returns(false);
            testIdentifiersMock.Setup(ti => ti.ToString()).Returns(string.Join(",", testNames));
            
            // Setup intersection to return itself or a new mock
            // Create a new mock for intersection results to avoid recursion
            var intersectionMock = new Mock<ITestIdentifiers>();
            intersectionMock.Setup(ti => ti.IsEmpty).Returns(false);
            intersectionMock.Setup(ti => ti.ToString()).Returns(string.Join(",", testNames.Take(1)));
            
            testIdentifiersMock.Setup(ti => ti.Intersect(It.IsAny<ITestIdentifiers>()))
                               .Returns(intersectionMock.Object);
                               
            return testIdentifiersMock.Object;
        }

        private Mock<IHOMHeuristic> CreateHeuristicMock(
            string name, 
            double weight, 
            double scoreToReturn, 
            bool shouldFilter,
            bool canGuideSearch)
        {
            var mock = new Mock<IHOMHeuristic>();
            mock.Setup(h => h.Name).Returns(name);
            mock.Setup(h => h.Weight).Returns(weight);
            mock.Setup(h => h.ScoreCandidate(It.IsAny<List<IMutant>>())).Returns(scoreToReturn);
            mock.Setup(h => h.ShouldFilterCandidate(It.IsAny<List<IMutant>>())).Returns(shouldFilter);
            mock.Setup(h => h.CanGuideSearch).Returns(canGuideSearch);
            
            return mock;
        }

        #endregion
    }
}
