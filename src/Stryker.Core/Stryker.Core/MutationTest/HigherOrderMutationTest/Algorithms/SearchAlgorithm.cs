using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest; // Assuming MutationTestInput is in this namespace
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using System.Text; // Added missing using directive
using Microsoft.CodeAnalysis;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms
{
    /// <summary>
    /// Implements a search-based algorithm for finding Higher-Order Mutants
    /// </summary>
    public class SearchAlgorithm : IHOMSearchAlgorithm
    {
        public string Name => "Search";

        /// <summary>
        /// Generates candidate Higher-Order Mutants (HOMs) by creating all unique pairs of
        /// First-Order Mutants (FOMs), resulting in second-order mutants.
        /// <param name="availableFOMs">The pool of FOMs to select from.</param>
        /// <param name="heuristics">A list of heuristics that guide the search.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="input">Mutation test input for additional context.</param>
        /// <returns>An enumerable of lists, where each inner list represents a candidate HOM that might be a SSHOM.</returns>
        public IEnumerable<List<IMutant>> GenerateCandidates(
            IReadOnlyCollection<IMutant> availableFOMs,
            IReadOnlyList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            MutationTestInput input)
        {
            // Convert the read-only collection to a list for efficient indexed access
            var fomList = availableFOMs.ToList();

            // Track already generated candidates to avoid duplicates
            var generatedCandidates = new HashSet<string>(StringComparer.Ordinal);

            // Maximum mutation order to consider (most SSHOMs are composed of at most 4 FOMs)
            var maxOrder = 4;

            // Optional limit on the number of candidates to generate
            // Assuming a default value since IStrykerOptions doesn't have HigherOrder property in this context
            var maxCandidates = 1000; // Default value
            var generatedCount = 0;

            // Dictionary to store FOMs grouped by their killing tests signature
            var fomsByKillingTests = GroupFomsByKillingTests(fomList);

            // Dictionary to store FOMs grouped by their source location (class, file)
            var fomsByLocation = GroupFomsByLocation(fomList);

            // Previously identified SSHOMs (for N+1 rule) - this would be populated in a real implementation
            // For now, we'll use an empty list as a placeholder
            var previousSSHOMs = new List<List<IMutant>>();

            // Generate candidates using various heuristics

            // 1. First, generate candidates based on Equivalent Test Failures heuristic
            // (FOMs killed by the same set of tests are good SSHOM candidates)
            foreach (var group in fomsByKillingTests.Values.Where(g => g.Count >= 2))
            {
                // Generate combinations within the group, limiting to maxOrder
                for (var order = 2; order <= Math.Min(maxOrder, group.Count); order++)
                {
                    foreach (var candidate in GenerateCombinations(group, order))
                    {
                        string candidateKey = GetCandidateKey(candidate);
                        if (!generatedCandidates.Contains(candidateKey))
                        {
                            generatedCandidates.Add(candidateKey);
                            yield return candidate;

                            generatedCount++;
                            if (generatedCount >= maxCandidates)
                            {
                                yield break;
                            }
                        }
                    }
                }
            }

            // 2. Apply the Proximity heuristic
            // Generate candidates from FOMs that are in the same class/file
            foreach (var locationGroup in fomsByLocation.Values.Where(g => g.Count >= 2))
            {
                // Limit to pairs and triplets for proximity-based candidates
                for (int order = 2; order <= Math.Min(3, locationGroup.Count); order++)
                {
                    foreach (var candidate in GenerateCombinations(locationGroup, order))
                    {
                        string candidateKey = GetCandidateKey(candidate);
                        if (!generatedCandidates.Contains(candidateKey))
                        {
                            generatedCandidates.Add(candidateKey);
                            yield return candidate;

                            generatedCount++;
                            if (generatedCount >= maxCandidates)
                            {
                                yield break;
                            }
                        }
                    }
                }
            }

            // 3. Apply N+1 rule: combine each previously identified SSHOM with one additional FOM
            foreach (var sshom in previousSSHOMs)
            {
                if (sshom.Count >= maxOrder)
                {
                    continue; // Skip if already at max order
                }

                var sshomMutantIds = new HashSet<int>(sshom.Select(m => m.Id));

                foreach (var fom in fomList)
                {
                    // Skip if this FOM is already part of the SSHOM
                    if (sshomMutantIds.Contains(fom.Id))
                    {
                        continue;
                    }

                    var newCandidate = new List<IMutant>(sshom) { fom };
                    string candidateKey = GetCandidateKey(newCandidate);

                    if (!generatedCandidates.Contains(candidateKey))
                    {
                        generatedCandidates.Add(candidateKey);
                        yield return newCandidate;

                        generatedCount++;
                        if (generatedCount >= maxCandidates)
                        {
                            yield break;
                        }
                    }
                }
            }

            // 4. If we still haven't reached the max candidates count, generate additional second-order mutants
            // This serves as a fallback to ensure we have enough candidates
            if (generatedCount < maxCandidates)
            {
                for (int i = 0; i < fomList.Count; i++)
                {
                    for (int j = i + 1; j < fomList.Count; j++)
                    {
                        var candidate = new List<IMutant> { fomList[i], fomList[j] };
                        string candidateKey = GetCandidateKey(candidate);

                        if (!generatedCandidates.Contains(candidateKey))
                        {
                            generatedCandidates.Add(candidateKey);
                            yield return candidate;

                            generatedCount++;
                            if (generatedCount >= maxCandidates)
                            {
                                yield break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Groups FOMs by their killing tests signature.
        /// FOMs killed by the same set of tests are grouped together.
        /// </summary>
        private Dictionary<string, List<IMutant>> GroupFomsByKillingTests(List<IMutant> foms)
        {
            var result = new Dictionary<string, List<IMutant>>();

            foreach (var fom in foms)
            {
                if (fom.KillingTests == null || fom.KillingTests.IsEmpty)
                {
                    continue; // Skip FOMs with no killing tests
                }

                // Create a signature based on the killing tests
                // Convert ITestIdentifiers to a string signature
                string signature = fom.KillingTests.ToString();

                if (!result.TryGetValue(signature, out var group))
                {
                    group = new List<IMutant>();
                    result[signature] = group;
                }

                group.Add(fom);
            }

            return result;
        }

        /// <summary>
        /// Groups FOMs by their source location (class, file).
        /// FOMs from the same source location are grouped together.
        /// </summary>
        private Dictionary<string, List<IMutant>> GroupFomsByLocation(List<IMutant> foms)
        {
            var result = new Dictionary<string, List<IMutant>>();

            foreach (var fom in foms)
            {
                // Extract location information from the mutation
                string location = GetMutationLocation(fom.Mutation);

                if (!result.TryGetValue(location, out var group))
                {
                    group = new List<IMutant>();
                    result[location] = group;
                }

                group.Add(fom);
            }

            return result;
        }

        /// <summary>
        /// Extracts a location identifier from a mutation to group related mutations together.
        /// </summary>
        private string GetMutationLocation(Mutation mutation)
        {
            // For simplicity, we'll use the file path as the location
            // In a more sophisticated implementation, this might include class and method information
            return mutation.OriginalNode?.SyntaxTree?.FilePath ?? "unknown";
        }

        /// <summary>
        /// Generates all possible combinations of the specified order from the given list of FOMs.
        /// </summary>
        private IEnumerable<List<IMutant>> GenerateCombinations(List<IMutant> foms, int order)
        {
            if (order <= 0 || order > foms.Count)
            {
                yield break;
            }

            if (order == 1)
            {
                foreach (var fom in foms)
                {
                    yield return new List<IMutant> { fom };
                }
                yield break;
            }

            // Generate combinations recursively
            var indices = new int[order];
            for (int i = 0; i < order; i++)
            {
                indices[i] = i;
            }

            while (true)
            {
                // Yield current combination
                var combination = new List<IMutant>(order);
                for (int i = 0; i < order; i++)
                {
                    combination.Add(foms[indices[i]]);
                }
                yield return combination;

                // Generate next combination
                int j = order - 1;
                while (j >= 0 && indices[j] == foms.Count - order + j)
                {
                    j--;
                }

                if (j < 0)
                {
                    break; // No more combinations
                }

                indices[j]++;
                for (int k = j + 1; k < order; k++)
                {
                    indices[k] = indices[k - 1] + 1;
                }
            }
        }

        /// <summary>
        /// Creates a unique key for a candidate HOM based on the IDs of its constituent mutants.
        /// </summary>
        private string GetCandidateKey(List<IMutant> candidate)
        {
            return string.Join(",", candidate.OrderBy(m => m.Id).Select(m => m.Id));
        }
    }
}
