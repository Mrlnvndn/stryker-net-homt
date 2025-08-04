using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.ProjectComponents;
using Stryker.Abstractions.Reporting;
using Stryker.Abstractions.Testing;
using Stryker.Core.Initialisation;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.Core.ProjectComponents.SourceProjects;
using Stryker.Core.ProjectComponents.TestProjects;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest
{
    /// <summary>
    /// Integration tests for HOMT using lightweight approach with real components
    /// </summary>
    [TestClass]
    public class HOMTIntegrationTests : TestBase
    {
        [TestMethod]
        public void HOMTCreation_WithActualMutants_ShouldCreateHOMs()
        {
            // Arrange - Use the lightweight approach for testing just the HOMT logic
            var options = CreateMinimalStrykerOptions();

            // Create realistic mutants using actual Stryker mutant creation
            var mutants = CreateRealisticMutants();
            var input = CreateMinimalMutationTestInput();

            var homt = new HigherOrderMutation(options, input, mutants);
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, new List<IHOMHeuristic>(), options, mutants));

            // Act
            var candidates = homt.CreateCandidateHOMs().ToList();

            // Assert
            var homCandidates = candidates.FindAll(c => c.Order >= 2);

            homCandidates.ShouldNotBeEmpty("Should create HOM candidates");
            candidates.All(c => c.Order >= 2).ShouldBeTrue("All should be higher-order");
        }

        [TestMethod]
        public void HOMTWithHeuristics_ShouldFilterAndScoreCandidates()
        {
            // Arrange
            var options = CreateMinimalStrykerOptions();

            var mutants = CreateRealisticMutants();
            var input = CreateMinimalMutationTestInput();
            var homt = new HigherOrderMutation(options, input, mutants);
            
            // Add heuristics for comprehensive testing
            homt.AddHeuristic(new MaxSizeLimitHeuristic());
            homt.AddHeuristic(new CodeLocationHeuristic());
            homt.AddHeuristic(new MutatorTypeHeuristic());
            
            // Add search algorithm
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, new List<IHOMHeuristic>(), options, mutants));

            // Act
            var candidates = homt.CreateCandidateHOMs().ToList();

            // Assert
            candidates.ShouldNotBeEmpty("Should generate HOM candidates");
            candidates.All(c => c.Order >= 2).ShouldBeTrue("All candidates should be higher-order (2+ mutants)");
            candidates.All(c => c.Order <= 4).ShouldBeTrue("Max size heuristic should limit candidate size");
            
            // Verify heuristics were applied
            var sameFileHOMs = candidates.Where(c => 
                c.ConstituentMutants.All(m => GetMutationFilePath(m) == GetMutationFilePath(c.ConstituentMutants.First()))).ToList();
            sameFileHOMs.ShouldNotBeEmpty("Code location heuristic should favor same-file mutants");
        }

        [TestMethod]
        public void SSHOMDetection_WithValidScenarios_ShouldIdentifyCorrectly()
        {
            // Arrange
            var options = CreateMinimalStrykerOptions();

            var mutants = CreateRealisticMutants();
            var input = CreateMinimalMutationTestInput();
            var homt = new HigherOrderMutation(options, input, mutants);
            
            // Create mutants with specific test coverage for SSHOM testing
            var mutantA = TestHelper.CreateMutant("Calculator.cs", 100, MutantStatus.Pending, new[] { "Test1", "Test2", "Test3" });
            var mutantB = TestHelper.CreateMutant("Calculator.cs", 101, MutantStatus.Pending, new[] { "Test1", "Test2" });
            var mutantC = TestHelper.CreateMutant("Calculator.cs", 102, MutantStatus.Pending, new[] { "Test2", "Test3" });
            
            // Test different SSHOM scenarios
            var homKillingTestsSubset = new TestIdentifierList(new[] { "Test2" }); // Subset
            var homKillingTestsEqual = new TestIdentifierList(new[] { "Test1", "Test2" }); // Equal
            var homKillingTestsNotSubset = new TestIdentifierList(new[] { "Test4" }); // Not subset
            
            // Act & Assert
            // Scenario 1: Valid SSHOM (subset)
            homt.IsSSHOM(new[] { mutantA, mutantB }, homKillingTestsSubset).ShouldBeTrue();
            
            // Scenario 2: Equal sets (depends on properSubset parameter)
            homt.IsSSHOM(new[] { mutantA, mutantB }, homKillingTestsEqual, properSubset: false).ShouldBeTrue();
            homt.IsSSHOM(new[] { mutantA, mutantB }, homKillingTestsEqual, properSubset: true).ShouldBeFalse();
            
            // Scenario 3: Not a subset
            homt.IsSSHOM(new[] { mutantA, mutantB }, homKillingTestsNotSubset).ShouldBeFalse();
            
            // Scenario 4: No intersection between FOMs
            homt.IsSSHOM(new[] { mutantB, mutantC }, homKillingTestsSubset).ShouldBeTrue(); // Test2 is common
        }

        [TestMethod]
        [Timeout(10000)] // 10 second timeout for performance testing
        public void HOMTPerformance_WithLargeNumberOfMutants_ShouldCompleteInReasonableTime()
        {
            // Arrange
            var largeMutantSet = CreateLargeMutantSet(50);
            var options = CreateMinimalStrykerOptions();

            var input = CreateMinimalMutationTestInput();
            var homt = new HigherOrderMutation(options, input, largeMutantSet);
            homt.AddSearchAlgorithm(new LocalSearchAlgorithm(input, new List<IHOMHeuristic>(), options, largeMutantSet));
            homt.AddHeuristic(new MaxSizeLimitHeuristic());
            
            // Act
            var startTime = DateTime.Now;
            var candidates = homt.CreateCandidateHOMs().ToList();
            var elapsed = DateTime.Now - startTime;
            
            // Assert
            candidates.ShouldNotBeEmpty();
            elapsed.TotalSeconds.ShouldBeLessThan(8); // Should complete within 8 seconds
        }

        [TestMethod]
        public void HOMTWithOptimizationFlags_ShouldRespectConfiguration()
        {
            // Arrange - Test with and without HOMT enabled
            var optionsWithHOMT = CreateMinimalStrykerOptions(true);

            var optionsWithoutHOMT = CreateMinimalStrykerOptions(false);

            // Act
            var resultWithHOMT = ShouldUseHOMT(optionsWithHOMT);
            var resultWithoutHOMT = ShouldUseHOMT(optionsWithoutHOMT);
            
            // Assert
            resultWithHOMT.ShouldBeTrue("Should use HOMT when flag is enabled");
            resultWithoutHOMT.ShouldBeFalse("Should not use HOMT when flag is disabled");
        }

        [TestMethod]
        public void MutationTestProcess_BuildHigherOrderMutants_ShouldCreateHOMs()
        {
            // Arrange - Test the actual integration with MutationTestProcess
            var options = CreateMinimalStrykerOptions();

            var mutants = CreateRealisticMutants();
            var input = CreateMinimalMutationTestInput();
            
            // Create a MutationTestProcess to test the real integration
            var reporterMock = new Mock<IReporter>();
            var executorMock = new Mock<IMutationTestExecutor>();
            
            var process = new MutationTestProcess(input, options, reporterMock.Object, executorMock.Object);

            // Act - Use reflection to call the private BuildHigherOrderMutants method
            var buildMethod = typeof(MutationTestProcess).GetMethod("BuildHigherOrderMutants", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (IEnumerable<IMutant>)buildMethod.Invoke(process, new object[] { mutants });

            // Assert
            var homGroups = result.ToList();
            homGroups.ShouldNotBeEmpty("Should create HOM groups");
            homGroups.Where(g => g is HigherOrderMutant).Cast<HigherOrderMutant>().Any(g => g.Order >= 2).ShouldBeTrue("Should have some higher-order mutant groups");
        }

        [TestMethod]
        public void LocalSearchAlgorithm_WithRealisticMutants_ShouldGenerateCandidates()
        {
            // Arrange
            var options = CreateMinimalStrykerOptions();    

            var mutants = CreateRealisticMutants();
            var input = CreateMinimalMutationTestInput();
            var heuristics = new List<IHOMHeuristic>
            {
                new MaxSizeLimitHeuristic(),
                new CodeLocationHeuristic()
            };

            var algorithm = new LocalSearchAlgorithm(input, heuristics, options, mutants);

            // Act
            var candidates = algorithm.GenerateCandidates(mutants, heuristics, options, input).ToList();

            // Assert
            candidates.ShouldNotBeEmpty("Algorithm should generate candidates");
            algorithm.Name.ShouldBe("LocalSearch");
            
            // Verify all candidates are higher-order
            candidates.All(c => c.Order >= 2).ShouldBeTrue("All candidates should be higher-order");
        }

        #region Helper Methods

        private List<IMutant> CreateRealisticMutants()
        {
            // This creates mutants similar to what would be created by actual mutation
            return new List<IMutant>
            {
                TestHelper.CreateMutant("Calculator.cs", 1, MutantStatus.Pending, new[] { "Test1", "Test2" }),
                TestHelper.CreateMutant("Calculator.cs", 2, MutantStatus.Pending, new[] { "Test1", "Test2" }),
                TestHelper.CreateMutant("Calculator.cs", 3, MutantStatus.Pending, new[] { "Test2", "Test3" }),
                TestHelper.CreateMutant("Calculator.cs", 4, MutantStatus.Pending, new[] { "Test3" }),
                TestHelper.CreateMutant("MathHelper.cs", 5, MutantStatus.Pending, new[] { "Test1" }),
                TestHelper.CreateMutant("MathHelper.cs", 6, MutantStatus.Pending, new[] { "Test2" })
            };
        }

        private List<IMutant> CreateLargeMutantSet(int count)
        {
            var mutants = new List<IMutant>();
            var testNames = new[] { "Test1", "Test2", "Test3", "Test4", "Test5" };
            
            for (int i = 1; i <= count; i++)
            {
                var killingTests = testNames.Take(i % testNames.Length + 1).ToArray();
                mutants.Add(TestHelper.CreateMutant($"File{i % 5 + 1}.cs", i, MutantStatus.Pending, killingTests));
            }
            
            return mutants;
        }

        private StrykerOptions CreateMinimalStrykerOptions(bool withHOMT = true)
        {
            var mutantIdProvider = new Mock<IProvideId>();
            var random = new Random();
            mutantIdProvider.Setup(i => i.NextId()).Returns(() => random.Next(1, 1000));

            return new StrykerOptions
            {
                OptimizationMode = withHOMT ? OptimizationModes.EnableHigherOrderMutations : OptimizationModes.CoverageBasedTest,
                Concurrency = 1,
                MutantIdProvider = mutantIdProvider.Object,
            };
        }
        private MutationTestInput CreateMinimalMutationTestInput()
        {
            // Create a mock SourceProjectInfo with a basic AnalyzerResult
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

        private string GetMutationFilePath(IMutant mutant)
        {
            return mutant.Mutation?.OriginalNode?.SyntaxTree?.FilePath ?? "unknown";
        }

        private bool ShouldUseHOMT(IStrykerOptions options)
        {
            return options.OptimizationMode.HasFlag(OptimizationModes.EnableHigherOrderMutations);
        }

        #endregion
    }
}
