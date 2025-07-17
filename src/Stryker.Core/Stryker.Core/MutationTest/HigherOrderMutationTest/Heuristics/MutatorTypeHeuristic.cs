using Stryker.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that prioritizes certain mutation types that research suggests are more likely to form SSHOMs,
    /// such as relational operation removals and statement block removals.
    /// This is primarily a FITNESS SCORING heuristic.
    /// </summary>
    public class MutatorTypeHeuristic : BaseHOMHeuristic
    {
        /// <summary>
        /// The set of preferred mutation types
        /// </summary>
        private readonly HashSet<string> _preferredMutationTypes;
        
        /// <summary>
        /// Cache of mutant type scores
        /// </summary>
        private readonly Dictionary<int, double> _mutantScores = new();
        
        public override string Name => "MutatorType";
        
        public override double Weight => 1.5;
        
        public override bool IsFitnessScoringHeuristic => true;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="MutatorTypeHeuristic"/> class with default
        /// preferred mutation types based on research.
        /// </summary>
        public MutatorTypeHeuristic() : this([])
        {
            // TODO: fill _defaultPreferredMutationTypes with mutation types that are known to be more likely to form SSHOMs 
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MutatorTypeHeuristic"/> class with the specified
        /// preferred mutation types.
        /// </summary>
        /// <param name="preferredMutationTypes">A collection of mutation type names to prioritize.</param>
        public MutatorTypeHeuristic(IEnumerable<string> preferredMutationTypes)
        {
            _preferredMutationTypes = new HashSet<string>(preferredMutationTypes);
        }
        
        protected override void OnInitialized()
        {
            _mutantScores.Clear();
            
            foreach (var mutant in AvailableFOMs)
            {
                string mutationType = GetMutationType(mutant);
                
                // Higher score for preferred mutation types
                double score = IsMutationTypePreferred(mutationType) ? 1.0 : 0.3;
                _mutantScores[mutant.Id] = score;
            }
        }
        
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
                return 0.5; // Neutral score if we can't determine mutation types
            }
            
            return NormalizeScore(totalScore / scoredMutants);
        }
        
        /// <summary>
        /// Determines whether the specified mutation type is preferred.
        /// </summary>
        /// <param name="mutationType">The mutation type to check.</param>
        /// <returns>True if the mutation type is preferred, false otherwise.</returns>
        private bool IsMutationTypePreferred(string mutationType)
        {
            return _preferredMutationTypes.Contains(mutationType);
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
    }
}
