using Microsoft.CodeAnalysis;
using Stryker.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that prevents Higher-Order Mutants from containing multiple mutants
    /// that target the same SyntaxNode, which would cause conflicts in the mutant schemata.
    /// This is a FILTERING heuristic that rejects candidates with conflicting mutants.
    /// 
    /// Example conflict scenario:
    /// If two mutants both target the same "x > y" expression (changing it to "x < y" and "x >= y"),
    /// activating both in a HOM would result in undefined behavior in the conditional expression.
    /// </summary>
    public class SyntaxNodeConflictHeuristic : BaseHOMHeuristic
    {
        public override string Name => "SyntaxNodeConflict";
        
        public override double Weight => 0.0; // Not a scoring heuristic
        
        public override bool IsFilteringHeuristic => true;
        
        public override bool IsFitnessScoringHeuristic => true;

        public override double ScoreCandidate(List<IMutant> candidate) => ShouldFilterCandidate(candidate) ? 0 : 1;

        public override bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count < 2)
            {
                return false; // Single mutants can't conflict with themselves
            }
            
            // Group mutants by their original syntax nodes
            var nodeGroups = candidate
                .Where(m => m.Mutation?.OriginalNode != null)
                .GroupBy(m => m.Mutation.OriginalNode)
                .ToList();
            
            // Check if any syntax node has multiple mutants targeting it
            var conflictingGroups = nodeGroups.Where(group => group.Count() > 1).ToList();
            
            if (conflictingGroups.Any())
            {
                // Log the conflicts for debugging
                foreach (var conflictGroup in conflictingGroups)
                {
                    var mutantIds = string.Join(", ", conflictGroup.Select(m => m.Id));
                    var nodeText = conflictGroup.Key.ToString().Trim();
                    if (nodeText.Length > 50)
                    {
                        nodeText = nodeText.Substring(0, 47) + "...";
                    }
                    
                    System.Diagnostics.Debug.WriteLine(
                        $"SyntaxNodeConflictHeuristic: Filtering HOM candidate - " +
                        $"Mutants [{mutantIds}] all target the same SyntaxNode: '{nodeText}'");
                }
                
                return true; // Filter out this candidate
            }
            
            return false; // No conflicts, candidate is acceptable
        }
    }
}
