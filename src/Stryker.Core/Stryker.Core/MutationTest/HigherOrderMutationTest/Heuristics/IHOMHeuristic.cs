namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Interface for heuristics that can guide HOM search algorithms.
    /// </summary>
    public interface IHOMHeuristic
    {
        string Name { get; }

        // This is a placeholder. Actual methods would depend on how heuristics are applied.
        // For example, a heuristic might rank FOMs, or evaluate HOM candidates.
        // void Initialize(IReadOnlyCollection<IMutant> availableFOMs, IStrykerOptions options, MutationTestInput input);
        // double ScoreCandidate(List<IMutant> homCandidate);
        // List<IMutant> SelectNextMutant(List<IMutant> currentHomCandidate, IReadOnlyCollection<IMutant> remainingFOMs);
    }
}