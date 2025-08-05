using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.ProjectComponents;
using Stryker.Abstractions.Testing;
using Stryker.Core.Initialisation;
using Stryker.Core.Mutants;
using Stryker.Core.Mutators;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest;
using Stryker.Core.ProjectComponents.SourceProjects;
using System.Diagnostics;
using Stryker.TestRunner.Tests;
using Stryker.Core.InjectedHelpers;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;

namespace Stryker.Core.UnitTest.MutationTest
{
    /// <summary>
    /// Integration test for the complete Higher Order Mutation Testing flow with real source code.
    /// This test validates performance and functionality without mocking core components.
    /// </summary>
    [TestClass]
    public class HigherOrderMutationIntegrationTest : TestBase
    {
        private const string CalculatorSourceCode = @"
using System;

namespace TestProject
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b; // Arithmetic mutation opportunity
        }

        public int Subtract(int a, int b)
        {
            return a - b; // Arithmetic mutation opportunity
        }

        public int Multiply(int a, int b)
        {
            return a * b; // Arithmetic mutation opportunity
        }

        public int Divide(int a, int b)
        {
            if (b == 0) // Equality mutation opportunity
            {
                throw new ArgumentException(""Division by zero"");
            }
            return a / b; // Arithmetic mutation opportunity
        }

        public bool IsPositive(int value)
        {
            return value > 0; // Equality mutation opportunity
        }

        public bool IsEven(int value)
        {
            return value % 2 == 0; // Arithmetic & Equality mutation opportunities
        }

        public string GetGrade(int score)
        {
            if (score >= 90) // Equality mutation opportunity
            {
                return ""A""; // String mutation opportunity
            }
            else if (score >= 80) // Equality mutation opportunity
            {
                return ""B""; // String mutation opportunity
            }
            else if (score >= 70) // Equality mutation opportunity
            {
                return ""C""; // String mutation opportunity
            }
            return ""F""; // String mutation opportunity
        }
    }
}";

        private const string MathHelperSourceCode = @"
using System;

namespace TestProject
{
    public static class MathHelper
    {
        public static double Square(double value)
        {
            return value * value; // Arithmetic mutation opportunity
        }

        public static double Cube(double value)
        {
            return value * value * value; // Multiple arithmetic mutation opportunities
        }

        public static bool IsPrime(int number)
        {
            if (number <= 1) // Equality mutation opportunity
            {
                return false; // Boolean mutation opportunity
            }

            for (int i = 2; i <= Math.Sqrt(number); i++) // Equality mutation opportunity
            {
                if (number % i == 0) // Arithmetic & Equality mutation opportunities
                {
                    return false; // Boolean mutation opportunity
                }
            }
            return true; // Boolean mutation opportunity
        }

        public static int Factorial(int n)
        {
            if (n <= 1) // Equality mutation opportunity
            {
                return 1;
            }
            return n * Factorial(n - 1); // Arithmetic mutation opportunities
        }
    }
}";

        private const string StringUtilsSourceCode = @"
using System;

namespace TestProject
{
    public static class StringUtils
    {
        public static string Reverse(string input)
        {
            if (string.IsNullOrEmpty(input)) // String method mutation opportunity
            {
                return input;
            }

            char[] chars = input.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }

        public static bool IsPalindrome(string input)
        {
            if (string.IsNullOrEmpty(input)) // String method mutation opportunity
            {
                return true; // Boolean mutation opportunity
            }

            string normalized = input.ToLower(); // String method mutation opportunity
            return normalized == Reverse(normalized); // Equality mutation opportunity
        }

        public static string Capitalize(string input)
        {
            if (string.IsNullOrEmpty(input)) // String method mutation opportunity
            {
                return input;
            }

            return input.Substring(0, 1).ToUpper() + input.Substring(1).ToLower(); // String method mutations
        }
    }
}";

        [TestMethod]
        [Timeout(30000)] // 30 second timeout for performance validation
        public void HigherOrderMutationFlow_WithRealSourceCode_ShouldGenerateHOMsAndProvidePerformanceMetrics()
        {
            // Arrange - Create real source files and syntax trees
            var sourceFiles = new Dictionary<string, string>
            {
                { "Calculator.cs", CalculatorSourceCode },
                { "MathHelper.cs", MathHelperSourceCode },
                { "StringUtils.cs", StringUtilsSourceCode }
            };

            var stopwatch = Stopwatch.StartNew();
            var performanceMetrics = new PerformanceMetrics();

            // Create syntax trees from actual source code
            var syntaxTrees = sourceFiles.Select(sf => 
                CSharpSyntaxTree.ParseText(sf.Value, path: sf.Key)).ToList();

            // Create compilation for semantic analysis
            var compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Math).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Array).Assembly.Location))
                .AddSyntaxTrees(syntaxTrees);

            performanceMetrics.SetupTime = stopwatch.Elapsed;
            stopwatch.Restart();

            // Generate actual mutants using Stryker's mutation process
            var options = CreateStrykerOptionsWithHOMT();
            var mutants = GenerateActualMutants(syntaxTrees, compilation, options);

            performanceMetrics.MutantGenerationTime = stopwatch.Elapsed;
            performanceMetrics.TotalMutantsGenerated = mutants.Count;
            stopwatch.Restart();

            // Create MutationTestInput with real components
            var input = CreateRealMutationTestInput(syntaxTrees, compilation);

            // Test the Higher Order Mutation flow
            var higherOrderMutation = new HigherOrderMutation(options, input, mutants);

            // Add algorithm for HOM generation
            var algorithm = new LocalSearchAlgorithm(input, null, options, mutants);

            higherOrderMutation.AddSearchAlgorithm(algorithm);


            // Use the actual centralized method from MutationTestProcess
            var result = higherOrderMutation.BuildAndOptimizeHigherOrderMutants(
                mutants, 
                isPreTestRun: true, 
                includeAllIndividualMutants: true
            );

            performanceMetrics.HOMGenerationTime = stopwatch.Elapsed;
            stopwatch.Stop();

            // Act & Assert - Comprehensive validation
            ValidateHOMGenerationResults(result, mutants, performanceMetrics);
            ValidateSSHOMCapabilities(higherOrderMutation, mutants);
            ValidatePerformanceMetrics(performanceMetrics);

            // Log performance results for analysis
            LogPerformanceResults(performanceMetrics, result);
        }

        [TestMethod]
        [Timeout(15000)] // 15 second timeout
        public void MutationTestProcess_WithHOMTEnabled_ShouldIntegrateSeamlessly()
        {
            // Arrange - Test the actual MutationTestProcess integration
            var sourceFiles = new Dictionary<string, string>
            {
                { "Calculator.cs", CalculatorSourceCode }
            };

            var syntaxTrees = sourceFiles.Select(sf => 
                CSharpSyntaxTree.ParseText(sf.Value, path: sf.Key)).ToList();

            var compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(syntaxTrees);

            var options = CreateStrykerOptionsWithHOMT();
            var mutants = GenerateActualMutants(syntaxTrees, compilation, options);
            var input = CreateRealMutationTestInput(syntaxTrees, compilation);

            // Create HigherOrderMutation instance directly
            var higherOrderMutation = new HigherOrderMutation(options, input, mutants);

            input.HigherOrderMutation = higherOrderMutation;

            // Add algorithm for HOM generation
            var algorithm = new LocalSearchAlgorithm(input, null, options, mutants);
            higherOrderMutation.AddSearchAlgorithm(algorithm);

            // Act - Use the public BuildAndOptimizeHigherOrderMutants method
            var stopwatch = Stopwatch.StartNew();
            var result = higherOrderMutation.BuildAndOptimizeHigherOrderMutants(
                mutants.Take(10).ToList(),
                isPreTestRun: true,
                includeAllIndividualMutants: true
            );
            stopwatch.Stop();

            // Assert
            result.ShouldNotBeNull("Should return HOM generation result");
            result.MutantGroups.ShouldNotBeEmpty("Should generate mutant groups");
            
            var homGroups = result.MutantGroups.OfType<HigherOrderMutant>().ToList();
            homGroups.ShouldNotBeEmpty("Should contain Higher Order Mutants");
            
            // Validate that HOMs are properly structured
            foreach (var hom in homGroups)
            {
                hom.Order.ShouldBeGreaterThanOrEqualTo(2, "All HOMs should be order 2 or higher");
                hom.ConstituentMutants.ShouldNotBeEmpty("HOMs should have constituent mutants");
                hom.ConstituentMutants.All(m => m != null).ShouldBeTrue("All constituent mutants should be valid");
            }

            // Verify input.HigherOrderMutation was set
            input.HigherOrderMutation.ShouldNotBeNull("HigherOrderMutation should be set on input");

            // Validate result metadata
            result.AlgorithmUsed.ShouldNotBeNullOrEmpty("Should specify algorithm used");
            result.GenerationTime.ShouldBeGreaterThan(TimeSpan.Zero, "Should track generation time");
            result.MutantsIncludedInHOMs.ShouldBeGreaterThan(0, "Should include some mutants in HOMs");

            stopwatch.ElapsedMilliseconds.ShouldBeLessThan(10000, "Should complete within 10 seconds");
        }

        [TestMethod]
        public void HigherOrderMutation_PerformanceComparison_ShouldShowScalability()
        {
            // Arrange - Test performance scaling with different mutant set sizes
            var testSizes = new[] { 10, 25, 50 };
            var performanceResults = new Dictionary<int, TimeSpan>();

            foreach (var size in testSizes)
            {
                var mutants = GenerateTestMutants(size);
                var options = CreateStrykerOptionsWithHOMT();
                var input = CreateMinimalMutationTestInput();

                var homt = new HigherOrderMutation(options, input, mutants);

                // Measure time for HOM generation
                var stopwatch = Stopwatch.StartNew();
                var result = homt.BuildAndOptimizeHigherOrderMutants(mutants);
                stopwatch.Stop();

                performanceResults[size] = stopwatch.Elapsed;

                // Basic validation
                result.MutantGroups.ShouldNotBeEmpty($"Should generate groups for {size} mutants");
            }

            // Assert - Performance should scale reasonably (not exponentially)
            performanceResults[10].TotalMilliseconds.ShouldBeLessThan(2000, "10 mutants should complete quickly");
            performanceResults[25].TotalMilliseconds.ShouldBeLessThan(5000, "25 mutants should complete reasonably");
            performanceResults[50].TotalMilliseconds.ShouldBeLessThan(12000, "50 mutants should complete within acceptable time");

            // Performance scaling validation
            var ratioSmallToMedium = performanceResults[25].TotalMilliseconds / performanceResults[10].TotalMilliseconds;
            var ratioMediumToLarge = performanceResults[50].TotalMilliseconds / performanceResults[25].TotalMilliseconds;

            ratioSmallToMedium.ShouldBeLessThan(8, "Performance should not degrade dramatically");
            ratioMediumToLarge.ShouldBeLessThan(5, "Performance should remain manageable");
        }

        #region Helper Methods

        private List<IMutant> GenerateActualMutants(List<SyntaxTree> syntaxTrees, Compilation compilation, IStrykerOptions options)
        {
            var mutants = new List<IMutant>();
            var mutators = new IMutator[]
            {
                new MathMutator(),
                new BooleanMutator(),
                new StringMutator(),
                new StringMethodMutator()
            };

            var orchestrator = new CsharpMutantOrchestrator(new MutantPlacer(new CodeInjection()), mutators, options);

            foreach (var syntaxTree in syntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var mutatedTree = orchestrator.Mutate(syntaxTree, semanticModel);
                var newMutants = orchestrator.GetLatestMutantBatch();
                mutants.AddRange(newMutants);
            }

            return mutants;
        }

        private List<IMutant> GenerateTestMutants(int count)
        {
            var mutants = new List<IMutant>();
            var testNames = new[] { "Test1", "Test2", "Test3", "Test4", "Test5" };
            var files = new[] { "Calculator.cs", "MathHelper.cs", "StringUtils.cs" };

            for (int i = 1; i <= count; i++)
            {
                var killingTests = testNames.Take((i % testNames.Length) + 1).ToArray();
                var file = files[i % files.Length];
                mutants.Add(TestHelper.CreateMutant(file, i, MutantStatus.Pending, killingTests));
            }

            return mutants;
        }

        private StrykerOptions CreateStrykerOptionsWithHOMT()
        {
            var mutantIdProvider = new Mock<IProvideId>();
            var random = new Random();
            mutantIdProvider.Setup(i => i.NextId()).Returns(() => random.Next(1, 10000));

            return new StrykerOptions
            {
                OptimizationMode = OptimizationModes.EnableHigherOrderMutants | OptimizationModes.CoverageBasedTest,
                Concurrency = 1,
                MutationLevel = MutationLevel.Complete,
                MutantIdProvider = mutantIdProvider.Object
            };
        }

        private MutationTestInput CreateRealMutationTestInput(List<SyntaxTree> syntaxTrees, Compilation compilation)
        {
            // Create a more realistic MutationTestInput
            var sourceProjectInfoMock = new Mock<SourceProjectInfo>();
            var analyzerResult = TestHelper.SetupProjectAnalyzerResult(
                properties: new Dictionary<string, string>
                {
                    { "AssemblyName", "TestProject" },
                    { "TargetDir", Path.Combine(Directory.GetCurrentDirectory(), "bin", "Debug", "net8.0") },
                    { "TargetFileName", "TestProject.dll" },
                    { "Language", "C#" },
                    { "RootNamespace", "TestProject" }
                },
                projectFilePath: Path.Combine(Directory.GetCurrentDirectory(), "TestProject.csproj"),
                sourceFiles: syntaxTrees.Select(st => st.FilePath).ToArray()).Object;

            sourceProjectInfoMock.SetupGet(x => x.AnalyzerResult).Returns(analyzerResult);

            var testProjectsInfo = Mock.Of<ITestProjectsInfo>();
            var testRunResultMock = new Mock<ITestRunResult>();
            
            // Setup realistic test execution results
            testRunResultMock.Setup(r => r.ExecutedTests).Returns(new TestIdentifierList(new[] { "Test1", "Test2", "Test3", "Test4", "Test5" }));
            testRunResultMock.Setup(r => r.FailingTests).Returns(new TestIdentifierList(new string[0]));
            testRunResultMock.Setup(r => r.TimedOutTests).Returns(new TestIdentifierList(new string[0]));

            var timeoutValueCalculator = Mock.Of<ITimeoutValueCalculator>();
            var initialTestRun = new InitialTestRun(testRunResultMock.Object, timeoutValueCalculator);

            return new MutationTestInput
            {
                SourceProjectInfo = sourceProjectInfoMock.Object,
                TestProjectsInfo = testProjectsInfo,
                InitialTestRun = initialTestRun
            };
        }

        private MutationTestInput CreateMinimalMutationTestInput()
        {
            var sourceProjectInfoMock = new Mock<SourceProjectInfo>();
            var analyzerResult = TestHelper.SetupProjectAnalyzerResult(
                properties: new Dictionary<string, string>
                {
                    { "AssemblyName", "TestAssembly" },
                    { "TargetDir", "/bin/Debug/net8.0" },
                    { "TargetFileName", "TestAssembly.dll" },
                    { "Language", "C#" },
                    { "RootNamespace", "TestAssembly" }
                },
                projectFilePath: "TestProject.csproj").Object;

            sourceProjectInfoMock.SetupGet(x => x.AnalyzerResult).Returns(analyzerResult);

            var testProjectsInfo = Mock.Of<ITestProjectsInfo>();
            var testRunResult = Mock.Of<ITestRunResult>();
            var timeoutValueCalculator = Mock.Of<ITimeoutValueCalculator>();
            var initialTestRun = new InitialTestRun(testRunResult, timeoutValueCalculator);

            return new MutationTestInput
            {
                SourceProjectInfo = sourceProjectInfoMock.Object,
                TestProjectsInfo = testProjectsInfo,
                InitialTestRun = initialTestRun
            };
        }

        private static void ValidateHOMGenerationResults(HOMGenerationResult result, List<IMutant> originalMutants, PerformanceMetrics metrics)
        {
            // Basic result validation
            result.ShouldNotBeNull("HOM generation should return a result");
            result.MutantGroups.ShouldNotBeEmpty("Should generate mutant groups");
            
            // Metadata validation
            result.AlgorithmUsed.ShouldNotBeNullOrEmpty("Should specify which algorithm was used");
            result.GenerationTime.ShouldBeGreaterThan(TimeSpan.Zero, "Should track generation time");
            result.MutantsIncludedInHOMs.ShouldBeGreaterThan(0, "Should cover some mutants with HOMs");

            // HOM validation
            var homs = result.MutantGroups.OfType<HigherOrderMutant>().ToList();
            homs.ShouldNotBeEmpty("Should contain Higher Order Mutants");

            foreach (var hom in homs)
            {
                hom.Order.ShouldBeGreaterThanOrEqualTo(2, "All HOMs should be order 2 or higher");
                hom.ConstituentMutants.ShouldNotBeEmpty("HOMs should have constituent mutants");
                hom.ConstituentMutants.Count.ShouldBe(hom.Order, "HOM order should match constituent count");
                
                // Validate that all constituent mutants are from the original set
                foreach (var constituent in hom.ConstituentMutants)
                {
                    originalMutants.ShouldContain(constituent, "All constituent mutants should be from original set");
                }
            }

            metrics.TotalHOMsGenerated = homs.Count;
            metrics.MutantsCoveredByHOMs = result.MutantsIncludedInHOMs;
        }

        private static void ValidateSSHOMCapabilities(HigherOrderMutation homt, List<IMutant> mutants)
        {
            // Test SSHOM detection with realistic scenarios
            if (mutants.Count >= 2)
            {
                var testMutants = mutants.Take(2).ToList();
                
                // Create test scenarios for SSHOM validation
                var subsetTests = new TestIdentifierList(new[] { "Test1" });
                var equalTests = new TestIdentifierList(new[] { "Test1", "Test2" });
                var supersetTests = new TestIdentifierList(new[] { "Test1", "Test2", "Test3" });

                // Basic SSHOM validation should work without throwing
                Should.NotThrow(() => homt.IsSSHOM(testMutants, subsetTests));
                Should.NotThrow(() => homt.IsSSHOM(testMutants, equalTests, properSubset: false));
                Should.NotThrow(() => homt.IsSSHOM(testMutants, equalTests, properSubset: true));
            }
        }

        private void ValidatePerformanceMetrics(PerformanceMetrics metrics)
        {
            // Performance assertions
            metrics.SetupTime.TotalMilliseconds.ShouldBeLessThan(5000, "Setup should complete quickly");
            metrics.MutantGenerationTime.TotalMilliseconds.ShouldBeLessThan(10000, "Mutant generation should be reasonably fast");
            metrics.HOMGenerationTime.TotalMilliseconds.ShouldBeLessThan(15000, "HOM generation should complete in reasonable time");
            
            metrics.TotalMutantsGenerated.ShouldBeGreaterThan(10, "Should generate a meaningful number of mutants");
            metrics.TotalHOMsGenerated.ShouldBeGreaterThan(0, "Should generate some HOMs");
            metrics.MutantsCoveredByHOMs.ShouldBeGreaterThan(0, "HOMs should cover some mutants");
        }

        private void LogPerformanceResults(PerformanceMetrics metrics, HOMGenerationResult result)
        {
            Console.WriteLine("=== Higher Order Mutation Testing Performance Results ===");
            Console.WriteLine($"Setup Time: {metrics.SetupTime.TotalMilliseconds:F2} ms");
            Console.WriteLine($"Mutant Generation Time: {metrics.MutantGenerationTime.TotalMilliseconds:F2} ms");
            Console.WriteLine($"HOM Generation Time: {metrics.HOMGenerationTime.TotalMilliseconds:F2} ms");
            Console.WriteLine($"Total Mutants Generated: {metrics.TotalMutantsGenerated}");
            Console.WriteLine($"Total HOMs Generated: {metrics.TotalHOMsGenerated}");
            Console.WriteLine($"Mutants Covered by HOMs: {metrics.MutantsCoveredByHOMs}/{metrics.TotalMutantsGenerated} ({(double)metrics.MutantsCoveredByHOMs / metrics.TotalMutantsGenerated * 100:F1}%)");
            Console.WriteLine($"Algorithm Used: {result.AlgorithmUsed}");
            Console.WriteLine($"Heuristics Count: {result.HeuristicsUsed}");
            Console.WriteLine("========================================================");
        }

        #endregion

        #region Supporting Classes

        private class PerformanceMetrics
        {
            public TimeSpan SetupTime { get; set; }
            public TimeSpan MutantGenerationTime { get; set; }
            public TimeSpan HOMGenerationTime { get; set; }
            public int TotalMutantsGenerated { get; set; }
            public int TotalHOMsGenerated { get; set; }
            public int MutantsCoveredByHOMs { get; set; }
        }

        #endregion
    }
}
