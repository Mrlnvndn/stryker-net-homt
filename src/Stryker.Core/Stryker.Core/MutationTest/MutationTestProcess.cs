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

        if (_options.OptimizationMode.HasFlag(OptimizationModes.HOMTAccelerate) || 
            _options.OptimizationMode.HasFlag(OptimizationModes.HOMTValidate))
        {
            firstAndHigherOrderMutants = BuildHigherOrderMutants(mutantsToTestList);
            var firstAndHigherOrderMutantsList = firstAndHigherOrderMutants.ToList();
            
            var homCount = firstAndHigherOrderMutantsList.OfType<HigherOrderMutant>().Count();
            var fomCount = firstAndHigherOrderMutantsList.Count - homCount;
            Logger.LogInformation("HOMT: Final test set contains {HOMCount} Higher-Order Mutants and {FOMCount} First-Order Mutants", 
                homCount, fomCount);

            // Expose HOM context for reporters
            HomMetricsCollector.HomContext = Input.HigherOrderMutation;

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
        ITestIdentifiers timedOutTests, ISet<IMutant> reportedMutants)
    {
        var testsFailingInitially = Input.InitialTestRun.Result.FailingTests.GetIdentifiers().ToHashSet();
        
        // Force DisableBail when HOMTValidate is enabled to ensure complete killing test collection for SSHOM validation
        // Allow early bail in HOMTAccelerate mode for performance testing
        var continueTestRun = _options.OptimizationMode.HasFlag(OptimizationModes.DisableBail) ||
                              _options.OptimizationMode.HasFlag(OptimizationModes.HOMTValidate);
                              
        if (testsFailingInitially.Count > 0 && failedTests.GetIdentifiers().Any(testsFailingInitially.Contains))
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
            mutant.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);
            
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

    private List<IMutant> BuildHigherOrderMutants(IReadOnlyCollection<IMutant> mutantsToTest)
    {
        var isAccelerateMode = _options.OptimizationMode.HasFlag(OptimizationModes.HOMTAccelerate);
        var isValidateMode = _options.OptimizationMode.HasFlag(OptimizationModes.HOMTValidate);

        if (!isAccelerateMode && !isValidateMode)
        {
            // Fallback to accelerate mode if neither is explicitly set but HOM generation is somehow called
            Logger.LogWarning("HOMT: Neither accelerate nor validate mode specified, defaulting to accelerate mode");
            isAccelerateMode = true;
        }

        var modeDescription = isValidateMode ? "Validate" : "Accelerate";
        Logger.LogInformation("HOMT: Starting Higher-Order Mutant generation in {Mode} mode for {FOMCount} First-Order Mutants", 
            modeDescription, mutantsToTest.Count);
        
        // Create the HigherOrderMutation instance and delegate all logic to it
        var higherOrderMutation = new HigherOrderMutation(_options, Input, mutantsToTest);

        // Create heuristics based on options
        var heuristics = CreateHeuristicsFromOptions(_options.HOMTHeuristics);

        foreach (var heuristic in heuristics)
        {
            higherOrderMutation.AddHeuristic(heuristic);
        }

        // Generate a single random seed and pass it to all algorithms for reproducibility
        var randomSeed = Math.Abs(Environment.TickCount);
        HomMetricsCollector.RandomSeed = randomSeed;
        Logger.LogInformation("HOMT: Using random seed {Seed} for algorithm initialization", randomSeed);

        switch (_options.HOMTAlgorithm)
        {
            case HOMTAlgorithmKind.Genetic:
                higherOrderMutation.AddSearchAlgorithm(new GeneticSearchAlgorithm(Input, heuristics, _options, mutantsToTest, registerAllHeuristics: false, earlyFiltering: false, geneticOptions: new GeneticSearchOptions(RandomSeed: randomSeed)));
                break;
            case HOMTAlgorithmKind.Local:
                higherOrderMutation.AddSearchAlgorithm(new LocalSearchAlgorithmV2(Input, heuristics, _options, mutantsToTest, registerAllHeuristics: false, randomSeed: randomSeed));
                break;
            case HOMTAlgorithmKind.Both:
                higherOrderMutation.AddSearchAlgorithm(new GeneticSearchAlgorithm(Input, heuristics, _options, mutantsToTest, registerAllHeuristics: false, earlyFiltering: false, geneticOptions: new GeneticSearchOptions(RandomSeed: randomSeed)));
                higherOrderMutation.AddSearchAlgorithm(new LocalSearchAlgorithmV2(Input, heuristics, _options, mutantsToTest, registerAllHeuristics: false, randomSeed: randomSeed));
                break;
        }

        Input.HigherOrderMutation = higherOrderMutation;

        // In validate mode, include all individual mutants; in accelerate mode, filter out constituent FOMs
        var includeAllIndividualMutants = isValidateMode;
        
        var result = higherOrderMutation.BuildAndOptimizeHigherOrderMutants(
            mutantsToTest, isPreTestRun: true, includeAllIndividualMutants: includeAllIndividualMutants
        );

        Logger.LogInformation("HOMT: Generation completed - Mode: {Mode}, Algorithm: {AlgorithmUsed}, HOM Candidates: {CandidatesGenerated}, Heuristics: {HeuristicsUsed}, Time: {GenerationTime:F2}ms",
            modeDescription, result.AlgorithmUsed, result.CandidatesGenerated, result.HeuristicsUsed, result.GenerationTime.TotalMilliseconds);

        // expose generation result
        HomMetricsCollector.GenerationResult = result;

        var homResults = result.HomCandidates.ToList();
        List<IMutant> fomResults;
        if (isValidateMode)
        {
            // validate mode => all original FOMs + HOMs
            fomResults = [.. mutantsToTest.Where(m => m is not HigherOrderMutant).Cast<IMutant>()];
        }
        else
        {
            // accelerate mode => only non constituent FOMs provided by result
            fomResults = [.. result.MissingFirstOrderMutants];
        }

        if (isAccelerateMode && !includeAllIndividualMutants)
        {
            var finalMutants = homResults.Cast<IMutant>().Concat(fomResults).ToList();
            Logger.LogInformation("HOMT: Final test set - {HOMCount} HOMs + {FOMCount} non-constituent FOMs = {Total}", homResults.Count, fomResults.Count, finalMutants.Count);
            return finalMutants;
        }
        else
        {
            Logger.LogInformation("HOMT: Validate mode - Testing {HOMCount} HOMs alongside {FOMCount} original FOMs", homResults.Count, fomResults.Count);
            if (homResults.Count > 0)
            {
                var orders = homResults.GroupBy(h => h.Order).OrderBy(g => g.Key);
                foreach (var orderGroup in orders)
                {
                    Logger.LogDebug("HOMT: {Count} HOMs of order {Order}", orderGroup.Count(), orderGroup.Key);
                }
            }
            return homResults.Cast<IMutant>().Concat(fomResults).ToList();
        }
    }

    /// <summary>
    /// Creates heuristic instances based on the configured heuristic kinds.
    /// </summary>
    private static List<IHOMHeuristic> CreateHeuristicsFromOptions(IEnumerable<HOMTHeuristicKind> heuristicKinds)
    {
        var heuristics = new List<IHOMHeuristic>();
        
        foreach (var heuristicKind in heuristicKinds)
        {
            IHOMHeuristic heuristic = heuristicKind switch
            {
                HOMTHeuristicKind.CodeLocation => new CodeLocationHeuristic(),
                HOMTHeuristicKind.EmptyAssessingTests => new EmptyAssessingTestsHeuristic(),
                HOMTHeuristicKind.MutatorType => new MutatorTypeHeuristic(),
                HOMTHeuristicKind.MaxSizeLimit => new MaxSizeLimitHeuristic(),
                HOMTHeuristicKind.OverlappingTests => new OverlappingTestsHeuristic(),
                HOMTHeuristicKind.SyntaxNodeConflict => new SyntaxNodeConflictHeuristic(),
                _ => throw new ArgumentOutOfRangeException(nameof(heuristicKind), heuristicKind, "Unknown heuristic kind")
            };
            
            heuristics.Add(heuristic);
        }

        Logger.LogInformation("HOMT: Using {Count} heuristics: {Heuristics}", 
            heuristics.Count, string.Join(", ", heuristicKinds));
            
        return heuristics;
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
