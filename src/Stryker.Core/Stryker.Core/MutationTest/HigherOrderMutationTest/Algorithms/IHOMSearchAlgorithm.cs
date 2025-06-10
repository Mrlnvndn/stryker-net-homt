using System.Collections.Generic;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms
{
    /// <summary>
    /// Interface for search algorithms that generate candidate Higher-Order Mutants.
    /// </summary>
    public interface IHOMSearchAlgorithm
    {
        string Name { get; }

        /// <summary>
        /// Generates candidate HOMs from a collection of available First-Order Mutants (FOMs).
        /// </summary>
        /// <param name="availableFOMs">The pool of FOMs to select from.</param>
        /// <param name="heuristics">A list of heuristics to guide the search.</param>
        /// <param name="options">Stryker options.</param>
        /// <param name="input">Mutation test input for additional context.</param>
        /// <returns>An enumerable of lists, where each inner list represents a candidate HOM.</returns>
        IEnumerable<List<IMutant>> GenerateCandidates(
            IReadOnlyCollection<IMutant> availableFOMs,
            IReadOnlyList<IHOMHeuristic> heuristics,
            IStrykerOptions options,
            MutationTestInput input);
    }
}