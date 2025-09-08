using Microsoft.CodeAnalysis;
using Stryker.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that favors FOMs with high mutual dependency (CPDA - Causal Program Dependence Analysis).
    /// Research suggests that mutants with high coupling are more likely to form SSHOMs.
    /// This is primarily a FITNESS SCORING heuristic.
    /// </summary>
    public class DependencyHeuristic : BaseHOMHeuristic
    {
        /// <summary>
        /// Cache of dependency relationships between mutants
        /// Key: Mutant ID, Value: Set of mutant IDs that this mutant depends on or is dependent upon
        /// </summary>
        private readonly Dictionary<int, HashSet<int>> _dependencyGraph = new();
        
        public override string Name => "Dependency";
        
        public override double Weight => 2.0;
        
        public override bool IsFitnessScoringHeuristic => true;
        
        protected override void OnInitialized()
        {
            _dependencyGraph.Clear();
            
            // Build a map of file paths to mutants in those files
            var fileToMutants = new Dictionary<string, List<IMutant>>();
            foreach (var mutant in AvailableFOMs)
            {
                if (mutant?.Mutation?.OriginalNode != null)
                {
                    var location = mutant.Mutation.OriginalNode.GetLocation();
                    var filePath = location?.SourceTree?.FilePath;
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        filePath = Path.GetFullPath(filePath);
                        if (!fileToMutants.TryGetValue(filePath, out var mutants))
                        {
                            mutants = new List<IMutant>();
                            fileToMutants[filePath] = mutants;
                        }
                        mutants.Add(mutant);
                    }
                }
            }
            
            // For each mutant, analyze dependencies with other mutants
            foreach (var mutant in AvailableFOMs)
            {
                if (mutant?.Mutation?.OriginalNode == null) 
                    continue;
                    
                var location = mutant.Mutation.OriginalNode.GetLocation();
                var filePath = location?.SourceTree?.FilePath;
                if (string.IsNullOrEmpty(filePath))
                    continue;
                    
                filePath = Path.GetFullPath(filePath);
                var dependencies = new HashSet<int>();
                
                // Add immediate dependencies within the same file
                // In a real implementation, this would use more sophisticated code analysis
                // to determine actual dependencies between code elements
                if (fileToMutants.TryGetValue(filePath, out var mutantsInFile))
                {
                    // Heuristic: Mutants close to each other in the same file likely have dependencies
                    var lineNumber = mutant.Mutation.OriginalNode.GetLocation().GetLineSpan().StartLinePosition.Line;
                    var closeByMutants = mutantsInFile
                        .Where(m => m.Id != mutant.Id && 
                               m.Mutation?.OriginalNode != null &&
                               IsWithinScope(lineNumber, 
                                 m.Mutation.OriginalNode.GetLocation().GetLineSpan().StartLinePosition.Line, 15));
                               
                    foreach (var closeByMutant in closeByMutants)
                    {
                        dependencies.Add(closeByMutant.Id);
                    }
                }
                
                // Store the dependency relationships
                _dependencyGraph[mutant.Id] = dependencies;
            }
        }
        
        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count < 2)
            {
                return 0.0;
            }

            // Calculate how many dependencies exist between the mutants in this candidate
            int totalConnections = 0;
            int possibleConnections = 0;
            
            for (int i = 0; i < candidate.Count; i++)
            {
                var mutantId = candidate[i].Id;
                
                if (_dependencyGraph.TryGetValue(mutantId, out var dependencies))
                {
                    // Count connections to other mutants in the candidate
                    for (int j = i + 1; j < candidate.Count; j++)
                    {
                        var otherId = candidate[j].Id;
                        if (dependencies.Contains(otherId))
                        {
                            totalConnections++;
                        }
                        possibleConnections++;
                    }
                }
            }
            
            if (possibleConnections == 0)
            {
                return 0.5; // Neutral score if we can't determine dependencies
            }
            
            // Calculate connection density (0 to 1)
            return NormalizeScore((double)totalConnections / possibleConnections);
        }
        
        /// <summary>
        /// Determines whether two line numbers are within a specified distance of each other.
        /// </summary>
        /// <param name="line1">The first line number.</param>
        /// <param name="line2">The second line number.</param>
        /// <param name="maxDistance">The maximum distance between lines to be considered "within scope".</param>
        /// <returns>True if the lines are within scope of each other, false otherwise.</returns>
        private bool IsWithinScope(int line1, int line2, int maxDistance)
        {
            return System.Math.Abs(line1 - line2) <= maxDistance;
        }
    }
}
