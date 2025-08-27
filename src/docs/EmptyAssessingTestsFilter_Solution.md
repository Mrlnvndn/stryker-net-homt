# Solution: Filtering HOMs with Empty Assessing Tests

## Problem Analysis

The duplicate FOM warnings in VsTestRunner (`"FOM 659 was already in mutant test map, skipping duplicate"`) occur when:

1. **Higher-Order Mutants (HOMs) are generated** from constituent FOMs that have **no overlapping assessing tests**
2. The **intersection of assessing tests is empty** (e.g., FOM A has `["TestX", "TestY"]`, FOM B has `["TestZ", "TestW"]`)
3. These **HOMs with empty assessing tests can be grouped** with regular FOMs in `BuildMutantGroupsForTest` because they don't overlap with any test coverage
4. **VsTestRunner's dual-mode expansion** tries to add both the regular FOM and the HOM's constituent FOM to the `mutantTestsMap`, causing duplicates

## Root Cause

The issue stems from the HOM generation algorithms not filtering out HOMs that would be **untestable** due to having no assessing tests. These HOMs:
- Cannot be killed (no tests assess them)
- Cause grouping issues (empty assessing tests don't overlap with anything)
- Lead to duplicate FOM IDs in the mutation test mapping

## Solution: Multi-Layer Filtering Approach

### 1. **EmptyAssessingTestsFilterHeuristic**
- **New filtering heuristic** that detects and filters out HOMs with empty assessing tests
- **Integrated into both algorithms** (LocalSearchAlgorithm and GeneticSearchAlgorithm)
- **Prevents generation** of problematic candidates at the source

```csharp
public override bool ShouldFilterCandidate(List<IMutant> candidate)
{
    if (candidate == null || candidate.Count < 2)
        return true; // Filter out invalid candidates
    
    // Create temporary HOM to calculate assessing tests intersection
    var tempHOM = new HigherOrderMutant(candidate);
    return tempHOM.AssessingTests?.IsEmpty ?? true;
}
```

### 2. **Algorithm-Level Filtering**
- **LocalSearchAlgorithm**: Uses `_heuristicRegistry.ShouldFilterCandidate(candidate)` before yielding
- **GeneticSearchAlgorithm**: Uses `_heuristicRegistry.ShouldFilterCandidate(candidate)` during population processing
- **Prevents bad candidates** from ever reaching the main HOM generation pipeline

### 3. **Safety Net in CreateCandidateHOMs**
- **Additional validation** in `HigherOrderMutation.CreateCandidateHOMs()`
- **Double-checks** that no HOM with empty assessing tests slips through
- **Logs filtered candidates** for debugging purposes

```csharp
// Safety check: Skip HOMs with empty assessing tests
if (candidate.AssessingTests?.IsEmpty ?? true)
{
    _logger.LogDebug("Filtered HOM candidate {CandidateOrder} with empty assessing tests", candidate.Order);
    continue;
}
```

## Benefits

1. **Eliminates Duplicate Warnings**: No more HOMs with empty assessing tests means no problematic grouping
2. **Improves Test Efficiency**: Only testable HOMs are generated and tested
3. **Better Resource Usage**: Avoids creating and processing untestable mutants
4. **Maintains HOM Quality**: Focuses on HOMs that can actually be validated as SSHOMs

## Implementation Details

### Files Modified:
- `EmptyAssessingTestsFilterHeuristic.cs` (new)
- `HeuristicRegistry.cs` - Added to default heuristics
- `LocalSearchAlgorithm.cs` - Added to algorithm-specific heuristics  
- `HigherOrderMutation.cs` - Added safety net validation

### Files Added:
- `EmptyAssessingTestsFilterHeuristicTests.cs` - Unit tests
- `EmptyAssessingTestsFilterIntegrationTests.cs` - Integration tests

## Testing

The solution includes comprehensive tests:
- **Unit tests** for the heuristic itself
- **Integration tests** for the end-to-end filtering
- **Scenario tests** for the original problem case

## Backward Compatibility

- **No breaking changes** to public APIs
- **Purely additive** solution (new heuristic + validation)
- **Existing functionality** remains unchanged
- **Performance impact** is minimal (early filtering actually improves performance)
