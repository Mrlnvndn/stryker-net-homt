# Higher-Order Mutant (HOM) Dual-Mode XML Generation Fix

## Overview

This document describes the comprehensive fix implemented to resolve HOM (Higher-Order Mutant) tracking issues in Stryker.NET's mutation testing framework. The fix ensures that both HOM status tracking and FOM (First-Order Mutant) activation work correctly during mutation testing.

## Problem Statement

The original issue was that when Higher-Order Mutants were created, only the HOM ID was being passed to the XML generation and `MutantControl.IsActive()` system. This caused:

1. **MutantControl Activation Failures**: `MutantControl.IsActive(fom_id)` returned false because the FOM IDs weren't in the active mutant set
2. **Broken HOM Status Tracking**: HOM status was never updated because the constituent FOMs couldn't report back to their parent HOM
3. **SSHOM Validation Issues**: SSHOM validation couldn't work because HOMs didn't retain their test execution results

## Solution: Dual-Mode XML Generation

The solution implements a **dual-mode XML generation system** that maintains both HOM and FOM information throughout the testing pipeline.

### Key Components

#### 1. Enhanced VsTestRunner.TestCases Method (`Stryker.TestRunner.VsTest\VsTestRunner.cs`)

```csharp
private ICollection<string> TestCases(IReadOnlyList<IMutant> mutants, Dictionary<int, ITestIdentifiers> mutantTestsMap)
{
    var homToFomMapping = new Dictionary<int, List<int>>();
    var fomToHomMapping = new Dictionary<int, int>();
    
    // For each HOM, add BOTH the HOM ID and all constituent FOM IDs
    if (mutant.GetType().Name == "HigherOrderMutant")
    {
        // CRITICAL: Add BOTH HOM and FOM IDs to mutantTestsMap
        mutantTestsMap.Add(mutant.Id, tests); // HOM for status tracking
        
        foreach (var constituentMutant in constituentMutants)
        {
            // Add FOM for MutantControl activation  
            mutantTestsMap.Add(constituentMutant.Id, tests);
            
            // Create bidirectional mapping
            fomIds.Add(constituentMutant.Id);
            fomToHomMapping[constituentMutant.Id] = mutant.Id;
        }
        
        homToFomMapping[mutant.Id] = fomIds;
    }
    
    // Store mappings in context for XML generation
    _context.SetHomMappings(homToFomMapping, fomToHomMapping);
}
```

**What this achieves:**
- ✅ HOM IDs are included for status tracking
- ✅ FOM IDs are included for `MutantControl.IsActive()` 
- ✅ Bidirectional mapping preserves relationships

#### 2. Enhanced VsTestContextInformation (`Stryker.TestRunner.VsTest\VsTestContextInformation.cs`)

```csharp
public sealed class VsTestContextInformation : IDisposable
{
    private Dictionary<int, List<int>> _homToFomMapping = new();
    private Dictionary<int, int> _fomToHomMapping = new();

    public void SetHomMappings(Dictionary<int, List<int>> homToFom, Dictionary<int, int> fomToHom)
    {
        _homToFomMapping = homToFom ?? new Dictionary<int, List<int>>();
        _fomToHomMapping = fomToHom ?? new Dictionary<int, int>();
    }

    public string GenerateRunSettings(...)
    {
        var dataCollectorSettings = needDataCollector
            ? CoverageCollector.GetVsTestSettings(..., _homToFomMapping) // Pass HOM mapping
            : string.Empty;
    }
}
```

**What this achieves:**
- ✅ Stores HOM-FOM mappings for XML generation
- ✅ Passes mappings to CoverageCollector for rich metadata

#### 3. Enhanced CoverageCollector XML Generation (`Stryker.DataCollector\Stryker.DataCollector\CoverageCollector.cs`)

```csharp
public static string GetVsTestSettings(bool needCoverage,
    IEnumerable<(int mutant, IEnumerable<Guid> coveringTests)> mutantTestsMap,
    string helperNameSpace, bool isHomt = false, 
    Dictionary<int, List<int>> homToFomMapping = null)
{
    foreach (var (mutant, coveringTests) in mutantTestsMap)
    {
        // Check if this is a HOM with constituent FOMs
        if (homToFomMapping?.ContainsKey(mutant) == true)
        {
            // This is a HOM - add with metadata
            configuration.AppendFormat("<Mutant id='{0}' tests='{1}' type='hom' constituents='{2}'/>", 
                mutant, testGuids, string.Join(",", homToFomMapping[mutant]));
        }
        else
        {
            // Check if this is a FOM that belongs to a HOM
            var parentHomId = homToFomMapping?.FirstOrDefault(kvp => kvp.Value.Contains(mutant)).Key;
            if (parentHomId.HasValue && parentHomId != 0)
            {
                // This is a constituent FOM
                configuration.AppendFormat("<Mutant id='{0}' tests='{1}' type='fom' parent='{2}'/>", 
                    mutant, testGuids, parentHomId.Value);
            }
            else
            {
                // This is a regular FOM
                configuration.AppendFormat("<Mutant id='{0}' tests='{1}' type='fom'/>", mutant, testGuids);
            }
        }
    }
}
```

**Generated XML Example:**
```xml
<RunSettings>
  <DataCollectionConfiguration>
    <DataCollectors>
      <DataCollector friendlyName="Stryker">
        <Configuration>
          <NameSpace>StrykerDataCollector</NameSpace>
          <Homt>true</Homt>
          <Mutants>
            <!-- HOM entry for status tracking -->
            <Mutant id="100" type="hom" constituents="1,2" tests="guid1,guid2"/>
            <!-- Constituent FOM entries for MutantControl activation -->
            <Mutant id="1" type="fom" parent="100" tests="guid1,guid2"/>
            <Mutant id="2" type="fom" parent="100" tests="guid1,guid2"/>
            <!-- Regular FOM -->
            <Mutant id="5" type="fom" tests="guid3"/>
          </Mutants>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </RunSettings>
</RunSettings>
```

**What this achieves:**
- ✅ Rich metadata showing HOM-FOM relationships
- ✅ Both HOM and FOM entries for complete tracking
- ✅ Type information for future extensibility

#### 4. Enhanced MutationTestProcess Status Tracking (`Stryker.Core\Stryker.Core\MutationTest\MutationTestProcess.cs`)

```csharp
private bool TestUpdateHandler(IEnumerable<IMutant> testedMutants, ITestIdentifiers failedTests, ITestIdentifiers ranTests,
    ITestIdentifiers timedOutTest, ISet<IMutant> reportedMutants)
{
    var updatedHoms = new HashSet<int>(); // Track which HOMs we've already updated

    foreach (var mutant in testedMutants)
    {
        mutant.AnalyzeTestRun(failedTests, ranTests, timedOutTest, false);

        // Enhanced HOM status tracking: If this is a FOM that belongs to a HOM, update the parent HOM
        if (mutant.GetType().Name != "HigherOrderMutant")
        {
            var parentHom = FindParentHOM(mutant.Id, testedMutants);
            if (parentHom != null && !updatedHoms.Contains(parentHom.Id))
            {
                UpdateHOMWithFOMResults(parentHom, failedTests, ranTests, timedOutTest);
                updatedHoms.Add(parentHom.Id);
            }
        }
        // ... rest of logic
    }
}

private void UpdateHOMWithFOMResults(IMutant hom, ITestIdentifiers failedTests, ITestIdentifiers ranTests, ITestIdentifiers timedOutTest)
{
    if (hom.GetType().GetProperty("ConstituentMutants")?.GetValue(hom) is IEnumerable<IMutant> constituentFoms)
    {
        var constituentsList = constituentFoms.ToList();
        
        // Strategy: HOM is killed if any constituent FOM is killed
        if (constituentsList.Any(fom => fom.ResultStatus == MutantStatus.Killed))
        {
            hom.ResultStatus = MutantStatus.Killed;
            // Merge killing tests from all killed FOMs
            var allKillingTests = constituentsList.Where(f => f.ResultStatus == MutantStatus.Killed)
                                                 .Aggregate(TestIdentifierList.NoTest(), 
                                                           (current, killedFom) => current.Merge(killedFom.KillingTests));
            hom.KillingTests = allKillingTests;
        }
        // ... other status logic
    }
}
```

**What this achieves:**
- ✅ HOMs get updated when constituent FOMs are tested
- ✅ HOM status reflects constituent FOM results
- ✅ Killing tests are properly aggregated from FOMs to HOM

## Benefits of the Solution

### 1. Fixes MutantControl.IsActive()
- FOM IDs are now in the XML, so `MutantControl.IsActive(fom_id)` returns true
- Higher-order mutations are properly activated during test execution

### 2. Enables HOM Status Tracking
- HOM IDs are also in the XML, ensuring status can be tracked
- HOMs receive status updates based on their constituent FOM results
- Proper aggregation of killing tests from FOMs to HOMs

### 3. Supports SSHOM Validation
- HOMs retain their test execution results for analysis
- SSHOM (Strongly Subsuming Higher-Order Mutant) validation can function correctly
- Complete test result data is available for advanced HOM analysis

### 4. Rich XML Metadata
- Clear type differentiation between HOMs and FOMs
- Parent-child relationships explicitly documented
- Constituent information preserved for analysis

### 5. Backward Compatibility
- Regular FOMs work exactly as before
- No breaking changes to existing functionality
- Graceful fallback for edge cases

## Test Coverage

### CoverageCollector XML Generation Tests
- **File**: `Stryker.DataCollector\Stryker.DataCollector.UnitTest\CoverageCollectorDualModeXmlTests.cs`
- **Coverage**: XML structure validation, HOM-FOM relationships, complex scenarios

### VsTestRunner Dual-Mode Tests  
- **File**: `Stryker.TestRunner.VsTest.UnitTest\VsTestRunnerDualModeTests.cs`
- **Coverage**: Dual-mode mutant mapping, MutantControl compatibility, complex HOM scenarios

### Integration Tests
- **File**: `Stryker.TestRunner.VsTest.UnitTest\VsTestRunnerHOMExpansionTests.cs`
- **Coverage**: End-to-end HOM expansion, fallback scenarios, mixed HOM/FOM testing

## Implementation Details

### Key Design Decisions

1. **Dual-Mode Approach**: Instead of choosing between HOM or FOM IDs, include both for complete functionality
2. **Bidirectional Mapping**: Maintain both HOM→FOM and FOM→HOM mappings for efficient lookups
3. **Rich XML Metadata**: Include type and relationship information for future extensibility
4. **Graceful Fallbacks**: Handle reflection failures and edge cases robustly

### Performance Considerations

- **Memory**: Small increase due to bidirectional mapping storage
- **XML Size**: Larger XML due to dual entries, but provides complete information
- **Processing**: Minimal overhead for mapping lookups during status updates

### Compatibility

- **Forward Compatible**: Rich XML metadata supports future HOM features
- **Backward Compatible**: Existing FOM functionality unchanged
- **Fallback Safe**: Graceful handling of reflection failures and edge cases

## Testing the Fix

To verify the fix works correctly:

1. **Run the Unit Tests**:
   ```bash
   dotnet test Stryker.DataCollector.UnitTest --filter "CoverageCollectorDualModeXmlTests"
   dotnet test Stryker.TestRunner.VsTest.UnitTest --filter "VsTestRunnerDualModeTests"
   ```

2. **Check XML Output**: Verify that generated XML contains both HOM and FOM entries with proper metadata

3. **Validate MutantControl**: Ensure `MutantControl.IsActive(fom_id)` returns true for constituent FOMs

4. **Test HOM Status**: Verify that HOMs receive proper status updates when constituent FOMs are tested

## Future Enhancements

This dual-mode approach enables several future enhancements:

1. **Advanced SSHOM Analysis**: Rich test result data supports sophisticated SSHOM detection
2. **HOM Performance Metrics**: Detailed timing and execution data for HOMs vs constituent FOMs  
3. **Interactive Reporting**: UI can show HOM-FOM relationships and drill-down capabilities
4. **Optimization Opportunities**: Analysis of which HOMs provide testing efficiency gains

## Conclusion

The dual-mode XML generation fix provides a comprehensive solution to HOM tracking issues while maintaining backward compatibility and enabling future enhancements. The solution ensures that both HOM status tracking and FOM activation work correctly, supporting the full spectrum of higher-order mutation testing capabilities in Stryker.NET.
