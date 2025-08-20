using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Spectre.Console;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.ProjectComponents;
using Stryker.Abstractions.Reporting;
using Stryker.Core.Mutants;
using Stryker.Core.ProjectComponents;
using Stryker.Core.ProjectComponents.TestProjects;

namespace Stryker.Core.Reporters;

/// <summary>
/// The clear text tree reporter, prints a tree structure with results.
/// </summary>
public class ClearTextTreeReporter : IReporter
{
    private readonly IStrykerOptions _options;
    private readonly IAnsiConsole _console;

    public ClearTextTreeReporter(IStrykerOptions strykerOptions, IAnsiConsole console = null)
    {
        _options = strykerOptions;
        _console = console ?? AnsiConsole.Console;
    }

    public void OnMutantsCreated(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo)
    {
        // This reporter does not report during the testrun
    }

    public void OnStartMutantTestRun(IEnumerable<IReadOnlyMutant> mutantsToBeTested)
    {
        // This reporter does not report during the testrun
    }

    public void OnMutantTested(IReadOnlyMutant result)
    {
        // This reporter does not report during the testrun
    }

    public void OnAllMutantsTested(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo)
    {
        Tree root = null;

        var stack = new Stack<IHasTreeNodes>();

        // setup display handlers
        reportComponent.DisplayFolder = (current) =>
        {
            var name = Path.GetFileName(current.RelativePath);

            if (root is null)
            {
                root = new Tree("All files" + DisplayComponent(current));
                stack.Push(root);
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                stack.Push(stack.Peek().AddNode(name + DisplayComponent(current)));
            }
        };

        reportComponent.DisplayFile = (current) =>
        {
            var name = Path.GetFileName(current.RelativePath);

            var fileNode = stack.Peek().AddNode(name + DisplayComponent(current));

            if (current.FullPath == current.Parent.Children.Last().FullPath)
            {
                stack.Pop();
            }

            var totalMutants = current.TotalMutants();
            foreach (var mutant in totalMutants)
            {
                var status = mutant.ResultStatus switch
                {
                    MutantStatus.Killed or MutantStatus.Timeout => $"[Green][[{mutant.ResultStatus}]][/]",
                    MutantStatus.NoCoverage => $"[Yellow][[{mutant.ResultStatus}]][/]",
                    _ => $"[Red][[{mutant.ResultStatus}]][/]",
                };

                var mutantLine = mutant.Mutation.OriginalNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                
                // Add HOM indicator for Higher-Order Mutants
                var mutantType = mutant is HigherOrderMutant hom ? $"[Cyan]HOM-{hom.Order}[/] " : "";
                var mutantNode = fileNode.AddNode(status + $" {mutantType}{mutant.Mutation.DisplayName} on line {mutantLine}");
                
                mutantNode.AddNode(Markup.Escape($"[-] {mutant.Mutation.OriginalNode}"));
                mutantNode.AddNode(Markup.Escape($"[+] {mutant.Mutation.ReplacementNode}"));
                
                // For Higher-Order Mutants, show constituent mutant information
                if (mutant is HigherOrderMutant higherOrderMutant)
                {
                    var constituentNode = mutantNode.AddNode($"[Dim]Constituent mutants: {string.Join(", ", higherOrderMutant.ConstituentMutants.Select(m => m.Id.ToString()))}[/]");
                }
            }
        };

        // print empty line for readability
        _console.WriteLine();
        _console.WriteLine();
        _console.WriteLine("All mutants have been tested, and your mutation score has been calculated");

        // start recursive invocation of handlers
        reportComponent.Display();

        _console.Write(root);
        
        // Add Higher-Order Mutant summary after the tree
        var allMutants = reportComponent.TotalMutants().ToList();
        var higherOrderMutants = allMutants.OfType<HigherOrderMutant>().ToList();
        
        if (higherOrderMutants.Any())
        {
            _console.WriteLine();
            _console.MarkupLine("[Bold underline]Higher-Order Mutation Summary:[/]");
            
            var homTotal = higherOrderMutants.Count;
            var homKilled = higherOrderMutants.Count(h => h.ResultStatus == MutantStatus.Killed);
            var homSurvived = higherOrderMutants.Count(h => h.ResultStatus == MutantStatus.Survived);
            var homTimeout = higherOrderMutants.Count(h => h.ResultStatus == MutantStatus.Timeout);
            
            _console.MarkupLine($"Total Higher-Order Mutants: [Cyan]{homTotal}[/]");
            _console.MarkupLine($"  - Killed:   [Green]{homKilled}[/]");
            _console.MarkupLine($"  - Survived: [Red]{homSurvived}[/]");
            _console.MarkupLine($"  - Timeout:  [Yellow]{homTimeout}[/]");
            
            if (higherOrderMutants.Any())
            {
                var avgOrder = higherOrderMutants.Average(h => h.Order);
                var maxOrder = higherOrderMutants.Max(h => h.Order);
                _console.MarkupLine($"Average HOM order: [Cyan]{avgOrder:F1}[/], Maximum: [Cyan]{maxOrder}[/]");
                
                // Show mutation score for HOMs specifically
                var homScore = homTotal > 0 ? (double)homKilled / homTotal : double.NaN;
                if (!double.IsNaN(homScore))
                {
                    var homScoreColor = homScore >= 0.8 ? "Green" : homScore >= 0.6 ? "Yellow" : "Red";
                    _console.MarkupLine($"Higher-Order Mutant Score: [{homScoreColor}]{homScore:P2}[/]");
                }
            }
        }
    }

    private string DisplayComponent(IReadOnlyProjectComponent inputComponent)
    {
        var mutationScore = inputComponent.GetMutationScore();

        var stringBuilder = new StringBuilder();

        // Convert the threshold integer values to decimal values
        stringBuilder.Append($" [[{inputComponent.DetectedMutants().Count()}/{inputComponent.TotalMutants().Count()} ");

        if (inputComponent.IsComponentExcluded(_options.Mutate))
        {
            stringBuilder.Append("[Gray](Excluded)[/]");
        }
        else if (double.IsNaN(mutationScore))
        {
            stringBuilder.Append("[Gray](N/A)[/]");
        }
        else
        {
            // print the score as a percentage
            var scoreText = string.Format("({0:P2})", mutationScore);
            if (inputComponent.CheckHealth(_options.Thresholds) is Health.Good)
            {
                stringBuilder.Append($"[Green]{scoreText}[/]");
            }
            else if (inputComponent.CheckHealth(_options.Thresholds) is Health.Warning)
            {
                stringBuilder.Append($"[Yellow]{scoreText}[/]");
            }
            else if (inputComponent.CheckHealth(_options.Thresholds) is Health.Danger)
            {
                stringBuilder.Append($"[Red]{scoreText}[/]");
            }
        }

        stringBuilder.Append("]]");

        return stringBuilder.ToString();
    }
}
