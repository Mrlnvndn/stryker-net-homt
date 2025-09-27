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
            _mockFOMs = HeuristicTestHelpers.CreateTestMutants(5);

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
            var heuristic1 = HeuristicTestHelpers.CreateScoringHeuristicMock("Heuristic1", 2.0, 0.5);
            var heuristic2 = HeuristicTestHelpers.CreateScoringHeuristicMock("Heuristic2", 1.0, 1.0);
            var heuristic3 = HeuristicTestHelpers.CreateScoringHeuristicMock("Heuristic3", 0.0, 0.0); // Zero weight, should be ignored
            
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
            var heuristic1 = HeuristicTestHelpers.CreateScoringHeuristicMock("Heuristic1", 0.0, 0.5);
            var heuristic2 = HeuristicTestHelpers.CreateScoringHeuristicMock("Heuristic2", 0.0, 1.0);
            
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
            
            // Create filtering heuristics with different filtering behaviors
            var nonFilteringHeuristic = HeuristicTestHelpers.CreateNonCapabilityHeuristicMock("NonFilteringHeuristic");
            var filteringHeuristicNoFilter = HeuristicTestHelpers.CreateFilteringHeuristicMock("FilteringHeuristicNoFilter", false);
            var filteringHeuristicWithFilter = HeuristicTestHelpers.CreateFilteringHeuristicMock("FilteringHeuristicWithFilter", true);
            
            _sut.RegisterHeuristic(nonFilteringHeuristic.Object);
            _sut.RegisterHeuristic(filteringHeuristicNoFilter.Object);
            _sut.RegisterHeuristic(filteringHeuristicWithFilter.Object);

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue();
            nonFilteringHeuristic.Verify(h => h.ShouldFilterCandidate(candidate), Times.Never);
            filteringHeuristicNoFilter.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
            filteringHeuristicWithFilter.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
        }

        [TestMethod]
        public void ShouldFilterCandidate_WhenNoHeuristicFilters_ShouldReturnFalse()
        {
            // Arrange
            var candidate = new List<IMutant> { _mockFOMs[0], _mockFOMs[1] };
            
            // Create filtering and non-filtering heuristics
            var nonFilteringHeuristic = HeuristicTestHelpers.CreateNonCapabilityHeuristicMock("NonFilteringHeuristic");
            var filteringHeuristic1 = HeuristicTestHelpers.CreateFilteringHeuristicMock("FilteringHeuristic1", false);
            var filteringHeuristic2 = HeuristicTestHelpers.CreateFilteringHeuristicMock("FilteringHeuristic2", false);
            
            _sut.RegisterHeuristic(nonFilteringHeuristic.Object);
            _sut.RegisterHeuristic(filteringHeuristic1.Object);
            _sut.RegisterHeuristic(filteringHeuristic2.Object);

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse();
            nonFilteringHeuristic.Verify(h => h.ShouldFilterCandidate(candidate), Times.Never);
            filteringHeuristic1.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
            filteringHeuristic2.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
        }

        [TestMethod]
        public void GetScoringHeuristics_ShouldReturnOnlyHeuristicsWithFitnessScoringAndPositiveWeight()
        {
            // Arrange
            var registry = new HeuristicRegistry(_mockFOMs, _optionsMock.Object, _inputMock.Object, false);
            
            var fitnessScoringWithWeight = HeuristicTestHelpers.CreateScoringHeuristicMock("FitnessScoringWithWeight", 1.5);
            var fitnessScoringZeroWeight = HeuristicTestHelpers.CreateScoringHeuristicMock("FitnessScoringZeroWeight", 0.0);
            var notFitnessScoring = HeuristicTestHelpers.CreateNonCapabilityHeuristicMock("NotFitnessScoring", 1.0);
            
            registry.RegisterHeuristic(fitnessScoringWithWeight.Object);
            registry.RegisterHeuristic(fitnessScoringZeroWeight.Object);
            registry.RegisterHeuristic(notFitnessScoring.Object);
            
            // Act
            var scoringHeuristics = registry.GetScoringHeuristics().ToList();
            
            // Assert
            scoringHeuristics.Count.ShouldBe(1);
            scoringHeuristics.ShouldContain(h => h.Name == "FitnessScoringWithWeight");
            scoringHeuristics.ShouldNotContain(h => h.Name == "FitnessScoringZeroWeight");
            scoringHeuristics.ShouldNotContain(h => h.Name == "NotFitnessScoring");
        }

        [TestMethod]
        public void GetFilteringHeuristics_ShouldReturnOnlyFilteringHeuristics()
        {
            // Arrange
            var registry = new HeuristicRegistry(_mockFOMs, _optionsMock.Object, _inputMock.Object, false);
            
            var filteringHeuristic = HeuristicTestHelpers.CreateFilteringHeuristicMock("FilteringHeuristic");
            var nonFilteringHeuristic = HeuristicTestHelpers.CreateNonCapabilityHeuristicMock("NonFilteringHeuristic");
            
            registry.RegisterHeuristic(filteringHeuristic.Object);
            registry.RegisterHeuristic(nonFilteringHeuristic.Object);
            
            // Act
            var filteringHeuristics = registry.GetFilteringHeuristics().ToList();
            
            // Assert
            filteringHeuristics.Count.ShouldBe(1);
            filteringHeuristics.ShouldContain(h => h.Name == "FilteringHeuristic");
            filteringHeuristics.ShouldNotContain(h => h.Name == "NonFilteringHeuristic");
        }

        [TestMethod]
        public void GetSearchGuidanceHeuristics_ShouldReturnOnlySearchStrategyHeuristics()
        {
            // Arrange
            var registry = new HeuristicRegistry(_mockFOMs, _optionsMock.Object, _inputMock.Object, false);
            
            var searchHeuristic = HeuristicTestHelpers.CreateSearchHeuristicMock("SearchHeuristic", 
                new List<List<IMutant>> { new List<IMutant> { _mockFOMs[0] } });
            var nonSearchHeuristic = HeuristicTestHelpers.CreateNonCapabilityHeuristicMock("NonSearchHeuristic");
            
            registry.RegisterHeuristic(searchHeuristic.Object);
            registry.RegisterHeuristic(nonSearchHeuristic.Object);
            
            // Act
            var searchHeuristics = registry.GetSearchGuidanceHeuristics().ToList();
            
            // Assert
            searchHeuristics.Count.ShouldBe(1);
            searchHeuristics.ShouldContain(h => h.Name == "SearchHeuristic");
            searchHeuristics.ShouldNotContain(h => h.Name == "NonSearchHeuristic");
        }

        [TestMethod]
        public void ShouldFilterCandidate_ShortCircuitsOnFirstFilterTrue()
        {
            // Arrange
            var candidate = new List<IMutant> { _mockFOMs[0], _mockFOMs[1] };
            var registry = new HeuristicRegistry(_mockFOMs, _optionsMock.Object, _inputMock.Object, false);
            
            // First filtering heuristic returns true (should filter)
            var filteringHeuristic1 = new Mock<IHOMHeuristic>(MockBehavior.Strict);
            filteringHeuristic1.Setup(h => h.Name).Returns("FilteringHeuristic1");
            filteringHeuristic1.Setup(h => h.IsFilteringHeuristic).Returns(true);
            filteringHeuristic1.Setup(h => h.ShouldFilterCandidate(candidate)).Returns(true);
            
            // Second filtering heuristic should never be called due to short-circuiting
            var filteringHeuristic2 = new Mock<IHOMHeuristic>(MockBehavior.Strict);
            filteringHeuristic2.Setup(h => h.Name).Returns("FilteringHeuristic2");
            filteringHeuristic2.Setup(h => h.IsFilteringHeuristic).Returns(true);
            // We do not setup ShouldFilterCandidate for filteringHeuristic2, so it will throw if called
            
            registry.RegisterHeuristic(filteringHeuristic1.Object);
            registry.RegisterHeuristic(filteringHeuristic2.Object);
            
            // Act
            var result = registry.ShouldFilterCandidate(candidate);
            
            // Assert
            result.ShouldBeTrue();
            filteringHeuristic1.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
            // We expect the second heuristic to never be called due to short-circuiting
        }

        [TestMethod]
        public void ShouldFilterCandidate_CallsAllFilteringHeuristicsUntilOneFilters()
        {
            // Arrange
            var candidate = new List<IMutant> { _mockFOMs[0], _mockFOMs[1] };
            var registry = new HeuristicRegistry(_mockFOMs, _optionsMock.Object, _inputMock.Object, false);
            
            // Order matters for this test - we're testing the short-circuit behavior
            var filteringHeuristic1 = HeuristicTestHelpers.CreateFilteringHeuristicMock("FilteringHeuristic1", false);
            var filteringHeuristic2 = HeuristicTestHelpers.CreateFilteringHeuristicMock("FilteringHeuristic2", true);
            
            // Third filtering heuristic should never be called due to short-circuiting after the second
            var filteringHeuristic3 = new Mock<IHOMHeuristic>(MockBehavior.Strict);
            filteringHeuristic3.Setup(h => h.Name).Returns("FilteringHeuristic3");
            filteringHeuristic3.Setup(h => h.IsFilteringHeuristic).Returns(true);
            // We do not setup ShouldFilterCandidate for filteringHeuristic3, so it will throw if called
            
            // Register in specific order for test
            registry.RegisterHeuristic(filteringHeuristic1.Object);
            registry.RegisterHeuristic(filteringHeuristic2.Object);
            registry.RegisterHeuristic(filteringHeuristic3.Object);
            
            // Act
            var result = registry.ShouldFilterCandidate(candidate);
            
            // Assert
            result.ShouldBeTrue();
            filteringHeuristic1.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
            filteringHeuristic2.Verify(h => h.ShouldFilterCandidate(candidate), Times.Once);
            // We expect the third heuristic to never be called due to short-circuiting
        }
    }
}

// Helper Methods for creating mock heuristics
public static class HeuristicTestHelpers
{
    public static Mock<IHOMHeuristic> CreateScoringHeuristicMock(string name, double weight = 1.0, double score = 0.5)
    {
        var mock = new Mock<IHOMHeuristic>();
        mock.Setup(h => h.Name).Returns(name);
        mock.Setup(h => h.IsFilteringHeuristic).Returns(false);
        mock.Setup(h => h.IsFitnessScoringHeuristic).Returns(true);
        mock.Setup(h => h.IsSearchStrategyHeuristic).Returns(false);
        mock.Setup(h => h.Weight).Returns(weight);
        mock.Setup(h => h.ScoreCandidate(It.IsAny<List<IMutant>>())).Returns(score);
        return mock;
    }

    public static Mock<IHOMHeuristic> CreateFilteringHeuristicMock(string name, bool shouldFilter = false)
    {
        var mock = new Mock<IHOMHeuristic>();
        mock.Setup(h => h.Name).Returns(name);
        mock.Setup(h => h.IsFilteringHeuristic).Returns(true);
        mock.Setup(h => h.IsFitnessScoringHeuristic).Returns(false);
        mock.Setup(h => h.IsSearchStrategyHeuristic).Returns(false);
        mock.Setup(h => h.Weight).Returns(0.0);
        mock.Setup(h => h.ShouldFilterCandidate(It.IsAny<List<IMutant>>())).Returns(shouldFilter);
        return mock;
    }

    public static Mock<IHOMHeuristic> CreateSearchHeuristicMock(string name, List<List<IMutant>> suggestions)
    {
        var mock = new Mock<IHOMHeuristic>();
        mock.Setup(h => h.Name).Returns(name);
        mock.Setup(h => h.IsFilteringHeuristic).Returns(false);
        mock.Setup(h => h.IsFitnessScoringHeuristic).Returns(false);
        mock.Setup(h => h.IsSearchStrategyHeuristic).Returns(true);
        mock.Setup(h => h.Weight).Returns(0.0);
        mock.Setup(h => h.SuggestNextCandidates(It.IsAny<List<IMutant>>(), It.IsAny<IReadOnlyCollection<IMutant>>()))
            .Returns(suggestions);
        return mock;
    }

    public static Mock<IHOMHeuristic> CreateMultiCapabilityHeuristicMock(string name, bool isFiltering = false, 
        bool isScoring = false, bool isSearch = false, double weight = 0.0, double score = 0.5)
    {
        var mock = new Mock<IHOMHeuristic>();
        mock.Setup(h => h.Name).Returns(name);
        mock.Setup(h => h.IsFilteringHeuristic).Returns(isFiltering);
        mock.Setup(h => h.IsFitnessScoringHeuristic).Returns(isScoring);
        mock.Setup(h => h.IsSearchStrategyHeuristic).Returns(isSearch);
        mock.Setup(h => h.Weight).Returns(weight);
        
        if (isScoring)
        {
            mock.Setup(h => h.ScoreCandidate(It.IsAny<List<IMutant>>())).Returns(score);
        }
        
        if (isFiltering)
        {
            mock.Setup(h => h.ShouldFilterCandidate(It.IsAny<List<IMutant>>())).Returns(false);
        }
        
        if (isSearch)
        {
            mock.Setup(h => h.SuggestNextCandidates(It.IsAny<List<IMutant>>(), It.IsAny<IReadOnlyCollection<IMutant>>()))
                .Returns(new List<List<IMutant>>());
        }
        
        return mock;
    }

    public static Mock<IHOMHeuristic> CreateNonCapabilityHeuristicMock(string name, double weight = 0.0)
    {
        var mock = new Mock<IHOMHeuristic>();
        mock.Setup(h => h.Name).Returns(name);
        mock.Setup(h => h.IsFilteringHeuristic).Returns(false);
        mock.Setup(h => h.IsFitnessScoringHeuristic).Returns(false);
        mock.Setup(h => h.IsSearchStrategyHeuristic).Returns(false);
        mock.Setup(h => h.Weight).Returns(weight);
        return mock;
    }

    public static List<IMutant> CreateTestMutants(int count)
    {
        var mutants = new List<IMutant>();
        for (int i = 0; i < count; i++)
        {
            var mock = new Mock<IMutant>();
            mock.Setup(m => m.Id).Returns(i);
            mutants.Add(mock.Object);
        }
        return mutants;
    }
}
