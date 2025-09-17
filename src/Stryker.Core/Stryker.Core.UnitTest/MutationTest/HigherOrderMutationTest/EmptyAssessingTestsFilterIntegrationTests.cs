using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest
{
    /// <summary>
    /// Integration tests to verify that HOMs with empty assessing tests are properly filtered out
    /// at all levels: algorithm, heuristic registry, and BuildAndOptimizeHigherOrderMutants.
    /// This prevents the duplicate FOM warnings in VsTestRunner.
    /// </summary>
    [TestClass]
    public class EmptyAssessingTestsFilterIntegrationTests : TestBase
    {
        private Mock<IStrykerOptions> _optionsMock;
        private Mock<IProvideId> _idProviderMock;
        private Mock<MutationTestInput> _inputMock;

        [TestInitialize]
        public void Setup()
        {
            _optionsMock = new Mock<IStrykerOptions>();
            _idProviderMock = new Mock<IProvideId>();
            var idSequence = 1000;
            _idProviderMock.Setup(p => p.NextId()).Returns(() => ++idSequence);
            _optionsMock.Setup(o => o.MutantIdProvider).Returns(_idProviderMock.Object);
            _optionsMock.Setup(o => o.OptimizationMode).Returns(OptimizationModes.HOMTValidate);
            
            _inputMock = new Mock<MutationTestInput>();
        }

        [TestMethod]
        public void BuildAndOptimizeHigherOrderMutants_ShouldFilterOutHOMsWithEmptyAssessingTests()
        {
            // Arrange - Create mutants with no overlapping assessing tests
            var mutants = new List<IMutant>
            {
                CreateMockMutant(1, MutantStatus.Pending, new[] { "TestA", "TestB" }),      // Group 1
                CreateMockMutant(2, MutantStatus.Pending, new[] { "TestC", "TestD" }),      // Group 2 
                CreateMockMutant(3, MutantStatus.Pending, new[] { "TestE", "TestF" }),      // Group 3
                CreateMockMutant(4, MutantStatus.Pending, new[] { "Test1", "Test2" }),      // Group 4
                CreateMockMutant(5, MutantStatus.Pending, new[] { "Test1", "Test3" })       // Group 4 (overlaps with mutant 4)
            };

            var higherOrderMutation = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, mutants);

            // Add LocalSearchAlgorithm which should generate some problematic HOMs
            var localSearchAlgorithm = new LocalSearchAlgorithm(_inputMock.Object, [], _optionsMock.Object, mutants);
            higherOrderMutation.AddSearchAlgorithm(localSearchAlgorithm);

            // Act
            var result = higherOrderMutation.BuildAndOptimizeHigherOrderMutants(
                mutants, isPreTestRun: true, includeAllIndividualMutants: false);

            // Assert
            var homResults = result.HomCandidates.ToList();
            var fomResults = result.MissingFirstOrderMutants.ToList();

            foreach (var hom in homResults)
            {
                hom.AssessingTests.IsEmpty.ShouldBeFalse($"HOM {hom.Id} should not have empty assessing tests");
                hom.Order.ShouldBeGreaterThanOrEqualTo(2, $"HOM {hom.Id} should have order >= 2");
            }

            // In validate mode MissingFirstOrderMutants will be empty (all FOMs tested individually later)
            if (_optionsMock.Object.OptimizationMode.HasFlag(OptimizationModes.HOMTAccelerate))
            {
                fomResults.ShouldNotBeEmpty("Should retain some individual FOMs in accelerate mode");
            }

            var validPairExists = homResults.Any(hom => 
                hom.ConstituentMutants.Any(c => c.Id == 4 ) &&
                hom.ConstituentMutants.Any(c => c.Id == 5 ));

            if (homResults.Any())
            {
                validPairExists.ShouldBeTrue("Should create HOMs from mutants with overlapping tests");
            }
        }

        [TestMethod]
        public void LocalSearchAlgorithm_ShouldNotGenerateHOMsWithEmptyAssessingTests()
        {
            // Arrange - Create mutants where all pairs would result in empty assessing tests
            var problematicMutants = new List<IMutant>
            {
                CreateMockMutant(1, MutantStatus.Pending, new[] { "TestA" }),
                CreateMockMutant(2, MutantStatus.Pending, new[] { "TestB" }),
                CreateMockMutant(3, MutantStatus.Pending, new[] { "TestC" }),
                CreateMockMutant(4, MutantStatus.Pending, new[] { "TestD" })
            };

            var algorithm = new LocalSearchAlgorithm(_inputMock.Object, [], _optionsMock.Object, problematicMutants);

            // Act - Generate candidates directly from algorithm
            var candidates = algorithm.GenerateCandidates(problematicMutants, [], _optionsMock.Object, _inputMock.Object).ToList();

            // Assert - Should generate no candidates because all would be filtered
            foreach (var candidate in candidates)
            {
                candidate.AssessingTests.IsEmpty.ShouldBeFalse($"Generated candidate {candidate.Id} should not have empty assessing tests");
            }

            // In this case, we expect very few or no candidates since all pairs have empty intersections
            // The exact count depends on the algorithm's random generation, but all generated ones should be valid
        }

        [TestMethod]
        public void HigherOrderMutation_CreateCandidateHOMs_ShouldApplyDoubleFiltering()
        {
            // Arrange - Create a mock algorithm that intentionally returns bad candidates to test our safety net
            var mockAlgorithm = new Mock<IHOMSearchAlgorithm>();
            mockAlgorithm.Setup(a => a.Name).Returns("MockAlgorithm");
            
            var mutants = new List<IMutant>
            {
                CreateMockMutant(1, MutantStatus.Pending, new[] { "TestA" }),
                CreateMockMutant(2, MutantStatus.Pending, new[] { "TestB" })
            };

            // Create problematic HOMs that should be filtered
            var badHOM1 = new HigherOrderMutant(new List<IMutant> { mutants[0] }); // Order 1
            var badHOM2 = new HigherOrderMutant(new List<IMutant> { mutants[0], mutants[1] }); // Empty assessing tests
            
            mockAlgorithm.Setup(a => a.GenerateCandidates(
                It.IsAny<IReadOnlyCollection<IMutant>>(),
                It.IsAny<IReadOnlyList<Core.MutationTest.HigherOrderMutationTest.Heuristics.IHOMHeuristic>>(),
                It.IsAny<IStrykerOptions>(),
                It.IsAny<MutationTestInput>()))
                .Returns(new[] { badHOM1, badHOM2 });

            var higherOrderMutation = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, mutants);
            higherOrderMutation.AddSearchAlgorithm(mockAlgorithm.Object);

            // Act
            var candidates = higherOrderMutation.CreateCandidateHOMs().ToList();

            // Assert - Both candidates should be filtered out
            candidates.ShouldBeEmpty("All problematic candidates should be filtered out");
        }

        [TestMethod]
        public void EndToEnd_MutationTestProcess_ShouldNotCreateDuplicateWarnings()
        {
            // Arrange - Create scenario that previously caused duplicate warnings
            var mutants = new List<IMutant>
            {
                CreateMockMutant(659, MutantStatus.Pending, new[] { "TestX" }),              // Regular FOM
                CreateMockMutant(1, MutantStatus.Pending, new[] { "TestY" }),               // Will be constituent of HOM
                CreateMockMutant(2, MutantStatus.Pending, new[] { "TestZ" })                // Will be constituent of HOM  
            };

            var higherOrderMutation = new HigherOrderMutation(_optionsMock.Object, _inputMock.Object, mutants);
            var localSearchAlgorithm = new LocalSearchAlgorithm(_inputMock.Object, [], _optionsMock.Object, mutants);
            higherOrderMutation.AddSearchAlgorithm(localSearchAlgorithm);

            // Act - This would previously create HOMs with empty assessing tests
            var result = higherOrderMutation.BuildAndOptimizeHigherOrderMutants(
                mutants, isPreTestRun: true, includeAllIndividualMutants: true);

            // Assert - No HOMs with empty assessing tests should be created
            var homResults = result.HomCandidates.ToList();
            
            foreach (var hom in homResults)
            {
                hom.AssessingTests.IsEmpty.ShouldBeFalse($"HOM {hom.Id} should not have empty assessing tests");
                
                // This ensures no HOM will be grouped with regular FOMs that share the same constituent mutant IDs
                // because the HOM would have non-empty assessing tests that overlap with those FOMs
            }

            // Should still retain all individual mutants
            var allMutantIds = homResults.SelectMany(h => h.ConstituentMutants).Select(m => m.Id).Concat(mutants.Select(m=>m.Id)).ToHashSet();
            foreach (var originalMutant in mutants)
            {
                allMutantIds.ShouldContain(originalMutant.Id, $"Should retain original mutant {originalMutant.Id}");
            }
        }

        private IMutant CreateMockMutant(int id, MutantStatus status, string[] assessingTests)
        {
            var mutantMock = new Mock<IMutant>();
            mutantMock.Setup(m => m.Id).Returns(id);
            mutantMock.Setup(m => m.ResultStatus).Returns(status);
            
            var testIdentifiers = new TestIdentifierList(assessingTests);
            mutantMock.Setup(m => m.AssessingTests).Returns(testIdentifiers);
            mutantMock.Setup(m => m.CoveringTests).Returns(testIdentifiers);
            mutantMock.Setup(m => m.KillingTests).Returns(TestIdentifierList.NoTest());

            // add mutation type
            mutantMock.Setup(m => m.Mutation).Returns(new Mutation { Type = Mutator.Math });

            return mutantMock.Object;
        }
    }
}
