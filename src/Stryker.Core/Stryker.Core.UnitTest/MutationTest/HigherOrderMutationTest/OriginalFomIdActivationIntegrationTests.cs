using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Testing;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest;

[TestClass]
public class OriginalFomIdActivationIntegrationTests : TestBase
{
    [TestMethod]
    public void EndToEndFlow_OriginalFomIdsUsedForActivation_ShouldWorkCorrectly()
    {
        // This test simulates the complete flow from HOM creation to MutantControl activation
        
        // Arrange - Create original FOMs
        var originalFOM1 = new Mutant 
        {
            Id = 10,
            ResultStatus = MutantStatus.Pending,
            CoveringTests = new TestIdentifierList(new[] { "TestA" }),
            AssessingTests = new TestIdentifierList(new[] { "TestA" }),
            Mutation = new Mutation { DisplayName = "x + y -> x - y", Type = Mutator.Arithmetic }
        };
        
        var originalFOM2 = new Mutant 
        {
            Id = 20,
            ResultStatus = MutantStatus.Pending,
            CoveringTests = new TestIdentifierList(new[] { "TestA" }),
            AssessingTests = new TestIdentifierList(new[] { "TestA" }),
            Mutation = new Mutation { DisplayName = "true -> false", Type = Mutator.Boolean }
        };

        // Step 1: Create HOM with deep copies
        var hom = new HigherOrderMutant(new List<IMutant> { originalFOM1, originalFOM2 }) { Id = 1000 };
        
        var mockIdProvider = new Mock<IProvideId>();
        mockIdProvider.SetupSequence(x => x.NextId())
            .Returns(1001) // Composite ID for FOM1 copy
            .Returns(1002); // Composite ID for FOM2 copy
            
        hom.CreateDeepCopies(mockIdProvider.Object);
        
        // Step 2: Verify deep copies were created with composite IDs
        hom.ConstituentMutants.Count.ShouldBe(2);
        hom.ConstituentMutants[0].Id.ShouldBe(1001); // Composite ID
        hom.ConstituentMutants[1].Id.ShouldBe(1002); // Composite ID
        
        var constituent1 = hom.ConstituentMutants[0] as Mutant;
        var constituent2 = hom.ConstituentMutants[1] as Mutant;
        constituent1.OriginalFomId.ShouldBe(10); // Original FOM ID preserved
        constituent2.OriginalFomId.ShouldBe(20); // Original FOM ID preserved
        
        // Step 3: Verify GetOriginalFomIdsForActivation returns original IDs
        var originalIdsForActivation = hom.GetOriginalConstituentMutantIdsForActivation();
        originalIdsForActivation.ShouldBe(new[] { 10, 20 });
        
        // Step 4: Simulate what VsTestRunner.TestCases would do
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();
        
        // HOM is added for status tracking
        mutantTestsMap[hom.Id] = hom.AssessingTests; // HOM ID: 1000
        
        // Original FOM IDs are added for activation (this is the key fix)
        foreach (var originalFomId in originalIdsForActivation)
        {
            mutantTestsMap[originalFomId] = hom.AssessingTests;
        }
        
        // Step 5: Verify the mutantTestsMap contains the correct entries
        mutantTestsMap.ShouldContainKey(1000); // HOM ID for status tracking
        mutantTestsMap.ShouldContainKey(10);   // Original FOM ID for activation
        mutantTestsMap.ShouldContainKey(20);   // Original FOM ID for activation
        mutantTestsMap.ShouldNotContainKey(1001); // Composite IDs should NOT be in activation map
        mutantTestsMap.ShouldNotContainKey(1002); // Composite IDs should NOT be in activation map
        
        // Step 6: Simulate what CoverageCollector would do
        var activeSet = new HashSet<int>(mutantTestsMap.Keys);
        
        // Step 7: Simulate what MutantControl.IsActive would check
        // This is what the injected mutation code would call
        var fom1WouldBeActive = activeSet.Contains(10); // Original FOM 1 ID
        var fom2WouldBeActive = activeSet.Contains(20); // Original FOM 2 ID
        
        // Step 8: Assert that the original mutations would be activated
        fom1WouldBeActive.ShouldBeTrue("Original FOM 1 (ID=10) should be activatable for HOM testing");
        fom2WouldBeActive.ShouldBeTrue("Original FOM 2 (ID=20) should be activatable for HOM testing");
        
        // Step 9: Verify that composite IDs would NOT interfere
        var composite1WouldBeActive = activeSet.Contains(1001); // Composite ID should not be active
        var composite2WouldBeActive = activeSet.Contains(1002); // Composite ID should not be active
        
        composite1WouldBeActive.ShouldBeFalse("Composite ID 1001 should NOT be in activation set");
        composite2WouldBeActive.ShouldBeFalse("Composite ID 1002 should NOT be in activation set");
        
        // Success! The flow ensures:
        // 1. Original mutations (10, 20) can be activated by MutantControl.IsActive()
        // 2. HOM (1000) can receive status updates
        // 3. Composite IDs (1001, 1002) are used only for internal tracking
        // 4. No interference between individual FOM results and HOM constituent results
    }

    [TestMethod]
    public void MultipleHOMsWithSharedFOM_ShouldHandleActivationCorrectly()
    {
        // Test scenario where a FOM is part of multiple HOMs
        
        // Arrange - Create original FOMs
        var fom1 = new Mutant { Id = 1, CoveringTests = new TestIdentifierList(new[] { "Test1" }), AssessingTests = new TestIdentifierList(new[] { "Test1" }) };
        var fom2 = new Mutant { Id = 2, CoveringTests = new TestIdentifierList(new[] { "Test2" }), AssessingTests = new TestIdentifierList(new[] { "Test2" }) };
        var fom3 = new Mutant { Id = 3, CoveringTests = new TestIdentifierList(new[] { "Test3" }), AssessingTests = new TestIdentifierList(new[] { "Test3" }) };

        // Create HOMs where FOM1 is shared
        var hom1 = new HigherOrderMutant(new List<IMutant> { fom1, fom2 }) { Id = 100 }; // HOM1: FOM1 + FOM2
        var hom2 = new HigherOrderMutant(new List<IMutant> { fom1, fom3 }) { Id = 200 }; // HOM2: FOM1 + FOM3
        
        var mockIdProvider1 = new Mock<IProvideId>();
        mockIdProvider1.SetupSequence(x => x.NextId()).Returns(101).Returns(102);
        
        var mockIdProvider2 = new Mock<IProvideId>();
        mockIdProvider2.SetupSequence(x => x.NextId()).Returns(201).Returns(203);
        
        hom1.CreateDeepCopies(mockIdProvider1.Object);
        hom2.CreateDeepCopies(mockIdProvider2.Object);
        
        // Act - Get original IDs for activation
        var hom1OriginalIds = hom1.GetOriginalConstituentMutantIdsForActivation();
        var hom2OriginalIds = hom2.GetOriginalConstituentMutantIdsForActivation();
        
        // Assert - Both HOMs should reference the original FOM1 ID
        hom1OriginalIds.ShouldBe(new[] { 1, 2 }); // HOM1: Original FOM1 + FOM2
        hom2OriginalIds.ShouldBe(new[] { 1, 3 }); // HOM2: Original FOM1 + FOM3
        
        // Simulate mutant test mapping
        var mutantTestsMap = new Dictionary<int, ITestIdentifiers>();
        
        // Add HOMs for status tracking
        mutantTestsMap[100] = hom1.AssessingTests;
        mutantTestsMap[200] = hom2.AssessingTests;
        
        // Add original FOM IDs for activation (FOM1 will be added twice, but that's OK)
        foreach (var originalId in hom1OriginalIds)
        {
            mutantTestsMap[originalId] = hom1.AssessingTests;
        }
        foreach (var originalId in hom2OriginalIds)
        {
            // Merge tests if FOM is already in map (FOM1 case)
            if (mutantTestsMap.TryGetValue(originalId, out var existingTests))
            {
                mutantTestsMap[originalId] = existingTests.Merge(hom2.AssessingTests);
            }
            else
            {
                mutantTestsMap[originalId] = hom2.AssessingTests;
            }
        }
        
        // Verify shared FOM1 can be activated for both HOMs
        mutantTestsMap.ShouldContainKey(1, "Shared FOM1 should be activatable");
        mutantTestsMap.ShouldContainKey(2, "FOM2 should be activatable for HOM1");
        mutantTestsMap.ShouldContainKey(3, "FOM3 should be activatable for HOM2");
        mutantTestsMap.ShouldContainKey(100, "HOM1 should be trackable");
        mutantTestsMap.ShouldContainKey(200, "HOM2 should be trackable");
        
        // Composite IDs should not be in the activation map
        mutantTestsMap.ShouldNotContainKey(101);
        mutantTestsMap.ShouldNotContainKey(102);
        mutantTestsMap.ShouldNotContainKey(201);
        mutantTestsMap.ShouldNotContainKey(203);
    }
}
