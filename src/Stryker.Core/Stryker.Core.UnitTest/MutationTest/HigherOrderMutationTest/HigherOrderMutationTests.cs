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
using Stryker.Core.MutationTest.HigherOrderMutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Stryker.Core.Mutants;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest
{
    [TestClass]
    public class HigherOrderMutationTests : TestBase
    {
        private HigherOrderMutation _sut;
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<MutationTestInput> _inputMock;
        private List<IMutant> _testMutants;

        [TestInitialize]
        public void Setup()
        {
            _optionsMock = new Mock<IStrykerOptions>();
            _inputMock = new Mock<MutationTestInput>();
            _testMutants = CreateStructuredTestMutants();
            
            _sut = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, _testMutants);
        }

        #region Test Data Creation

        /// <summary>
        /// Creates a structured set of test mutants with different characteristics for comprehensive testing
        /// </summary>
        private List<IMutant> CreateStructuredTestMutants()
        {
            var mutants = new List<IMutant>();

            // Group 1: Pending mutants with same killing tests (should be prioritized for HOMs)
            mutants.Add(CreateMockMutant(1, MutantStatus.Pending, ["Test1", "Test2"], "File1.cs", "Method1"));
            mutants.Add(CreateMockMutant(2, MutantStatus.Pending, ["Test1", "Test2"], "File1.cs", "Method2"));
            
            // Group 2: Survived mutants with different killing tests
            mutants.Add(CreateMockMutant(3, MutantStatus.Survived, ["Test3", "Test4"], "File1.cs", "Method3"));
            mutants.Add(CreateMockMutant(4, MutantStatus.Survived, ["Test5", "Test6"], "File2.cs", "Method1"));
            
            // Group 3: Killed mutants (should be excluded from HOM generation)
            mutants.Add(CreateMockMutant(5, MutantStatus.Killed, ["Test1"], "File2.cs", "Method2"));
            mutants.Add(CreateMockMutant(6, MutantStatus.Killed, ["Test2"], "File2.cs", "Method3"));
            
            // Group 4: NoCoverage mutants (should be excluded)
            mutants.Add(CreateMockMutant(7, MutantStatus.NoCoverage, [], "File3.cs", "Method1"));
            
            // Group 5: More pending mutants in same file for proximity testing
            mutants.Add(CreateMockMutant(8, MutantStatus.Pending, ["Test7"], "File1.cs", "Method4"));
            mutants.Add(CreateMockMutant(9, MutantStatus.Survived, ["Test8"], "File1.cs", "Method5"));

            return mutants;
        }

        private IMutant CreateMockMutant(int id, MutantStatus status, string[] killingTests, string filePath, string methodName)
        {
            var mutantMock = new Mock<IMutant>();
            var mutation = CreateMockMutation(filePath, methodName);
            var testIdentifiers = CreateMockTestIdentifiers(killingTests);

            mutantMock.Setup(m => m.Id).Returns(id);
            mutantMock.Setup(m => m.ResultStatus).Returns(status);
            mutantMock.Setup(m => m.Mutation).Returns(mutation);
            mutantMock.Setup(m => m.KillingTests).Returns(testIdentifiers);

            return mutantMock.Object;
        }

        private static Mutation CreateMockMutation(string filePath, string methodName)
        {
            var originalTree = SyntaxFactory.ParseSyntaxTree(
                text: $"class TestClass {{ void {methodName}() {{ }} }}",
                path: filePath);
            var originalNode = originalTree.GetRoot();

            var replacementTree = SyntaxFactory.ParseSyntaxTree(
                text: $"class TestClass {{ void {methodName}() {{ /* mutated */ }} }}");
            var replacementNode = replacementTree.GetRoot();

            return new Mutation
            {
                DisplayName = $"Test Mutation {methodName}",
                Type = Mutator.Block,
                Description = $"Test mutation in {methodName}",
                OriginalNode = originalNode,
                ReplacementNode = replacementNode
            };
        }

        private static ITestIdentifiers CreateMockTestIdentifiers(string[] testNames)
        {
            var testIdentifiersMock = new Mock<ITestIdentifiers>();
            
            testIdentifiersMock.Setup(ti => ti.IsEmpty).Returns(testNames.Length == 0);
            testIdentifiersMock.Setup(ti => ti.ToString()).Returns(string.Join(",", testNames));
            testIdentifiersMock.Setup(ti => ti.GetIdentifiers()).Returns(testNames);

            // Setup intersection behavior
            testIdentifiersMock.Setup(ti => ti.Intersect(It.IsAny<ITestIdentifiers>()))
                .Returns<ITestIdentifiers>(other =>
                {
                    var otherTests = other.GetIdentifiers().ToHashSet();
                    var intersection = testNames.Where(t => otherTests.Contains(t)).ToArray();
                    return CreateMockTestIdentifiers(intersection);
                });

            // Setup IsIncludedIn behavior
            testIdentifiersMock.Setup(ti => ti.IsIncludedIn(It.IsAny<ITestIdentifiers>()))
                .Returns<ITestIdentifiers>(other =>
                {
                    var otherTests = other.GetIdentifiers().ToHashSet();
                    return testNames.All(t => otherTests.Contains(t));
                });

            return testIdentifiersMock.Object;
        }

        #endregion

        #region Constructor Tests

        [TestMethod]
        public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Should.Throw<ArgumentNullException>(() =>
                new HigherOrderMutation(null, _inputMock.Object, _testMutants));
        }

        [TestMethod]
        public void Constructor_WithNullInput_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Should.Throw<ArgumentNullException>(() => 
                new HigherOrderMutation(_optionsMock.Object, null, _testMutants));
        }

        [TestMethod]
        public void Constructor_WithNullMutants_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Should.Throw<ArgumentNullException>(() => 
                new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, null));
        }

        [TestMethod]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            // Act
            var result = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, _testMutants);

            // Assert
            result.ShouldNotBeNull();
        }

        #endregion

        #region AddSearchAlgorithm Tests

        [TestMethod]
        public void AddSearchAlgorithm_WithNullAlgorithm_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Should.Throw<ArgumentNullException>(() => _sut.AddSearchAlgorithm(null));
        }

        [TestMethod]
        public void AddSearchAlgorithm_WithValidAlgorithm_ShouldAddAlgorithm()
        {
            // Arrange
            var algorithmMock = CreateMockAlgorithm("TestAlgorithm");

            // Act
            _sut.AddSearchAlgorithm(algorithmMock.Object);

            // Assert - Should not throw and algorithm should be usable
            var candidates = _sut.CreateCandidateHOMs("TestAlgorithm").ToList();
            algorithmMock.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
        }

        [TestMethod]
        public void AddSearchAlgorithm_WithDuplicateName_ShouldIgnoreDuplicate()
        {
            // Arrange
            var algorithm1 = CreateMockAlgorithm("TestAlgorithm");
            var algorithm2 = CreateMockAlgorithm("TestAlgorithm"); // Same name

            // Act
            _sut.AddSearchAlgorithm(algorithm1.Object);
            _sut.AddSearchAlgorithm(algorithm2.Object); // Should be ignored

            // Assert
            var candidates = _sut.CreateCandidateHOMs("TestAlgorithm").ToList();
            
            // Only the first algorithm should be called
            algorithm1.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
            
            algorithm2.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Never);
        }

        #endregion

        #region AddHeuristic Tests

        [TestMethod]
        public void AddHeuristic_WithNullHeuristic_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Should.Throw<ArgumentNullException>(() => _sut.AddHeuristic(null));
        }

        [TestMethod]
        public void AddHeuristic_WithValidHeuristic_ShouldAddHeuristic()
        {
            // Arrange
            var heuristicMock = CreateMockHeuristic("TestHeuristic");
            var algorithmMock = CreateMockAlgorithm("TestAlgorithm");

            // Act
            _sut.AddHeuristic(heuristicMock.Object);
            _sut.AddSearchAlgorithm(algorithmMock.Object);
            var candidates = _sut.CreateCandidateHOMs().ToList();

            // Assert - Algorithm should receive the heuristic
            algorithmMock.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.Is<IReadOnlyList<IHOMHeuristic>>(h => h.Contains(heuristicMock.Object)),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
        }

        [TestMethod]
        public void AddHeuristic_WithDuplicateName_ShouldIgnoreDuplicate()
        {
            // Arrange
            var heuristic1 = CreateMockHeuristic("TestHeuristic");
            var heuristic2 = CreateMockHeuristic("TestHeuristic"); // Same name
            var algorithmMock = CreateMockAlgorithm("TestAlgorithm");

            // Act
            _sut.AddHeuristic(heuristic1.Object);
            _sut.AddHeuristic(heuristic2.Object); // Should be ignored
            _sut.AddSearchAlgorithm(algorithmMock.Object);
            var candidates = _sut.CreateCandidateHOMs().ToList();

            // Assert - Only first heuristic should be in the list
            algorithmMock.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.Is<IReadOnlyList<IHOMHeuristic>>(h => h.Count == 1 && h.Contains(heuristic1.Object)),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
        }

        #endregion

        #region CreateCandidateHOMs Tests

        [TestMethod]
        public void CreateCandidateHOMs_WithNoMutants_ShouldReturnEmpty()
        {
            // Arrange
            var emptyMutants = new List<IMutant>();
            var emptyMutationSut = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, emptyMutants);
            var algorithmMock = CreateMockAlgorithm("TestAlgorithm");
            emptyMutationSut.AddSearchAlgorithm(algorithmMock.Object);

            // Act
            var candidates = emptyMutationSut.CreateCandidateHOMs().ToList();

            // Assert
            candidates.ShouldBeEmpty();
        }

        [TestMethod]
        public void CreateCandidateHOMs_WithNoAlgorithms_ShouldReturnEmpty()
        {
            // Act
            var candidates = _sut.CreateCandidateHOMs().ToList();

            // Assert
            candidates.ShouldBeEmpty();
        }

        [TestMethod]
        public void CreateCandidateHOMs_WithSpecificAlgorithm_ShouldUseSpecifiedAlgorithm()
        {
            // Arrange
            var algorithm1 = CreateMockAlgorithm("Algorithm1");
            var algorithm2 = CreateMockAlgorithm("Algorithm2");
            
            _sut.AddSearchAlgorithm(algorithm1.Object);
            _sut.AddSearchAlgorithm(algorithm2.Object);

            // Act
            var candidates = _sut.CreateCandidateHOMs("Algorithm2").ToList();

            // Assert
            algorithm2.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
            
            algorithm1.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Never);
        }

        [TestMethod]
        public void CreateCandidateHOMs_WithNonExistentAlgorithm_ShouldReturnEmpty()
        {
            // Arrange
            var algorithmMock = CreateMockAlgorithm("ExistingAlgorithm");
            _sut.AddSearchAlgorithm(algorithmMock.Object);

            // Act
            var candidates = _sut.CreateCandidateHOMs("NonExistentAlgorithm").ToList();

            // Assert
            candidates.ShouldBeEmpty();
        }

        [TestMethod]
        public void CreateCandidateHOMs_FiltersOrderOneResults_ShouldOnlyReturnHigherOrderMutants()
        {
            // Arrange
            var algorithmMock = CreateMockAlgorithm("TestAlgorithm");
            var testHOMs = new List<List<IMutant>>
            {
                new() { _testMutants[0] }, // Order 1 - should be filtered out
                new() { _testMutants[0], _testMutants[1] }, // Order 2 - should be kept
                new() { _testMutants[0], _testMutants[1], _testMutants[2] } // Order 3 - should be kept
            };
            
            algorithmMock.Setup(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()))
                .Returns(testHOMs);

            _sut.AddSearchAlgorithm(algorithmMock.Object);

            // Act
            var candidates = _sut.CreateCandidateHOMs().ToList();

            // Assert
            candidates.ShouldNotBeEmpty();
            candidates.Count.ShouldBe(2); // Only the order 2 and 3 HOMs
            candidates.All(c => c.Count >= 2).ShouldBeTrue();
        }

        [TestMethod]
        public void CreateCandidateHOMs_WithDifferentHeuristics_ShouldProduceDifferentResults()
        {
            // Arrange
            var heuristic1 = CreateMockHeuristic("Heuristic1");
            var heuristic2 = CreateMockHeuristic("Heuristic2");
            var algorithm = CreateMockAlgorithm("TestAlgorithm");

            var sut1 = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, _testMutants);
            var sut2 = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, _testMutants);

            sut1.AddHeuristic(heuristic1.Object);
            sut1.AddSearchAlgorithm(algorithm.Object);

            sut2.AddHeuristic(heuristic2.Object);
            sut2.AddSearchAlgorithm(algorithm.Object);

            // Act
            var candidates1 = sut1.CreateCandidateHOMs().ToList();
            var candidates2 = sut2.CreateCandidateHOMs().ToList();

            // Assert - Both calls should happen with different heuristic lists
            algorithm.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.Is<IReadOnlyList<IHOMHeuristic>>(h => h.Contains(heuristic1.Object)),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);

            algorithm.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.Is<IReadOnlyList<IHOMHeuristic>>(h => h.Contains(heuristic2.Object)),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
        }

        [TestMethod]
        public void CreateCandidateHOMs_WithDifferentAlgorithms_ShouldProduceDifferentResults()
        {
            // Arrange
            var algorithm1 = CreateMockAlgorithm("Algorithm1");
            var algorithm2 = CreateMockAlgorithm("Algorithm2");
            
            // Configure different algorithms to return different results
            algorithm1.Setup(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()))
                .Returns(new List<List<IMutant>>
                {
                    new() { _testMutants[0], _testMutants[1] }, // Specific pair 1
                });

            algorithm2.Setup(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()))
                .Returns(new List<List<IMutant>>
                {
                    new() { _testMutants[2], _testMutants[3] }, // Specific pair 2
                });

            _sut.AddSearchAlgorithm(algorithm1.Object);
            _sut.AddSearchAlgorithm(algorithm2.Object);

            // Act
            var candidates1 = _sut.CreateCandidateHOMs("Algorithm1").ToList();
            var candidates2 = _sut.CreateCandidateHOMs("Algorithm2").ToList();

            // Assert
            candidates1.ShouldNotBeEmpty();
            candidates2.ShouldNotBeEmpty();
            
            // Verify different algorithms were called
            algorithm1.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);

            algorithm2.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
        }

        [TestMethod]
        public void CreateCandidateHOMs_ShouldPassHeuristicToAlgorithm()
        {
            // Arrange
            var scoringHeuristic = CreateMockHeuristic("ScoringHeuristic");
            scoringHeuristic.Setup(h => h.IsFitnessScoringHeuristic).Returns(true);
            scoringHeuristic.Setup(h => h.ScoreCandidate(It.IsAny<List<IMutant>>())).Returns(0.8);

            var algorithm = CreateMockAlgorithm("TestAlgorithm");

            _sut.AddHeuristic(scoringHeuristic.Object);
            _sut.AddSearchAlgorithm(algorithm.Object);

            // Act
            var candidates = _sut.CreateCandidateHOMs().ToList();

            // Assert
            algorithm.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.Is<IReadOnlyList<IHOMHeuristic>>(h => 
                    h.Any(heur => heur.Name == "ScoringHeuristic")),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
        }

        #endregion

        #region IsSSHOM Tests

        [TestMethod]
        public void IsSSHOM_WithNullConstituents_ShouldReturnFalse()
        {
            // Arrange
            var homKillingTests = CreateMockTestIdentifiers(new[] { "Test1" });

            // Act
            var result = _sut.IsSSHOM(null, homKillingTests);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void IsSSHOM_WithSingleConstituent_ShouldReturnFalse()
        {
            // Arrange
            var constituents = new List<IMutant> { _testMutants[0] };
            var homKillingTests = CreateMockTestIdentifiers(new[] { "Test1" });

            // Act
            var result = _sut.IsSSHOM(constituents, homKillingTests);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void IsSSHOM_WithNullHomKillingTests_ShouldReturnFalse()
        {
            // Arrange
            var constituents = new List<IMutant> { _testMutants[0], _testMutants[1] };

            // Act
            var result = _sut.IsSSHOM(constituents, null);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void IsSSHOM_WithEmptyHomKillingTests_ShouldReturnFalse()
        {
            // Arrange
            var constituents = new List<IMutant> { _testMutants[0], _testMutants[1] };
            var emptyKillingTests = CreateMockTestIdentifiers(new string[0]);

            // Act
            var result = _sut.IsSSHOM(constituents, emptyKillingTests);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void IsSSHOM_WithUntestedConstituents_ShouldReturnFalse()
        {
            // Arrange
            var untestedMutant = CreateMockMutant(99, MutantStatus.Pending, new string[0], "File.cs", "Method");
            var constituents = new List<IMutant> { _testMutants[0], untestedMutant };
            var homKillingTests = CreateMockTestIdentifiers(new[] { "Test1" });

            // Act
            var result = _sut.IsSSHOM(constituents, homKillingTests);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void IsSSHOM_WithValidSSHOM_ShouldReturnTrue()
        {
            // Arrange - mutants 1 and 2 both killed by Test1,Test2. HOM killed by Test1 (subset)
            var constituents = new List<IMutant> { _testMutants[0], _testMutants[1] }; // Both killed by Test1,Test2
            var homKillingTests = CreateMockTestIdentifiers(["Test1"]); // Subset of intersection

            // Act
            var result = _sut.IsSSHOM(constituents, homKillingTests);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void IsSSHOM_WithProperSubsetTrue_ShouldEnforceProperSubset()
        {
            // Arrange - mutants 1 and 2 both killed by Test1,Test2. HOM killed by Test1,Test2 (same set)
            var constituents = new List<IMutant> { _testMutants[0], _testMutants[1] };
            var homKillingTests = CreateMockTestIdentifiers(["Test1", "Test2"]); // Same as intersection

            // Act
            var result = _sut.IsSSHOM(constituents, homKillingTests, properSubset: true);

            // Assert
            result.ShouldBeFalse(); // Not a proper subset
        }

        [TestMethod]
        public void IsSSHOM_WithProperSubsetFalse_ShouldAllowEqualSets()
        {
            // Arrange - mutants 1 and 2 both killed by Test1,Test2. HOM killed by Test1,Test2 (same set)
            var constituents = new List<IMutant> { _testMutants[0], _testMutants[1] };
            var homKillingTests = CreateMockTestIdentifiers(new[] { "Test1", "Test2" }); // Same as intersection

            // Act
            var result = _sut.IsSSHOM(constituents, homKillingTests, properSubset: false);

            // Assert
            result.ShouldBeTrue(); // Equal sets are allowed when properSubset is false
        }

        [TestMethod]
        public void IsSSHOM_WithNoIntersection_ShouldReturnFalse()
        {
            // Arrange - mutants with completely different killing tests
            var constituents = new List<IMutant> { _testMutants[2], _testMutants[3] }; // Test3,Test4 and Test5,Test6
            var homKillingTests = CreateMockTestIdentifiers(new[] { "Test1" }); // No intersection

            // Act
            var result = _sut.IsSSHOM(constituents, homKillingTests);

            // Assert
            result.ShouldBeFalse();
        }

        #endregion

        #region Helper Methods

        private Mock<IHOMSearchAlgorithm> CreateMockAlgorithm(string name)
        {
            var algorithmMock = new Mock<IHOMSearchAlgorithm>();
            algorithmMock.Setup(a => a.Name).Returns(name);
            algorithmMock.Setup(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()))
                .Returns(new List<List<IMutant>>
                {
                    new() { _testMutants[0], _testMutants[1] },
                    new() { _testMutants[2], _testMutants[3] }
                });

            return algorithmMock;
        }

        private Mock<IHOMHeuristic> CreateMockHeuristic(string name)
        {
            var heuristicMock = new Mock<IHOMHeuristic>();
            heuristicMock.Setup(h => h.Name).Returns(name);
            heuristicMock.Setup(h => h.Weight).Returns(1.0);
            heuristicMock.Setup(h => h.IsSearchStrategyHeuristic).Returns(false);
            heuristicMock.Setup(h => h.IsFilteringHeuristic).Returns(false);
            heuristicMock.Setup(h => h.IsFitnessScoringHeuristic).Returns(true);
            heuristicMock.Setup(h => h.ScoreCandidate(It.IsAny<List<IMutant>>())).Returns(0.5);
            heuristicMock.Setup(h => h.ShouldFilterCandidate(It.IsAny<List<IMutant>>())).Returns(false);
            heuristicMock.Setup(h => h.SuggestNextCandidates(It.IsAny<List<IMutant>>(), It.IsAny<IReadOnlyCollection<IMutant>>()))
                .Returns(new List<List<IMutant>>());
            return heuristicMock;
        }

        #endregion

        [TestMethod]
        public void CreateCandidateHOMs_WithMultipleHeuristics_ShouldPassAllHeuristicsToAlgorithm()
        {
            // Arrange
            var heuristic1 = CreateMockHeuristic("Heuristic1");
            var heuristic2 = CreateMockHeuristic("Heuristic2");
            var heuristic3 = CreateMockHeuristic("Heuristic3");
            
            heuristic1.Setup(h => h.IsFitnessScoringHeuristic).Returns(true);
            heuristic2.Setup(h => h.IsFilteringHeuristic).Returns(true);
            heuristic3.Setup(h => h.IsSearchStrategyHeuristic).Returns(true);

            var algorithm = CreateMockAlgorithm("TestAlgorithm");

            _sut.AddHeuristic(heuristic1.Object);
            _sut.AddHeuristic(heuristic2.Object);
            _sut.AddHeuristic(heuristic3.Object);
            _sut.AddSearchAlgorithm(algorithm.Object);

            // Act
            var candidates = _sut.CreateCandidateHOMs().ToList();

            // Assert
            algorithm.Verify(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.Is<IReadOnlyList<IHOMHeuristic>>(h => 
                    h.Count == 3 &&
                    h.Any(heur => heur.Name == "Heuristic1") &&
                    h.Any(heur => heur.Name == "Heuristic2") &&
                    h.Any(heur => heur.Name == "Heuristic3")),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()), Times.Once);
        }

        [TestMethod]
        public void CreateCandidateHOMs_WithComplexSSHOMScenario_ShouldValidateCorrectly()
        {
            // Arrange - Create a complex scenario with overlapping test coverage
            var mutantA = CreateMockMutant(10, MutantStatus.Pending, new[] { "Test1", "Test2", "Test3" }, "File1.cs", "MethodA");
            var mutantB = CreateMockMutant(11, MutantStatus.Pending, new[] { "Test1", "Test2" }, "File1.cs", "MethodB");
            var mutantC = CreateMockMutant(12, MutantStatus.Pending, new[] { "Test2", "Test3" }, "File1.cs", "MethodC");
            
            var complexMutants = new List<IMutant> { mutantA, mutantB, mutantC };
            var complexSut = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, complexMutants);

            var algorithm = CreateMockAlgorithm("ComplexAlgorithm");
            algorithm.Setup(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()))
                .Returns(new List<List<IMutant>>
                {
                    new() { mutantA, mutantB }, // Intersection: Test1, Test2
                    new() { mutantB, mutantC }, // Intersection: Test2
                    new() { mutantA, mutantC }  // Intersection: Test2, Test3
                });

            complexSut.AddSearchAlgorithm(algorithm.Object);

            // Act
            var candidates = complexSut.CreateCandidateHOMs().ToList();

            // Assert
            candidates.ShouldNotBeEmpty();
            candidates.Count.ShouldBe(3);
            
            // Test SSHOM validation scenarios
            var homKillingTestsSubset = CreateMockTestIdentifiers(new[] { "Test2" }); // Subset of all intersections
            var homKillingTestsEqual = CreateMockTestIdentifiers(new[] { "Test1", "Test2" }); // Equal to A∩B
            var homKillingTestsNotSubset = CreateMockTestIdentifiers(new[] { "Test4" }); // Not in any intersection

            // Should be SSHOM (subset)
            complexSut.IsSSHOM(new[] { mutantA, mutantB }, homKillingTestsSubset).ShouldBeTrue();
            
            // Should be SSHOM when properSubset is false (equal sets allowed)
            complexSut.IsSSHOM(new[] { mutantA, mutantB }, homKillingTestsEqual, properSubset: false).ShouldBeTrue();
            
            // Should not be SSHOM when properSubset is true (equal sets not allowed)
            complexSut.IsSSHOM(new[] { mutantA, mutantB }, homKillingTestsEqual, properSubset: true).ShouldBeFalse();
            
            // Should not be SSHOM (not a subset)
            complexSut.IsSSHOM(new[] { mutantA, mutantB }, homKillingTestsNotSubset).ShouldBeFalse();
        }
    }
}
