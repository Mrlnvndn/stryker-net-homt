using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Exceptions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.ProjectComponents;
using Stryker.Abstractions.Reporting;
using Stryker.Abstractions.Testing;
using Stryker.Core.CoverageAnalysis;
using Stryker.Core.MutationTest.HigherOrderMutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;
using Stryker.Utilities.Buildalyzer;
using Stryker.Utilities.Logging;

namespace Stryker.Core.MutationTest;

public interface IMutationTestProcess
{
    MutationTestInput Input { get; }
    void Mutate();
    StrykerRunResult Test(IEnumerable<IMutant> mutantsToTest);
    void Restore();
    void GetCoverage();
    void FilterMutants();
}

public class MutationTestProcess : IMutationTestProcess
{
    private static readonly ILogger Logger = ApplicationLogging.LoggerFactory.CreateLogger<MutationTestProcess>();
    private readonly IReadOnlyProjectComponent _projectContents;
    private readonly IMutationTestExecutor _mutationTestExecutor;
    private readonly IReporter _reporter;
    private readonly ICoverageAnalyser _coverageAnalyser;
    private readonly IStrykerOptions _options;
    private readonly IMutationProcess _mutationProcess;
    private static readonly Dictionary<Language, Func<IStrykerOptions, IMutationProcess>> LanguageMap = [];

    static MutationTestProcess() => DeclareMutationProcessForLanguage<CsharpMutationProcess>(Language.Csharp);

    public static void DeclareMutationProcessForLanguage<T>(Language language) where T : IMutationProcess
    {
        var constructor = typeof(T).GetConstructor([typeof(IStrykerOptions)]);
        if (constructor == null)
        {
            throw new NotSupportedException(
                $"Failed to find a constructor with the appropriate signature for type {typeof(T)}");
        }

        LanguageMap[language] = y => (IMutationProcess)constructor.Invoke([y]);
    }

    public MutationTestProcess(MutationTestInput input,
        IStrykerOptions options,
        IReporter reporter,
        IMutationTestExecutor executor,
        IMutationProcess mutationProcess = null,
        ICoverageAnalyser coverageAnalyzer = null)
    {
        Input = input;
        _reporter = reporter;
        _options = options;
        _mutationTestExecutor = executor;
        _mutationProcess = mutationProcess ?? BuildMutationProcess();
        _coverageAnalyser = coverageAnalyzer ?? new CoverageAnalyser(_options);
        _projectContents = input.SourceProjectInfo.ProjectContents;
    }

    public MutationTestInput Input { get; }

    private IMutationProcess BuildMutationProcess()
    {
        if (LanguageMap.ContainsKey(Input.SourceProjectInfo.AnalyzerResult.GetLanguage()))
        {
            return LanguageMap[Input.SourceProjectInfo.AnalyzerResult.GetLanguage()](_options);
        }

        throw new GeneralStrykerException("no valid language detected || no valid csproj or fsproj was given.");
    }

    public void Mutate()
    {
        Input.TestProjectsInfo.BackupOriginalAssembly(Input.SourceProjectInfo.AnalyzerResult);
        _mutationProcess.Mutate(Input);
    }

    public void FilterMutants() => _mutationProcess.FilterMutants(Input);

    public StrykerRunResult Test(IEnumerable<IMutant> mutantsToTest)
    {
        if (!MutantsToTest(mutantsToTest))
        {
            return new StrykerRunResult(_options, double.NaN);
        }

        TestMutants(mutantsToTest);

        return new StrykerRunResult(_options, _projectContents.GetMutationScore());
    }

    public void Restore() => Input.TestProjectsInfo.RestoreOriginalAssembly(Input.SourceProjectInfo.AnalyzerResult);

    //TODO: add creation of homt to this function
    private void TestMutants(IEnumerable<IMutant> mutantsToTest)
    {
        var mutantsToTestList = mutantsToTest.ToList();
        Logger.LogInformation("Starting mutation testing for {MutantCount} mutants", mutantsToTestList.Count);
        
        IEnumerable<IMutant> firstAndHigherOrderMutants;
        IEnumerable<List<IMutant>> mutantGroups;

        if (_options.OptimizationMode.HasFlag(OptimizationModes.EnableHigherOrderMutants))
        {
            firstAndHigherOrderMutants = BuildHigherOrderMutants(mutantsToTestList);
            var firstAndHigherOrderMutantsList = firstAndHigherOrderMutants.ToList();
            
            var homCount = firstAndHigherOrderMutantsList.OfType<HigherOrderMutant>().Count();
            var fomCount = firstAndHigherOrderMutantsList.Count - homCount;
            Logger.LogInformation("HOMT: Generated {HOMCount} Higher-Order Mutants and retained {FOMCount} First-Order Mutants", 
                homCount, fomCount);

            // Check for potential duplicate FOMs across HOMs
            var allFomIds = new HashSet<int>();
            var duplicateFomIds = new HashSet<int>();
            
            foreach (var hom in firstAndHigherOrderMutantsList.OfType<HigherOrderMutant>())
            {
                foreach (var fom in hom.ConstituentMutants)
                {
                    if (!allFomIds.Add(fom.Id))
                    {
                        duplicateFomIds.Add(fom.Id);
                    }
                }
            }
            
            if (duplicateFomIds.Count > 0)
            {
                Logger.LogInformation("HOMT: Found {DuplicateFOMCount} FOMs shared between multiple HOMs: [{DuplicateIds}]", 
                    duplicateFomIds.Count, string.Join(", ", duplicateFomIds.OrderBy(id => id)));
            }

            mutantGroups = BuildMutantGroupsForTest(firstAndHigherOrderMutantsList);
        }
        else
        {
            mutantGroups = BuildMutantGroupsForTest(mutantsToTestList);
        }

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _options.Concurrency };

        Parallel.ForEach(mutantGroups, parallelOptions, mutants =>
        {
            var reportedMutants = new HashSet<IMutant>();

            _mutationTestExecutor.Test(Input.SourceProjectInfo, mutants,
                Input.InitialTestRun.TimeoutValueCalculator,
                (testedMutants, tests, ranTests, outTests) =>
                    TestUpdateHandler(testedMutants, tests, ranTests, outTests, reportedMutants));

            OnMutantsTested(mutants, reportedMutants);
        });
    }


    private bool TestUpdateHandler(IEnumerable<IMutant> testedMutants, ITestIdentifiers failedTests, ITestIdentifiers ranTests,
        ITestIdentifiers timedOutTest, ISet<IMutant> reportedMutants)
    {
        var testsFailingInitially = Input.InitialTestRun.Result.FailingTests.GetIdentifiers().ToHashSet();
        
        // Force DisableBail when Higher Order Mutants are enabled to ensure complete killing test collection for SSHOM validation
        var continueTestRun = _options.OptimizationMode.HasFlag(OptimizationModes.DisableBail) ||
                              _options.OptimizationMode.HasFlag(OptimizationModes.EnableHigherOrderMutants);
                              
        if (testsFailingInitially.Count > 0 && failedTests.GetIdentifiers().Any(id => testsFailingInitially.Contains(id)))
        {
            // some of the failing tests were failing without any mutation
            // we discard those tests
            failedTests = new TestIdentifierList(
                failedTests.GetIdentifiers().Where(t => !testsFailingInitially.Contains(t)));
        }

        foreach (var mutant in testedMutants)
        {
            var oldStatus = mutant.ResultStatus;
            
            // AnalyzeTestRun handles everything automatically:
            // - For regular FOMs: Updates the FOM directly
            // - For HOMs: Updates the HOM AND all its constituent copies
            mutant.AnalyzeTestRun(failedTests, ranTests, timedOutTest, false);
            
            Logger.LogTrace("HOMT: Mutant {MutantId} status: {OldStatus} -> {NewStatus}", 
                mutant.Id, oldStatus, mutant.ResultStatus);

            if (mutant.ResultStatus == MutantStatus.Pending)
            {
                continueTestRun = true; // Not all mutants in this group were tested so we continue
                Logger.LogTrace("HOMT: Mutant {MutantId} still pending, continuing test run", mutant.Id);
            }

            OnMutantTested(mutant, reportedMutants);
        }

        return continueTestRun;
    }

    /// <summary>
    /// Finds the parent HOM for a given FOM ID by checking all mutants in the current test group.
    /// Uses interface-based checking instead of reflection for better performance and reliability.
    /// </summary>
    /// <param name="fomId">The FOM ID to find the parent for</param>
    /// <param name="allMutants">All mutants in the current test group</param>
    /// <returns>The parent HOM if found, null otherwise</returns>
    private static HigherOrderMutant FindParentHOM(int fomId, IReadOnlyCollection<IMutant> allMutants)
    {
        var parentHom = allMutants.OfType<HigherOrderMutant>()
                                 .FirstOrDefault(hom => hom.ConstituentMutants.Any(c => c.Id == fomId));
        
        if (parentHom != null)
        {
            Logger.LogTrace("HOMT: Found parent HOM {HOMId} for FOM {FOMId}", parentHom.Id, fomId);
        }
        
        return parentHom;
    }

    private static IEnumerable<HigherOrderMutant> FindAllParentHOMs(int fomId, IReadOnlyCollection<IMutant> allMutants)
    {
        var parentHoms = allMutants.OfType<HigherOrderMutant>()
                              .Where(hom => hom.ConstituentMutants.Any(c => c.Id == fomId));
    
        Logger.LogTrace("HOMT: Found {ParentCount} parent HOMs for FOM {FOMId}", 
            parentHoms.Count(), fomId);
    
        return parentHoms;
    }

    /// <summary>
    /// Updates a HOM's status based on its constituent FOMs' results.
    /// Uses interface-based checking and improved error handling.
    /// </summary>
    /// <param name="hom">The HOM to update</param>
    /// <param name="failedTests">Tests that failed during this run</param>
    /// <param name="ranTests">Tests that ran during this run</param>
    /// <param name="timedOutTest">Tests that timed out during this run</param>
    private static void UpdateHOMWithFOMResults(IMutant hom, ITestIdentifiers failedTests, ITestIdentifiers ranTests, ITestIdentifiers timedOutTest)
    {
        if (hom is not HigherOrderMutant higherOrderMutant)
        {
            Logger.LogWarning("HOMT: Expected HigherOrderMutant but got {Type} for mutant {Id}", hom.GetType().Name, hom.Id);
            return;
        }

        var constituentsList = higherOrderMutant.ConstituentMutants.ToList();
        var oldStatus = hom.ResultStatus;
        
        Logger.LogTrace("HOMT: Updating HOM {HOMId} (Order: {Order}) with {ConstituentCount} constituents. Current status: {CurrentStatus}", 
            hom.Id, higherOrderMutant.Order, constituentsList.Count, oldStatus);
        
        // Log constituent statuses for debugging
        var constituentStatuses = constituentsList.Select(f => $"{f.Id}:{f.ResultStatus}").ToList();
        Logger.LogTrace("HOMT: Constituent FOMs statuses - {ConstituentStatuses}", string.Join(", ", constituentStatuses));
        
        // Strategy: HOM is killed if any constituent FOM is killed
        if (constituentsList.Any(fom => fom.ResultStatus == MutantStatus.Killed))
        {
            hom.ResultStatus = MutantStatus.Killed;
            // Merge killing tests from all killed FOMs
            var killedFoms = constituentsList.Where(f => f.ResultStatus == MutantStatus.Killed).ToList();
            var allKillingTests = killedFoms.Aggregate(TestIdentifierList.NoTest(), 
                                                      (current, killedFom) => current.Merge(killedFom.KillingTests));
            hom.KillingTests = allKillingTests;
            
            Logger.LogDebug("HOMT: HOM {HOMId} marked as KILLED - {KilledFOMCount}/{TotalFOMCount} constituent FOMs were killed", 
                hom.Id, killedFoms.Count, constituentsList.Count);
        }
        else if (constituentsList.Any(fom => fom.ResultStatus == MutantStatus.Timeout))
        {
            hom.ResultStatus = MutantStatus.Timeout;
            var timedOutFoms = constituentsList.Count(f => f.ResultStatus == MutantStatus.Timeout);
            Logger.LogDebug("HOMT: HOM {HOMId} marked as TIMEOUT - {TimedOutFOMCount}/{TotalFOMCount} constituent FOMs timed out", 
                hom.Id, timedOutFoms, constituentsList.Count);
        }
        else if (constituentsList.All(fom => fom.ResultStatus == MutantStatus.Survived))
        {
            hom.ResultStatus = MutantStatus.Survived;
            Logger.LogDebug("HOMT: HOM {HOMId} marked as SURVIVED - all {TotalFOMCount} constituent FOMs survived", 
                hom.Id, constituentsList.Count);
        }
        else
        {
            var pendingFoms = constituentsList.Count(f => f.ResultStatus == MutantStatus.Pending);
            Logger.LogTrace("HOMT: HOM {HOMId} remains PENDING - {PendingFOMCount}/{TotalFOMCount} constituent FOMs still pending", 
                hom.Id, pendingFoms, constituentsList.Count);
        }
        
        if (oldStatus != hom.ResultStatus)
        {
            Logger.LogInformation("HOMT: HOM {HOMId} status updated: {OldStatus} -> {NewStatus}", 
                hom.Id, oldStatus, hom.ResultStatus);
        }
    }

    private void OnMutantsTested(IEnumerable<IMutant> mutants, ISet<IMutant> reportedMutants)
    {
        foreach (var mutant in mutants)
        {
            if (mutant.ResultStatus == MutantStatus.Pending)
            {
                Logger.LogWarning("HOMT: Mutation {Id} was not fully tested - final status: {Status}", 
                    mutant.Id, mutant.ResultStatus);
            }

            OnMutantTested(mutant, reportedMutants);
        }
    }

    private void OnMutantTested(IMutant mutant, ISet<IMutant> reportedMutants)
    {
        if (mutant.ResultStatus == MutantStatus.Pending || reportedMutants.Contains(mutant))
        {
            // skip duplicates or useless notifications
            return;
        }

        var mutantType = mutant is HigherOrderMutant hom ? $"HOM(Order:{hom.Order})" : "FOM";
        Logger.LogTrace("HOMT: Reporting {MutantType} {MutantId} with status {Status}", 
            mutantType, mutant.Id, mutant.ResultStatus);

        _reporter?.OnMutantTested(mutant);
        reportedMutants.Add(mutant);
    }

    private static bool MutantsToTest(IEnumerable<IMutant> mutantsToTest)
    {
        if (!mutantsToTest.Any())
        {
            return false;
        }

        if (mutantsToTest.Any(x => x.ResultStatus != MutantStatus.Pending))
        {
            throw new GeneralStrykerException(
                "Only mutants to run should be passed to the mutation test process. If you see this message please report an issue.");
        }

        return true;
    }

    private IEnumerable<IMutant> BuildHigherOrderMutants(IReadOnlyCollection<IMutant> mutantsToTest)
    {
        Logger.LogInformation("HOMT: Starting Higher-Order Mutant generation for {FOMCount} First-Order Mutants", 
            mutantsToTest.Count);
        
        // Create the HigherOrderMutation instance and delegate all logic to it
        var higherOrderMutation = new HigherOrderMutation(_options, Input, mutantsToTest);

        // Add Algorithm to HigherOrderMutation instance
        var localSearchAlgorithm = new LocalSearchAlgorithm(Input, [],_options, mutantsToTest);
        higherOrderMutation.AddSearchAlgorithm(localSearchAlgorithm);
        Logger.LogDebug("HOMT: Registered LocalSearchAlgorithm for HOM generation");

        Input.HigherOrderMutation = higherOrderMutation;

        var result = higherOrderMutation.BuildAndOptimizeHigherOrderMutants(
            mutantsToTest, isPreTestRun: true, includeAllIndividualMutants: true
        );

        // Log the metadata for debugging
        Logger.LogInformation("HOMT: Generation completed - Algorithm: {AlgorithmUsed}, Candidates: {CandidatesGenerated}, " +
                             "Heuristics: {HeuristicsUsed}, Time: {GenerationTime:F2}ms, Total Mutants: {TotalMutants}",
            result.AlgorithmUsed, result.CandidatesGenerated, result.HeuristicsUsed, 
            result.GenerationTime.TotalMilliseconds, result.MutantGroups.Count());
            
        // Log breakdown of mutant types
        var homResults = result.MutantGroups.OfType<HigherOrderMutant>().ToList();
        var fomResults = result.MutantGroups.Where(m => m is not HigherOrderMutant).ToList();
        
        Logger.LogInformation("HOMT: Generated {HOMCount} HOMs and retained {FOMCount} FOMs for testing", 
            homResults.Count, fomResults.Count);
            
        if (homResults.Count > 0)
        {
            var orders = homResults.GroupBy(h => h.Order).OrderBy(g => g.Key);
            foreach (var orderGroup in orders)
            {
                Logger.LogDebug("HOMT: {Count} HOMs of order {Order}", orderGroup.Count(), orderGroup.Key);
            }
        }

        return result.MutantGroups;
    }

    private IEnumerable<List<IMutant>> BuildMutantGroupsForTest(IReadOnlyCollection<IMutant> mutantsNotRun)
    {
        Logger.LogDebug("HOMT: Building mutant groups for testing from {MutantCount} mutants", mutantsNotRun.Count);
        
        if (_options.OptimizationMode.HasFlag(OptimizationModes.DisableMixMutants) ||
            !_options.OptimizationMode.HasFlag(OptimizationModes.CoverageBasedTest))
        {
            Logger.LogDebug("HOMT: Using individual mutant groups (DisableMixMutants or no coverage-based testing)");
            // For HOMs, ensure each group contains only the HOM itself, not its constituents
            return mutantsNotRun.Select(x => new List<IMutant> { x });
        }

        Logger.LogDebug("HOMT: Using coverage-based mutant grouping");

        var blocks = new List<List<IMutant>>(mutantsNotRun.Count);
        var mutantsToGroup = mutantsNotRun.ToList();
        // we deal with mutants needing full testing first
        var fullTestMutants = mutantsToGroup.Where(m => m.AssessingTests.IsEveryTest).ToList();
        blocks.AddRange(fullTestMutants.Select(m => new List<IMutant> { m }));
        mutantsToGroup.RemoveAll(m => m.AssessingTests.IsEveryTest);
        
        if (fullTestMutants.Count > 0)
        {
            Logger.LogDebug("HOMT: {FullTestCount} mutants require full test suite execution", fullTestMutants.Count);
        }

        mutantsToGroup = mutantsToGroup.Where(m => m.ResultStatus == MutantStatus.Pending).ToList();

        var testsCount = Input.InitialTestRun.Result.ExecutedTests.Count;
        mutantsToGroup = mutantsToGroup.OrderBy(m => m.AssessingTests.Count).ToList();
        
        Logger.LogTrace("HOMT: Grouping {PendingMutants} pending mutants with {TotalTests} total tests available", 
            mutantsToGroup.Count, testsCount);
        
        while (mutantsToGroup.Count > 0)
        {
            // we pick the first mutant
            var usedTests = mutantsToGroup[0].AssessingTests;
            var nextBlock = new List<IMutant> { mutantsToGroup[0] };
            var firstMutant = mutantsToGroup[0];
            mutantsToGroup.RemoveAt(0);
            
            for (var j = 0; j < mutantsToGroup.Count; j++)
            {
                var currentMutant = mutantsToGroup[j];
                var nextSet = currentMutant.AssessingTests;
                if (nextSet.Count + usedTests.Count > testsCount)
                {
                    break;
                }

                if (nextSet.ContainsAny(usedTests))
                {
                    continue;
                }
                

                // add this mutant to the block
                nextBlock.Add(currentMutant);
                // remove the mutant from the list of mutants to group
                mutantsToGroup.RemoveAt(j--);
                // add this mutant's tests
                usedTests = usedTests.Merge(nextSet);
            }

            blocks.Add(nextBlock);
            
            if (nextBlock.Count > 1)
            {
                var mutantTypes = nextBlock.Select(m => m is HigherOrderMutant ? "HOM" : "FOM");
                Logger.LogTrace("HOMT: Created group with {GroupSize} mutants: [{MutantIds}] - Types: [{MutantTypes}]", 
                    nextBlock.Count, string.Join(", ", nextBlock.Select(m => m.Id)), string.Join(", ", mutantTypes));
            }
        }

        if (mutantsNotRun.Count > blocks.Count)
        {
            Logger.LogInformation("HOMT: Optimized mutation testing - {BlocksCount} test runs instead of {MutantsNotRun} " +
                                 "(saved {SavedRuns} test executions)", blocks.Count, mutantsNotRun.Count, 
                                 mutantsNotRun.Count - blocks.Count);
        }
        else
        {
            Logger.LogInformation("HOMT: Created {BlocksCount} test runs for mutation testing", blocks.Count);
        }

        return blocks;
    }

    public void GetCoverage() => _coverageAnalyser.DetermineTestCoverage(Input.SourceProjectInfo,
        _mutationTestExecutor.TestRunner, _projectContents.Mutants, Input.InitialTestRun.Result.FailingTests);
}
