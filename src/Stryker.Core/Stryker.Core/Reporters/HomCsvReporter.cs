using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using Spectre.Console;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.ProjectComponents;
using Stryker.Abstractions.Reporting;
using Stryker.Abstractions.Testing;
using Stryker.Core.MutationTest.HigherOrderMutationTest;
using Stryker.Core.Mutants;

namespace Stryker.Core.Reporters;

/// <summary>
/// Outputs a single-row CSV with HOMT/SSHOM metrics for easy analysis.
/// </summary>
public class HomCsvReporter : IReporter
{
    private readonly IStrykerOptions _options;
    private readonly IAnsiConsole _console;
    private readonly IFileSystem _fileSystem;

    public HomCsvReporter(IStrykerOptions options, IFileSystem fileSystem = null, IAnsiConsole console = null)
    {
        _options = options;
        _console = console ?? AnsiConsole.Console;
        _fileSystem = fileSystem ?? new FileSystem();
    }

    public void OnMutantsCreated(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo)
    {
        // no-op
    }

    public void OnStartMutantTestRun(IEnumerable<IReadOnlyMutant> mutantsToBeTested)
    {
        // no-op
    }

    public void OnMutantTested(IReadOnlyMutant result)
    {
        // no-op
    }

    public void OnAllMutantsTested(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo)
    {
        var mutationScore = reportComponent.GetMutationScore();

        var homContext = HomMetricsCollector.HomContext;
        var generation = HomMetricsCollector.GenerationResult;
        var analysis = HomMetricsCollector.SshomAnalysis;
        var totalRunDuration = HomMetricsCollector.TotalRunDuration;
        var randomSeed = HomMetricsCollector.RandomSeed;

        int totalHoms = 0, hom2 = 0, hom3 = 0, hom4 = 0;
        var sshomRate = 0.0;
        int sshom2 = 0, sshom3 = 0, sshom4 = 0;
        var homGenerationMs = 0.0;
        var usedAlgorithms = string.Empty;
        var usedHeuristics = string.Join("|", (_options.HOMTHeuristics ?? Array.Empty<HOMTHeuristicKind>()).Select(h => h.ToString()));
        var totalTestMs = totalRunDuration.TotalMilliseconds;

        // Aggregated per-algorithm stats from CreateCandidateHOMs
        var rawCandidatesTotal = 0;
        var dupWithinTotal = 0;
        var dupAcrossTotal = 0;
        var filteredEmptyTotal = 0;
        var filteredInvalidTotal = 0;
        var perAlgorithmStats = string.Empty;

        if (homContext is not null)
        {
            var created = homContext.CreatedCandidates;
            if (created is not null)
            {
                totalHoms = created.Count;
                hom2 = created.Count(h => h.Order == 2);
                hom3 = created.Count(h => h.Order == 3);
                hom4 = created.Count(h => h.Order == 4);
            }

            if (analysis is not null)
            {
                sshomRate = analysis.SSHOMRate * 100.0;
                sshom2 = analysis.GetSSHOMCountForOrder(2);
                sshom3 = analysis.GetSSHOMCountForOrder(3);
                sshom4 = analysis.GetSSHOMCountForOrder(4);
            }

            if (generation is not null)
            {
                homGenerationMs = generation.GenerationTime.TotalMilliseconds;
                usedAlgorithms = generation.AlgorithmUsed;

                if (generation.AlgorithmStats is not null && generation.AlgorithmStats.Count > 0)
                {
                    rawCandidatesTotal = generation.AlgorithmStats.Sum(s => s.RawCandidates);
                    dupWithinTotal = generation.AlgorithmStats.Sum(s => s.DuplicateWithinAlgorithm);
                    dupAcrossTotal = generation.AlgorithmStats.Sum(s => s.DuplicateAcrossAlgorithms);
                    filteredEmptyTotal = generation.AlgorithmStats.Sum(s => s.FilteredEmptyAssessing);
                    filteredInvalidTotal = generation.AlgorithmStats.Sum(s => s.FilteredInvalidOrder);

                    perAlgorithmStats = string.Join("|", generation.AlgorithmStats.Select(s =>
                        $"{s.AlgorithmName}:raw={s.RawCandidates};kept={s.Kept};dupWithin={s.DuplicateWithinAlgorithm};dupAcross={s.DuplicateAcrossAlgorithms};empty={s.FilteredEmptyAssessing};invalid={s.FilteredInvalidOrder};highest={(s.HighestScore.HasValue ? s.HighestScore.Value.ToString("F4", CultureInfo.InvariantCulture) : string.Empty)};lowest={(s.LowestScore.HasValue ? s.LowestScore.Value.ToString("F4", CultureInfo.InvariantCulture) : string.Empty)};avg={(s.AverageScore.HasValue ? s.AverageScore.Value.ToString("F4", CultureInfo.InvariantCulture) : string.Empty)};median={(s.MedianScore.HasValue ? s.MedianScore.Value.ToString("F4", CultureInfo.InvariantCulture) : string.Empty)}"));
                }
            }
            else
            {
                usedAlgorithms = _options.HOMTAlgorithm.ToString();
            }
        }

        // Write CSV
        var filename = _options.ReportFileName + "-hom.csv";
        var reportPath = Path.Combine(_options.ReportPath, filename);

        _fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(reportPath));

        var writeHeader = !_fileSystem.File.Exists(reportPath);
        using var stream = _fileSystem.File.Open(reportPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (writeHeader)
        {
            writer.WriteLine("total_homs,hom_2,hom_3,hom_4,sshom_rate_percent,sshom_2,sshom_3,sshom_4,hom_generation_ms,used_algorithms,used_heuristics,total_test_duration_ms,mutation_score_percent,raw_candidates_total,dup_within_total,dup_across_total,filtered_empty_total,filtered_invalid_total,per_algorithm_stats,random_seed");
        }

        var fields = new[]
        {
            totalHoms.ToString(CultureInfo.InvariantCulture),
            hom2.ToString(CultureInfo.InvariantCulture),
            hom3.ToString(CultureInfo.InvariantCulture),
            hom4.ToString(CultureInfo.InvariantCulture),
            sshomRate.ToString("F2", CultureInfo.InvariantCulture),
            sshom2.ToString(CultureInfo.InvariantCulture),
            sshom3.ToString(CultureInfo.InvariantCulture),
            sshom4.ToString(CultureInfo.InvariantCulture),
            homGenerationMs.ToString("F2", CultureInfo.InvariantCulture),
            Escape(usedAlgorithms),
            Escape(usedHeuristics),
            totalTestMs.ToString("F2", CultureInfo.InvariantCulture),
            (double.IsNaN(mutationScore) ? 0.0 : mutationScore * 100.0).ToString("F2", CultureInfo.InvariantCulture),
            rawCandidatesTotal.ToString(CultureInfo.InvariantCulture),
            dupWithinTotal.ToString(CultureInfo.InvariantCulture),
            dupAcrossTotal.ToString(CultureInfo.InvariantCulture),
            filteredEmptyTotal.ToString(CultureInfo.InvariantCulture),
            filteredInvalidTotal.ToString(CultureInfo.InvariantCulture),
            Escape(perAlgorithmStats),
            (randomSeed.HasValue ? randomSeed.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)
        };
        writer.WriteLine(string.Join(",", fields));

        _console.WriteLine();
        _console.MarkupLine($"[Green]Your HOM CSV report has been generated at:[/]");
        var uri = "file://" + reportPath.Replace("\\", "/");
        _console.MarkupLineInterpolated(_console.Profile.Capabilities.Links
            ? $"[Green][link={uri}]{reportPath}[/][/]"
            : (FormattableString)$"[Green]{uri}[/]");
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }
}
