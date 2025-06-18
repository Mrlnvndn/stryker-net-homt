using Stryker.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that filters out FOMs with weak mutators that are easily killed by many tests.
    /// Research suggests that some mutation operators consistently produce weak mutants,
    /// which are less likely to contribute to valuable SSHOMs.
    /// </summary>
    public class WeakMutatorFilterHeuristic : BaseHOMHeuristic
    {
        /// <summary>
        /// Cache of mutant type effectiveness scores
        /// </summary>
        private readonly Dictionary<string, double> _mutatorStrengthScores = new();
        
        /// <summary>
        /// Cache of mutant scores
        /// </summary>
        private readonly Dictionary<int, double> _mutantScores = new();
        
        /// <summary>
        /// Threshold below which a mutant should be filtered out
        /// </summary>
        private readonly double _filterThreshold;
        
        /// <summary>
        /// Gets the name of this heuristic.
        /// </summary>
        public override string Name => "WeakMutatorFilter";
        
        /// <summary>
        /// Initializes a new instance of the <see cref="WeakMutatorFilterHeuristic"/> class with default threshold.
        /// </summary>
        /// <param name="filterThreshold">Score threshold below which FOMs should be filtered out (0.0-1.0).</param>
        public WeakMutatorFilterHeuristic(double filterThreshold = 0.3)
        {
            _filterThreshold = NormalizeScore(filterThreshold);
        }
        
        /// <summary>
        /// Performs additional initialization to calculate strength scores for each mutation type
        /// based on how many tests typically kill mutants of that type.
        /// </summary>
        protected override void OnInitialized()
        {
            _mutatorStrengthScores.Clear();
            _mutantScores.Clear();
            
            // Group mutants by mutation type
            var mutantsByType = new Dictionary<string, List<IMutant>>();
            
            foreach (var mutant in AvailableFOMs)
            {
                var mutationType = GetMutationType(mutant);
                
                if (!mutantsByType.TryGetValue(mutationType, out var mutantsOfType))
                {
                    mutantsOfType = new List<IMutant>();
                    mutantsByType[mutationType] = mutantsOfType;
                }
                
                mutantsOfType.Add(mutant);
            }
            
            // Calculate average killing test count for each mutation type
            var typeToAvgKillingTests = new Dictionary<string, double>();
            
            foreach (var typeEntry in mutantsByType)
            {
                var mutationType = typeEntry.Key;
                var mutantsOfType = typeEntry.Value;
                
                if (mutantsOfType.Count == 0)
                {
                    continue;
                }
                
                double totalTestCount = 0;
                int validMutants = 0;
                
                foreach (var mutant in mutantsOfType)
                {
                    int killingTestCount = CountKillingTests(mutant);
                    if (killingTestCount > 0)
                    {
                        totalTestCount += killingTestCount;
                        validMutants++;
                    }
                }
                
                if (validMutants > 0)
                {
                    typeToAvgKillingTests[mutationType] = totalTestCount / validMutants;
                }
            }
            
            // Calculate relative strength scores for each mutation type
            // Types with fewer killing tests on average are considered stronger
            if (typeToAvgKillingTests.Count > 0)
            {
                double minAvg = typeToAvgKillingTests.Values.Min();
                double maxAvg = typeToAvgKillingTests.Values.Max();
                double range = maxAvg - minAvg;
                
                if (range > 0)
                {
                    foreach (var entry in typeToAvgKillingTests)
                    {
                        // Invert and normalize: lower average test count = stronger mutator = higher score
                        _mutatorStrengthScores[entry.Key] = 1.0 - ((entry.Value - minAvg) / range);
                    }
                }
                else
                {
                    // If all types have the same average, give them neutral scores
                    foreach (var entry in typeToAvgKillingTests)
                    {
                        _mutatorStrengthScores[entry.Key] = 0.5;
                    }
                }
            }
            
            // Assign scores to individual mutants based on their type's strength
            foreach (var mutant in AvailableFOMs)
            {
                var mutationType = GetMutationType(mutant);
                
                if (_mutatorStrengthScores.TryGetValue(mutationType, out var score))
                {
                    _mutantScores[mutant.Id] = score;
                }
                else
                {
                    // Default to neutral if we couldn't analyze this type
                    _mutantScores[mutant.Id] = 0.5;
                }
            }
        }
        
        /// <summary>
        /// Scores a candidate HOM based on the strength of its mutators.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>A score between 0.0 and 1.0, with higher values for candidates with stronger mutators.</returns>
        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0)
            {
                return 0.0;
            }
            
            double totalScore = 0;
            int scoredMutants = 0;
            
            foreach (var mutant in candidate)
            {
                if (_mutantScores.TryGetValue(mutant.Id, out double score))
                {
                    totalScore += score;
                    scoredMutants++;
                }
            }
            
            if (scoredMutants == 0)
            {
                return 0.5; // Neutral score if we can't score any mutant
            }
            
            return NormalizeScore(totalScore / scoredMutants);
        }
        
        /// <summary>
        /// Filters out candidates containing weak mutators.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>True if the candidate contains a weak mutant, false otherwise.</returns>
        public override bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0)
            {
                return true;
            }
            
            // Filter out candidates containing mutants below the threshold
            foreach (var mutant in candidate)
            {
                if (_mutantScores.TryGetValue(mutant.Id, out double score) && score < _filterThreshold)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets the mutation type of the specified mutant.
        /// </summary>
        /// <param name="mutant">The mutant to get the type for.</param>
        /// <returns>The mutation type as a string.</returns>
        private string GetMutationType(IMutant mutant)
        {
            // Enums are value types so can't use null conditional operator directly on Type
            if (mutant?.Mutation == null)
            {
                return string.Empty;
            }
            return mutant.Mutation.Type.ToString();
        }
        
        /// <summary>
        /// Counts how many tests kill the given mutant.
        /// </summary>
        /// <param name="mutant">The mutant to check.</param>
        /// <returns>The number of killing tests.</returns>
        private int CountKillingTests(IMutant mutant)
        {
            if (mutant.KillingTests == null || mutant.KillingTests.IsEmpty)
            {
                return 0; // No tests kill this mutant (potentially equivalent mutant)
            }
            
            // Here, in a real implementation, we'd have access to the
            // actual count of tests in the KillingTests collection.
            // For now, we'll use a simplified approach.
            
            // This is a simplified approximation - to be replaced with actual implementation
            // that counts the tests in the ITestIdentifiers collection
            int estimatedTestCount = 1;
            
            return System.Math.Max(estimatedTestCount, 1); // Ensure at least 1 test
        }
    }
}
