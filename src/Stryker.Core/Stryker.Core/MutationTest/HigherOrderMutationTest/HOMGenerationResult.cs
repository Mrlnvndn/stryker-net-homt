using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Core.Mutants;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest
{
    /// <summary>
    /// Result of Higher-Order Mutant generation with metadata about the process.
    /// </summary>
    public class HOMGenerationResult
    {
        public HOMGenerationResult(
            IEnumerable<IMutant> mutantGroups,
            bool isPreTestRun,
            string algorithmUsed,
            int heuristicsUsed,
            int mutantsIncludedInHOMs,
            int mutantsMissingFromHOMs,
            TimeSpan generationTime,
            IEnumerable<HigherOrderMutant> candidatesCreated = null)
        {
            MutantGroups = mutantGroups.ToList();
            IsPreTestRun = isPreTestRun;
            AlgorithmUsed = algorithmUsed ?? "Unknown";
            HeuristicsUsed = heuristicsUsed;
            MutantsIncludedInHOMs = mutantsIncludedInHOMs;
            MutantsMissingFromHOMs = mutantsMissingFromHOMs;
            GenerationTime = generationTime;
            CandidatesCreated = candidatesCreated?.ToList() ?? new List<HigherOrderMutant>();
        }

        /// <summary>
        /// Additional constructor for more parameters.
        /// </summary>
        public HOMGenerationResult(
            IEnumerable<IMutant> mutantGroups,
            bool isPreTestRun,
            string algorithmUsed,
            int heuristicsUsed,
            int candidatesGenerated,
            int candidatesFiltered,
            int mutantsIncludedInHOMs,
            int mutantsMissingFromHOMs,
            TimeSpan generationTime,
            IEnumerable<HigherOrderMutant> candidatesCreated = null)
        {
            MutantGroups = mutantGroups.ToList();
            IsPreTestRun = isPreTestRun;
            AlgorithmUsed = algorithmUsed ?? "Unknown";
            HeuristicsUsed = heuristicsUsed;
            CandidatesGenerated = candidatesGenerated;
            CandidatesFiltered = candidatesFiltered;
            MutantsIncludedInHOMs = mutantsIncludedInHOMs;
            MutantsMissingFromHOMs = mutantsMissingFromHOMs;
            GenerationTime = generationTime;
            CandidatesCreated = candidatesCreated?.ToList() ?? new List<HigherOrderMutant>();
        }

        /// <summary>
        /// The generated HOM groups ready for testing.
        /// </summary>
        public IReadOnlyList<IMutant> MutantGroups { get; }

        /// <summary>
        /// The individual HOM candidates created during generation.
        /// </summary>
        public IReadOnlyList<HigherOrderMutant> CandidatesCreated { get; }

        /// <summary>
        /// Whether this was a pre-test run (without killing test data) or post-test run (with killing test data).
        /// </summary>
        public bool IsPreTestRun { get; }

        /// <summary>
        /// The name of the algorithm used for HOM generation.
        /// </summary>
        public string AlgorithmUsed { get; }

        /// <summary>
        /// The number of heuristics used during generation.
        /// </summary>
        public int HeuristicsUsed { get; }

        /// <summary>
        /// The total number of HOM candidates generated before filtering.
        /// </summary>
        public int CandidatesGenerated { get; }

        /// <summary>
        /// The number of candidates filtered out by heuristics.
        /// </summary>
        public int CandidatesFiltered { get; }

        /// <summary>
        /// The number of mutants that were included in at least one HOM.
        /// </summary>
        public int MutantsIncludedInHOMs { get; }

        /// <summary>
        /// The number of mutants that were not included in any HOM.
        /// </summary>
        public int MutantsMissingFromHOMs { get; }

        /// <summary>
        /// The time taken to generate the HOMs.
        /// </summary>
        public TimeSpan GenerationTime { get; }

        /// <summary>
        /// Gets the total number of test groups (HOMs + missing mutants if included).
        /// </summary>
        public int TotalTestGroups => MutantGroups.Count;

        /// <summary>
        /// Gets the total number of mutants covered by the test plan.
        /// </summary>
        public int TotalMutantsCovered => MutantGroups.Where(h => h is HigherOrderMutant).Cast<HigherOrderMutant>().SelectMany(g => g.ConstituentMutants).Count() + MutantGroups.Count(h => !(h is HigherOrderMutant));

        /// <summary>
        /// Gets the average HOM size.
        /// </summary>
        public double AverageHOMSize => CandidatesCreated.Any() ? CandidatesCreated.Average(c => c.Order) : 0;

        /// <summary>
        /// Gets the efficiency ratio (mutants in HOMs vs total mutants).
        /// </summary>
        public double EfficiencyRatio => TotalMutantsCovered > 0 ? (double)MutantsIncludedInHOMs / TotalMutantsCovered : 0;

        /// <summary>
        /// Gets the tested HOM candidates.
        /// </summary>
        public IEnumerable<HigherOrderMutant> TestedCandidates => CandidatesCreated.Where(c => c.HasBeenTested);

        /// <summary>
        /// Gets the validated SSHOMs.
        /// </summary>
        public IEnumerable<HigherOrderMutant> ValidatedSSHOMs => CandidatesCreated.Where(c => c.IsValidatedSSHOM == true);
    }
}
