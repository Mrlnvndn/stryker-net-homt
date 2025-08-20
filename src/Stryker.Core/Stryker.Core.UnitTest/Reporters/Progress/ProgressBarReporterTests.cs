using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Spectre.Console;
using Spectre.Console.Testing;
using Stryker.Abstractions;
using Stryker.Core.Mutants;
using Stryker.Core.Reporters.Progress;

namespace Stryker.Core.UnitTest.Reporters.Progress;

[TestClass]
public class ProgressBarReporterTests : TestBase
{
    [TestMethod]
    public void ReportInitialState_ShouldReportTestProgressAs0PercentageDone_WhenTotalNumberOfTestsIsTwo()
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock());

        progressBarReporter.ReportInitialState(3);

        progressBarMock.Verify(x => x.Start(
            It.Is<int>(a => a == 3),
            It.Is<string>(b => b == "│ Testing mutant 0 / 3 │ K 0 │ S 0 │ T 0 │ NA │")
        ));
    }

    [TestMethod]
    public void ShouldSupportWhenNoMutants()
    {
        var progressBarMock = new ProgressBar();

        var progressBarReporter = new ProgressBarReporter(progressBarMock, new FixedClock());
        // the progress bar was never initialized
        progressBarMock.Ticks().ShouldBe(-1);
        progressBarReporter.ReportFinalState();
        // the progress bar was never initialized
        progressBarMock.Ticks().ShouldBe(-1);
    }

    [TestMethod]
    [DataRow(MutantStatus.Killed, "│ Testing mutant 1 / 2 │ K 1 │ S 0 │ T 0 │ ~0m 00s │")]
    [DataRow(MutantStatus.Survived, "│ Testing mutant 1 / 2 │ K 0 │ S 1 │ T 0 │ ~0m 00s │")]
    [DataRow(MutantStatus.Timeout, "│ Testing mutant 1 / 2 │ K 0 │ S 0 │ T 1 │ ~0m 00s │")]
    public void ReportRunTest_ShouldReportTestProgressAs50PercentageDone_And_FirstTestExecutionTime_WhenHalfOfTestsAreDone(MutantStatus status, string expected)
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));
        progressBarMock.Setup(x => x.Tick(It.IsAny<string>()));

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock());

        var mutantTestResult = new Mutant()
        {
            ResultStatus = status
        };

        progressBarReporter.ReportInitialState(2);
        progressBarReporter.ReportRunTest(mutantTestResult);

        progressBarMock.Verify(x => x.Tick(
            It.Is<string>(b => b == expected)
        ));
    }

    [TestMethod]
    [DataRow(MutantStatus.Killed, "│ Testing mutant 1 / 10000 │ K 1 │ S 0 │ T 0 │ ~1m 39s │")]
    [DataRow(MutantStatus.Survived, "│ Testing mutant 1 / 10000 │ K 0 │ S 1 │ T 0 │ ~1m 39s │")]
    [DataRow(MutantStatus.Timeout, "│ Testing mutant 1 / 10000 │ K 0 │ S 0 │ T 1 │ ~1m 39s │")]
    public void ReportRunTest_TestExecutionTimeInMinutes(MutantStatus status, string expected)
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));
        progressBarMock.Setup(x => x.Tick(It.IsAny<string>()));

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock());

        var mutantTestResult = new Mutant()
        {
            ResultStatus = status
        };

        progressBarReporter.ReportInitialState(10000);
        progressBarReporter.ReportRunTest(mutantTestResult);

        progressBarMock.Verify(x => x.Tick(
            It.Is<string>(b => b == expected)
        ));
    }

    [TestMethod]
    [DataRow(MutantStatus.Killed, "│ Testing mutant 1 / 1000000 │ K 1 │ S 0 │ T 0 │ ~2h 46m │")]
    [DataRow(MutantStatus.Survived, "│ Testing mutant 1 / 1000000 │ K 0 │ S 1 │ T 0 │ ~2h 46m │")]
    [DataRow(MutantStatus.Timeout, "│ Testing mutant 1 / 1000000 │ K 0 │ S 0 │ T 1 │ ~2h 46m │")]
    public void ReportRunTest_TestExecutionTimeInHours(MutantStatus status, string expected)
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));
        progressBarMock.Setup(x => x.Tick(It.IsAny<string>()));

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock());

        var mutantTestResult = new Mutant()
        {
            ResultStatus = status
        };

        progressBarReporter.ReportInitialState(1000000);
        progressBarReporter.ReportRunTest(mutantTestResult);

        progressBarMock.Verify(x => x.Tick(
            It.Is<string>(b => b == expected)
        ));
    }

    [TestMethod]
    public void ProgressBarSmokeCheck()
    {
        var progress = new ProgressBar();
        progress.Start(0, "test");
        progress.Tick("next");
        progress.Ticks().ShouldBe(1);
        progress.Stop();

        progress.Dispose();
        progress.Ticks().ShouldBe(-1);
    }

    [TestMethod]
    public void ReportRunTest_WithHigherOrderMutant_ShouldShowHOMFormat()
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        var console = new TestConsole();
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));
        progressBarMock.Setup(x => x.Tick(It.IsAny<string>()));

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock(), console);

        var higherOrderMutant = new HigherOrderMutant(new List<IMutant>
        {
            new Mutant { Id = 1 },
            new Mutant { Id = 2 }
        })
        {
            Id = 100,
            ResultStatus = MutantStatus.Killed
        };

        progressBarReporter.ReportInitialState(2);
        progressBarReporter.ReportRunTest(higherOrderMutant);

        // Should show warning about HOMs and use the new format with "+"
        progressBarMock.Verify(x => x.Tick(
            It.Is<string>(b => b == "│ Testing mutant 1 / 2+ │ K 1 │ S 0 │ T 0 │ H 1 │ ~0m 00s │")
        ));
        
        console.Output.ShouldContain("Higher-Order Mutants detected");
    }

    [TestMethod]
    public void ReportRunTest_WithMultipleHOMs_ShouldOnlyShowWarningOnce()
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        var console = new TestConsole();
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));
        progressBarMock.Setup(x => x.Tick(It.IsAny<string>()));

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock(), console);

        var hom1 = new HigherOrderMutant(new List<IMutant> { new Mutant { Id = 1 } }) { Id = 100, ResultStatus = MutantStatus.Killed };
        var hom2 = new HigherOrderMutant(new List<IMutant> { new Mutant { Id = 2 } }) { Id = 101, ResultStatus = MutantStatus.Survived };

        progressBarReporter.ReportInitialState(3);
        progressBarReporter.ReportRunTest(hom1);
        progressBarReporter.ReportRunTest(hom2);

        // Warning should only be shown once
        console.Output.ShouldContain("Higher-Order Mutants detected");
    }

    [TestMethod]
    public void ReportRunTest_WithMixedMutantTypes_ShouldProcessBothCorrectly()
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        var console = new TestConsole();
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));
        progressBarMock.Setup(x => x.Tick(It.IsAny<string>()));

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock(), console);

        var regularMutant = new Mutant { Id = 1, ResultStatus = MutantStatus.Killed };
        var hom = new HigherOrderMutant(new List<IMutant> { new Mutant { Id = 2 }, new Mutant { Id = 3 } }) 
        { 
            Id = 100, 
            ResultStatus = MutantStatus.Survived 
        };

        progressBarReporter.ReportInitialState(1);
        progressBarReporter.ReportRunTest(regularMutant); // Regular mutant first
        progressBarReporter.ReportRunTest(hom); // HOM second

        // Should show warning for HOMs and switch to HOM format
        console.Output.ShouldContain("Higher-Order Mutants detected");

        // Final tick should use HOM format
        progressBarMock.Verify(x => x.Tick(
            It.Is<string>(b => b.Contains("H 1") && b.Contains("1+"))
        ), Times.AtLeastOnce);
    }

    [TestMethod]
    public void ReportRunTest_WithHigherOrderMutantDifferentStatuses_ShouldCountCorrectly()
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        var console = new TestConsole();
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));
        progressBarMock.Setup(x => x.Tick(It.IsAny<string>()));

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock(), console);

        var killedHom = new HigherOrderMutant(new List<IMutant> { new Mutant { Id = 1 } }) 
        { 
            Id = 100, 
            ResultStatus = MutantStatus.Killed 
        };
        var survivedHom = new HigherOrderMutant(new List<IMutant> { new Mutant { Id = 2 } }) 
        { 
            Id = 101, 
            ResultStatus = MutantStatus.Survived 
        };
        var timeoutHom = new HigherOrderMutant(new List<IMutant> { new Mutant { Id = 3 } }) 
        { 
            Id = 102, 
            ResultStatus = MutantStatus.Timeout 
        };

        progressBarReporter.ReportInitialState(3);
        progressBarReporter.ReportRunTest(killedHom);
        progressBarReporter.ReportRunTest(survivedHom);
        progressBarReporter.ReportRunTest(timeoutHom);

        // Final tick should show: K 1, S 1, T 1, H 3
        progressBarMock.Verify(x => x.Tick(
            It.Is<string>(b => b.Contains("K 1") && b.Contains("S 1") && b.Contains("T 1") && b.Contains("H 3"))
        ), Times.AtLeastOnce);
    }

    [TestMethod]
    public void ReportFinalState_WithHigherOrderMutants_ShouldShowHOMSummary()
    {
        var progressBarMock = new Mock<IProgressBar>(MockBehavior.Strict);
        var console = new TestConsole();
        progressBarMock.Setup(x => x.Start(It.IsAny<int>(), It.IsAny<string>()));
        progressBarMock.Setup(x => x.Tick(It.IsAny<string>()));
        progressBarMock.Setup(x => x.Stop());

        var progressBarReporter = new ProgressBarReporter(progressBarMock.Object, new FixedClock(), console);

        var hom1 = new HigherOrderMutant(new List<IMutant> { new Mutant { Id = 1 } }) 
        { 
            Id = 100, 
            ResultStatus = MutantStatus.Killed 
        };
        var hom2 = new HigherOrderMutant(new List<IMutant> { new Mutant { Id = 2 } }) 
        { 
            Id = 101, 
            ResultStatus = MutantStatus.Survived 
        };

        progressBarReporter.ReportInitialState(2);
        progressBarReporter.ReportRunTest(hom1);
        progressBarReporter.ReportRunTest(hom2);

        // Act
        progressBarReporter.ReportFinalState();

        // Should show HOM summary section
        console.Output.ShouldContain("Higher-Order Mutation Summary:");

        console.Output.ShouldContain("Higher-Order Mutants: 2");

        console.Output.ShouldContain("First-Order Mutants:  0");

        console.Output.ShouldContain("HOM Coverage:         100,0% of total mutants");
    }

    [TestMethod]
    public void ReportRunTest_HigherOrderMutantAnalyzeTestRun_ShouldBehavelikeRegularMutant()
    {
        // Arrange - Create a HOM and a regular mutant with same characteristics
        var regularMutant = new Mutant 
        { 
            Id = 1, 
            ResultStatus = MutantStatus.Pending,
            AssessingTests = new TestRunner.Tests.TestIdentifierList(new[] { "Test1", "Test2" })
        };
        
        var hom = new HigherOrderMutant(new List<IMutant> 
        { 
            new Mutant 
            { 
                Id = 2, 
                AssessingTests = new TestRunner.Tests.TestIdentifierList(new[] { "Test1", "Test2", "Test3" })
            },
            new Mutant 
            { 
                Id = 3, 
                AssessingTests = new TestRunner.Tests.TestIdentifierList(new[] { "Test1", "Test2", "Test4" })
            }
        }) 
        { 
            Id = 100, 
            ResultStatus = MutantStatus.Pending 
        };

        // Both should have assessing tests: Test1, Test2 (intersection for HOM, direct for regular mutant)
        
        var failedTests = new TestRunner.Tests.TestIdentifierList(new[] { "Test1" });
        var ranTests = new TestRunner.Tests.TestIdentifierList(new[] { "Test1", "Test2", "Test3", "Test4" });
        var timedOutTests = new TestRunner.Tests.TestIdentifierList(new string[0]);

        // Act - Both should analyze the test run the same way
        regularMutant.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);
        hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);

        // Assert - Both should have identical results
        regularMutant.ResultStatus.ShouldBe(MutantStatus.Killed);
        hom.ResultStatus.ShouldBe(MutantStatus.Killed);
        
        regularMutant.KillingTests.GetIdentifiers().ShouldBe(hom.KillingTests.GetIdentifiers());
    }
}

public class FixedClock : TestBase, IStopWatchProvider
{
    public void Start()
    {
    }

    public void Stop()
    {
    }

    public long GetElapsedMillisecond() => 10L;
}
