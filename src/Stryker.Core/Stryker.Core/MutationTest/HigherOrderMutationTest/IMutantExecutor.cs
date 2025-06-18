using Stryker.Abstractions;
using System.Collections.Generic;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest
{
    /// <summary>
    /// Interface for executing mutants and retrieving test results.
    /// </summary>
    public interface IMutantExecutor
    {
        /// <summary>
        /// Executes tests on a mutant and returns the result.
        /// </summary>
        /// <param name="mutant">The mutant to execute tests on.</param>
        /// <returns>True if the mutant was killed, false otherwise.</returns>
        bool ExecuteMutant(IMutant mutant);
        
        /// <summary>
        /// Executes tests on a composite higher-order mutant.
        /// </summary>
        /// <param name="mutants">The collection of mutants comprising the HOM.</param>
        /// <returns>True if the mutant was killed, false otherwise.</returns>
        bool ExecuteHOM(IReadOnlyCollection<IMutant> mutants);
    }
}
