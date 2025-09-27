using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stryker.Abstractions;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest;

/// <summary>
/// Class to represent a candidate with its score (same as original LocalSearchAlgorithm).
/// </summary>
public class ScoredCandidate(List<IMutant> candidate, double score)
{
    public List<IMutant> Candidate { get; } = candidate;
    public double Score { get; } = score;
}
