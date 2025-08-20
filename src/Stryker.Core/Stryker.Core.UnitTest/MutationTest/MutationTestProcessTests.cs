using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Exceptions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Reporting;
using Stryker.Core.Initialisation;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.ProjectComponents.Csharp;
using Stryker.Core.ProjectComponents.SourceProjects;
using Stryker.Core.ProjectComponents.TestProjects;

namespace Stryker.Core.UnitTest.MutationTest;

[TestClass]
public class MutationTestProcessTests : TestBase
{
    private string CurrentDirectory { get; }
    private string FilesystemRoot { get; }
    private string SourceFile { get; }
    private MockFileSystem fileSystemMock { get; } = new MockFileSystem();
    private CsharpFolderComposite _folder { get; } = new CsharpFolderComposite();
    private FullRunScenario _testScenario { get; } = new FullRunScenario();

    private MutationTestInput _input { get; }

    public MutationTestProcessTests()
    {
        CurrentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        FilesystemRoot = Path.GetPathRoot(CurrentDirectory);
        SourceFile = File.ReadAllText(CurrentDirectory + "/TestResources/ExampleSourceFile.cs");
        _input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo()
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "/bin/Debug/netcoreapp2.1" },
                        { "TargetFileName", "ProjectUnderTest.dll" },
                        { "Language", "C#" }
                    }).Object,
                ProjectContents = _folder
            },
            TestProjectsInfo = new TestProjectsInfo(fileSystemMock)
            {
                TestProjects = new List<TestProject> {
                    new TestProject(fileSystemMock, TestHelper.SetupProjectAnalyzerResult(properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", Path.Combine(FilesystemRoot, "TestProject", "bin", "Debug", "netcoreapp2.0") },
                        { "TargetFileName", "TestName.dll" },
                        { "Language", "C#" }
                    }).Object)
                }
            },
        };
    }

    [TestMethod]
    public void ShouldCallMutantionProcess_MutateAndFilterMutants()
    {
        // Arrange
        var options = new StrykerOptions()
        {
            ExcludedMutations = new Mutator[] { }
        };
        var mutationTestExecutorMock = new Mock<IMutationTestExecutor>(MockBehavior.Strict);
        var mutantionProcessMock = new Mock<IMutationProcess>(MockBehavior.Strict);
        mutantionProcessMock.Setup(x => x.Mutate(It.IsAny<MutationTestInput>()));
        mutantionProcessMock.Setup(x => x.FilterMutants(It.IsAny<MutationTestInput>()));

        var target = new MutationTestProcess(_input, options, null, mutationTestExecutorMock.Object, mutantionProcessMock.Object);

        // Act
        target.Mutate();

        target.FilterMutants();

        // Assert
        mutantionProcessMock.Verify(x => x.Mutate(It.IsAny<MutationTestInput>()), Times.Once);
        mutantionProcessMock.Verify(x => x.FilterMutants(It.IsAny<MutationTestInput>()), Times.Once);
    }

    [TestMethod]
    public void ShouldCallExecutorForEveryCoveredMutant()
    {
        _testScenario.CreateMutants(1, 2);
        // we need at least one test
        _testScenario.CreateTest(1);
        // and we need to declare that the mutant is covered
        _testScenario.DeclareCoverageForMutant(1);
        var basePath = Path.Combine(FilesystemRoot, "ExampleProject.Test");

        _folder.Add(new CsharpFileLeaf()
        {
            SourceCode = SourceFile,
            Mutants = _testScenario.GetMutants()
        });

        var mutationExecutor = new MutationTestExecutor(_testScenario.GetTestRunnerMock().Object);

        var options = new StrykerOptions()
        {
            ProjectPath = basePath,
            Concurrency = 1,
            OptimizationMode = OptimizationModes.CoverageBasedTest
        };
        _input.InitialTestRun = new InitialTestRun(_testScenario.GetInitialRunResult(), new TimeoutValueCalculator(500));

        var target = new MutationTestProcess(_input, options, null, mutationExecutor);

        target.GetCoverage();
        target.Test(_testScenario.GetCoveredMutants());

        _testScenario.GetMutantStatus(1).ShouldBe(MutantStatus.Survived);
        _testScenario.GetMutantStatus(2).ShouldBe(MutantStatus.NoCoverage);
    }

    [TestMethod]
    public void ShouldCallExecutorForEveryMutantWhenNoOptimization()
    {
        var scenario = new FullRunScenario();
        _testScenario.CreateMutants(1, 2);
        // we need at least one test
        _testScenario.CreateTest(1);
        // and we need to declare that the mutant is covered
        _testScenario.DeclareCoverageForMutant(1);
        _testScenario.SetMode(OptimizationModes.None);
        var basePath = Path.Combine(FilesystemRoot, "ExampleProject.Test");

        _folder.Add(new CsharpFileLeaf()
        {
            SourceCode = SourceFile,
            Mutants = _testScenario.GetMutants()
        });

        var reporterMock = new Mock<IReporter>(MockBehavior.Strict);
        reporterMock.Setup(x => x.OnMutantTested(It.IsAny<IMutant>()));

        var mutationExecutor = new MutationTestExecutor(_testScenario.GetTestRunnerMock().Object);

        var options = new StrykerOptions()
        {
            OutputPath = basePath,
            Concurrency = 1,
            OptimizationMode = OptimizationModes.None
        };
        _input.InitialTestRun = new InitialTestRun(_testScenario.GetInitialRunResult(), new TimeoutValueCalculator(500));

        var target = new MutationTestProcess(_input, options, null, mutationExecutor);

        target.GetCoverage();
        target.Test(_testScenario.GetMutants());

        _testScenario.GetMutantStatus(1).ShouldBe(MutantStatus.Survived);
        _testScenario.GetMutantStatus(2).ShouldBe(MutantStatus.Survived);
    }

    [TestMethod]
    public void ShouldHandleCoverage()
    {
        var basePath = Path.Combine(FilesystemRoot, "ExampleProject.Test");
        _testScenario.CreateMutants(1, 2, 3);

        _folder.Add(new CsharpFileLeaf()
        {
            SourceCode = SourceFile,
            Mutants = _testScenario.GetMutants()
        });
        _testScenario.CreateTests(1, 2);

        // mutant 1 is covered by both tests
        _testScenario.DeclareCoverageForMutant(1);
        // mutant 2 is covered only by test 1
        _testScenario.DeclareCoverageForMutant(2, 1);
        // mutant 3 as no coverage
        // test 1 succeeds, test 2 fails
        _testScenario.DeclareTestsFailingWhenTestingMutant(1, 2);
        var runnerMock = _testScenario.GetTestRunnerMock();

        // setup coverage
        var executor = new MutationTestExecutor(runnerMock.Object);

        var options = new StrykerOptions
        {
            ProjectPath = basePath,
            Concurrency = 1,
            OptimizationMode = OptimizationModes.CoverageBasedTest
        };
        _input.InitialTestRun = new InitialTestRun(_testScenario.GetInitialRunResult(), new TimeoutValueCalculator(500));

        var target = new MutationTestProcess(_input, options, null, executor);
        // test mutants
        target.GetCoverage();

        target.Test(_input.SourceProjectInfo.ProjectContents.Mutants.Where(m => m.ResultStatus == MutantStatus.Pending));
        // first mutant should be killed by test 2
        _testScenario.GetMutantStatus(1).ShouldBe(MutantStatus.Killed);
        // other mutant survives
        _testScenario.GetMutantStatus(2).ShouldBe(MutantStatus.Survived);
        // third mutant appears as no coverage
        _testScenario.GetMutantStatus(3).ShouldBe(MutantStatus.NoCoverage);
    }

    [TestMethod]
    public void ShouldNotKillMutantIfOnlyKilledByFailingTest()
    {
        var basePath = Path.Combine(FilesystemRoot, "ExampleProject.Test");
        _testScenario.CreateMutants(1);

        _folder.Add(new CsharpFileLeaf()
        {
            SourceCode = SourceFile,
            Mutants = _testScenario.GetMutants()
        });
        _testScenario.CreateTests(1, 2, 3);

        // mutant 1 is covered by all tests
        _testScenario.DeclareCoverageForMutant(1);
        // mutant 2 is covered only by test 1
        _testScenario.DeclareTestsFailingAtInit(1);
        // test 1 succeeds, test 2 fails
        _testScenario.DeclareTestsFailingWhenTestingMutant(1, 1);
        var runnerMock = _testScenario.GetTestRunnerMock();

        // setup coverage
        var executor = new MutationTestExecutor(runnerMock.Object);

        var options = new StrykerOptions
        {
            ProjectPath = basePath,
            Concurrency = 1,
            OptimizationMode = OptimizationModes.CoverageBasedTest
        };

        _input.InitialTestRun = new InitialTestRun(_testScenario.GetInitialRunResult(), new TimeoutValueCalculator(500));

        var target = new MutationTestProcess(_input, options, null, executor);

        // test mutants
        target.GetCoverage();

        target.Test(_input.SourceProjectInfo.ProjectContents.Mutants);
        // first mutant should be marked as survived
        _testScenario.GetMutantStatus(1).ShouldBe(MutantStatus.Survived);
    }

    [TestMethod]
    public void ShouldNotKillMutantIfOnlyCoveredByFailingTest()
    {
        var basePath = Path.Combine(FilesystemRoot, "ExampleProject.Test");
        _testScenario.CreateMutants(1, 2);

        _folder.Add(new CsharpFileLeaf()
        {
            SourceCode = SourceFile,
            Mutants = _testScenario.GetMutants()
        });
        _testScenario.CreateTests(1, 2, 3);

        // mutant 1 is covered by both tests
        _testScenario.DeclareCoverageForMutant(1, 1, 2, 3);
        // mutant 2 is covered only by test 1
        _testScenario.DeclareTestsFailingAtInit(1, 2, 3);
        // test 1 succeeds, test 2 fails
        _testScenario.DeclareTestsFailingWhenTestingMutant(1, 1, 2, 3);
        var runnerMock = _testScenario.GetTestRunnerMock();

        // setup coverage
        var executor = new MutationTestExecutor(runnerMock.Object);

        var options = new StrykerOptions
        {
            ProjectPath = basePath,
            Concurrency = 1,
            OptimizationMode = OptimizationModes.CoverageBasedTest
        };
        _input.InitialTestRun = new InitialTestRun(_testScenario.GetInitialRunResult(), new TimeoutValueCalculator(500));

        var target = new MutationTestProcess(_input, options, null, executor);
        // test mutants
        target.GetCoverage();

        // first mutant should be marked as survived without any test
        _testScenario.GetMutantStatus(1).ShouldBe(MutantStatus.Survived);
    }

    [TestMethod]
    public void ShouldKillMutantKilledByFailingTestAndNormalTest()
    {
        var basePath = Path.Combine(FilesystemRoot, "ExampleProject.Test");
        _testScenario.CreateMutants(1);

        _folder.Add(new CsharpFileLeaf()
        {
            SourceCode = SourceFile,
            Mutants = _testScenario.GetMutants()
        });
        _testScenario.CreateTests(1, 2, 3);

        // mutant 1 is covered by both tests
        _testScenario.DeclareCoverageForMutant(1);
        // mutant 2 is covered only by test 1
        _testScenario.DeclareTestsFailingAtInit(1);
        // test 1 succeeds, test 2 fails
        _testScenario.DeclareTestsFailingWhenTestingMutant(1, 1, 2);
        var runnerMock = _testScenario.GetTestRunnerMock();

        // setup coverage
        var executor = new MutationTestExecutor(runnerMock.Object);

        var options = new StrykerOptions
        {
            ProjectPath = basePath,
            Concurrency = 1,
            OptimizationMode = OptimizationModes.CoverageBasedTest
        };
        _input.InitialTestRun = new InitialTestRun(_testScenario.GetInitialRunResult(), new TimeoutValueCalculator(500));

        var target = new MutationTestProcess(_input, options, null, executor);

        // test mutants
        target.GetCoverage();

        target.Test(_input.SourceProjectInfo.ProjectContents.Mutants);
        // first mutant should be killed by test 2
        _testScenario.GetMutantStatus(1).ShouldBe(MutantStatus.Killed);
    }

    [TestMethod]
    [DataRow(MutantStatus.Ignored)]
    [DataRow(MutantStatus.CompileError)]
    public void ShouldThrowExceptionWhenOtherStatusThanNotRunIsPassed(MutantStatus status)
    {
        var mutants = new List<Mutant> { new Mutant { Id = 1, ResultStatus = status } };

        var mutationProcessMock = Mock.Of<IMutationProcess>();
        Should.Throw<GeneralStrykerException>(() => new MutationTestProcess(_input, null, null, null, mutationProcessMock).Test(mutants));
    }

    [TestMethod]
    public void ShouldNotTest_WhenThereAreNoMutations()
    {
        var reporter = Mock.Of<IReporter>();
        var mutationTestExecutor = Mock.Of<IMutationTestExecutor>();
        var mutationProcessMock = Mock.Of<IMutationProcess>();
        var result = new MutationTestProcess(_input, null, reporter, mutationTestExecutor, mutationProcessMock).Test(Enumerable.Empty<Mutant>());

        Mock.Get(reporter).VerifyNoOtherCalls();
        Mock.Get(mutationTestExecutor).VerifyNoOtherCalls();
        result.MutationScore.ShouldBe(double.NaN);
    }

    [TestMethod]
    public void ShouldGroupHigherOrderMutantsCorrectly()
    {
        // Arrange - Create regular mutants and set up coverage first
        _testScenario.CreateMutants(1, 2, 3, 4);
        _testScenario.CreateTests(1, 2, 3);
        
        var basePath = Path.Combine(FilesystemRoot, "ExampleProject.Test");
        
        // Set up coverage for regular mutants
        _testScenario.DeclareCoverageForMutant(1, 1);
        _testScenario.DeclareCoverageForMutant(2, 2);
        _testScenario.DeclareCoverageForMutant(3, 1, 2);
        _testScenario.DeclareCoverageForMutant(4, 3);


        // Create a placeholder mutant in the scenario for the HOM's ID
        _testScenario.CreateMutant(100);
        _testScenario.DeclareCoverageForMutant(100, 1, 2); // Union of constituent mutants' coverage
        _testScenario.DeclareTestsFailingWhenTestingMutant(100, 2); // Make test 2 fail to kill the HOM

        // Get the regular mutants and add them to folder first
        var regularMutants = _testScenario.GetMutants().ToList();
        _folder.Add(new CsharpFileLeaf()
        {
            SourceCode = SourceFile,
            Mutants = regularMutants
        });
        
        var mutationExecutor = new MutationTestExecutor(_testScenario.GetTestRunnerMock().Object);
        
        var options = new StrykerOptions()
        {
            ProjectPath = basePath,
            Concurrency = 1,
            OptimizationMode = OptimizationModes.CoverageBasedTest,
            MutantIdProvider = new Mock<IProvideId>().Object
        };
        _input.InitialTestRun = new InitialTestRun(_testScenario.GetInitialRunResult(), new TimeoutValueCalculator(500));
        
        var target = new MutationTestProcess(_input, options, null, mutationExecutor);
        
        // Get coverage first - this populates CoveringTests and AssessingTests for regular mutants
        target.GetCoverage();
        
        // NOW create the higher order mutant after coverage is populated
        var higherOrderMutant = new HigherOrderMutant(new List<IMutant> { regularMutants[1], regularMutants[2] })
        {
            Id = 100,
            ResultStatus = MutantStatus.Pending
        };


        // Make test 2 fail when testing the HOM, which should kill it
        _testScenario.DeclareTestsFailingWhenTestingMutant(100, 2); // 100 is the HOM's ID, 2 is the test ID

        // DEBUG: Verify the HOM has proper coverage from its constituents
        var expectedCoveringTests = regularMutants[1].CoveringTests.Merge(regularMutants[2].CoveringTests);
        var expectedAssessingTests = regularMutants[1].AssessingTests.Intersect(regularMutants[2].AssessingTests);
        
        higherOrderMutant.CoveringTests.GetIdentifiers().ShouldBe(expectedCoveringTests.GetIdentifiers());
        higherOrderMutant.AssessingTests.GetIdentifiers().ShouldBe(expectedAssessingTests.GetIdentifiers());
        
        // The HOM should have AssessingTests = test 2 (intersection of test 2 and tests 1,2)
        higherOrderMutant.AssessingTests.Count.ShouldBe(1, "HOM should have exactly one assessing test");
        higherOrderMutant.AssessingTests.IsEmpty.ShouldBeFalse("HOM should have assessing tests");
        
    
        // Create the mixed list of mutants (regular + higher order)  
        var allMutants = new List<IMutant> { regularMutants[0], regularMutants[1], regularMutants[2], regularMutants[3], higherOrderMutant };
        
        // Act - Test the mutants including the higher order mutant
        target.Test(allMutants);
        
        // Assert - Verify that both regular and higher order mutants are processed
        allMutants[0].ResultStatus.ShouldNotBe(MutantStatus.Pending, "Regular mutant 1 should be tested");
        allMutants[1].ResultStatus.ShouldNotBe(MutantStatus.Pending, "Regular mutant 2 should be tested");
        allMutants[2].ResultStatus.ShouldNotBe(MutantStatus.Pending, "Regular mutant 3 should be tested");
        allMutants[3].ResultStatus.ShouldNotBe(MutantStatus.Pending, "Regular mutant 4 should be tested");
        higherOrderMutant.ResultStatus.ShouldNotBe(MutantStatus.Pending, "Higher order mutant should be tested");
        higherOrderMutant.ResultStatus.ShouldNotBe(MutantStatus.NoCoverage, "Higher order mutant should have coverage from its constituents");
        higherOrderMutant.ResultStatus.ShouldBe(MutantStatus.Killed, "Higher order mutant should be killed");

    }
}
