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
        
        /// <summary>
        /// Gets the name of this heuristic.
        /// </summary>
        public override string Name => "MutatorType";
        
        /// <summary>
        /// Medium weight for mutation type preference
        /// </summary>
        public override double Weight => 1.5;
        
        /// <summary>
        /// This is purely a fitness scoring heuristic.
        /// </summary>
        public override bool IsFitnessScoringHeuristic => true;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="MutatorTypeHeuristic"/> class with default
        /// preferred mutation types based on research.
        /// </summary>
        public MutatorTypeHeuristic() : this(new[]
        {
            // These are examples - in a real implementation, these would match actual mutation types used in Stryker
            "RelationalOperator", 
            "ConditionalExpression",
            "BlockStatement",
            "Statement"
        })
        {
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
        
        /// <summary>
        /// Performs additional initialization to calculate scores for each FOM based on its mutation type.
        /// </summary>
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
        
        /// <summary>
        /// Scores a candidate HOM based on how many of its FOMs have preferred mutation types.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>A score between 0.0 and 1.0, with higher values for candidates with preferred mutation types.</returns>
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
