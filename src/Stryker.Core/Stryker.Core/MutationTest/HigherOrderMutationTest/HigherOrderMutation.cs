using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.Core.MutationTest.HigherOrderMutationTest.SSHOM;
using Stryker.Utilities.Logging;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest
{
    public class HigherOrderMutation
    {
        private readonly IStrykerOptions _options;
        private readonly MutationTestInput _input;
        private readonly IReadOnlyCollection<IMutant> _allFirstOrderMutants;
        private readonly ILogger<HigherOrderMutation> _logger;

        private readonly List<IHOMSearchAlgorithm> _searchAlgorithms = [];
        private readonly HeuristicRegistry _heuristicRegistry;
        private readonly List<IHOMHeuristic> _heuristics = [];

        // State management for HOM candidates and testing progress
        private readonly Dictionary<int, HigherOrderMutant> _homCandidates = [];
        private readonly Dictionary<string, HigherOrderMutant> _homCandidatesByMutantIds = [];

        private readonly List<HigherOrderMutant> _verifiedSSHOMs = [];

        public HigherOrderMutation(
            IStrykerOptions options,
            MutationTestInput input, // Provides context like initial test run results
            IReadOnlyCollection<IMutant> firstOrderMutants)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _allFirstOrderMutants = firstOrderMutants ?? throw new ArgumentNullException(nameof(firstOrderMutants));
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<HigherOrderMutation>();

            _heuristicRegistry = new HeuristicRegistry(_allFirstOrderMutants, _options, _input, false);

            // TODO: Register implemented search algorithms and heuristics here
        }

        /// <summary>
        /// Gets all HOM candidates that have been created.
        /// </summary>
        public IReadOnlyCollection<HigherOrderMutant> CreatedCandidates => _homCandidates.Values.ToList();

        /// <summary>
        /// Gets HOM candidates that have been tested.
        /// </summary>
        public IEnumerable<HigherOrderMutant> TestedCandidates => _homCandidates.Values.Where(c => c.HasBeenTested);

        /// <summary>
        /// Gets HOM candidates that have been validated as SSHOMs.
        /// </summary>
        public IEnumerable<HigherOrderMutant> ValidatedSSHOMs => _homCandidates.Values.Where(c => c.IsValidatedSSHOM == true);


        public void AddSearchAlgorithm(IHOMSearchAlgorithm algorithm)
        {
            if (algorithm == null)
            {
                throw new ArgumentNullException(nameof(algorithm));
            }

            if (!_searchAlgorithms.Any(a => a.Name.Equals(algorithm.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _searchAlgorithms.Add(algorithm);
                _logger.LogDebug("Registered HOM search algorithm: {AlgorithmName}", algorithm.Name);
            }
        }

        public void AddHeuristic(IHOMHeuristic heuristic)
        {
            if (heuristic == null)
            {
                throw new ArgumentNullException(nameof(heuristic));
            }

            if (!_heuristics.Any(h => h.Name.Equals(heuristic.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _heuristics.Add(heuristic);
                _heuristicRegistry.RegisterHeuristic(heuristic);
                _logger.LogDebug("Registered HOM heuristic: {HeuristicName}", heuristic.Name);
            }
        }

        /// <summary>
        /// Validates all tested HOM candidates as potential SSHOMs.
        /// /// <param name="requireProperSubset">Whether to require proper subsets for SSHOM validation.</param>
        /// <returns>The number of candidates validated as SSHOMs.</returns>
        public int ValidateAllTestedCandidatesAsSSHOMs(bool requireProperSubset = false)
        {
            var testedCandidates = TestedCandidates.ToList();
            var validatedCount = 0;

            foreach (var candidate in testedCandidates)
            {
                if (ValidateSSHOMFromCandidate(candidate, requireProperSubset))
                {
                    _verifiedSSHOMs.Add(candidate);
                    validatedCount++;
                }
            }

            _logger.LogInformation("Validated {ValidatedCount} SSHOMs out of {TestedCount} tested HOM candidates",
                validatedCount, testedCandidates.Count);

            return validatedCount;
        }

        /// <summary>
        /// Validates if a HOM candidate is an SSHOM using the stored test results.
        /// /// </summary>
        /// <param name="candidate">The HOM candidate to validate.</param>
        /// <param name="requireProperSubset">Whether to require a proper subset for SSHOM validation.</param>
        /// <returns>True if the candidate is a validated SSHOM, false otherwise.</returns>
        public bool ValidateSSHOMFromCandidate(HigherOrderMutant candidate, bool requireProperSubset = false)
        {
            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            if (!candidate.HasBeenTested)
            {
                _logger.LogWarning("Cannot validate SSHOM for untested candidate {CandidateId}", candidate.Id);
                candidate.SSHOMValidationResult = "Cannot validate: HOM not tested";
                return false;
            }

            var isSSHOM = IsSSHOM(candidate.ConstituentMutants, candidate.KillingTests, requireProperSubset);
            candidate.IsValidatedSSHOM = isSSHOM;
            candidate.SSHOMValidationResult = isSSHOM 
                ? "Validated as SSHOM" 
                : "Not an SSHOM";

            _logger.LogDebug("SSHOM validation for candidate {CandidateId}: {Result}", 
                candidate.Id, candidate.SSHOMValidationResult);

            return isSSHOM;
        }

        /// <summary>
        /// Gets a HOM candidate by its constituent mutants.
        /// </summary>
        /// <param name="mutants">The constituent mutants.</param>
        /// <returns>The HOM candidate if found, null otherwise.</returns>
        public HigherOrderMutant GetCandidateByMutants(List<IMutant> mutants)
        {
            var mutantIdsKey = string.Join(",", mutants.OrderBy(m => m.Id).Select(m => m.Id));
            return _homCandidatesByMutantIds.TryGetValue(mutantIdsKey, out var candidate) ? candidate : null;
        }

        /// <summary>
        /// Gets a HOM candidate by its ID.
        /// </summary>
        /// <param name="candidateId">The candidate ID.</param>
        /// <returns>The HOM candidate if found, null otherwise.</returns>
        public HigherOrderMutant GetCandidateById(int candidateId) => _homCandidates.TryGetValue(candidateId, out var candidate) ? candidate : null;

        /// <summary>
        /// Builds and optimizes Higher-Order Mutants with comprehensive configuration options and metadata.
        /// This method centralizes all HOM generation logic.
        /// </summary>
        /// <param name="mutantsToTest">The collection of mutants to create HOMs from.</param>
        /// <param name="isPreTestRun">If null, auto-detects based on test execution data availability. If true, runs pre-test scenario. If false, runs post-test scenario.</param>
        /// <param name="customAlgorithms">Optional custom algorithms to use. If not provided, uses default LocalSearchAlgorithm.</param>
        /// <param name="customHeuristics">Optional custom heuristics to use. If not provided, uses HeuristicRegistry defaults.</param>
        /// <param name="validateSSHOM">Whether to validate SSHOMs during post-test runs (only applicable when isPreTestRun is false).</param>
        /// <param name="requireProperSubset">When validating SSHOMs, whether to require proper subsets or allow equal sets.</param>
        /// <param name="includeMissingMutants">Whether to include mutants not covered by HOMs as a separate group.</param>
        /// <param name="includeAllIndividualMutants">Whether to test all mutants individually in addition to HOMs.</param>
        /// <param name="algorithmName">Name of specific algorithm to use from registered algorithms. If null, uses first available.</param>
        /// <returns>A HOMGenerationResult with the generated mutant groups and metadata about the process.</returns>
        public HOMGenerationResult BuildAndOptimizeHigherOrderMutants(
            IReadOnlyCollection<IMutant> mutantsToTest,
            bool isPreTestRun = true,
            bool requireProperSubset = false,
            bool includeMissingMutants = true,
            bool includeAllIndividualMutants = false)
        {
            var startTime = DateTime.Now;
            
            if (mutantsToTest == null)
            {
                throw new ArgumentNullException(nameof(mutantsToTest));
            }

            // Early return if HOM is not enabled
            if (!_options.OptimizationMode.HasFlag(OptimizationModes.EnableHigherOrderMutants))
            {
                _logger.LogDebug("Higher-Order Mutations not enabled, returning single mutant groups.");                
                return new HOMGenerationResult(mutantsToTest, false, "None (HOM disabled)", 0, 0, mutantsToTest.Count, DateTime.Now - startTime);
            }

            if (mutantsToTest.Count == 0)
            {
                return new HOMGenerationResult([],false, "None (no mutants)", 0, 0, 0, DateTime.Now - startTime);
            }
            
            _logger.LogDebug("Building Higher-Order Mutants: {RunType} scenario with {MutantCount} mutants.",
                isPreTestRun ? "Pre-test" : "Post-test", mutantsToTest.Count);

            // Generate HOM candidates with metadata tracking
            var homCandidates = CreateCandidateHOMs(isPreTestRun, requireProperSubset);
            var homCandidatesList = homCandidates.ToList();
            
            _logger.LogInformation("Generated {HomCount} HOM candidates.", homCandidatesList.Count);

            // Calculate mutant coverage
            var mutantsInHOMs = homCandidatesList.SelectMany(h => h.ConstituentMutants).Distinct();
            var mutantsMissing = mutantsToTest.Except(mutantsInHOMs).ToList();

            var mutantGroups = new List<IMutant>(homCandidatesList);

            if (includeAllIndividualMutants)
            {
                _logger.LogDebug("Adding all mutants individually as well");
                mutantGroups.AddRange(mutantsToTest);
            }
            else if (includeMissingMutants && mutantsMissing.Count > 0)
            {
                _logger.LogDebug("Adding {MissingCount} mutants not included in HOMs as separate test group.",
                    mutantsMissing.Count);
                mutantGroups.AddRange(mutantsMissing);
            }

            var endTime = DateTime.Now;
            var generationTime = endTime - startTime;

            _logger.LogInformation("Final HOM test plan: {GroupCount} HOMs covering {TotalMutants} mutants",
                homCandidatesList.Count, mutantsInHOMs.Count());

            var algorithmsUsed = string.Join(", ", _searchAlgorithms.Select(s => s.Name));

            return new HOMGenerationResult(
                mutantGroups,
                isPreTestRun,
                algorithmsUsed,
                _heuristicRegistry?.RegisteredHeuristics.Count ?? 0,
                mutantsInHOMs.Count(),
                mutantsMissing.Count,
                generationTime,
                CreatedCandidates);
        }

        /// <summary>
        /// Creates candidate Higher-Order Mutants (HOMs) using a specified search algorithm.
        /// These candidates are groups of FOMs that need to be tested together.
        /// </summary>
        /// <param name="algorithmName">The name of the search algorithm to use. If null/empty, the first registered algorithm is used.</param>
        /// <param name="isPreTestRun">Whether this is a pre-test run (without killing test data) or post-test run (with killing test data).</param>
        /// <param name="validateSSHOM">Whether to validate potential SSHOMs using killing test data (only applicable for post-test runs).</param>
        /// <param name="requireProperSubset">When validating SSHOMs, whether to require a proper subset (stricter) or allow equal sets.</param>
        /// <returns>An enumerable of HigherOrderMutant candidates.</returns>
        public IEnumerable<HigherOrderMutant> CreateCandidateHOMs( bool isPreTestRun = false, bool requireProperSubset = false)
        {
            if (!_allFirstOrderMutants.Any())
            {
                _logger.LogInformation("No first-order mutants available to create HOM candidates.");
                yield break;
            }
            
            var selectedAlgorithm = _searchAlgorithms.FirstOrDefault();
            if (selectedAlgorithm == null)
            {
                _logger.LogWarning("No HOM search algorithm specified and no default algorithm registered. Cannot create HOM candidates.");
                yield break;
            }
            _logger.LogInformation("Using default HOM search algorithm: {AlgorithmName}", selectedAlgorithm.Name);

            var runType = isPreTestRun ? "pre-test" : "post-test";

            _logger.LogDebug("Generating {RunType} HOM candidates using {AlgorithmName} with {FOMCount} eligible FOMs and {HeuristicCount} heuristics.",
                runType, selectedAlgorithm.Name, _allFirstOrderMutants.Count, _heuristics.Count);

            // Filter heuristics based on the run type
            var applicableHeuristics = FilterHeuristicsForRunType(_heuristics, isPreTestRun);

            foreach (var candidate in selectedAlgorithm.GenerateCandidates(_allFirstOrderMutants, applicableHeuristics, _options, _input))
            {
                // Skip null candidates
                if (candidate == null)
                {
                    continue;
                }

                // Skip FOMs (order 1) - we only want HOMs (order 2+)
                if (candidate.Order <= 1)
                {
                    _logger.LogTrace("Search algorithm generated an invalid HOM");
                    continue;
                }
                
                // Assign ID and store candidate
                candidate.Id = _options.MutantIdProvider.NextId();
                _homCandidates[candidate.Id] = candidate;
    
                var mutantIdsKey = string.Join(",", candidate.ConstituentMutants.OrderBy(m => m.Id).Select(m => m.Id));
                _homCandidatesByMutantIds[mutantIdsKey] = candidate;

                yield return candidate;
            }
        }

        /// <summary>
        /// Checks if a tested Higher-Order Mutant (HOM) is a Strongly Subsuming HOM (SSHOM).
        /// A HOM is strongly subsuming if its set of killing tests is a non-empty proper subset
        /// of the intersection of the killing tests of its constituent first-order mutants (FOMs).
        /// However, as we are only interested in the 'one-kill covers all of these FOMs' guarantee,
        /// we loosen the definition to allow regular subsets.
        /// </summary>
        /// <param name="constituentMutants">The list of first-order mutants that make up the HOM.
        /// It's assumed these FOMs have been tested and their KillingTests property is populated.</param>
        /// <param name="homKillingTests">The set of tests that kill the HOM itself.</param>
        /// <returns>True if the HOM is an SSHOM, false otherwise.</returns>
        public bool IsSSHOM(IReadOnlyList<IMutant> constituentMutants, ITestIdentifiers homKillingTests, bool properSubset = false)
        {
            // Condition 1: A HOM must be composed of at least two FOMs.
            if (constituentMutants == null || constituentMutants.Count < 2)
            {
                _logger.LogTrace("SSHOM Check: Fail - HOM must have at least 2 constituent FOMs. Found: {Count}", constituentMutants?.Count ?? 0);
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
            if (constituentMutants.Any(fom => fom.KillingTests == null || fom.KillingTests.IsEmpty))
            {
                _logger.LogTrace("SSHOM Check: Fail - Not all constituent FOMs were killed or have KillingTests data.");
                return false;
            }

            // Condition 4: Calculate the intersection of the killing tests of all constituent FOMs.
            // Start with the killing tests of the first FOM.
            ITestIdentifiers intersectionFomKillingTests = constituentMutants[0].KillingTests;
            for (int i = 1; i < constituentMutants.Count; i++)
            {
                intersectionFomKillingTests = intersectionFomKillingTests.Intersect(constituentMutants[i].KillingTests);

                // If the intersection becomes empty at any point, the SSHOM condition cannot be met.
                if (intersectionFomKillingTests.IsEmpty)
                {
                    _logger.LogTrace("SSHOM Check: Fail - Intersection of FOM killing tests became empty.");
                    return false;
                }
            }
            // At this point, intersectionFomKillingTests contains tests that kill ALL constituent FOMs.
            // It cannot be empty due to the check inside the loop and condition 3.

            // Condition 5: The set of tests killing the HOM (homKillingTests) must be a subset of the intersection of FOM killing tests.
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

        /// <summary>
        /// Filters heuristics based on whether this is a pre-test or post-test run.
        /// Pre-test runs should exclude heuristics that require test execution data.
        /// </summary>
        /// <param name="allHeuristics">All available heuristics.</param>
        /// <param name="isPreTestRun">True if this is a pre-test run, false if post-test.</param>
        /// <returns>Filtered list of applicable heuristics.</returns>
        private List<IHOMHeuristic> FilterHeuristicsForRunType(IEnumerable<IHOMHeuristic> allHeuristics, bool isPreTestRun)
        {
            var filteredHeuristics = new List<IHOMHeuristic>();

            foreach (var heuristic in allHeuristics)
            {
                // For pre-test runs, we skip heuristics that require test execution data
                if (isPreTestRun && heuristic.RequiresPreRun)
                {
                    _logger.LogTrace("Skipping heuristic '{HeuristicName}' for pre-test run as it requires test execution data.", heuristic.Name);
                    continue;
                }

                filteredHeuristics.Add(heuristic);
            }

            _logger.LogDebug("Filtered {FilteredCount} heuristics out of {TotalCount} for {RunType} run.",
                filteredHeuristics.Count, allHeuristics.Count(), isPreTestRun ? "pre-test" : "post-test");

            return filteredHeuristics;
        }

        /// <summary>
        /// Analyzes all tested HOM candidates for SSHOMs and records results.
        /// This should be called after all mutation testing is complete.
        /// </summary>
        /// <param name="requireProperSubset">Whether to require proper subsets for SSHOM validation.</param>
        /// <returns>A summary of SSHOM analysis results.</returns>
        public SSHOMAnalysisResult AnalyzeHOMsForSSHOMs(bool requireProperSubset = false)
        {
            var startTime = DateTime.Now;
            _logger.LogDebug("Starting SSHOM analysis...");
            
            var testedCandidates = TestedCandidates.ToList();
            var sshomCount = 0;
            var analyzedCount = 0;

            sshomCount = ValidateAllTestedCandidatesAsSSHOMs(requireProperSubset);

            var analysisTime = DateTime.Now - startTime;
            
            _logger.LogInformation("SSHOM Analysis Complete: {SSHOMCount} SSHOMs found out of {AnalyzedCount} tested candidates in {AnalysisTime}ms", 
                sshomCount, analyzedCount, analysisTime.TotalMilliseconds);

            return new SSHOMAnalysisResult(sshomCount, analyzedCount, testedCandidates.Count, analysisTime);
        }

        /// <summary>
        /// Gets summary information about SSHOM analysis results.
        /// </summary>
        public SSHOMSummary GetSSHOMSummary()
        {
            var validatedSSHOMs = ValidatedSSHOMs.ToList();
            var testedCandidates = TestedCandidates.ToList();
            
            return new SSHOMSummary
            {
                TotalSSHOMs = validatedSSHOMs.Count,
                TotalTestedCandidates = testedCandidates.Count,
                TotalCreatedCandidates = CreatedCandidates.Count,
                SSHOMCandidates = validatedSSHOMs,
                SSHOMValidationRate = testedCandidates.Count > 0 ? (double)validatedSSHOMs.Count / testedCandidates.Count : 0.0
            };
        }
    }
}
