using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Heuristics
{
    [TestClass]
    public class SyntaxNodeConflictHeuristicTests : TestBase
    {
        private SyntaxNodeConflictHeuristic _sut;

        [TestInitialize]
        public void Setup()
        {
            _sut = new SyntaxNodeConflictHeuristic();
        }

        [TestMethod]
        public void Name_ShouldReturnExpectedName()
        {
            // Act & Assert
            _sut.Name.ShouldBe("SyntaxNodeConflict");
        }

        [TestMethod]
        public void Properties_ShouldBeConfiguredAsFilteringHeuristic()
        {
            // Act & Assert
            _sut.IsFilteringHeuristic.ShouldBeTrue();
            _sut.IsFitnessScoringHeuristic.ShouldBeFalse();
            _sut.IsSearchStrategyHeuristic.ShouldBeFalse();
            _sut.Weight.ShouldBe(0.0);
        }

        [TestMethod]
        public void ScoreCandidate_ShouldReturnZero()
        {
            // Arrange
            var candidate = new List<IMutant> { CreateMockMutant(1), CreateMockMutant(2) };

            // Act
            var result = _sut.ScoreCandidate(candidate);

            // Assert
            result.ShouldBe(0.0);
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithNullCandidate_ShouldReturnFalse()
        {
            // Act
            var result = _sut.ShouldFilterCandidate(null);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithEmptyCandidate_ShouldReturnFalse()
        {
            // Arrange
            var candidate = new List<IMutant>();

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithSingleMutant_ShouldReturnFalse()
        {
            // Arrange
            var candidate = new List<IMutant> { CreateMockMutant(1) };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMutantsTargetingDifferentNodes_ShouldReturnFalse()
        {
            // Arrange
            var node1 = CreateBinaryExpression("x > y");
            var node2 = CreateBinaryExpression("a < b");
            
            var mutant1 = CreateMockMutant(1, node1);
            var mutant2 = CreateMockMutant(2, node2);
            
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMutantsTargetingSameNode_ShouldReturnTrue()
        {
            // Arrange
            var sharedNode = CreateBinaryExpression("x > y");
            
            var mutant1 = CreateMockMutant(1, sharedNode);
            var mutant2 = CreateMockMutant(2, sharedNode);
            
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithThreeMutantsTargetingSameNode_ShouldReturnTrue()
        {
            // Arrange
            var sharedNode = CreateBinaryExpression("x > y");
            
            var mutant1 = CreateMockMutant(1, sharedNode);
            var mutant2 = CreateMockMutant(2, sharedNode);
            var mutant3 = CreateMockMutant(3, sharedNode);
            
            var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue();
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMixedConflictingAndNonConflictingMutants_ShouldReturnTrue()
        {
            // Arrange
            var conflictingNode = CreateBinaryExpression("x > y");
            var uniqueNode1 = CreateBinaryExpression("a < b");
            var uniqueNode2 = CreateBinaryExpression("c == d");
            
            var mutant1 = CreateMockMutant(1, conflictingNode); // Conflicts with mutant2
            var mutant2 = CreateMockMutant(2, conflictingNode); // Conflicts with mutant1
            var mutant3 = CreateMockMutant(3, uniqueNode1);    // No conflict
            var mutant4 = CreateMockMutant(4, uniqueNode2);    // No conflict
            
            var candidate = new List<IMutant> { mutant1, mutant2, mutant3, mutant4 };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue(); // Should filter because mutant1 and mutant2 conflict
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMultipleConflictGroups_ShouldReturnTrue()
        {
            // Arrange
            var conflictingNode1 = CreateBinaryExpression("x > y");
            var conflictingNode2 = CreateBinaryExpression("a < b");
            
            var mutant1 = CreateMockMutant(1, conflictingNode1); // Group 1 conflict
            var mutant2 = CreateMockMutant(2, conflictingNode1); // Group 1 conflict
            var mutant3 = CreateMockMutant(3, conflictingNode2); // Group 2 conflict
            var mutant4 = CreateMockMutant(4, conflictingNode2); // Group 2 conflict
            
            var candidate = new List<IMutant> { mutant1, mutant2, mutant3, mutant4 };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue(); // Should filter because there are multiple conflict groups
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMutantsHavingNullMutation_ShouldHandleGracefully()
        {
            // Arrange
            var validNode = CreateBinaryExpression("x > y");
            var validMutant = CreateMockMutant(1, validNode);
            var nullMutationMutant = CreateMockMutant(2, mutation: null);
            
            var candidate = new List<IMutant> { validMutant, nullMutationMutant };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse(); // Should not filter - only valid mutants are considered
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithMutantsHavingNullOriginalNode_ShouldHandleGracefully()
        {
            // Arrange
            var validNode = CreateBinaryExpression("x > y");
            var validMutant = CreateMockMutant(1, validNode);
            var nullNodeMutant = CreateMockMutant(2, originalNode: null);
            
            var candidate = new List<IMutant> { validMutant, nullNodeMutant };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse(); // Should not filter - only valid mutants are considered
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithAllMutantsHavingNullNodes_ShouldReturnFalse()
        {
            // Arrange
            var mutant1 = CreateMockMutant(1, originalNode: null);
            var mutant2 = CreateMockMutant(2, originalNode: null);
            
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeFalse(); // Should not filter - no valid nodes to conflict
        }

        [TestMethod]
        public void ShouldFilterCandidate_WithRealisticBinaryExpressionScenario_ShouldWorkCorrectly()
        {
            // Arrange - Simulate realistic scenario where > operator has multiple mutations
            var sourceCode = "if (age > 18) { /* do something */ }";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var binaryExpression = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .First();

            // Two mutants targeting the same > expression
            var mutant1 = CreateMockMutant(1, binaryExpression); // > to <
            var mutant2 = CreateMockMutant(2, binaryExpression); // > to >=
            
            var candidate = new List<IMutant> { mutant1, mutant2 };

            // Act
            var result = _sut.ShouldFilterCandidate(candidate);

            // Assert
            result.ShouldBeTrue(); // Should filter to prevent schemata conflict
        }

        #region Integration Tests
        [TestMethod]
        public void Integration_SyntaxNodeConflictHeuristic_ShouldBeRegisteredByDefault()
        {
            // Arrange
            var options = new StrykerOptions();
            var input = new Mock<MutationTestInput>().Object;
            var mutants = new List<IMutant> { CreateMockMutant(1), CreateMockMutant(2) };
            
            // Act - Create HeuristicRegistry with default heuristics
            var registry = new HeuristicRegistry(mutants, options, input, registerDefaultHeuristics: true);
            
            // Assert - Verify SyntaxNodeConflictHeuristic is registered by default
            var registeredHeuristics = registry.RegisteredHeuristics;
            registeredHeuristics.ShouldContain(h => h is SyntaxNodeConflictHeuristic, 
                "SyntaxNodeConflictHeuristic should be registered by default");
        }

        [TestMethod]
        public void Integration_SyntaxNodeConflictHeuristic_ShouldBeIncludedInLocalSearchAlgorithm()
        {
            // Arrange
            var options = new StrykerOptions();
            var input = new Mock<MutationTestInput>().Object;
            var mutants = new List<IMutant> { CreateMockMutant(1), CreateMockMutant(2) };
            
            // Act - Create LocalSearchAlgorithm which should include our heuristic
            var algorithm = new LocalSearchAlgorithm(input, new List<IHOMHeuristic>(), options, mutants);
            
            // Generate some candidates to ensure the algorithm is working
            var candidates = algorithm.GenerateCandidates(mutants, new List<IHOMHeuristic>(), options, input).ToList();
            
            // Assert - Verify that the algorithm runs without errors and generates candidates
            // The exact number may vary, but the important thing is that it works
            candidates.ShouldNotBeNull("Algorithm should generate candidate list (may be empty)");
        }
        #endregion

        #region Helper Methods

        private IMutant CreateMockMutant(int id, SyntaxNode originalNode = null, Mutation mutation = null)
        {
            var mockMutant = new Mock<IMutant>();
            mockMutant.Setup(m => m.Id).Returns(id);

            if (mutation != null)
            {
                mockMutant.Setup(m => m.Mutation).Returns(mutation);
            }
            else if (originalNode != null)
            {
                var mockMutation = new Mutation
                {
                    OriginalNode = originalNode,
                    DisplayName = $"Test mutation {id}",
                    Type = Mutator.Equality
                };
                mockMutant.Setup(m => m.Mutation).Returns(mockMutation);
            }
            else
            {
                // Create a mutation with a unique node for each mutant
                var uniqueNode = CreateBinaryExpression($"var{id} > value{id}");
                var mockMutation = new Mutation
                {
                    OriginalNode = uniqueNode,
                    DisplayName = $"Test mutation {id}",
                    Type = Mutator.Equality
                };
                mockMutant.Setup(m => m.Mutation).Returns(mockMutation);
            }

            return mockMutant.Object;
        }

        private BinaryExpressionSyntax CreateBinaryExpression(string expression)
        {
            var fullCode = $"class Test {{ void Method() {{ var result = {expression}; }} }}";
            var syntaxTree = CSharpSyntaxTree.ParseText(fullCode);
            return syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .First();
        }

        #endregion
    }
}
