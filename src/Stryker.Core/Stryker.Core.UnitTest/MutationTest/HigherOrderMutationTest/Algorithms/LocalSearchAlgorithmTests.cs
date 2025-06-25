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
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Algorithms
{
    [TestClass]
    public class LocalSearchAlgorithmTests : TestBase
    {
        private LocalSearchAlgorithm _sut;
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<MutationTestInput> _inputMock;
        private List<IMutant> _testMutants;
        private List<IHOMHeuristic> _heuristics;
        // Dictionary to store file paths for mutations
        private Dictionary<int, string> _mutationFilePaths;
        
        [TestInitialize]
        public void Setup()
        {
            // Create the system under test with default parameters
            _sut = new LocalSearchAlgorithm(maxIterations: 3, candidatePoolSize: 10, maxOrder: 3);
            _optionsMock = new Mock<IStrykerOptions>();
            _inputMock = new Mock<MutationTestInput>();
            _heuristics = new List<IHOMHeuristic>();
            _mutationFilePaths = new Dictionary<int, string>();
            
            // Create test mutants
            _testMutants = CreateTestMutants();
        }

        private List<IMutant> CreateTestMutants()
        {
            // Create test mutants with different killing tests patterns
            var mutants = new List<IMutant>();
            
            // Create mock test identifiers for different test sets
            var testSet1 = CreateMockTestIdentifiers(new[] { "Test1", "Test2" });
            var testSet2 = CreateMockTestIdentifiers(new[] { "Test1", "Test2" });  // Same tests as set 1
            var testSet3 = CreateMockTestIdentifiers(new[] { "Test3", "Test4" });
            var testSet4 = CreateMockTestIdentifiers(new[] { "Test1", "Test3" });
            var testSet5 = CreateMockTestIdentifiers(new[] { "Test4", "Test5" });

            // Create mock mutations with different file paths
            var mutation1 = CreateMockMutation("File1.cs");
            var mutation2 = CreateMockMutation("File1.cs");  // Same file as mutation1
            var mutation3 = CreateMockMutation("File1.cs");  // Same file as mutation1
            var mutation4 = CreateMockMutation("File2.cs");
            var mutation5 = CreateMockMutation("File2.cs");  // Same file as mutation4
            
            // Create mutants with the mock components
            mutants.Add(CreateMockMutant(1, mutation1, testSet1));
            mutants.Add(CreateMockMutant(2, mutation2, testSet2));
            mutants.Add(CreateMockMutant(3, mutation3, testSet3));
            mutants.Add(CreateMockMutant(4, mutation4, testSet4));
            mutants.Add(CreateMockMutant(5, mutation5, testSet5));
            
            return mutants;
        }

        private IMutant CreateMockMutant(int id, Mutation mutation, ITestIdentifiers killingTests)
        {
            var mutantMock = new Mock<IMutant>();
            mutantMock.Setup(m => m.Id).Returns(id);
            mutantMock.Setup(m => m.Mutation).Returns(mutation);
            mutantMock.Setup(m => m.KillingTests).Returns(killingTests);
            
            // Store the file path for later retrieval
            _mutationFilePaths[id] = mutation.OriginalNode?.SyntaxTree?.FilePath ?? "unknown";
            
            return mutantMock.Object;
        }

        private static Mutation CreateMockMutation(string filePath)
        {
            // Create a real SyntaxTree for the original node using SyntaxFactory
            var originalTree = SyntaxFactory.ParseSyntaxTree(
                text: "class OriginalPlaceholder {}",
                path: filePath
            );
            var originalNode = originalTree.GetRoot();

            // Create a real SyntaxNode for the replacement node using SyntaxFactory
            var replacementTree = SyntaxFactory.ParseSyntaxTree(
                text: "class ReplacementPlaceholder {}"
            );
            var replacementNode = replacementTree.GetRoot();

            var mutation = new Mutation
            {
                DisplayName = "Test Mutation",
                Type = Mutator.Block,
                Description = "Test Description",
                OriginalNode = originalNode,
                ReplacementNode = replacementNode
            };

            return mutation;
        }

        private ITestIdentifiers CreateMockTestIdentifiers(string[] testNames)
        {
            var testIdentifiersMock = new Mock<ITestIdentifiers>();
            
            testIdentifiersMock.Setup(ti => ti.IsEmpty).Returns(false);
            testIdentifiersMock.Setup(ti => ti.ToString()).Returns(string.Join(",", testNames));
            
            return testIdentifiersMock.Object;
        }
        
        [TestMethod]
        public void LocalSearchAlgorithm_ShouldHaveCorrectName()
        {
            // Assert
            _sut.Name.ShouldBe("LocalSearch", "Algorithm should have correct name");
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldGenerateCandidates()
        {
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert
            candidates.ShouldNotBeEmpty("Local search should generate candidates");
            candidates.All(c => c.Count >= 2).ShouldBeTrue("All candidates should have at least 2 mutants");
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldRespectMaxOrder()
        {
            // Arrange
            var localSearch = new LocalSearchAlgorithm(maxOrder: 2); // Maximum 2 mutants per HOM
            
            // Act
            var candidates = localSearch.GenerateCandidates(
                _testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();

            // Assert
            localSearch.heuGetSearchGuidanceHeuristics()
            _heuristics.ShouldBeEmpty("No heuristics should be used in this test");
            candidates.ShouldNotBeEmpty();
            candidates.All(c => c.Count <= 2).ShouldBeTrue("No candidate should exceed max order");
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldNotProduceDuplicates()
        {
            // Arrange
            var sut = new LocalSearchAlgorithm(maxIterations: 5); // More iterations to potentially create duplicates
            
            // Act
            var candidates = sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert - Check if we have any duplicates by creating string representations and comparing
            var candidateSignatures = new HashSet<string>();
            bool hasDuplicates = false;
            
            foreach (var candidate in candidates)
            {
                string signature = string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id));
                if (!candidateSignatures.Add(signature))
                {
                    hasDuplicates = true;
                    break;
                }
            }
            
            hasDuplicates.ShouldBeFalse("Should not contain duplicate candidates");
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldUseHeuristicRegistry_ForFiltering()
        {
            // Arrange
            var filteringHeuristic = new Mock<IHOMHeuristic>();
            filteringHeuristic.Setup(h => h.ShouldFilterCandidate(It.IsAny<List<IMutant>>()))
                .Returns(true); // Filter everything
            
            _heuristics.Add(filteringHeuristic.Object);
            
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert
            candidates.ShouldBeEmpty("All candidates should have been filtered");
            filteringHeuristic.Verify(h => h.Initialize(_testMutants, _optionsMock.Object, _inputMock.Object), 
                Times.Once, "Heuristic should be initialized");
            filteringHeuristic.Verify(h => h.ShouldFilterCandidate(It.IsAny<List<IMutant>>()), 
                Times.AtLeastOnce, "Filter should be called");
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldUseHeuristicRegistry_ForScoring()
        {
            // Arrange
            var scoringHeuristic = new Mock<IHOMHeuristic>();
            scoringHeuristic.Setup(h => h.Weight).Returns(1.0);
            scoringHeuristic.Setup(h => h.ScoreCandidate(It.IsAny<List<IMutant>>()))
                .Returns(0.5);
            
            _heuristics.Add(scoringHeuristic.Object);
            
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert
            candidates.ShouldNotBeEmpty();
            scoringHeuristic.Verify(h => h.Initialize(_testMutants, _optionsMock.Object, _inputMock.Object), 
                Times.Once);
            scoringHeuristic.Verify(h => h.ScoreCandidate(It.IsAny<List<IMutant>>()), 
                Times.AtLeastOnce);
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldUseHeuristicRegistry_ForSuggestions()
        {
            // Arrange
            var suggestionHeuristic = new Mock<IHOMHeuristic>();
            suggestionHeuristic.Setup(h => h.CanGuideSearch).Returns(true);
            
            // Create a suggested candidate
            var suggestedCandidate = new List<IMutant> { _testMutants[0], _testMutants[1] };
            
            suggestionHeuristic.Setup(h => h.SuggestNextCandidates(
                    It.IsAny<List<IMutant>>(), 
                    It.IsAny<IReadOnlyCollection<IMutant>>()))
                .Returns(new List<List<IMutant>> { suggestedCandidate });
            
            _heuristics.Add(suggestionHeuristic.Object);
            
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert
            candidates.ShouldNotBeEmpty();
            suggestionHeuristic.Verify(h => h.Initialize(_testMutants, _optionsMock.Object, _inputMock.Object), 
                Times.Once);
            suggestionHeuristic.Verify(h => h.SuggestNextCandidates(
                    It.IsAny<List<IMutant>>(), 
                    It.IsAny<IReadOnlyCollection<IMutant>>()), 
                Times.AtLeastOnce);
        }
    }
}
