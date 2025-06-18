namespace Stryker.Core.MutationTest.HigherOrderMutationTest
{
    /// <summary>
    /// Interface for reporting timeout occurrences during heuristic-guided searches.
    /// </summary>
    public interface ITimeoutHeuristicReporter
    {
        /// <summary>
        /// Reports a timeout occurrence.
        /// </summary>
        void ReportTimeout();
        
        /// <summary>
        /// Gets the current number of timeouts that have occurred.
        /// </summary>
        int TimeoutCount { get; }
    }
}
