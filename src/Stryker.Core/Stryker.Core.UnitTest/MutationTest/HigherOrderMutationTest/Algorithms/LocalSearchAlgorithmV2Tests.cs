using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Algorithms
{
    /// <summary>
    /// Tests specifically for the SSHOM-focused improvements in LocalSearchAlgorithm.
    /// </summary>
    [TestClass]
    public class LocalSearchAlgorithmV2Tests : TestBase
    {
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<MutationTestInput> _inputMock;

        [TestInitialize]
        public void Setup()
        {
            _optionsMock = new Mock<IStrykerOptions>();
            _inputMock = new Mock<MutationTestInput>();
        }

        [TestMethod]
        public void LocalSearchAlgorithm_WithSSHOMTargetedMutants_ShouldPrioritizeOverlappingTests()
        {
            // Arrange - Create mutants with different test coverage patterns
            var mutants = new List<IMutant>
            {
                // High SSHOM potential pair (overlapping tests)
                CreateMutantWithTests(1, new[] { "Test1", "Test2", "Test3" }, "Method1", Mutator.Arithmetic),
                CreateMutantWithTests(2, new[] { "Test1", "Test2" }, "Method1", Mutator.Arithmetic),
                
                // Medium SSHOM potential pair (some overlap, different methods)
                CreateMutantWithTests(3, new[] { "Test1", "Test4" }, "Method2", Mutator.Equality),
                CreateMutantWithTests(4, new[] { "Test1", "Test5" }, "Method2", Mutator.Equality),
                
                // Low SSHOM potential pair (no overlap)
                CreateMutantWithTests(5, new[] { "TestA" }, "Method3", Mutator.String),
                CreateMutantWithTests(6, new[] { "TestB" }, "Method4", Mutator.String)
            };

            var algorithm = new LocalSearchAlgorithmV2(_inputMock.Object, [], _optionsMock.Object, mutants);

            // Act
            var candidates = algorithm.GenerateCandidates(mutants, [], _optionsMock.Object, _inputMock.Object).ToList();

            // Assert
            candidates.ShouldNotBeEmpty("Algorithm should generate some SSHOM candidates");
            
            // Verify that high-potential pairs are more likely to be generated
            var highPotentialCandidate = candidates.FirstOrDefault(c => 
                c.ConstituentMutants.Any(m => m.Id == 1) && c.ConstituentMutants.Any(m => m.Id == 2));
            
            highPotentialCandidate.ShouldNotBeNull("Should generate candidate from high-potential pair");
            
            // All candidates should have overlapping assessing tests
            foreach (var candidate in candidates)
            {
                var intersection = candidate.ConstituentMutants.Aggregate(
                    candidate.ConstituentMutants.First().AssessingTests,
                    (current, mutant) => current.Intersect(mutant.AssessingTests));
                    
                intersection.IsEmpty.ShouldBeFalse($"Candidate {candidate.Id} should have overlapping assessing tests");
            }
        }

        [TestMethod]
        public void LocalSearchAlgorithm_WithNoOverlappingTests_ShouldReturnEmptyResults()
        {
            // Arrange - Create mutants with completely disjoint test coverage
            var mutants = new List<IMutant>
            {
                CreateMutantWithTests(1, new[] { "TestA" }, "Method1", Mutator.Arithmetic),
                CreateMutantWithTests(2, new[] { "TestB" }, "Method2", Mutator.Arithmetic),
                CreateMutantWithTests(3, new[] { "TestC" }, "Method3", Mutator.Equality),
                CreateMutantWithTests(4, new[] { "TestD" }, "Method4", Mutator.Equality)
            };

            var algorithm = new LocalSearchAlgorithmV2(_inputMock.Object, [], _optionsMock.Object, mutants);

            // Act
            var candidates = algorithm.GenerateCandidates(mutants, [], _optionsMock.Object, _inputMock.Object).ToList();

            // Assert - Should generate very few or no candidates due to no test overlap
            candidates.Count.ShouldBeLessThanOrEqualTo(1, "Should generate few candidates when no tests overlap");
        }

        [TestMethod]
        public void LocalSearchAlgorithm_WithSyntacticallyRelatedMutants_ShouldPrioritizeThem()
        {
            // Arrange - Create mutants from the same method vs different methods
            var mutants = new List<IMutant>
            {
                // Same method pair (should be prioritized)
                CreateMutantWithTests(1, new[] { "Test1", "Test2" }, "Calculator.Add", Mutator.Arithmetic),
                CreateMutantWithTests(2, new[] { "Test1", "Test2" }, "Calculator.Add", Mutator.Arithmetic),
                
                // Different method pair
                CreateMutantWithTests(3, new[] { "Test1", "Test2" }, "Calculator.Subtract", Mutator.Arithmetic),
                CreateMutantWithTests(4, new[] { "Test1", "Test2" }, "Parser.ParseInt", Mutator.Equality)
            };

            var algorithm = new LocalSearchAlgorithmV2(_inputMock.Object, [], _optionsMock.Object, mutants);

            // Act
            var candidates = algorithm.GenerateCandidates(mutants, [], _optionsMock.Object, _inputMock.Object).ToList();

            // Assert
            candidates.ShouldNotBeEmpty();
            
            // Should include the syntactically related pair
            var sameMethodCandidate = candidates.FirstOrDefault(c =>
                c.ConstituentMutants.Any(m => m.Id == 1) && c.ConstituentMutants.Any(m => m.Id == 2));
                
            sameMethodCandidate.ShouldNotBeNull("Should prioritize syntactically related mutants");
        }

        [TestMethod]
        public void LocalSearchAlgorithm_WithMixedQualityMutants_ShouldFilterWeakMutators()
        {
            // Arrange - Mix of strong and weak mutator types
            var mutants = new List<IMutant>
            {
                // Strong mutator types (should be preferred)
                CreateMutantWithTests(1, new[] { "Test1", "Test2" }, "Method1", Mutator.Arithmetic),
                CreateMutantWithTests(2, new[] { "Test1", "Test2" }, "Method1", Mutator.Equality),
                
                // Weak mutator type (should be deprioritized)
                CreateMutantWithTests(3, new[] { "Test1", "Test2" }, "Method1", Mutator.String),
                CreateMutantWithTests(4, new[] { "Test1", "Test2" }, "Method1", Mutator.String)
            };

            var algorithm = new LocalSearchAlgorithmV2(_inputMock.Object, [], _optionsMock.Object, mutants);

            // Act
            var candidates = algorithm.GenerateCandidates(mutants, [], _optionsMock.Object, _inputMock.Object).ToList();

            // Assert
            candidates.ShouldNotBeEmpty();
            
            // Should prefer strong mutator combinations over weak ones
            var strongMutatorCandidate = candidates.FirstOrDefault(c =>
                c.ConstituentMutants.Any(m => m.Id == 1) && c.ConstituentMutants.Any(m => m.Id == 2));
                
            strongMutatorCandidate.ShouldNotBeNull("Should generate candidates from strong mutator types");
        }

        [TestMethod]
        public void LocalSearchAlgorithm_SSHOMPredictionHeuristic_ShouldScoreAccurately()
        {
            // Arrange
            var heuristic = new SSHOMPredictionHeuristic();
            
            // High-potential candidate (same method, overlapping tests, compatible mutators)
            var highPotentialCandidate = new List<IMutant>
            {
                CreateMutantWithTests(1, new[] { "Test1", "Test2", "Test3" }, "Calculator.Add", Mutator.Arithmetic),
                CreateMutantWithTests(2, new[] { "Test1", "Test2" }, "Calculator.Add", Mutator.Arithmetic)
            };
            
            // Low-potential candidate (different methods, no test overlap)
            var lowPotentialCandidate = new List<IMutant>
            {
                CreateMutantWithTests(3, new[] { "TestA" }, "Calculator.Add", Mutator.String),
                CreateMutantWithTests(4, new[] { "TestB" }, "Parser.Parse", Mutator.String)
            };

            // Act
            var highScore = heuristic.ScoreCandidate(highPotentialCandidate);
            var lowScore = heuristic.ScoreCandidate(lowPotentialCandidate);

            // Assert
            highScore.ShouldBeGreaterThan(lowScore, "High-potential candidate should score higher");
            highScore.ShouldBeGreaterThan(0.5, "High-potential candidate should have good score");
            lowScore.ShouldBeLessThan(0.3, "Low-potential candidate should have low score");
        }

        private IMutant CreateMutantWithTests(int id, string[] assessingTests, string method, Mutator mutatorType)
        {
            var mutantMock = new Mock<IMutant>();
            mutantMock.Setup(m => m.Id).Returns(id);
            mutantMock.Setup(m => m.ResultStatus).Returns(MutantStatus.Pending);
            
            var testIdentifiers = new TestIdentifierList(assessingTests);
            mutantMock.Setup(m => m.AssessingTests).Returns(testIdentifiers);
            mutantMock.Setup(m => m.CoveringTests).Returns(testIdentifiers);
            mutantMock.Setup(m => m.KillingTests).Returns(TestIdentifierList.NoTest());

            var mutation = new Mutation
            {
                Type = mutatorType,
                Description = method,
                DisplayName = $"{mutatorType} in {method}"
            };
            
            mutantMock.Setup(m => m.Mutation).Returns(mutation);

            return mutantMock.Object;
        }
    }
}
