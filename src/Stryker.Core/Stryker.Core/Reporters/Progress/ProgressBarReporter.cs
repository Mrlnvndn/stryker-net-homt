using System;
using Spectre.Console;
using Stryker.Abstractions;
using Stryker.Core.Mutants;

namespace Stryker.Core.Reporters.Progress;

public interface IProgressBarReporter
{
    void ReportInitialState(int mutantsToBeTested);
    void ReportRunTest(IReadOnlyMutant mutantTestResult);
    void ReportFinalState();
}

public class ProgressBarReporter : IProgressBarReporter, IDisposable
{
    private const string LoggingFormat = "│ Testing mutant {0} / {1} │ K {2} │ S {3} │ T {4} │ {5} │";
    private const string HOMLoggingFormat = "│ Testing mutant {0} / {1} │ K {2} │ S {3} │ T {4} │ H {5} │ {6} │";

    private readonly IProgressBar _progressBar;
    private readonly IStopWatchProvider _stopWatch;
    private readonly IAnsiConsole _console;

    private int _mutantsToBeTested;
    private int _initialMutantCount; // Track the original count for dynamic updates
    private int _numberOfMutantsRan;
    private bool _disposedValue;

    private int _mutantsKilledCount;
    private int _mutantsSurvivedCount;
    private int _mutantsTimeoutCount;
    private int _higherOrderMutantsCount;
    private bool _hasHigherOrderMutants;
    private bool _totalUpdated = false; // Track if we've already updated the total

    public ProgressBarReporter(IProgressBar progressBar, IStopWatchProvider stopWatch, IAnsiConsole console = null)
    {
        _progressBar = progressBar;
        _stopWatch = stopWatch;
        _console = console ?? AnsiConsole.Console;
    }

    public void ReportInitialState(int mutantsToBeTested)
    {
        _stopWatch.Start();
        _mutantsToBeTested = mutantsToBeTested;
        _initialMutantCount = mutantsToBeTested;

        // Always start with the regular format - we'll detect HOMs dynamically
        var initialMessage = string.Format(LoggingFormat, 0, _mutantsToBeTested, _mutantsKilledCount, _mutantsSurvivedCount, _mutantsTimeoutCount, RemainingTime());

        _progressBar.Start(_mutantsToBeTested, initialMessage);
    }

    public void ReportRunTest(IReadOnlyMutant mutantTestResult)
    {
        _numberOfMutantsRan++;

        // Check if this is a Higher-Order Mutant
        var isHigherOrderMutant = mutantTestResult is HigherOrderMutant;
        if (isHigherOrderMutant)
        {
            _higherOrderMutantsCount++;
            
            // First time we detect HOMs and we haven't updated the total yet
            if (!_hasHigherOrderMutants && !_totalUpdated)
            {
                _hasHigherOrderMutants = true;
                // We can't change the progress bar total mid-execution, but we can inform the user
                // The actual total will be higher than initially reported
                _console.WriteLine();
                _console.MarkupLine("[Yellow]⚠ Higher-Order Mutants detected! Total mutant count will be higher than initially reported.[/]");
                _totalUpdated = true;
            }
            else if (isHigherOrderMutant && !_hasHigherOrderMutants)
            {
                _hasHigherOrderMutants = true;
            }
        }

        switch (mutantTestResult.ResultStatus)
        {
            case MutantStatus.Killed:
                _mutantsKilledCount++;
                break;
            case MutantStatus.Survived:
                _mutantsSurvivedCount++;
                break;
            case MutantStatus.Timeout:
                _mutantsTimeoutCount++;
                break;
        }

        // Update the display format - we'll show current/initial but add additional context
        var message = _hasHigherOrderMutants 
            ? string.Format(HOMLoggingFormat, _numberOfMutantsRan, $"{_initialMutantCount}+", _mutantsKilledCount, _mutantsSurvivedCount, _mutantsTimeoutCount, _higherOrderMutantsCount, RemainingTime())
            : string.Format(LoggingFormat, _numberOfMutantsRan, _mutantsToBeTested, _mutantsKilledCount, _mutantsSurvivedCount, _mutantsTimeoutCount, RemainingTime());

        _progressBar.Tick(message);
    }

    public void ReportFinalState()
    {
        // Update the final total to reflect reality
        _mutantsToBeTested = _numberOfMutantsRan;
        
        var finalMessage = _hasHigherOrderMutants 
            ? string.Format(HOMLoggingFormat, _numberOfMutantsRan, _numberOfMutantsRan, _mutantsKilledCount, _mutantsSurvivedCount, _mutantsTimeoutCount, _higherOrderMutantsCount, RemainingTime())
            : string.Format(LoggingFormat, _numberOfMutantsRan, _mutantsToBeTested, _mutantsKilledCount, _mutantsSurvivedCount, _mutantsTimeoutCount, RemainingTime());

        _progressBar.Tick(finalMessage);
        Dispose();

        var length = _numberOfMutantsRan.ToString().Length;

        _console.WriteLine();
        _console.MarkupLine($"Killed:   [Magenta]{_mutantsKilledCount.ToString().PadLeft(length)}[/]");
        _console.MarkupLine($"Survived: [Magenta]{_mutantsSurvivedCount.ToString().PadLeft(length)}[/]");
        _console.MarkupLine($"Timeout:  [Magenta]{_mutantsTimeoutCount.ToString().PadLeft(length)}[/]");

        // Add Higher-Order Mutant summary if HOMs were present
        if (_hasHigherOrderMutants)
        {
            _console.WriteLine();
            _console.MarkupLine($"[Bold]Higher-Order Mutation Summary:[/]");
            _console.MarkupLine($"Higher-Order Mutants: [Cyan]{_higherOrderMutantsCount.ToString().PadLeft(length)}[/]");
            _console.MarkupLine($"First-Order Mutants:  [Cyan]{(_numberOfMutantsRan - _higherOrderMutantsCount).ToString().PadLeft(length)}[/]");
            var homPercentage = _numberOfMutantsRan > 0 ? (double)_higherOrderMutantsCount / _numberOfMutantsRan * 100 : 0;
            _console.MarkupLine($"HOM Coverage:         [Cyan]{homPercentage:F1}% of total mutants[/]");
        }
    }

    private string RemainingTime()
    {
        if (_mutantsToBeTested == 0 || _numberOfMutantsRan == 0)
        {
            return "NA";
        }

        var elapsed = _stopWatch.GetElapsedMillisecond();
        var remaining = (_mutantsToBeTested - _numberOfMutantsRan) * elapsed / _numberOfMutantsRan;

        return MillisecondsToText(remaining);
    }

    private static string MillisecondsToText(double remaining)
    {
        var span = TimeSpan.FromMilliseconds(remaining);
        if (span.TotalDays >= 1)
        {
            return span.ToString(@"\~d\d\ h\h");
        }

        if (span.TotalHours >= 1)
        {
            return span.ToString(@"\~h\h\ mm\m");
        }

        return span.ToString(@"\~m\m\ ss\s");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _stopWatch?.Stop();
                _progressBar?.Stop();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
