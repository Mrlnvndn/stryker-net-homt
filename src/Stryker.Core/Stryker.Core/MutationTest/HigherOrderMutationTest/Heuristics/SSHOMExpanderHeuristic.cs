using Stryker.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that guides search by expanding existing SSHOMs with additional FOMs.
    /// This heuristic focuses on search guidance rather than just scoring.
    /// </summary>
    public class SSHOMExpanderHeuristic : BaseHOMHeuristic
    {
        /// <summary>
        /// Tracks potential SSHOMs discovered during search
        /// </summary>
        private readonly List<List<IMutant>> _potentialSSHOMs = new();
        
        /// <summary>
        /// Maximum size of HOM to consider
        /// </summary>
        private readonly int _maxHomSize;
        
        /// <summary>
        /// Gets the name of this heuristic.
        /// </summary>
        public override string Name => "SSHOMExpander";
        
        /// <summary>
        /// This heuristic can actively guide the search process
        /// </summary>
        public override bool CanGuideSearch => true;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="SSHOMExpanderHeuristic"/> class.
        /// </summary>
        /// <param name="maxHomSize">Maximum size of HOMs to consider.</param>
        public SSHOMExpanderHeuristic(int maxHomSize = 4)
        {
            _maxHomSize = maxHomSize;
        }
        
        /// <summary>
        /// Scores a candidate HOM based on how it relates to potential SSHOMs.
        /// </summary>
        /// <param name="candidate">The candidate HOM to evaluate.</param>
        /// <returns>A score between 0.0 and 1.0, with higher values for candidates that are expansions of potential SSHOMs.</returns>
        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count < 2)
            {
                return 0.0;
            }
            
            // Check if this candidate is similar to a potential SSHOM we're tracking
            var bestSimilarity = 0.0;
            foreach (var sshom in _potentialSSHOMs)
            {
                var similarity = CalculateSimilarity(sshom, candidate);
                bestSimilarity = System.Math.Max(bestSimilarity, similarity);
            }
            
            // If this is a promising candidate, add it to our potential SSHOMs list
            // This avoids duplicate checks from the SuggestNextCandidates method
            if (IsPotentialSSHOM(candidate) && candidate.Count < _maxHomSize)
            {
                // Check if we already have something very similar before adding
                bool isDuplicate = _potentialSSHOMs.Any(s => CalculateSimilarity(s, candidate) > 0.8);
                
                if (!isDuplicate)
                {
                    _potentialSSHOMs.Add(new List<IMutant>(candidate));
                }
            }
            
            return NormalizeScore(bestSimilarity);
        }
        
        /// <summary>
        /// Suggests next candidates to explore by expanding potential SSHOMs.
        /// </summary>
        /// <param name="currentCandidate">The current candidate HOM.</param>
        /// <param name="availableFOMs">Available first-order mutants to consider.</param>
        /// <returns>A list of suggested candidate HOMs to explore next.</returns>
        public override List<List<IMutant>> SuggestNextCandidates(List<IMutant> currentCandidate, IReadOnlyCollection<IMutant> availableFOMs)
        {
            var suggestedCandidates = new List<List<IMutant>>();
            
            // Don't expand if we're already at max size
            if (currentCandidate == null || currentCandidate.Count >= _maxHomSize)
            {
                return suggestedCandidates;
            }
            
            // Only expand potential SSHOMs
            if (IsPotentialSSHOM(currentCandidate))
            {
                // Get existing IDs to avoid duplicates
                var existingIds = new HashSet<int>(currentCandidate.Select(m => m.Id));
                
                // Find a few promising FOMs to add
                var candidatesToAdd = availableFOMs
                    .Where(fom => !existingIds.Contains(fom.Id))
                    .OrderBy(_ => System.Guid.NewGuid()) // Simple randomization
                    .Take(3); // Limit to a few suggestions to avoid explosion
                    
                foreach (var fomToAdd in candidatesToAdd)
                {
                    var newCandidate = new List<IMutant>(currentCandidate)
                    {
                        fomToAdd
                    };
                    
                    suggestedCandidates.Add(newCandidate);
                }
                
                // If this is a newly found potential SSHOM, add it to our tracking list
                bool isNewSSHOM = !_potentialSSHOMs.Any(s => CalculateSimilarity(s, currentCandidate) > 0.8);
                if (isNewSSHOM)
                {
                    _potentialSSHOMs.Add(new List<IMutant>(currentCandidate));
                }
            }
            
            return suggestedCandidates;
        }
        
        /// <summary>
        /// Determines whether a candidate is a potential SSHOM based on simplified criteria.
        /// In a real implementation, this would use more sophisticated analysis of test results.
        /// </summary>
        /// <param name="candidate">The candidate to evaluate.</param>
        /// <returns>True if the candidate is a potential SSHOM, false otherwise.</returns>
        private bool IsPotentialSSHOM(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count < 2)
            {
                return false;
            }
            
            // This is a simplified implementation. In a real implementation, this would analyze
            // test results to determine whether the HOM is strictly dominated by its constituent FOMs.
            
            // The simplified approach: if all mutants have killing tests and there's some overlap,
            // then it might be a potential SSHOM
            bool allHaveKillingTests = candidate.All(m => m.KillingTests != null && !m.KillingTests.IsEmpty);
            
            if (!allHaveKillingTests)
            {
                return false;
            }
            
            // Look at killing test overlap - in a real implementation this would be more sophisticated
            // Since we don't have access to the actual test IDs in this interface implementation,
            // we'll use a simple heuristic: if both have killing tests, assume there might be overlap
            // This is just a placeholder until we can implement proper test intersection logic
            bool hasOverlap = candidate.Skip(1).Any(m => 
                m.KillingTests != null && 
                !m.KillingTests.IsEmpty && 
                candidate[0].KillingTests != null && 
                !candidate[0].KillingTests.IsEmpty);
                
            // In a real implementation, this would check actual test intersection like:
            // return m.KillingTests.GetTestIds().Any(id => firstKillingTests.GetTestIds().Contains(id));
                
            return hasOverlap;
        }
        
        /// <summary>
        /// Calculates a similarity score between two candidate HOMs.
        /// </summary>
        /// <param name="candidate1">The first candidate.</param>
        /// <param name="candidate2">The second candidate.</param>
        /// <returns>A similarity score between 0.0 and 1.0, where 1.0 means identical.</returns>
        private double CalculateSimilarity(List<IMutant> candidate1, List<IMutant> candidate2)
        {
            if (candidate1 == null || candidate2 == null || candidate1.Count == 0 || candidate2.Count == 0)
            {
                return 0.0;
            }
            
            var ids1 = new HashSet<int>(candidate1.Select(m => m.Id));
            var ids2 = new HashSet<int>(candidate2.Select(m => m.Id));
            
            // Jaccard similarity: intersection / union
            var intersection = ids1.Intersect(ids2).Count();
            var union = ids1.Count + ids2.Count - intersection;
            
            return (double)intersection / union;
        }
    }
}
