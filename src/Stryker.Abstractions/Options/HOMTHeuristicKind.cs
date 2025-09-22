namespace Stryker.Abstractions.Options;

/// <summary>
/// Available heuristics for Higher-Order Mutation Testing.
/// </summary>
public enum HOMTHeuristicKind
{
    /// <summary>
    /// Scores candidates based on code location proximity.
    /// </summary>
    CodeLocation,
    
    /// <summary>
    /// Filters candidates with empty assessing tests.
    /// </summary>
    EmptyAssessingTests,
    
    /// <summary>
    /// Scores candidates based on mutator type compatibility.
    /// </summary>
    MutatorType,
    
    /// <summary>
    /// Filters candidates that exceed maximum size limits.
    /// </summary>
    MaxSizeLimit,
    
    /// <summary>
    /// Scores candidates based on overlapping test coverage.
    /// </summary>
    OverlappingTests,
    
    /// <summary>
    /// Filters candidates with syntax node conflicts.
    /// </summary>
    SyntaxNodeConflict
}
