using Stryker.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that filters out FOMs with weak mutators that are easily killed by many tests.
    /// Research suggests that some mutation operators consistently produce weak mutants,
    /// which are less likely to contribute to valuable SSHOMs.
    /// This is primarily a FILTERING heuristic but also provides fitness scoring.
    /// </summary>
    public class WeakMutatorFilterHeuristic : BaseHOMHeuristic
    {
        /// <summary>
        /// Stores MSI (Mutation Score Indicator) and statistics for each mutator type
        /// </summary>
        private readonly Dictionary<Mutator, MutatorStatistics> _mutatorStatistics = new();
        
        /// <summary>
        /// Threshold below which a mutant should be filtered out based on MSI
        /// </summary>
        private readonly double _filterThreshold;

        public override string Name => "WeakMutatorFilter";

        public override bool IsFilteringHeuristic => true;

        public override bool IsFitnessScoringHeuristic => true;

        public override bool RequiresPreRun => true;

        /// <summary>
        /// Initializes a new instance of the <see cref="WeakMutatorFilterHeuristic"/> class with default threshold.
        /// </summary>
        /// <param name="filterThreshold">MSI threshold below which FOMs should be filtered out (0.0-100.0).</param>
        public WeakMutatorFilterHeuristic(double filterThreshold = 30.0)
        {
            _filterThreshold = filterThreshold;
        }

        protected override void OnInitialized()
        {
            _mutatorStatistics.Clear();
            
            // Group mutants by mutation type
            var mutantsByType = new Dictionary<Mutator, List<IMutant>>();
            
            foreach (var mutant in AvailableFOMs)
            {
                var mutationType = mutant.Mutation.Type;

                if (!mutantsByType.TryGetValue(mutationType, out var mutantsOfType))
                {
                    mutantsOfType = new List<IMutant>();
                    mutantsByType[mutationType] = mutantsOfType;
                }
                
                mutantsOfType.Add(mutant);
            }
            
            // Calculate MSI for each mutation type
            foreach (var typeEntry in mutantsByType)
            {
                var mutationType = typeEntry.Key;
                var mutantsOfType = typeEntry.Value;
                
                if (mutantsOfType.Count == 0)
                {
                    continue;
                }
                
                var totalMutants = mutantsOfType.Count;
                var killedMutants = mutantsOfType.Count(mutant => mutant.ResultStatus == MutantStatus.Killed);
                
                // MSI = 100 * (D / N) where D is killed mutants and N is total mutants
                var msi = totalMutants > 0 ? 100.0 * killedMutants / totalMutants : 0.0;
                
                _mutatorStatistics[mutationType] = new MutatorStatistics
                {
                    MSI = msi,
                    TotalFOMs = totalMutants,
                    KilledFOMs = killedMutants
                };
            }
        }
        
        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0)
            {
                return 0.0;
            }
            
            var totalScore = 0.0;
            var scoredMutants = 0;
            
            foreach (var mutant in candidate)
            {
                var mutationType = mutant.Mutation.Type;
                
                if (_mutatorStatistics.TryGetValue(mutationType, out var statistics))
                {
                    // Score based on MSI (normalized to 0.0-1.0)
                    totalScore += statistics.MSI / 100.0;
                    scoredMutants++;
                }
            }
            
            if (scoredMutants == 0)
            {
                return 0.5; // Neutral score if we can't score any mutant
            }
            
            return NormalizeScore(totalScore / scoredMutants);
        }
        
        public override bool ShouldFilterCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0)
            {
                return true;
            }
            
            // Filter out candidates containing mutants with MSI below the threshold
            foreach (var mutant in candidate)
            {
                var mutationType = mutant.Mutation.Type;
                
                if (_mutatorStatistics.TryGetValue(mutationType, out var statistics) && 
                    statistics.MSI < _filterThreshold)
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Statistics for a specific mutator type
        /// </summary>
        private class MutatorStatistics
        {
            /// <summary>
            /// Mutation Score Indicator: 100 * (killed mutants / total mutants)
            /// </summary>
            public double MSI { get; set; }
            
            /// <summary>
            /// Total number of FOMs for this mutator type
            /// </summary>
            public int TotalFOMs { get; set; }
            
            /// <summary>
            /// Number of killed FOMs for this mutator type
            /// </summary>
            public int KilledFOMs { get; set; }
        }
    }
}
