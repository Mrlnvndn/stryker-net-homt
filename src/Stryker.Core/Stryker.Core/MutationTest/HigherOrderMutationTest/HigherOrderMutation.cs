using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.Utilities.Logging;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest
{
    public class HigherOrderMutation
    {
        private readonly IStrykerOptions _options;
        private readonly MutationTestInput _input;
        private readonly IReadOnlyCollection<IMutant> _allFirstOrderMutants;
        private readonly ILogger<HigherOrderMutation> _logger;

        private readonly List<IHOMSearchAlgorithm> _searchAlgorithms = new();
        private readonly List<IHOMHeuristic> _heuristics = new();

        public HigherOrderMutation(
            IStrykerOptions options,
            MutationTestInput input, // Provides context like initial test run results
            IReadOnlyCollection<IMutant> firstOrderMutants)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _allFirstOrderMutants = firstOrderMutants ?? throw new ArgumentNullException(nameof(firstOrderMutants));
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<HigherOrderMutation>();

            // Register implemented search algorithms and heuristics here
        }

        public void AddSearchAlgorithm(IHOMSearchAlgorithm algorithm)
        {
            if (algorithm == null)
                throw new ArgumentNullException(nameof(algorithm));
            if (!_searchAlgorithms.Any(a => a.Name.Equals(algorithm.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _searchAlgorithms.Add(algorithm);
                _logger.LogDebug("Registered HOM search algorithm: {AlgorithmName}", algorithm.Name);
            }
        }

        public void AddHeuristic(IHOMHeuristic heuristic)
        {
            if (heuristic == null)
                throw new ArgumentNullException(nameof(heuristic));
            if (!_heuristics.Any(h => h.Name.Equals(heuristic.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _heuristics.Add(heuristic);
                _logger.LogDebug("Registered HOM heuristic: {HeuristicName}", heuristic.Name);
            }
        }

        /// <summary>
        /// Creates candidate Higher-Order Mutants (HOMs) using a specified search algorithm.
        /// These candidates are groups of FOMs that need to be tested together.
        /// </summary>
        /// <param name="algorithmName">The name of the search algorithm to use. If null/empty, the first registered algorithm is used.</param>
        /// <returns>An enumerable of lists, where each inner list is a candidate HOM.</returns>
        public IEnumerable<List<IMutant>> CreateCandidateHOMs(string algorithmName = null)
        {
            if (!_allFirstOrderMutants.Any())
            {
                _logger.LogInformation("No first-order mutants available to create HOM candidates.");
                yield break;
            }

            IHOMSearchAlgorithm selectedAlgorithm;
            if (string.IsNullOrEmpty(algorithmName))
            {
                selectedAlgorithm = _searchAlgorithms.FirstOrDefault();
                if (selectedAlgorithm == null)
                {
                    _logger.LogWarning("No HOM search algorithm specified and no default algorithm registered. Cannot create HOM candidates.");
                    yield break;
                }
                _logger.LogInformation("Using default HOM search algorithm: {AlgorithmName}", selectedAlgorithm.Name);
            }
            else
            {
                selectedAlgorithm = _searchAlgorithms.FirstOrDefault(a => a.Name.Equals(algorithmName, StringComparison.OrdinalIgnoreCase));
                if (selectedAlgorithm == null)
                {
                    _logger.LogWarning("HOM Search algorithm '{AlgorithmName}' not found. Cannot create HOM candidates.", algorithmName);
                    yield break;
                }
                _logger.LogInformation("Using HOM search algorithm: {AlgorithmName}", selectedAlgorithm.Name);
            }

            // Typically, HOMs are built from FOMs that were not easily killed (survived or pending)
            // Or based on specific heuristics that might select any FOM.
            // For now, let's assume the search algorithm will handle which FOMs are eligible.
            var eligibleFOMs = _allFirstOrderMutants.Where(fom => fom.ResultStatus == MutantStatus.Pending || fom.ResultStatus == MutantStatus.Survived).ToList();

            if (!eligibleFOMs.Any())
            {
                _logger.LogInformation("No pending or survived first-order mutants available to form HOM candidates based on default eligibility.");
                // Optionally, allow algorithm to use all FOMs if it has its own filtering
                // eligibleFOMs = _allFirstOrderMutants.ToList(); 
            }

            _logger.LogDebug("Generating HOM candidates using {AlgorithmName} with {FOMCount} eligible FOMs and {HeuristicCount} heuristics.",
                selectedAlgorithm.Name, eligibleFOMs.Count, _heuristics.Count);

            foreach (var candidate in selectedAlgorithm.GenerateCandidates(eligibleFOMs, _heuristics, _options, _input))
            {
                // HOMs are typically of order 2 or higher.
                if (candidate != null && candidate.Count > 1)
                {
                    // Further validation of the candidate can be done here if needed
                    // e.g., ensuring mutants are from compatible locations if that's a constraint.
                    yield return candidate;
                }
                else if (candidate != null && candidate.Count == 1)
                {
                    _logger.LogTrace("Search algorithm generated a single mutant group (order 1), which is a FOM, not a HOM. Skipping: Mutant Id {MutantId}", candidate.First().Id);
                }
            }
        }

        /// <summary>
        /// Checks if a tested Higher-Order Mutant (HOM) is a Strongly Subsuming HOM (SSHOM).
        /// A HOM is strongly subsuming if its set of killing tests is a non-empty proper subset
        /// of the intersection of the killing tests of its constituent first-order mutants (FOMs).
        /// However, as we are only interested in the 'one-kill covers all of these FOMs' guarantee,
        /// we loosen the definition to allow regular subsets.
        /// </summary>
        /// <param name="constituentFOMs">The list of first-order mutants that make up the HOM.
        /// It's assumed these FOMs have been tested and their KillingTests property is populated.</param>
        /// <param name="homKillingTests">The set of tests that kill the HOM itself.</param>
        /// <returns>True if the HOM is an SSHOM, false otherwise.</returns>
        public bool IsSSHOM(IReadOnlyList<IMutant> constituentFOMs, ITestIdentifiers homKillingTests, bool properSubset = false)
        {
            // Condition 1: A HOM must be composed of at least two FOMs.
            if (constituentFOMs == null || constituentFOMs.Count < 2)
            {
                _logger.LogTrace("SSHOM Check: Fail - HOM must have at least 2 constituent FOMs. Found: {Count}", constituentFOMs?.Count ?? 0);
                return false;
            }

            // Condition 2: The HOM itself must be killed by at least one test.
            if (homKillingTests == null || homKillingTests.IsEmpty)
            {
                _logger.LogTrace("SSHOM Check: Fail - HOM was not killed by any test.");
                return false;
            }

            // Condition 3: All constituent FOMs must have been tested and killed.
            // (Their KillingTests should be populated and non-empty).
            if (constituentFOMs.Any(fom => fom.KillingTests == null || fom.KillingTests.IsEmpty))
            {
                _logger.LogTrace("SSHOM Check: Fail - Not all constituent FOMs were killed or have KillingTests data.");
                return false;
            }

            // Condition 4: Calculate the intersection of the killing tests of all constituent FOMs.
            // Start with the killing tests of the first FOM.
            ITestIdentifiers intersectionFomKillingTests = constituentFOMs[0].KillingTests;
            for (int i = 1; i < constituentFOMs.Count; i++)
            {
                intersectionFomKillingTests = intersectionFomKillingTests.Intersect(constituentFOMs[i].KillingTests);

                // If the intersection becomes empty at any point, the SSHOM condition cannot be met.
                if (intersectionFomKillingTests.IsEmpty)
                {
                    _logger.LogTrace("SSHOM Check: Fail - Intersection of FOM killing tests became empty.");
                    return false;
                }
            }
            // At this point, intersectionFomKillingTests contains tests that kill ALL constituent FOMs.
            // It cannot be empty due to the check inside the loop and condition 3.

            // Condition 5: The set of tests killing the HOM (homKillingTests) must be a subset of intersectionFomKillingTests.
            var isSubset = homKillingTests.IsIncludedIn(intersectionFomKillingTests);
            if (!isSubset)
            {
                _logger.LogTrace("SSHOM Check: Fail - HOM killing tests are not a subset of the intersection of FOM killing tests.");
                return false;
            }

            // if properSubset is false, we are done
            if (!properSubset)
            {
                return true;
            }

            // Condition 6: The subset must be proper.
            // This means homKillingTests is a subset of intersectionFomKillingTests,
            // AND they are not equal sets (i.e., intersectionFomKillingTests contains at least one test not in homKillingTests).
            var isProperSubset = !intersectionFomKillingTests.IsIncludedIn(homKillingTests);

            if (isProperSubset)
            {
               _logger.LogTrace("SSHOM Check: Success - Candidate IS an SSHOM.");
            }
            else
            {
               _logger.LogTrace("SSHOM Check: Fail - HOM killing tests are not a PROPER subset (they are equal to the intersection).");
            }

            return isProperSubset;
        }
    }
}
