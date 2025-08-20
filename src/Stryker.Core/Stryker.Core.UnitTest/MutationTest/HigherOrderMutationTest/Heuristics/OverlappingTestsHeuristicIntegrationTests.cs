using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Heuristics;

/// <summary>
/// Integration tests demonstrating the OverlappingTestsHeuristic in realistic scenarios
/// that show how it improves SSHOM potential detection
/// </summary>
[TestClass]
public class OverlappingTestsHeuristicIntegrationTests : TestBase
{
    [TestMethod]
    public void OverlappingTestsHeuristic_WithRealisticMutationScenario_ShouldIdentifySSHOMPotential()
    {
        // Arrange - Simulate a realistic scenario with different types of mutants
        var heuristic = new OverlappingTestsHeuristic();

        // Calculator class mutants - these have good test overlap
        var calculatorArithmetic1 = CreateMutant(1, "Calculator.cs", "Add", 
            new[] { "CalculatorTests.TestAdd", "CalculatorTests.TestAllOperations", "IntegrationTest1" });
        var calculatorArithmetic2 = CreateMutant(2, "Calculator.cs", "Subtract", 
            new[] { "CalculatorTests.TestSubtract", "CalculatorTests.TestAllOperations", "IntegrationTest1" });
        var calculatorBoundary = CreateMutant(3, "Calculator.cs", "Divide", 
            new[] { "CalculatorTests.TestDivide", "CalculatorTests.TestAllOperations", "IntegrationTest1", "BoundaryTests.TestDivisionByZero" });

        // Utility class mutants - these have less overlap
        var utilityString1 = CreateMutant(4, "StringUtils.cs", "Reverse", 
            new[] { "StringUtilsTests.TestReverse", "StringUtilsTests.TestPalindrome", "FormatTests.TestCasing" });
        var utilityString2 = CreateMutant(5, "StringUtils.cs", "ToUpperCase", 
            new[] { "StringUtilsTests.TestToUpperCase", "FormatTests.TestCasing" });

        // Unrelated mutants - no overlap
        var databaseMutant = CreateMutant(6, "DatabaseHelper.cs", "Connect", 
            new[] { "DatabaseTests.TestConnection", "IntegrationTest2" });

        // Test different candidate combinations
        var goodSSHOMCandidate = new List<IMutant> { calculatorArithmetic1, calculatorArithmetic2 };
        var mediumSSHOMCandidate = new List<IMutant> { calculatorArithmetic1, calculatorArithmetic2, calculatorBoundary };
        var poorSSHOMCandidate = new List<IMutant> { utilityString1, utilityString2 };
        var noSSHOMCandidate = new List<IMutant> { utilityString1, databaseMutant };

        // Act
        var goodScore = heuristic.ScoreCandidate(goodSSHOMCandidate);
        var mediumScore = heuristic.ScoreCandidate(mediumSSHOMCandidate);
        var poorScore = heuristic.ScoreCandidate(poorSSHOMCandidate);
        var noScore = heuristic.ScoreCandidate(noSSHOMCandidate);

        // Assert - Verify correct ranking based on SSHOM potential
        goodScore.ShouldBeGreaterThan(mediumScore, "Calculator mutants with less non-overlapping tests should score higher");
        mediumScore.ShouldBeGreaterThan(poorScore, "Calculator mutants should score higher than utility mutants");
        poorScore.ShouldBeGreaterThan(noScore, "Some overlap should score higher than no overlap");
        noScore.ShouldBe(0.0, "No overlap should result in zero score");

        Console.WriteLine($"Good SSHOM candidate (Calculator 3-way): {goodScore:F3}");
        Console.WriteLine($"Medium SSHOM candidate (Calculator 2-way): {mediumScore:F3}");
        Console.WriteLine($"Poor SSHOM candidate (Utility 2-way): {poorScore:F3}");
        Console.WriteLine($"No SSHOM candidate (Unrelated): {noScore:F3}");
    }

    [TestMethod]
    public void OverlappingTestsHeuristic_ShouldFilterOutNonViableCandidates()
    {
        // Arrange
        var heuristic = new OverlappingTestsHeuristic();

        var viableCandidate = new List<IMutant>
        {
            CreateMutant(1, "Calculator.cs", "Add", new[] { "Test1", "Test2" }),
            CreateMutant(2, "Calculator.cs", "Subtract", new[] { "Test1", "Test3" })
        };

        var nonViableCandidate = new List<IMutant>
        {
            CreateMutant(3, "Calculator.cs", "Add", new[] { "Test1", "Test2" }),
            CreateMutant(4, "StringUtils.cs", "Reverse", new[] { "Test3", "Test4" })
        };

        var emptyTestsCandidate = new List<IMutant>
        {
            CreateMutant(5, "Calculator.cs", "Add", new[] { "Test1" }),
            CreateMutant(6, "Calculator.cs", "Subtract", new string[0]) // No covering tests
        };

        // Act & Assert
        heuristic.ShouldFilterCandidate(viableCandidate).ShouldBeFalse("Should keep viable candidates");
        heuristic.ShouldFilterCandidate(nonViableCandidate).ShouldBeTrue("Should filter candidates with no overlap");
        heuristic.ShouldFilterCandidate(emptyTestsCandidate).ShouldBeTrue("Should filter candidates with empty test sets");
    }

    [TestMethod]
    public void OverlappingTestsHeuristic_WithAnalysisOutput_ShouldProvideInsights()
    {
        // Arrange
        var heuristic = new OverlappingTestsHeuristic();

        var candidate = new List<IMutant>
        {
            CreateMutant(1, "Calculator.cs", "Add", new[] { "UnitTest1", "UnitTest2", "IntegrationTest1", "PerformanceTest1" }),
            CreateMutant(2, "Calculator.cs", "Subtract", new[] { "UnitTest2", "IntegrationTest1", "UnitTest3", "PerformanceTest2" }),
            CreateMutant(3, "Calculator.cs", "Multiply", new[] { "IntegrationTest1", "UnitTest4" })
        };

        // Act
        var analysis = heuristic.GetOverlapAnalysis(candidate);
        var score = heuristic.ScoreCandidate(candidate);

        // Assert
        analysis.ShouldNotBeNullOrEmpty();
        analysis.ShouldContain("Intersection: 1"); // Only IntegrationTest1 is common to all
        analysis.ShouldContain("Union: 7"); // All unique tests combined
        analysis.ShouldContain("Jaccard:");
        analysis.ShouldContain("Score:");

        score.ShouldBeGreaterThan(0.0);
        Console.WriteLine($"Analysis: {analysis}");
    }

    [TestMethod]
    public void OverlappingTestsHeuristic_ComparedToOtherMetrics_ShouldComplementExistingHeuristics()
    {
        // Arrange - Test how this heuristic works with other quality metrics
        var overlappingTestsHeuristic = new OverlappingTestsHeuristic();

        // Candidate A: Same file, great test overlap
        var candidateA = new List<IMutant>
        {
            CreateMutant(1, "Calculator.cs", "Add", new[] { "Test1", "Test2", "Test3", "Test4" }),
            CreateMutant(2, "Calculator.cs", "Subtract", new[] { "Test1", "Test2", "Test5" })
        };

        // Candidate B: Different files, but perfect test overlap  
        var candidateB = new List<IMutant>
        {
            CreateMutant(3, "Calculator.cs", "Add", new[] { "IntegrationTest1", "IntegrationTest2" }),
            CreateMutant(4, "StringUtils.cs", "Format", new[] { "IntegrationTest1", "IntegrationTest2" })
        };

        // Candidate C: Same file, no test overlap
        var candidateC = new List<IMutant>
        {
            CreateMutant(5, "Calculator.cs", "Add", new[] { "Test1", "Test2" }),
            CreateMutant(6, "Calculator.cs", "Multiply", new[] { "Test3", "Test4" })
        };

        // Act
        var scoreA = overlappingTestsHeuristic.ScoreCandidate(candidateA);
        var scoreB = overlappingTestsHeuristic.ScoreCandidate(candidateB);
        var scoreC = overlappingTestsHeuristic.ScoreCandidate(candidateC);

        // Assert - OverlappingTestsHeuristic should prioritize test overlap over file location
        scoreB.ShouldBeGreaterThan(scoreA, "Perfect test overlap should beat partial overlap, even across files");
        scoreA.ShouldBeGreaterThan(scoreC, "Some overlap should beat no overlap");
        scoreC.ShouldBe(0.0, "No overlap should result in zero score");

        Console.WriteLine($"Same file, good overlap: {scoreA:F3}");
        Console.WriteLine($"Different files, perfect overlap: {scoreB:F3}");
        Console.WriteLine($"Same file, no overlap: {scoreC:F3}");
    }

    [TestMethod]
    public void OverlappingTestsHeuristic_WithProgressiveOverlapReduction_ShouldShowDiminishingScores()
    {
        // Arrange - Test how scores change as overlap decreases
        var heuristic = new OverlappingTestsHeuristic();
        
        var testScenarios = new[]
        {
            new { Name = "Perfect Overlap", Tests1 = new[] { "T1", "T2", "T3" }, Tests2 = new[] { "T1", "T2", "T3" } },
            new { Name = "High Overlap", Tests1 = new[] { "T1", "T2", "T3", "T4" }, Tests2 = new[] { "T1", "T2", "T3", "T5" } },
            new { Name = "Medium Overlap", Tests1 = new[] { "T1", "T2", "T3", "T4" }, Tests2 = new[] { "T1", "T2", "T5", "T6" } },
            new { Name = "Low Overlap", Tests1 = new[] { "T1", "T2", "T3", "T4" }, Tests2 = new[] { "T1", "T5", "T6", "T7" } },
            new { Name = "Minimal Overlap", Tests1 = new[] { "T1", "T2", "T3", "T4" }, Tests2 = new[] { "T1", "T5", "T6", "T7", "T8" } },
            new { Name = "No Overlap", Tests1 = new[] { "T1", "T2", "T3" }, Tests2 = new[] { "T4", "T5", "T6" } }
        };

        // Act & Assert
        var previousScore = 1.0;
        foreach (var scenario in testScenarios)
        {
            var candidate = new List<IMutant>
            {
                CreateMutant(1, "Test.cs", "Method1", scenario.Tests1),
                CreateMutant(2, "Test.cs", "Method2", scenario.Tests2)
            };

            var score = heuristic.ScoreCandidate(candidate);
            
            // Score should decrease as overlap decreases (except for the first iteration)
            if (previousScore < 1.0)
            {
                score.ShouldBeLessThanOrEqualTo(previousScore, $"{scenario.Name} should have score <= previous scenario");
            }
            
            previousScore = score;
            Console.WriteLine($"{scenario.Name}: Score = {score:F3}, Analysis = {heuristic.GetOverlapAnalysis(candidate)}");
        }

        // Final scenario should be 0
        previousScore.ShouldBe(0.0, "No overlap should result in zero score");
    }

    #region Helper Methods

    private static Mutant CreateMutant(int id, string filePath, string methodName, string[] coveringTests)
    {
        return new Mutant
        {
            Id = id,
            ResultStatus = MutantStatus.Pending,
            CoveringTests = new TestIdentifierList(coveringTests),
            AssessingTests = new TestIdentifierList(coveringTests), // Assume same for simplicity
            KillingTests = TestIdentifierList.NoTest(),
            Mutation = new Mutation
            {
                DisplayName = $"{methodName} Mutation",
                Type = Mutator.Arithmetic,
                Description = $"Arithmetic mutation in {filePath}::{methodName}",
                // Note: In a real scenario, OriginalNode would contain the actual syntax tree
                // For testing purposes, we can leave it null as the heuristic only uses CoveringTests
            }
        };
    }

    #endregion
}
