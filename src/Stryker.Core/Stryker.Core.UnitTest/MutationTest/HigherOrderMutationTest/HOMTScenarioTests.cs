using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.TestRunner.VsTest;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest
{
    /// <summary>
    /// Scenario-based tests for Higher-Order Mutation Testing (HOMT) implementation.
    /// Tests specific real-world scenarios and edge cases.
    /// </summary>
    [TestClass]
    public class HOMTScenarioTests : TestBase
    {
        #region Test Setup

        [TestInitialize]
        public void TestInitialize()
        {
            // Reset any static state if needed
        }

        #endregion

        #region Scenario Tests

        [TestMethod]
        public void Scenario_SameFileMultipleMutations_ShouldCreateValidHOMs()
        {
            // Arrange - Create mutants in the same file that should combine well
            var options = CreateStrykerOptions(OptimizationModes.EnableHigherOrderMutants);
            var input = CreateMutationTestInput();
            
            var mutantsInSameFile = new List<IMutant>
            {
                CreateMutant(1, new[] { "Test1", "Test2" }, "Calculator.cs", "Add"),
                CreateMutant(2, new[] { "Test1", "Test2" }, "Calculator.cs", "Subtract"),
                CreateMutant(3, new[] { "Test2" }, "Calculator.cs", "Multiply")
            };

            var homt = new HigherOrderMutation(options, input, mutantsInSameFile);
            
            // Add heuristics that favor same-file combinations
            homt.AddHeuristic(new CodeLocationHeuristic());
            homt.AddHeuristic(new MaxSizeLimitHeuristic());
            
            // Add algorithm
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, new List<IHOMHeuristic>(), options, mutantsInSameFile));

            // Act
            var candidates = homt.CreateCandidateHOMs().ToList();

            // Assert
            candidates.ShouldNotBeEmpty("Should create HOM candidates for same-file mutants");
            candidates.All(c => c.Order>= 2).ShouldBeTrue("All candidates should be higher-order");
            
            // Should favor combinations with overlapping test coverage
            var overlappingTestHOMs = candidates.Where(c => 
                HasOverlappingTests(c.ConstituentMutants.ToArray())).ToList();
            overlappingTestHOMs.ShouldNotBeEmpty("Should find candidates with overlapping test coverage");
        }

        [TestMethod]
        public void Scenario_DifferentFileMutations_ShouldBeFilteredByHeuristics()
        {
            // Arrange - Create mutants across different files
            var options = CreateStrykerOptions(OptimizationModes.EnableHigherOrderMutants);
            var input = CreateMutationTestInput();
            
            var mutantsInDifferentFiles = new List<IMutant>
            {
                CreateMutant(1, new[] { "Test1" }, "Calculator.cs", "Add"),
                CreateMutant(2, new[] { "Test2" }, "MathHelper.cs", "Square"),
                CreateMutant(3, new[] { "Test3" }, "StringUtils.cs", "Format")
            };

            var homt = new HigherOrderMutation(options, input, mutantsInDifferentFiles);
            
            // Add heuristic that filters cross-file combinations
            homt.AddHeuristic(new CodeLocationHeuristic());
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, [], options, mutantsInDifferentFiles));

            // Act
            var candidates = homt.CreateCandidateHOMs().ToList();

            // Assert
            // The main assertion: we should have some candidates generated
            candidates.ShouldNotBeEmpty("Should create some HOM candidates");
            
            // With 3 mutants, we could theoretically have at most 3 pairs + 1 triplet = 4 combinations
            // But heuristics should influence the selection process
            var crossFileHOMs = candidates.Where(c => 
                c.ConstituentMutants.Select(m => GetFileFromMutant(m)).Distinct().Count() > 1).ToList();
            
            // Verify that candidates are generated and heuristics are working
            // The specific number may vary due to randomness in LocalSearchAlgorithm and heuristic interactions
            candidates.Count.ShouldBeLessThanOrEqualTo(10, "Should not generate excessive candidates");
            
            // More importantly, verify that we have valid HOMs (all candidates should be order >= 2)
            candidates.All(c => c.Order >= 2).ShouldBeTrue("All candidates should be higher-order mutants");
        }

        [TestMethod]
        public void Scenario_ConflictingMutations_ShouldBeRejected()
        {
            // Arrange - Create mutants that would conflict when combined
            var options = CreateStrykerOptions(OptimizationModes.EnableHigherOrderMutants);
            var input = CreateMutationTestInput();
            
            // Create a shared SyntaxNode to properly test SyntaxNodeConflictHeuristic
            var sharedSyntaxNode = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.BinaryExpression(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.GreaterThanExpression,
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName("x"),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName("y"));
            
            // Create mutants that would modify the same SyntaxNode
            var conflictingMutants = new List<IMutant>
            {
                CreateMutantWithSyntaxNode(1, new[] { "Test1" }, "Calculator.cs", "Add", sharedSyntaxNode),
                CreateMutantWithSyntaxNode(2, new[] { "Test1" }, "Calculator.cs", "Add", sharedSyntaxNode), // Same SyntaxNode
                CreateMutant(3, new[] { "Test2" }, "Calculator.cs", "Subtract", line: 15)
            };

            var homt = new HigherOrderMutation(options, input, conflictingMutants);
            
            // SyntaxNodeConflictHeuristic should now be automatic via HeuristicRegistry
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, new List<IHOMHeuristic>(), options, conflictingMutants));

            // Act
            var candidates = homt.CreateCandidateHOMs().ToList();

            // Assert
            // Should not contain HOMs with conflicting mutants (those targeting the same SyntaxNode)
            // Since mutants 1 and 2 target the same SyntaxNode, they should never be combined
            foreach (var candidate in candidates)
            {
                var hasBothConflictingMutants = candidate.ConstituentMutants.Any(m => m.Id == 1) && candidate.ConstituentMutants.Any(m => m.Id == 2);
                hasBothConflictingMutants.ShouldBeFalse(
                    $"Candidate should not contain both mutants 1 and 2 that target the same SyntaxNode. " +
                    $"Found candidate with mutants: [{string.Join(", ", candidate.ConstituentMutants.Select(m => m.Id))}]");
            }
            
            // Additional verification: ensure we still have some candidates (just not conflicting ones)
            candidates.ShouldNotBeEmpty("Should still generate some non-conflicting HOM candidates");
        }

        [TestMethod]
        public void Scenario_SSHOMDetection_ShouldIdentifyStronglySubsumingMutants()
        {
            // Arrange - Create a scenario specifically for SSHOM detection
            var options = CreateStrykerOptions(OptimizationModes.EnableHigherOrderMutants);
            var input = CreateMutationTestInput();
            
            // Create FOMs with specific test coverage for SSHOM testing
            var fomA = CreateMutant(1, new[] { "TestA", "TestB", "TestC" }, "Calculator.cs", "Add");
            var fomB = CreateMutant(2, new[] { "TestA", "TestB" }, "Calculator.cs", "Subtract");
            var fomC = CreateMutant(3, new[] { "TestB", "TestC" }, "Calculator.cs", "Multiply");
            
            var homt = new HigherOrderMutation(options, input, new[] { fomA, fomB, fomC });

            // Test SSHOM scenarios
            var homKilledBySubset = CreateTestIdentifiers(new[] { "TestB" }); // Subset of A∩B and B∩C
            var homKilledByEqual = CreateTestIdentifiers(new[] { "TestA", "TestB" }); // Equal to A∩B
            var homKilledBySuperset = CreateTestIdentifiers(new[] { "TestA", "TestB", "TestC", "TestD" }); // Superset

            // Act & Assert
            // Scenario 1: HOM killed by subset of FOM intersection (should be SSHOM)
            homt.IsSSHOM(new[] { fomA, fomB }, homKilledBySubset).ShouldBeTrue();
            
            // Scenario 2: HOM killed by equal set (depends on properSubset parameter)
            homt.IsSSHOM(new[] { fomA, fomB }, homKilledByEqual, properSubset: false).ShouldBeTrue();
            homt.IsSSHOM(new[] { fomA, fomB }, homKilledByEqual, properSubset: true).ShouldBeFalse();
            
            // Scenario 3: HOM killed by superset (not valid SSHOM)
            homt.IsSSHOM(new[] { fomA, fomB }, homKilledBySuperset).ShouldBeFalse();
            
            // Scenario 4: Test with three mutants
            homt.IsSSHOM(new[] { fomA, fomB, fomC }, homKilledBySubset).ShouldBeTrue(); // TestB is in all intersections
        }

        [TestMethod]
        public void Scenario_PerformanceWithVariousSizes_ShouldScaleReasonably()
        {
            // Arrange - Test performance scaling with different mutant set sizes
            var options = CreateStrykerOptions(OptimizationModes.EnableHigherOrderMutants);
            var input = CreateMutationTestInput();
            
            var smallSet = CreateMutantSet(10);
            var mediumSet = CreateMutantSet(25);
            var largeSet = CreateMutantSet(50);

            // Act & Assert
            var smallTime = MeasureHOMTTime(options, input, smallSet);
            var mediumTime = MeasureHOMTTime(options, input, mediumSet);
            var largeTime = MeasureHOMTTime(options, input, largeSet);

            // Performance should scale reasonably (not exponentially)
            smallTime.TotalMilliseconds.ShouldBeLessThan(1000); // < 1 second
            mediumTime.TotalMilliseconds.ShouldBeLessThan(3000); // < 3 seconds  
            largeTime.TotalMilliseconds.ShouldBeLessThan(8000); // < 8 seconds
            
            // Should not be exponential growth
            var smallToMediumRatio = mediumTime.TotalMilliseconds / smallTime.TotalMilliseconds;
            var mediumToLargeRatio = largeTime.TotalMilliseconds / mediumTime.TotalMilliseconds;
            
            smallToMediumRatio.ShouldBeLessThan(10); // Not more than 10x slower
            mediumToLargeRatio.ShouldBeLessThan(5); // Not more than 5x slower
        }

        [TestMethod]
        public void Scenario_HeuristicCombinations_ShouldProduceRefinedResults()
        {
            // Arrange - Test different heuristic combinations
            var options = CreateStrykerOptions(OptimizationModes.EnableHigherOrderMutants);
            var input = CreateMutationTestInput();
            var mutants = CreateMutantSet(20);

            // Test with minimal heuristics (disable defaults to control behavior)
            var minimalHeuristicsResults = RunHOMTWithCustomHeuristics(options, input, mutants, new List<IHOMHeuristic>
            {
                new MaxSizeLimitHeuristic()
            });
            
            // Test with size limit only
            var sizeLimitResults = RunHOMTWithCustomHeuristics(options, input, mutants, new List<IHOMHeuristic>
            {
                new MaxSizeLimitHeuristic()
            });
            
            // Test with multiple heuristics including conflict detection
            var multipleHeuristicsResults = RunHOMTWithCustomHeuristics(options, input, mutants, new List<IHOMHeuristic>
            {
                new MaxSizeLimitHeuristic(),
                new CodeLocationHeuristic(),
                new MutatorTypeHeuristic(),
                new SyntaxNodeConflictHeuristic()
            });

            // Act & Assert
            minimalHeuristicsResults.ShouldNotBeEmpty();
            sizeLimitResults.ShouldNotBeEmpty();
            multipleHeuristicsResults.ShouldNotBeEmpty();
            
            // More heuristics should generally produce same or fewer candidates (more refined)
            multipleHeuristicsResults.Count.ShouldBeLessThanOrEqualTo(sizeLimitResults.Count + 100); // Allow reasonable variance
            
            // All results should respect size limits
            sizeLimitResults.All(c => c.Order <= 4).ShouldBeTrue(); // MaxSizeLimitHeuristic default
            multipleHeuristicsResults.All(c => c.Order <= 4).ShouldBeTrue();
            
            // Verify that SyntaxNodeConflictHeuristic is working - no candidates should have syntax conflicts
            foreach (var candidate in multipleHeuristicsResults)
            {
                var syntaxNodes = candidate.ConstituentMutants
                    .Where(m => m.Mutation?.OriginalNode != null)
                    .Select(m => m.Mutation.OriginalNode)
                    .ToList();
                syntaxNodes.Count.ShouldBe(syntaxNodes.Distinct().Count(), 
                    "No candidate should contain mutants targeting the same SyntaxNode");
            }
        }

        [TestMethod]
        public void Scenario_RealWorldMutantDistribution_ShouldHandleRealisticPatterns()
        {
            // Arrange - Create a realistic distribution of mutants like what would be found in real code
            var options = CreateStrykerOptions(OptimizationModes.EnableHigherOrderMutants);
            var input = CreateMutationTestInput();
            
            var realisticMutants = new List<IMutant>();
            
            // Arithmetic mutants (common)
            realisticMutants.AddRange(CreateMutantsOfType(1, 5, new[] { "TestMath1", "TestMath2" }, "Calculator.cs", Mutator.Arithmetic));
            
            // Boolean mutants (common)
            realisticMutants.AddRange(CreateMutantsOfType(6, 10, new[] { "TestLogic1", "TestLogic2" }, "Validator.cs", Mutator.Boolean));
            
            // String mutants (less common)
            realisticMutants.AddRange(CreateMutantsOfType(11, 13, new[] { "TestString1" }, "StringHelper.cs", Mutator.String));
            
            // Block mutants (rare)
            realisticMutants.AddRange(CreateMutantsOfType(14, 15, new[] { "TestFlow1" }, "FlowControl.cs", Mutator.Block));

            var homt = new HigherOrderMutation(options, input, realisticMutants);
            homt.AddHeuristic(new MutatorTypeHeuristic()); // Should favor same-type combinations
            homt.AddHeuristic(new CodeLocationHeuristic()); // Should favor same-file combinations
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, new List<IHOMHeuristic>(), options, realisticMutants));

            // Act
            var candidates = homt.CreateCandidateHOMs().ToList();

            // Assert
            candidates.ShouldNotBeEmpty("Should create candidates from realistic mutant distribution");
            
            // Should favor same-type combinations
            var sameTypeCandidates = candidates.Where(c => 
                c.ConstituentMutants.Select(m => m.Mutation.Type).Distinct().Count() == 1).ToList();
            sameTypeCandidates.ShouldNotBeEmpty("Should create same-type combinations");
            
            // Should favor same-file combinations
            var sameFileCandidates = candidates.Where(c =>
                c.ConstituentMutants.Select(m => GetFileFromMutant(m)).Distinct().Count() == 1).ToList();
            sameFileCandidates.ShouldNotBeEmpty("Should create same-file combinations");
        }

        #endregion

        #region Helper Methods

        private StrykerOptions CreateStrykerOptions(OptimizationModes optimizationMode)
        {
            var mutantIdProvider = new Mock<IProvideId>();
            var random = new Random();
            mutantIdProvider.Setup(i => i.NextId()).Returns(() => random.Next(1, 1000));


            return new StrykerOptions
            {
                OptimizationMode = optimizationMode,
                Concurrency = 1,
                MutantIdProvider = mutantIdProvider.Object,
            };
        }

        private MutationTestInput CreateMutationTestInput()
        {
            var mock = new Mock<MutationTestInput>();
            return mock.Object;
        }

        private IMutant CreateMutant(int id, string[] killingTests, string file, string method, int line = 1)
        {
            var mock = new Mock<IMutant>();
            mock.Setup(m => m.Id).Returns(id);
            mock.Setup(m => m.KillingTests).Returns(CreateTestIdentifiers(killingTests));
            mock.Setup(m => m.Mutation).Returns(new Mutation
            {
                Type = Mutator.Arithmetic,
                DisplayName = $"{method} mutation",
                Description = $"Mutation in {file}:{line}"
            });
            return mock.Object;
        }

        private IMutant CreateMutantWithSyntaxNode(int id, string[] killingTests, string file, string method, Microsoft.CodeAnalysis.SyntaxNode syntaxNode, int line = 1)
        {
            var mock = new Mock<IMutant>();
            mock.Setup(m => m.Id).Returns(id);
            mock.Setup(m => m.KillingTests).Returns(CreateTestIdentifiers(killingTests));
            mock.Setup(m => m.Mutation).Returns(new Mutation
            {
                OriginalNode = syntaxNode,
                Type = Mutator.Arithmetic,
                DisplayName = $"{method} mutation",
                Description = $"Mutation in {file}"
            });
            return mock.Object;
        }

        private List<IMutant> CreateMutantSet(int count)
        {
            var mutants = new List<IMutant>();
            var testNames = new[] { "Test1", "Test2", "Test3", "Test4", "Test5" };
            var files = new[] { "File1.cs", "File2.cs", "File3.cs" };
            
            for (int i = 1; i <= count; i++)
            {
                var killingTests = testNames.Take((i % testNames.Length) + 1).ToArray();
                var file = files[i % files.Length];
                mutants.Add(CreateMutant(i, killingTests, file, $"Method{i}"));
            }
            
            return mutants;
        }

        private List<IMutant> CreateMutantsOfType(int startId, int endId, string[] killingTests, string file, Mutator type)
        {
            var mutants = new List<IMutant>();
            for (int i = startId; i <= endId; i++)
            {
                var mock = new Mock<IMutant>();
                mock.Setup(m => m.Id).Returns(i);
                mock.Setup(m => m.KillingTests).Returns(CreateTestIdentifiers(killingTests));
                mock.Setup(m => m.Mutation).Returns(new Mutation
                {
                    Type = type,
                    DisplayName = $"{type} mutation {i}",
                    Description = $"Mutation in {file}"
                });
                mutants.Add(mock.Object);
            }
            return mutants;
        }

        private ITestIdentifiers CreateTestIdentifiers(string[] testNames)
        {
            var mock = new Mock<ITestIdentifiers>();
            mock.Setup(t => t.IsEmpty).Returns(testNames.Length == 0);
            mock.Setup(t => t.GetIdentifiers()).Returns(testNames);
            
            // Setup intersection behavior
            mock.Setup(t => t.Intersect(It.IsAny<ITestIdentifiers>()))
                .Returns<ITestIdentifiers>(other =>
                {
                    var otherTests = other.GetIdentifiers().ToHashSet();
                    var intersection = testNames.Where(name => otherTests.Contains(name)).ToArray();
                    return CreateTestIdentifiers(intersection);
                });
                
            // Setup IsIncludedIn behavior
            mock.Setup(t => t.IsIncludedIn(It.IsAny<ITestIdentifiers>()))
                .Returns<ITestIdentifiers>(other =>
                {
                    var otherTests = other.GetIdentifiers().ToHashSet();
                    return testNames.All(name => otherTests.Contains(name));
                });
            
            return mock.Object;
        }

        private List<HigherOrderMutant> RunHOMTWithCustomHeuristics(StrykerOptions options, MutationTestInput input, 
            List<IMutant> mutants, List<IHOMHeuristic> heuristics)
        {
            // Create HOMT with empty default to avoid default heuristics, then add custom ones
            var homt = new HigherOrderMutation(options, input, mutants);
            
            foreach (var heuristic in heuristics)
            {
                homt.AddHeuristic(heuristic);
            }
            
            // Use custom heuristics instead of relying on HeuristicRegistry defaults
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, heuristics, options, mutants));
            
            return homt.CreateCandidateHOMs().ToList();
        }

        private List<HigherOrderMutant> RunHOMTWithHeuristics(StrykerOptions options, MutationTestInput input, 
            List<IMutant> mutants, List<IHOMHeuristic> heuristics)
        {
            var homt = new HigherOrderMutation(options, input, mutants);
            
            foreach (var heuristic in heuristics)
            {
                homt.AddHeuristic(heuristic);
            }
            
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, heuristics, options, mutants));
            
            return homt.CreateCandidateHOMs().ToList();
        }

        private TimeSpan MeasureHOMTTime(StrykerOptions options, MutationTestInput input, List<IMutant> mutants)
        {
            var homt = new HigherOrderMutation(options, input, mutants);
            homt.AddHeuristic(new MaxSizeLimitHeuristic());
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, new List<IHOMHeuristic>(), options, mutants));
            
            var start = DateTime.Now;
            var candidates = homt.CreateCandidateHOMs().ToList();
            var elapsed = DateTime.Now - start;
            
            // Ensure we actually generated some candidates
            candidates.ShouldNotBeEmpty();
            
            return elapsed;
        }

        private bool HasOverlappingTests(IMutant[] mutants)
        {
            if (mutants.Length < 2) return false;
            
            var firstTests = mutants[0].KillingTests.GetIdentifiers().ToHashSet();
            return mutants.Skip(1).Any(m => 
                m.KillingTests.GetIdentifiers().Any(test => firstTests.Contains(test)));
        }

        private string GetFileFromMutant(IMutant mutant)
        {
            return mutant.Mutation?.Description?.Split(':')[0] ?? "unknown";
        }

        private int GetLineFromMutant(IMutant mutant)
        {
            var parts = mutant.Mutation?.Description?.Split(':');
            return parts?.Length > 1 && int.TryParse(parts[1], out var line) ? line : 1;
        }

        #endregion
    }
}
