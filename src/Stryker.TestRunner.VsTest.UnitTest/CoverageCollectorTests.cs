using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollector.InProcDataCollector;
using Moq;
using Shouldly;
using Stryker.Core.UnitTest;
using Stryker.DataCollector;

namespace Stryker.TestRunner.VsTest.UnitTest;

[TestClass]
public class CoverageCollectorTests : TestBase
{
    [TestInitialize]
    public void TestInitialize()
    {
        // Reset static state before each test
        MutantControl.CaptureCoverage = false;
        MutantControl.ActiveMutant = -1;
        MutantControl.ActiveMutants = new HashSet<int>();
        MutantControl.isHomt = false;
        MutantControl.ClearCoverageInfo();
    }

    [TestMethod]
    public void ProperlyCaptureParams()
    {
        var collector = new CoverageCollector();

        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(true, null, GetType().Namespace)
        };
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);
        collector.TestCaseStart(new TestCaseStartArgs(new TestCase("theTest", new Uri("xunit://"), "source.cs")));
        MutantControl.CaptureCoverage.ShouldBeTrue();
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void RedirectDebugAssert()
    {
        var collector = new CoverageCollector();

        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(false, null, GetType().Namespace)
        };
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
        collector.Initialize(mock.Object);
        collector.TestSessionStart(start);

        Debug.Write("This is lost.");
        Debug.WriteLine("This also.");

        var assert = () => Debug.Fail("test");

        assert.ShouldThrow<ArgumentException>();
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void ProperlySelectMutant()
    {
        var collector = new CoverageCollector();

        var testCase = new TestCase("theTest", new Uri("xunit://"), "source.cs");
        var nonCoveringTestCase = new TestCase("theOtherTest", new Uri("xunit://"), "source.cs");
        var mutantMap = new List<(int, IEnumerable<Guid>)> { (10, new List<Guid> { testCase.Id }), (5, new List<Guid> { nonCoveringTestCase.Id }) };

        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(false, mutantMap, GetType().Namespace)
        };
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);
        MutantControl.ActiveMutant.ShouldBe(-1);

        collector.TestCaseStart(new TestCaseStartArgs(testCase));

        MutantControl.ActiveMutant.ShouldBe(10);
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void SelectMutantEarlyIfSingle()
    {
        var collector = new CoverageCollector();

        var testCase = new TestCase("theTest", new Uri("xunit://"), "source.cs");
        var mutantMap = new List<(int, IEnumerable<Guid>)> { (5, new List<Guid> { testCase.Id }) };

        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(false, mutantMap, GetType().Namespace)
        };
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);

        MutantControl.ActiveMutant.ShouldBe(5);
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void ProperlyCaptureCoverage()
    {
        var collector = new CoverageCollector();

        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(true, null, GetType().Namespace)
        };
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);

        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);
        var testCase = new TestCase("theTest", new Uri("xunit://"), "source.cs");
        collector.TestCaseStart(new TestCaseStartArgs(testCase));
        MutantControl.HitNormal(0);
        MutantControl.HitNormal(1);
        MutantControl.HitStatic(1);
        var dataCollection = new DataCollectionContext(testCase);
        collector.TestCaseEnd(new TestCaseEndArgs(dataCollection, TestOutcome.Passed));

        mock.Verify(sink => sink.SendData(dataCollection, CoverageCollector.PropertyName, "0,1;1"), Times.Once);
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void ProperlyReportNoCoverage()
    {
        var collector = new CoverageCollector();

        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(true, null, GetType().Namespace)
        };
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);

        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);
        var testCase = new TestCase("theTest", new Uri("xunit://"), "source.cs");
        collector.TestCaseStart(new TestCaseStartArgs(testCase));
        var dataCollection = new DataCollectionContext(testCase);
        collector.TestCaseEnd(new TestCaseEndArgs(dataCollection, TestOutcome.Passed));

        mock.Verify(sink => sink.SendData(dataCollection, CoverageCollector.PropertyName, ";"), Times.Once);
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void ProperlyReportLeakedMutations()
    {
        var collector = new CoverageCollector();

        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(true, null, GetType().Namespace)
        };
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);

        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);
        var testCase = new TestCase("theTest", new Uri("xunit://"), "source.cs");
        MutantControl.HitNormal(0);
        collector.TestCaseStart(new TestCaseStartArgs(testCase));
        var dataCollection = new DataCollectionContext(testCase);
        MutantControl.HitNormal(1);
        collector.TestCaseEnd(new TestCaseEndArgs(dataCollection, TestOutcome.Passed));

        mock.Verify(sink => sink.SendData(dataCollection, CoverageCollector.PropertyName, "1;"), Times.Once);
        mock.Verify(sink => sink.SendData(dataCollection, CoverageCollector.OutOfTestsPropertyName, "0"), Times.Once);
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void ShouldActivateMultipleMutantsInHomtMode()
    {
        var collector = new CoverageCollector();

        // Create test cases for HOMT scenario
        var testCase1 = new TestCase("HomtTest1", new Uri("xunit://"), "source.cs");
        var testCase2 = new TestCase("HomtTest2", new Uri("xunit://"), "source.cs");
        
        // Configure multiple mutants for the same test in HOMT mode
        var mutantMap = new List<(int, IEnumerable<Guid>)> 
        { 
            (10, new List<Guid> { testCase1.Id }), // Test1 covers mutants 10, 15, 20
            (15, new List<Guid> { testCase1.Id }),
            (20, new List<Guid> { testCase1.Id }),
            (25, new List<Guid> { testCase2.Id })  // Test2 covers mutant 25
        };

        // Enable HOMT mode
        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(false, mutantMap, GetType().Namespace, isHomt: true)
        };
        
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
        collector.Initialize(mock.Object);

        // Start session and verify HOMT initialization
        collector.TestSessionStart(start);
        
        // Verify HOMT mode is active
        MutantControl.isHomt.ShouldBeTrue();
        
        // Start first test case - should activate multiple mutants
        collector.TestCaseStart(new TestCaseStartArgs(testCase1));
        
        // Verify that multiple mutants are active for this test
        MutantControl.ActiveMutants.ShouldContain(10);
        MutantControl.ActiveMutants.ShouldContain(15);
        MutantControl.ActiveMutants.ShouldContain(20);
        MutantControl.ActiveMutants.ShouldNotContain(25); // This belongs to test2
        
        // Verify IsActive returns true for all mutants assigned to this test
        MutantControl.IsActive(10).ShouldBeTrue();
        MutantControl.IsActive(15).ShouldBeTrue();
        MutantControl.IsActive(20).ShouldBeTrue();
        MutantControl.IsActive(25).ShouldBeFalse(); // Not active for this test
        MutantControl.IsActive(99).ShouldBeFalse(); // Non-existent mutant
        
        // Start second test case - should activate different mutant
        collector.TestCaseStart(new TestCaseStartArgs(testCase2));
        
        // Verify that only mutant 25 is active for test2
        MutantControl.ActiveMutants.ShouldContain(25);
        MutantControl.ActiveMutants.ShouldNotContain(10);
        MutantControl.ActiveMutants.ShouldNotContain(15);
        MutantControl.ActiveMutants.ShouldNotContain(20);
        
        // Verify IsActive behavior for test2
        MutantControl.IsActive(25).ShouldBeTrue();
        MutantControl.IsActive(10).ShouldBeFalse();
        MutantControl.IsActive(15).ShouldBeFalse();
        MutantControl.IsActive(20).ShouldBeFalse();
        
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void ShouldFallbackToSingleMutantModeWhenHomtDisabled()
    {
        var collector = new CoverageCollector();

        var testCase = new TestCase("RegularTest", new Uri("xunit://"), "source.cs");
        var mutantMap = new List<(int, IEnumerable<Guid>)> 
        { 
            (10, new List<Guid> { testCase.Id }),
            (15, new List<Guid> { testCase.Id })
        };

        // HOMT mode disabled (default)
        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(false, mutantMap, GetType().Namespace, isHomt: false)
        };
        
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);
        
        // Verify HOMT mode is not active
        MutantControl.isHomt.ShouldBeFalse();
        
        collector.TestCaseStart(new TestCaseStartArgs(testCase));
        
        // In non-HOMT mode, only one mutant should be active (the last one)
        MutantControl.ActiveMutant.ShouldBe(15);
        
        // IsActive should use single mutant logic
        MutantControl.IsActive(10).ShouldBeFalse();
        MutantControl.IsActive(15).ShouldBeTrue();
        
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void ShouldHandleHomtModeWithSingleMutant()
    {
        var collector = new CoverageCollector();

        var testCase = new TestCase("SingleMutantHomtTest", new Uri("xunit://"), "source.cs");
        var mutantMap = new List<(int, IEnumerable<Guid>)> 
        { 
            (42, new List<Guid> { testCase.Id })
        };

        // Enable HOMT mode with single mutant
        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(false, mutantMap, GetType().Namespace, isHomt: true)
        };
        
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);
        
        // Verify HOMT mode is active
        MutantControl.isHomt.ShouldBeTrue();
        
        collector.TestCaseStart(new TestCaseStartArgs(testCase));
        
        // Even with single mutant, HOMT mode should work
        MutantControl.ActiveMutants.ShouldContain(42);
        MutantControl.IsActive(42).ShouldBeTrue();
        MutantControl.IsActive(999).ShouldBeFalse();
        
        collector.TestSessionEnd(new TestSessionEndArgs());
    }

    [TestMethod]
    public void ShouldNotActivateMutantsInCoverageMode()
    {
        var collector = new CoverageCollector();

        var testCase = new TestCase("CoverageTest", new Uri("xunit://"), "source.cs");
        
        // Enable coverage mode (regardless of HOMT setting)
        var start = new TestSessionStartArgs
        {
            Configuration = CoverageCollector.GetVsTestSettings(true, null, GetType().Namespace, isHomt: true)
        };
        
        var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
        collector.Initialize(mock.Object);

        collector.TestSessionStart(start);
        collector.TestCaseStart(new TestCaseStartArgs(testCase));
        
        // In coverage mode, CaptureCoverage should be true and no mutants should be active
        MutantControl.CaptureCoverage.ShouldBeTrue();
        
        // IsActive should always return false in coverage mode
        MutantControl.IsActive(10).ShouldBeFalse();
        MutantControl.IsActive(15).ShouldBeFalse();
        
        collector.TestSessionEnd(new TestSessionEndArgs());
    }
}

// mock for the actual MutantControl class injected in the mutated assembly.
// used for unit test
public static class MutantControl
{
    public static bool CaptureCoverage;
    public static int ActiveMutant = -1;
    public static HashSet<int> ActiveMutants = new HashSet<int>();
    public static bool isHomt = false;
    
    private static List<int>[] coverageData = { new List<int>(), new List<int>() };
    
    public static IList<int>[] GetCoverageData()
    {
        var result = coverageData;
        ClearCoverageInfo();
        return result;
    }

    public static void ClearCoverageInfo() => coverageData = new[] { new List<int>(), new List<int>() };

    public static void HitNormal(int mutation) => coverageData[0].Add(mutation);

    public static void HitStatic(int mutation) => coverageData[1].Add(mutation);
    
    // Add IsActive method that matches your implementation
    public static bool IsActive(int id)
    {
        if (CaptureCoverage)
        {
            return false;
        }
        
        if (isHomt)
        {
            return ActiveMutants.Contains(id);
        }
        
        return id == ActiveMutant;
    }
}
