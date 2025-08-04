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

        private readonly List<IHOMSearchAlgorithm> _searchAlgorithms = new();
        private readonly List<IHOMHeuristic> _heuristics = new();

        // State management for HOM candidates and testing progress
        private readonly Dictionary<int, HigherOrderMutant> _homCandidates = new();
        private readonly Dictionary<string, HigherOrderMutant> _homCandidatesByMutantIds = new();

        public HigherOrderMutation(
            IStrykerOptions options,
            MutationTestInput input, // Provides context like initial test run results
            IReadOnlyCollection<IMutant> firstOrderMutants)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _allFirstOrderMutants = firstOrderMutants ?? throw new ArgumentNullException(nameof(firstOrderMutants));
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<HigherOrderMutation>();

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
        /// Records the test result for a HOM candidate that has been tested.
        /// </summary>
        /// <param name="mutants">The constituent mutants of the HOM that was tested.</param>
        /// <param name="killingTests">The tests that killed the HOM.</param>
        /// <returns>The HOM candidate if found, null otherwise.</returns>
        public HigherOrderMutant RecordHOMTestResult(List<IMutant> mutants, ITestIdentifiers killingTests)
        {
            var mutantIdsKey = string.Join(",", mutants.OrderBy(m => m.Id).Select(m => m.Id));
            
            if (_homCandidatesByMutantIds.TryGetValue(mutantIdsKey, out var candidate))
            {
                candidate.KillingTests = killingTests;
                _logger.LogDebug("Recorded test result for HOM candidate {CandidateId}: killed by {TestCount} tests", 
                    candidate.Id, killingTests?.GetIdentifiers().Count() ?? 0);
                return candidate;
            }

            _logger.LogWarning("Could not find HOM candidate for mutants: {MutantIds}", mutantIdsKey);
            return null;
        }

        /// <summary>
        /// Validates if a HOM candidate is an SSHOM using the stored test results.
        /// </summary>
        /// <param name="candidate">The HOM candidate to validate.</param>
        /// <param name="requireProperSubset">Whether to require a proper subset for SSHOM validation.</param>
        /// <returns>True if the candidate is a validated SSHOM, false otherwise.</returns>
        public bool ValidateSSHOMFromCandidate(HigherOrderMutant candidate, bool requireProperSubset = false)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));

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
        /// Validates all tested HOM candidates as potential SSHOMs.
        /// </summary>
        /// <param name="requireProperSubset">Whether to require proper subsets for SSHOM validation.</param>
        /// <returns>The number of candidates validated as SSHOMs.</returns>
        public int ValidateAllTestedCandidatesAsSSHOMs(bool requireProperSubset = false)
        {
            var testedCandidates = TestedCandidates.ToList();
            var validatedCount = 0;

            foreach (var candidate in testedCandidates)
            {
                if (ValidateSSHOMFromCandidate(candidate, requireProperSubset))
                {
                    validatedCount++;
                }
            }

            _logger.LogInformation("Validated {ValidatedCount} SSHOMs out of {TestedCount} tested HOM candidates", 
                validatedCount, testedCandidates.Count);

            return validatedCount;
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
        public HigherOrderMutant GetCandidateById(int candidateId)
        {
            return _homCandidates.TryGetValue(candidateId, out var candidate) ? candidate : null;
        }

        /// <summary>
        /// Creates candidate Higher-Order Mutants (HOMs) for the pre-test run scenario.
        /// This method generates promising HOMs without knowing which tests kill which first-order mutants.
        /// It relies on heuristics that don't require test execution data.
        /// </summary>
        /// <param name="algorithmName">The name of the search algorithm to use. If null/empty, the first registered algorithm is used.</param>
        /// <returns>An enumerable of lists, where each inner list is a candidate HOM.</returns>
        public IEnumerable<HigherOrderMutant> CreatePreTestCandidateHOMs(string algorithmName = null)
        {
            _logger.LogInformation("Generating pre-test HOM candidates (without test killing data).");
            
            return CreateCandidateHOMs(algorithmName, isPreTestRun: true);
        }

        /// <summary>
        /// Creates candidate Higher-Order Mutants (HOMs) for the post-test run scenario.
        /// This method generates promising HOMs while knowing which tests kill which first-order mutants.
        /// It can use SSHOM validation and test-coverage-based heuristics.
        /// </summary>
        /// <param name="algorithmName">The name of the search algorithm to use. If null/empty, the first registered algorithm is used.</param>
        /// <param name="validateSSHOM">Whether to validate potential SSHOMs using killing test data.</param>
        /// <param name="requireProperSubset">When validating SSHOMs, whether to require a proper subset (stricter) or allow equal sets.</param>
        /// <returns>An enumerable of lists, where each inner list is a candidate HOM.</returns>
        public IEnumerable<HigherOrderMutant> CreatePostTestCandidateHOMs(string algorithmName = null, bool validateSSHOM = true, bool requireProperSubset = false)
        {
            _logger.LogInformation("Generating post-test HOM candidates (with test killing data). SSHOM validation: {ValidateSSHOM}", validateSSHOM);
            
            return CreateCandidateHOMs(algorithmName, isPreTestRun: false, validateSSHOM, requireProperSubset);
        }

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
        /// <param name="algorithmName">Name of specific algorithm to use from registered algorithms. If null, uses first available.</param>
        /// <returns>A HOMGenerationResult with the generated mutant groups and metadata about the process.</returns>
        public HOMGenerationResult BuildAndOptimizeHigherOrderMutants(
            IReadOnlyCollection<IMutant> mutantsToTest,
            bool? isPreTestRun = null,
            IEnumerable<IHOMSearchAlgorithm> customAlgorithms = null,
            IEnumerable<IHOMHeuristic> customHeuristics = null,
            bool validateSSHOM = true,
            bool requireProperSubset = false,
            bool includeMissingMutants = true,
            bool testAllMutantsIndividually = false,
            string algorithmName = null)
        {
            var startTime = DateTime.Now;
            
            if (mutantsToTest == null)
                throw new ArgumentNullException(nameof(mutantsToTest));

            // Early return if HOM is not enabled
            if (!_options.OptimizationMode.HasFlag(OptimizationModes.EnableHigherOrderMutations))
            {
                _logger.LogDebug("Higher-Order Mutations not enabled, returning single mutant groups.");                
                return new HOMGenerationResult(
                    mutantsToTest,
                    false,
                    "None (HOM disabled)",
                    0,
                    0,
                    mutantsToTest.Count,
                    DateTime.Now - startTime);
            }

            if (!mutantsToTest.Any())
            {
                _logger.LogInformation("No mutants provided for HOM generation."); 
                return new HOMGenerationResult(
                    new List<IMutant>(),
                    false,
                    "None (no mutants)",
                    0,
                    0,
                    0,
                    DateTime.Now - startTime);
            }

            // Auto-detect pre/post test scenario if not specified
            var actualIsPreTestRun = isPreTestRun ?? !mutantsToTest.Any(m => m.KillingTests != null && !m.KillingTests.IsEmpty);
            
            _logger.LogInformation("Building Higher-Order Mutants: {RunType} scenario with {MutantCount} mutants.",
                actualIsPreTestRun ? "Pre-test" : "Post-test", mutantsToTest.Count);

            // Setup algorithms and heuristics using HeuristicRegistry
            var algorithmUsed = SetupAlgorithms(customAlgorithms, mutantsToTest, algorithmName);
            var heuristicRegistry = SetupHeuristics(customHeuristics, actualIsPreTestRun);

            // Generate HOM candidates with metadata trackin
            IEnumerable<IMutant> homCandidates;
            
            if (actualIsPreTestRun)
            {
                homCandidates = CreateCandidateHOMs(algorithmName, isPreTestRun: true, validateSSHOM, requireProperSubset);
            }
            else
            {
                homCandidates = CreateCandidateHOMs(algorithmName, isPreTestRun: false, validateSSHOM, requireProperSubset);
            }

            var homCandidatesList = homCandidates.ToList();
            
            _logger.LogInformation("Generated {HomCount} HOM candidates.", homCandidatesList.Count);

            // Calculate mutant coverage
            var mutantsInHOMs = homCandidatesList.Where(h => h is HigherOrderMutant).Cast<HigherOrderMutant>().SelectMany(h => h.ConstituentMutants).Distinct();
            var mutantsMissing = mutantsToTest.Except(mutantsInHOMs).ToList();

            var mutantGroups = new List<IMutant>(homCandidates);

            if (testAllMutantsIndividually)
            {
                _logger.LogDebug("Adding all mutants individuall aswell");
                mutantGroups.Concat(mutantsToTest);
            }
            else if (includeMissingMutants && mutantsMissing.Count > 0)
            {
                _logger.LogDebug("Adding {MissingCount} mutants not included in HOMs as separate test group.",
                    mutantsMissing.Count);
                mutantGroups.Concat(mutantsMissing);
            }

            var endTime = DateTime.Now;
            var generationTime = endTime - startTime;

            _logger.LogInformation("Final HOM test plan: {GroupCount} HOMs covering {TotalMutants} mutants",
                homCandidatesList.Count, mutantsInHOMs.Count());

            return new HOMGenerationResult(
                mutantGroups,
                actualIsPreTestRun,
                algorithmUsed,
                heuristicRegistry?.RegisteredHeuristics.Count ?? 0,
                mutantsInHOMs.Count(),
                mutantsMissing.Count,
                generationTime,
                CreatedCandidates);
        }

        /// <summary>
        /// Sets up search algorithms for HOM generation.
        /// </summary>
        /// <param name="customAlgorithms">Custom algorithms to use, or null for defaults.</param>
        /// <param name="mutantsToTest">The mutants that will be used for HOM generation.</param>
        /// <param name="algorithmName">Specific algorithm name to use.</param>
        /// <returns>The name of the algorithm that will be used.</returns>
        private string SetupAlgorithms(IEnumerable<IHOMSearchAlgorithm> customAlgorithms, IReadOnlyCollection<IMutant> mutantsToTest, string algorithmName)
        {
            if (customAlgorithms != null)
            {
                // Clear existing algorithms and add custom ones
                _searchAlgorithms.Clear();
                foreach (var algorithm in customAlgorithms)
                {
                    AddSearchAlgorithm(algorithm);
                }
                _logger.LogDebug("Using {AlgorithmCount} custom algorithms.", _searchAlgorithms.Count);
            }
            else if (!_searchAlgorithms.Any())
            {
                // Add default algorithm if none are registered
                var defaultAlgorithm = new LocalSearchAlgorithm(_input, new List<IHOMHeuristic>(), _options, mutantsToTest);
                AddSearchAlgorithm(defaultAlgorithm);
                _logger.LogDebug("No algorithms specified, using default LocalSearchAlgorithm.");
            }

            // Determine which algorithm will be used
            if (!string.IsNullOrEmpty(algorithmName))
            {
                var selectedAlgorithm = _searchAlgorithms.FirstOrDefault(a => a.Name.Equals(algorithmName, StringComparison.OrdinalIgnoreCase));
                return selectedAlgorithm?.Name ?? "LocalSearch (fallback)";
            }
            
            return _searchAlgorithms.FirstOrDefault()?.Name ?? "Unknown";
        }

        /// <summary>
        /// Sets up heuristics for HOM generation using HeuristicRegistry.
        /// </summary>
        /// <param name="customHeuristics">Custom heuristics to use, or null for defaults.</param>
        /// <param name="isPreTestRun">Whether this is a pre-test run, affecting heuristic selection.</param>
        /// <returns>The configured HeuristicRegistry.</returns>
        private HeuristicRegistry SetupHeuristics(IEnumerable<IHOMHeuristic> customHeuristics, bool isPreTestRun)
        {
            HeuristicRegistry registry;
            
            if (customHeuristics != null)
            {
                // Create registry without default heuristics and add custom ones
                registry = new HeuristicRegistry(_allFirstOrderMutants, _options, _input, registerDefaultHeuristics: false);
                foreach (var heuristic in customHeuristics)
                {
                    registry.RegisterHeuristic(heuristic);
                }
                _logger.LogDebug("Using {HeuristicCount} custom heuristics.", registry.RegisteredHeuristics.Count);
            }
            else
            {
                // Use HeuristicRegistry with defaults, then filter based on pre/post-test scenario
                registry = new HeuristicRegistry(_allFirstOrderMutants, _options, _input, registerDefaultHeuristics: true);
                _logger.LogDebug("Using HeuristicRegistry with {HeuristicCount} default heuristics.", registry.RegisteredHeuristics.Count);
            }

            // Clear existing heuristics and sync with registry
            _heuristics.Clear();
            var applicableHeuristics = FilterHeuristicsForRunType(registry.RegisteredHeuristics, isPreTestRun);
            foreach (var heuristic in applicableHeuristics)
            {
                _heuristics.Add(heuristic);
            }

            return registry;
        }

        /// <summary>
        /// Creates candidate Higher-Order Mutants (HOMs) using a specified search algorithm.
        /// These candidates are groups of FOMs that need to be tested together.
        /// </summary>
        /// <param name="algorithmName">The name of the search algorithm to use. If null/empty, the first registered algorithm is used.</param>
        /// <param name="isPreTestRun">Whether this is a pre-test run (without killing test data) or post-test run (with killing test data).</param>
        /// <param name="validateSSHOM">Whether to validate potential SSHOMs using killing test data (only applicable for post-test runs).</param>
        /// <param name="requireProperSubset">When validating SSHOMs, whether to require a proper subset (stricter) or allow equal sets.</param>
        /// <returns>An enumerable of lists, where each inner list is a candidate HOM.</returns>
        private IEnumerable<HigherOrderMutant> CreateCandidateHOMs(string algorithmName = null, bool isPreTestRun = false, bool validateSSHOM = true, bool requireProperSubset = false)
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

            var runType = isPreTestRun ? "pre-test" : "post-test";
            _logger.LogDebug("Generating {RunType} HOM candidates using {AlgorithmName} with {FOMCount} eligible FOMs and {HeuristicCount} heuristics.",
                runType, selectedAlgorithm.Name, _allFirstOrderMutants.Count, _heuristics.Count);

            // Filter heuristics based on the run type
            var applicableHeuristics = FilterHeuristicsForRunType(_heuristics, isPreTestRun);

            foreach (var candidate in selectedAlgorithm.GenerateCandidates(_allFirstOrderMutants, applicableHeuristics, _options, _input))
            {
                // HOMs are typically of order 2 or higher.
                if (candidate != null && candidate.Order > 1)
                {                    
                    // Use next available ID for the HOM
                    candidate.Id = _options.MutantIdProvider.NextId(); // Start HIMs at high ID to avoid conflicts
                    
                    _homCandidates[candidate.Id] = candidate;
                    
                    var mutantIdsKey = string.Join(",", candidate.ConstituentMutants.OrderBy(m => m.Id).Select(m => m.Id));
                    _homCandidatesByMutantIds[mutantIdsKey] = candidate;

                    // For post-test runs with test killing data, we can validate SSHOMs
                    if (!isPreTestRun && validateSSHOM)
                    {
                        // Check if we have enough test data to validate SSHOM
                        if (HasSufficientTestData(candidate.ConstituentMutants))
                        {
                            _logger.LogTrace("Generated post-test HOM candidate {CandidateId} for potential SSHOM validation: {MutantIds}",
                                candidate.Id, candidate.MutantIdsString);
                        }
                        else
                        {
                            _logger.LogTrace("Skipping HOM candidate {CandidateId} due to insufficient test data: {MutantIds}", 
                                candidate.Id, candidate.MutantIdsString);
                            continue;
                        }
                    }
                    else if (isPreTestRun)
                    {
                        _logger.LogTrace("Generated pre-test HOM candidate {CandidateId}: {MutantIds}", 
                            candidate.Id, candidate.MutantIdsString);
                    }

                    yield return candidate;
                }
                else if (candidate != null && candidate.Order == 1)
                {
                    _logger.LogTrace("Search algorithm generated a single mutant group (order 1), which is a FOM, not a HOM. Skipping: Mutant Id {MutantId}", candidate.ConstituentMutants.First().Id);
                }
            }
        }

        /// <summary>
        /// Validates if a HOM candidate that has been tested is a Strongly Subsuming HOM (SSHOM).
        /// This method should be called after testing a HOM to determine if it's an SSHOM.
        /// </summary>
        /// <param name="testedHOMCandidate">The HOM candidate that has been tested.</param>
        /// <param name="homKillingTests">The set of tests that killed the HOM.</param>
        /// <param name="requireProperSubset">Whether to require a proper subset (stricter) or allow equal sets.</param>
        /// <returns>True if the tested HOM is an SSHOM, false otherwise.</returns>
        public bool ValidateSSHOM(IReadOnlyList<IMutant> testedHOMCandidate, ITestIdentifiers homKillingTests, bool requireProperSubset = false)
        {
            return IsSSHOM(testedHOMCandidate, homKillingTests, requireProperSubset);
        }

        /// <summary>
        /// Filters the provided HOM candidates to only include those that are likely to be SSHOMs
        /// based on the provided killing test results.
        /// </summary>
        /// <param name="testedCandidates">A collection of tested HOM candidates with their killing test results.</param>
        /// <param name="requireProperSubset">Whether to require a proper subset (stricter) or allow equal sets.</param>
        /// <returns>An enumerable of HOM candidates that are validated as SSHOMs.</returns>
        public IEnumerable<HigherOrderMutant> FilterSSHOMs(IEnumerable<(HigherOrderMutant candidate, ITestIdentifiers killingTests)> testedCandidates, bool requireProperSubset = false)
        {
            var sshomCount = 0;
            var totalCount = 0;

            foreach (var (candidate, killingTests) in testedCandidates)
            {
                totalCount++;
                if (IsSSHOM(candidate.ConstituentMutants, killingTests, requireProperSubset))
                {
                    sshomCount++;
                    yield return candidate;
                }
            }

            _logger.LogInformation("Identified {SSHOMCount} SSHOMs out of {TotalCount} tested HOM candidates.", sshomCount, totalCount);
        }

        /// <summary>
        /// Legacy method for backward compatibility with existing tests.
        /// Creates candidate Higher-Order Mutants (HOMs) using a specified search algorithm.
        /// Defaults to pre-test behavior (no test execution data required).
        /// </summary>
        /// <param name="algorithmName">The name of the search algorithm to use. If null/empty, the first registered algorithm is used.</param>
        /// <returns>An enumerable of lists, where each inner list is a candidate HOM.</returns>
        [Obsolete("Use CreatePreTestCandidateHOMs() or CreatePostTestCandidateHOMs() instead for explicit pre/post-test scenarios")]
        public IEnumerable<HigherOrderMutant> CreateCandidateHOMs(string algorithmName = null)
        {
            // For backward compatibility, default to pre-test behavior
            return CreatePreTestCandidateHOMs(algorithmName);
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
        private IReadOnlyList<IHOMHeuristic> FilterHeuristicsForRunType(IEnumerable<IHOMHeuristic> allHeuristics, bool isPreTestRun)
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
        /// Checks if the HOM candidate has sufficient test data for SSHOM validation.
        /// This includes verifying that all constituent FOMs have killing test data.
        /// </summary>
        /// <param name="candidate">The HOM candidate to check.</param>
        /// <returns>True if there's sufficient test data, false otherwise.</returns>
        private bool HasSufficientTestData(List<IMutant> candidate)
        {
            return candidate.All(mutant => mutant.KillingTests != null && !mutant.KillingTests.IsEmpty);
        }

        /// <summary>
        /// Analyzes all tested HOM candidates for SSHOMs and records results.
        /// This should be called after all mutation testing is complete.
        /// </summary>
        /// <param name="requireProperSubset">Whether to require proper subsets for SSHOM validation.</param>
        /// <returns>A summary of SSHOM analysis results.</returns>
        public SSHOMAnalysisResult AnalyzeHOMsForSSHOMs(bool requireProperSubset = false)
        {
            if (!_options.OptimizationMode.HasFlag(OptimizationModes.EnableHigherOrderMutations))
            {
                _logger.LogDebug("Higher-Order Mutations not enabled, skipping SSHOM analysis.");
                return new SSHOMAnalysisResult(0, 0, 0, TimeSpan.Zero);
            }

            var startTime = DateTime.Now;
            _logger.LogInformation("Starting SSHOM analysis...");
            
            var testedCandidates = TestedCandidates.ToList();
            var sshomCount = 0;
            var analyzedCount = 0;

            foreach (var candidate in testedCandidates)
            {
                analyzedCount++;
                if (ValidateSSHOMFromCandidate(candidate, requireProperSubset))
                {
                    sshomCount++;
                    _logger.LogDebug("SSHOM found: Candidate {CandidateId} with mutants [{MutantIds}]", 
                        candidate.Id, candidate.MutantIdsString);
                }
            }

            var analysisTime = DateTime.Now - startTime;
            
            _logger.LogInformation("SSHOM Analysis Complete: {SSHOMCount} SSHOMs found out of {AnalyzedCount} tested candidates in {AnalysisTime}ms", 
                sshomCount, analyzedCount, analysisTime.TotalMilliseconds);

            return new SSHOMAnalysisResult(sshomCount, analyzedCount, testedCandidates.Count, analysisTime);
        }

        /// <summary>
        /// Creates HOM mutants from the generated candidates for integration into the existing testing flow.
        /// </summary>
        /// <param name="homCandidates">The HOM candidates to convert to mutants.</param>
        /// <param name="startingId">The starting ID for HOM mutants to avoid conflicts with FOMs.</param>
        /// <returns>A list of HOM mutants ready for testing.</returns>
        public List<HigherOrderMutant> CreateHOMMutantsFromCandidates(IEnumerable<List<IMutant>> homCandidates, int startingId)
        {
            var homMutants = new List<HigherOrderMutant>();
            var currentId = startingId;

            foreach (var candidate in homCandidates)
            {
                var higherOrderMutant = GetCandidateByMutants(candidate);
                if (candidate == null)
                {
                    _logger.LogWarning("Could not find HOM candidate for mutants: {MutantIds}", 
                        string.Join(",", candidate.Select(m => m.Id)));
                    continue;
                }

                // Set the Id and Mutation properties for testing integration
                higherOrderMutant.Id = currentId++;
                higherOrderMutant.Mutation = new Mutation
                {
                    DisplayName = $"HOM({string.Join(",", candidate.Select(m => m.Id))})",
                    Type = Mutator.Statement,
                    Description = $"Higher-Order Mutant combining {candidate.Count} mutations"
                };

                homMutants.Add(higherOrderMutant);
            }

            _logger.LogInformation("Prepared {HOMCount} HOM mutants for testing", homMutants.Count);
            return homMutants;
        }

        /// <summary>
        /// Records test results for HOM mutants and updates their test status.
        /// </summary>
        /// <param name="homMutants">The tested HOM mutants.</param>
        public void RecordHOMutantTestResults(IEnumerable<HigherOrderMutant> homMutants)
        {
            foreach (var homMutant in homMutants.Where(hom => hom.ResultStatus != MutantStatus.Pending))
            {
                _logger.LogDebug("Recorded test result for HOM mutant {MutantId}: {Status}", 
                    homMutant.Id, homMutant.ResultStatus);
            }
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
