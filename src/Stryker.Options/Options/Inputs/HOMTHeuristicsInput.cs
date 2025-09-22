using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions.Exceptions;
using Stryker.Abstractions.Options;

namespace Stryker.Abstractions.Options.Inputs;

public class HOMTHeuristicsInput : Input<IEnumerable<string>>
{
    public override IEnumerable<string> Default => new[] { "all" };

    protected override string Description => @"Select HOMT heuristics to use. Specify 'all' to use all heuristics, or provide a comma-separated list.
Available heuristics: CodeLocation, EmptyAssessingTests, MutatorType, MaxSizeLimit, OverlappingTests, SyntaxNodeConflict.
Example: ['CodeLocation', 'MutatorType'] or ['all']";

    protected override IEnumerable<string> AllowedOptions => new[] { "all", "CodeLocation", "EmptyAssessingTests", "MutatorType", "MaxSizeLimit", "OverlappingTests", "SyntaxNodeConflict" };

    public IEnumerable<HOMTHeuristicKind> Validate()
    {
        var input = SuppliedInput ?? Default;
        var heuristics = new List<HOMTHeuristicKind>();

        if (input.Any(h => h.Equals("all", System.StringComparison.OrdinalIgnoreCase)))
        {
            // Return all available heuristics
            return System.Enum.GetValues<HOMTHeuristicKind>();
        }

        foreach (var heuristicName in input)
        {
            if (System.Enum.TryParse<HOMTHeuristicKind>(heuristicName, true, out var heuristic))
            {
                heuristics.Add(heuristic);
            }
            else
            {
                var availableNames = string.Join(", ", System.Enum.GetNames<HOMTHeuristicKind>());
                throw new InputException($"Unknown HOMT heuristic '{heuristicName}'. Available: all, {availableNames}.");
            }
        }

        return heuristics.Distinct();
    }
}
