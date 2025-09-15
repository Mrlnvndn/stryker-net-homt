using Stryker.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that prioritizes FOMs that are harder to kill (killed by fewer tests).
    /// Research suggests that harder-to-kill FOMs are more likely to form SSHOMs.
    /// This is primarily a FITNESS SCORING heuristic.
    /// </summary>
    public class HardToKillHeuristic : BaseHOMHeuristic
    {
        /// <summary>
        /// Cache of test count scores for each mutant (higher score = harder to kill)
        /// </summary>
        private readonly Dictionary<int, double> _mutantScores = [];
        
        /// <summary>
        /// The minimum and maximum number of killing tests found for any mutant
        /// </summary>
        private int _minTests = int.MaxValue;
        private int _maxTests = int.MinValue;
        
        public override string Name => "HardToKill";
        
        public override double Weight => 2.5;
        
        public override bool IsFitnessScoringHeuristic => true;

        public override bool RequiresPreRun => true; // Needs killing test data

        protected override void OnInitialized()
        {
            // Reset statistics
            _minTests = int.MaxValue;
            _maxTests = int.MinValue;
            _mutantScores.Clear();
            
            foreach (var mutant in AvailableFOMs)
            {
                var killingTestCount = CountKillingTests(mutant);
                
                // Update min/max statistics
                _minTests = Math.Min(_minTests, killingTestCount);
                _maxTests = Math.Max(_maxTests, killingTestCount);
                
                // Store score for later use (initially just the count)
                _mutantScores[mutant.Id] = killingTestCount;
            }
            
            // Make sure we have valid min/max
            if (_minTests == int.MaxValue)
            {
                _minTests = 0;
            }
            
            if (_maxTests == int.MinValue)
            {
                _maxTests = 0;
            }
            
            // Normalize scores so that lower killing test counts = higher scores
            if (_maxTests > _minTests)
            {
                foreach (var mutantId in _mutantScores.Keys.ToList())
                {
                    // Invert the scale: 0 = easiest to kill, 1 = hardest to kill
                    var testCount = _mutantScores[mutantId];
                    _mutantScores[mutantId] = 1.0 - (testCount - _minTests) / (_maxTests - _minTests);
                }
            }
        }
        
        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0)
            {
                return 0.0;
            }
            
            // Calculate average difficulty score of the FOMs
            double totalScore = 0;
            var scoredMutants = 0;
            
            foreach (var mutant in candidate)
            {
                if (_mutantScores.TryGetValue(mutant.Id, out var score))
                {
                    totalScore += score;
                    scoredMutants++;
                }
            }
            
            if (scoredMutants == 0)
            {
                return 0.5; // Neutral score if we can't determine difficulty
            }
            
            return NormalizeScore(totalScore / scoredMutants);
        }
        
        /// <summary>
        /// Counts how many tests kill the given mutant.
        /// </summary>
        private static int CountKillingTests(IMutant mutant)
        {
            if (mutant.KillingTests == null || mutant.KillingTests.IsEmpty)
            {
                return 0; // No tests kill this mutant (potentially equivalent mutant)
            }
            
            // Count the actual number of killing tests
            return mutant.KillingTests.Count;
        }
    }
}
