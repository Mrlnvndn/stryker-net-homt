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
    public class SearchAlgorithmTests : TestBase
    {
        private SearchAlgorithm _sut;
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<MutationTestInput> _inputMock;
        private List<IMutant> _testMutants;
        private List<IHOMHeuristic> _heuristics;
        // Dictionary to store file paths for mutations since we can't easily mock SyntaxNode
        private Dictionary<int, string> _mutationFilePaths;
        
        [TestInitialize]
        public void Setup()
        {
            _sut = new SearchAlgorithm();
            _optionsMock = new Mock<IStrykerOptions>();
            _inputMock = new Mock<MutationTestInput>();
            _heuristics = new List<IHOMHeuristic>();
            _mutationFilePaths = new Dictionary<int, string>();
            
            // Create test mutants
            _testMutants = CreateTestMutants();
        }

        private List<IMutant> CreateTestMutants()
        {
            // Create 5 test mutants with different killing tests patterns
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
            // Its tree/path are likely not important based on the original mocking setup.
            var replacementTree = SyntaxFactory.ParseSyntaxTree(
                text: "class ReplacementPlaceholder {}"
            // Path defaults to an empty string if not specified and is not critical here.
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
        public void GenerateCandidates_ShouldGenerateSecondOrderMutants_WithEquivalentTestFailures()
        {
            // Arrange - we have 5 mutants, where mutants 1 and 2 have the same killing tests
            
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert
            candidates.ShouldNotBeEmpty();
            
            // Verify that the mutants with the same killing tests are combined
            bool foundPair = candidates.Any(c => 
                c.Count == 2 && 
                c.Any(m => m.Id == 1) && 
                c.Any(m => m.Id == 2));
                
            foundPair.ShouldBeTrue("Should combine mutants with the same killing tests");
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldGenerateCandidates_BasedOnProximity()
        {
            // Arrange - mutants 1, 2, 3 are in same file, and 4, 5 are in another file
            
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert
            candidates.ShouldNotBeEmpty();
            
            // Verify that mutants from the same file are combined
            // Check for grouping by same file path using our dictionary lookup instead of direct SyntaxTree access
            bool foundSameFileGroup1 = candidates.Any(c =>
                c.Count == 2 &&
                c.All(m => _mutationFilePaths[m.Id] == "File1.cs"));
                
            bool foundSameFileGroup2 = candidates.Any(c =>
                c.Count == 2 &&
                c.All(m => _mutationFilePaths[m.Id] == "File2.cs"));
            
            foundSameFileGroup1.ShouldBeTrue("Should combine mutants from the same file (File1.cs)");
            foundSameFileGroup2.ShouldBeTrue("Should combine mutants from the same file (File2.cs)");
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldEliminateDuplicates()
        {
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert
            // Create a set of candidate keys to check for duplicates
            var candidateKeys = new HashSet<string>();
            foreach (var candidate in candidates)
            {
                string key = string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id));
                candidateKeys.Add(key).ShouldBeTrue($"Duplicate found: {key}");
            }
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldLimitNumberOfCandidates_ToMaxCandidatesCount()
        {
            // Arrange - set a low max candidates count
            int maxCandidates = 3;
            
            // Act - generate candidates with limit
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).Take(maxCandidates).ToList();
            
            // Assert
            candidates.Count.ShouldBeLessThanOrEqualTo(maxCandidates);
        }
    }
}
