using System.Linq;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.ProjectComponents;
using Stryker.Core.Mutants;
using Stryker.Utilities.Logging;

namespace Stryker.Core.Reporters;

public class FilteredMutantsLogger
{
    private readonly ILogger<FilteredMutantsLogger> _logger;

    public FilteredMutantsLogger(ILogger<FilteredMutantsLogger> logger = null)
    {
        _logger = logger ?? ApplicationLogging.LoggerFactory.CreateLogger<FilteredMutantsLogger>();
    }

    public void OnMutantsCreated(IReadOnlyProjectComponent reportComponent)
    {
        var allMutants = reportComponent.Mutants.ToList();
        var skippedMutants = allMutants.Where(m => m.ResultStatus != MutantStatus.Pending);

        var skippedMutantGroups = skippedMutants.GroupBy(x => new { x.ResultStatus, x.ResultStatusReason }).OrderBy(x => x.Key.ResultStatusReason);

        foreach (var skippedMutantGroup in skippedMutantGroups)
        {
            _logger.LogInformation(
                FormatStatusReasonLogString(skippedMutantGroup.Count(), skippedMutantGroup.Key.ResultStatus),
                skippedMutantGroup.Count(), skippedMutantGroup.Key.ResultStatus, skippedMutantGroup.Key.ResultStatusReason);
        }

        if (skippedMutants.Any())
        {
            _logger.LogInformation(
                LeftPadAndFormatForMutantCount(skippedMutants.Count(), "total mutants are skipped for the above mentioned reasons"),
                skippedMutants.Count());
        }

        var notRunMutantsWithResultStatusReason = allMutants
            .Where(m => m.ResultStatus == MutantStatus.Pending && !string.IsNullOrEmpty(m.ResultStatusReason))
            .GroupBy(x => x.ResultStatusReason);

        foreach (var notRunMutantReason in notRunMutantsWithResultStatusReason)
        {
            _logger.LogInformation(
                LeftPadAndFormatForMutantCount(notRunMutantReason.Count(), "mutants will be tested because: {1}"),
                notRunMutantReason.Count(),
                notRunMutantReason.Key);
        }

        var notRunCount = allMutants.Count(m => m.ResultStatus == MutantStatus.Pending);

        // Check if we have Higher-Order Mutants
        var higherOrderMutants = allMutants.OfType<HigherOrderMutant>().ToList();
        var regularMutants = allMutants.Where(m => !(m is HigherOrderMutant)).ToList();
        
        if (higherOrderMutants.Any())
        {
            var homPendingCount = higherOrderMutants.Count(m => m.ResultStatus == MutantStatus.Pending);
            var fomPendingCount = regularMutants.Count(m => m.ResultStatus == MutantStatus.Pending);
            
            _logger.LogInformation(LeftPadAndFormatForMutantCount(fomPendingCount, "first-order mutants will be tested"), fomPendingCount);
            _logger.LogInformation(LeftPadAndFormatForMutantCount(homPendingCount, "higher-order mutants will be tested"), homPendingCount);
            
            // Show HOM composition statistics
            if (homPendingCount > 0)
            {
                var avgOrder = higherOrderMutants.Where(h => h.ResultStatus == MutantStatus.Pending).Average(h => h.Order);
                var maxOrder = higherOrderMutants.Where(h => h.ResultStatus == MutantStatus.Pending).Max(h => h.Order);
                _logger.LogInformation("Higher-Order Mutants: Average order {0:F1}, Maximum order {1}", avgOrder, maxOrder);
                
                var totalConstituentMutants = higherOrderMutants
                    .Where(h => h.ResultStatus == MutantStatus.Pending)
                    .SelectMany(h => h.ConstituentMutants)
                    .Distinct()
                    .Count();
                    
                _logger.LogInformation("Higher-Order Mutants cover {0} unique first-order mutants", totalConstituentMutants);
            }
        }

        _logger.LogInformation(LeftPadAndFormatForMutantCount(notRunCount, "total mutants will be tested"), notRunCount);
    }

    private string FormatStatusReasonLogString(int mutantCount, MutantStatus resultStatus)
    {
        // Pad for status CompileError length
        var padForResultStatusLength = 13 - resultStatus.ToString().Length;

        var formattedString = LeftPadAndFormatForMutantCount(mutantCount, "mutants got status {1}.");
        formattedString += "Reason: {2}".PadLeft(11 + padForResultStatusLength);

        return formattedString;
    }

    private string LeftPadAndFormatForMutantCount(int mutantCount, string logString)
    {
        // Pad for max 5 digits mutant amount
        var padLengthForMutantCount = 5 - mutantCount.ToString().Length;
        return "{0} " + logString.PadLeft(logString.Length + padLengthForMutantCount);
    }
}
