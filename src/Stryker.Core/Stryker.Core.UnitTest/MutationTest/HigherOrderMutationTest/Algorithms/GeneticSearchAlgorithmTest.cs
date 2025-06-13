using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Algorithms
{
    // We don't need this custom mock class as we'll use Moq to properly mock the interface
    
    [TestClass]
    public class GeneticSearchAlgorithmTest
    {
        private GeneticSearchAlgorithm _sut;
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<MutationTestInput> _inputMock;
        private List<IMutant> _testMutants;
        private List<IHOMHeuristic> _heuristics;
        
        [TestInitialize]
        public void Setup()
        {
            // Initialize the system under test
            _sut = new GeneticSearchAlgorithm();
            
            // Set up mocks
            _optionsMock = new Mock<IStrykerOptions>();
            _inputMock = new Mock<MutationTestInput>();
            
            // Create test heuristics
            _heuristics = new List<IHOMHeuristic>();
            
            // Create test mutants (FOMs)
            _testMutants = CreateTestMutants(10);
        }
        
        [TestMethod]
        public void Name_ShouldBeGeneticSearch()
        {
            // Assert
            _sut.Name.ShouldBe("GeneticSearch");
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldReturnNonEmptyList()
        {
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).Take(10).ToList();
            
            // Assert
            candidates.ShouldNotBeEmpty();
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldGenerateHOMsWithAtLeastTwoFOMs()
        {
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).Take(10).ToList();
            
            // Assert
            foreach (var candidate in candidates)
            {
                candidate.Count.ShouldBeGreaterThanOrEqualTo(2, "Each HOM should contain at least two FOMs");
            }
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldNotExceedMaximumOrder()
        {
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).Take(10).ToList();
            
            // Assert - Most SSHOMS are composed of at most 4 FOMs
            foreach (var candidate in candidates)
            {
                candidate.Count.ShouldBeLessThanOrEqualTo(4, "HOM order should not exceed the maximum defined limit");
            }
        }
        
        [TestMethod]
        public void GenerateCandidates_ShouldEliminateDuplicates()
        {
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).Take(10).ToList();
            
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
        public void GenerateCandidates_WithEmptyMutantList_ShouldReturnEmptyCollection()
        {
            // Act
            var candidates = _sut.GenerateCandidates(new List<IMutant>(), _heuristics, _optionsMock.Object, _inputMock.Object).ToList();
            
            // Assert
            candidates.ShouldBeEmpty();
        }

        [TestMethod]
        [Timeout(5000)] // Set a 5-second timeout
        public void GenerateCandidates_ShouldCompleteWithinTimeLimit()
        {
            // Act
            var candidates = _sut.GenerateCandidates(_testMutants, _heuristics, _optionsMock.Object, _inputMock.Object).Take(10).ToList();

            // Assert
            // The primary assertion is the [Timeout] attribute ensuring the method completes.
            // A functional assertion is included to verify work was done.
            candidates.ShouldNotBeEmpty();
        }

        [TestMethod]
        //Test the EvaluateCandidate method to ensure it correctly evaluates the HOM candidates
        public void EvaluateCandidate_ShouldReturnCorrectScore()
        {
            // Arrange
            var candidate = new List<IMutant> { CreateMockMutant(1), CreateMockMutant(2) };
            var expectedScore = 10; // Example score, adjust based on your scoring logic

            // Act
            var score = GeneticSearchAlgorithm.EvaluateCandidate(candidate, null);

            // Assert
            score.ShouldBe(expectedScore, "The score should match the expected value based on the candidate's properties");
        }

        // Helper method to create mock mutants for testing
        private List<IMutant> CreateTestMutants(int count)
        {
            var mutants = new List<IMutant>();
            for (int i = 1; i <= count; i++)
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
                               
            // Setup IsIncludedIn to return true
            testIdentifiersMock.Setup(ti => ti.IsIncludedIn(It.IsAny<ITestIdentifiers>()))
                               .Returns(true);
            
            return testIdentifiersMock.Object;
        }
        
    }
}
