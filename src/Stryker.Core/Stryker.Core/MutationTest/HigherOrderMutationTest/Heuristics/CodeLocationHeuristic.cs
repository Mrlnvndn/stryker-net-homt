using Microsoft.CodeAnalysis;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Heuristic that scores candidates based on whether their FOMs come from the same method or class.
    /// This is primarily a FITNESS SCORING heuristic that evaluates mutant location proximity.
    /// </summary>
    public class CodeLocationHeuristic : BaseHOMHeuristic
    {
        private readonly Dictionary<IMutant, string> _classMap = new();
        private readonly Dictionary<IMutant, string> _methodMap = new();

        public override string Name => "CodeLocation";

        public override double Weight => 1.5;

        public override bool IsSearchStrategyHeuristic => false;

        public override bool IsFitnessScoringHeuristic => true;

        protected override void OnInitialized()
        {
            foreach (var fom in AvailableFOMs)
            {
                // Extract location information from the mutation's original node
                // Using DisplayName as fallback for grouping
                if (fom?.Mutation?.OriginalNode != null)
                {
                    var originalNode = fom.Mutation.OriginalNode;
                    var location = originalNode.GetLocation();
                    var filePath = location?.SourceTree?.FilePath;

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var className = Path.GetFileNameWithoutExtension(filePath);
                        
                        // Use span start position as part of method identifier
                        // In a real implementation, you would use syntax analysis to get the actual method
                        var position = location.SourceSpan.Start;
                        string methodName = $"{className}_{position}";
                        
                        _classMap[fom] = className;
                        _methodMap[fom] = methodName;
                    }
                    else
                    {
                        // Fallback to using DisplayName
                        var parts = fom.Mutation.DisplayName?.Split(':');
                        if (parts?.Length > 1)
                        {
                            _classMap[fom] = parts[0];
                            _methodMap[fom] = fom.Mutation.DisplayName;
                        }
                    }
                }
            }
        }

        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count <= 1)
            {
                return 0; // Single mutant or empty candidate gets top score
            }

            // Check if all FOMs are from the same method
            var allSameMethod = true;
            var firstMethod = _methodMap.ContainsKey(candidate[0]) ? _methodMap[candidate[0]] : null;
            
            for (int i = 1; i < candidate.Count && allSameMethod; i++)
            {
                if (!_methodMap.ContainsKey(candidate[i]) || 
                    _methodMap[candidate[i]] != firstMethod)
                {
                    allSameMethod = false;
                }
            }

            if (allSameMethod && firstMethod != null)
            {
                return 1.0; // All FOMs from same method gets top score
            }

            // Check if all FOMs are at least from the same class
            bool allSameClass = true;
            string firstClass = _classMap.ContainsKey(candidate[0]) ? _classMap[candidate[0]] : null;
            
            for (int i = 1; i < candidate.Count && allSameClass; i++)
            {
                if (!_classMap.ContainsKey(candidate[i]) || 
                    _classMap[candidate[i]] != firstClass)
                {
                    allSameClass = false;
                }
            }

            if (allSameClass && firstClass != null)
            {
                return 0.8; // All FOMs from same class gets good score
            }

            // Calculate proportion of FOMs from the same class
            var classCount = new Dictionary<string, int>();
            foreach (var mutant in candidate)
            {
                if (_classMap.TryGetValue(mutant, out var className) && !string.IsNullOrEmpty(className))
                {
                    if (!classCount.ContainsKey(className))
                    {
                        classCount[className] = 0;
                    }
                    classCount[className]++;
                }
            }
                
            if (classCount.Count > 0)
            {
                double maxClassCount = classCount.Values.Max();
                double classRatio = maxClassCount / candidate.Count;
                return NormalizeScore(0.2 + (classRatio * 0.5)); // Scale score based on class grouping
            }

            return 0.2; // FOMs scattered across different classes gets lower score
        }
    }
}
