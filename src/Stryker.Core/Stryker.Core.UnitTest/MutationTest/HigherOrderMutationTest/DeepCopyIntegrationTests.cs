using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Algorithms;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest;

[TestClass]
public class DeepCopyIntegrationTests : TestBase
{
    [TestMethod]
    public void HigherOrderMutation_CreateCandidateHOMs_ShouldCreateDeepCopiesWithCompositeIds()
    {
        // Arrange
        var mockOptions = new Mock<IStrykerOptions>();
        var mockIdProvider = new Mock<IProvideId>();
        
        // Setup ID provider to return predictable values
        var idSequence = new Queue<int>(new[] { 1000, 2001, 2002, 3001, 3002, 3003 }); // HOM IDs and constituent IDs
        mockIdProvider.Setup(x => x.NextId()).Returns(() => idSequence.Dequeue());
        mockOptions.SetupGet(x => x.MutantIdProvider).Returns(mockIdProvider.Object);
        mockOptions.SetupGet(x => x.OptimizationMode).Returns(OptimizationModes.EnableHigherOrderMutants);

        var mockInput = new Mock<MutationTestInput>();
        
        // Create original FOMs
        var originalFOM1 = new Mutant 
        {
            Id = 1,
            ResultStatus = MutantStatus.Killed, // Has some previous results
            KillingTests = new TestIdentifierList(new[] { "OldKillingTest" }),
            CoveringTests = new TestIdentifierList(new[] { "CoverTest1" }),
            AssessingTests = new TestIdentifierList(new[] { "AssessTest1", "AssessTest2" }),
            Mutation = new Mutation { DisplayName = "FOM1 Mutation", Type = Mutator.Arithmetic }
        };
        
        var originalFOM2 = new Mutant 
        {
            Id = 2,
            ResultStatus = MutantStatus.Survived,
            CoveringTests = new TestIdentifierList(new[] { "CoverTest2" }),
            AssessingTests = new TestIdentifierList(new[] { "AssessTest1", "AssessTest3" }),
            Mutation = new Mutation { DisplayName = "FOM2 Mutation", Type = Mutator.Boolean }
        };

        var originalFOMs = new List<IMutant> { originalFOM1, originalFOM2 };

        // Create a mock algorithm that will generate HOMs
        var mockAlgorithm = new Mock<IHOMSearchAlgorithm>();
        mockAlgorithm.SetupGet(x => x.Name).Returns("TestAlgorithm");
        
        // Setup algorithm to return a HOM with both FOMs
        mockAlgorithm.Setup(x => x.GenerateCandidates(
            It.IsAny<IReadOnlyCollection<IMutant>>(),
            It.IsAny<IReadOnlyList<IHOMHeuristic>>(),
            It.IsAny<IStrykerOptions>(),
            It.IsAny<MutationTestInput>()))
            .Returns(new List<HigherOrderMutant> 
            {
                new HigherOrderMutant(new List<IMutant> { originalFOM1, originalFOM2 }, "TestAlgorithm")
            });

        var homt = new HigherOrderMutation(mockOptions.Object, mockInput.Object, originalFOMs);
        homt.AddSearchAlgorithm(mockAlgorithm.Object);

        // Act
        var candidateHOMs = homt.CreateCandidateHOMs().ToList();

        // Assert
        candidateHOMs.Count.ShouldBe(1);
        var hom = candidateHOMs[0];
        
        // Verify HOM got proper ID
        hom.Id.ShouldBe(1000);
        
        // Verify deep copies were created
        hom.ConstituentMutants.ShouldNotBeNull();
        hom.ConstituentMutants.Count.ShouldBe(2);
        
        var constituent1 = hom.ConstituentMutants[0] as Mutant;
        var constituent2 = hom.ConstituentMutants[1] as Mutant;
        
        // Verify first constituent is a deep copy with composite ID
        constituent1.ShouldNotBeNull();
        constituent1.Id.ShouldBe(2001); // New composite ID
        constituent1.OriginalFomId.ShouldBe(1); // Original FOM ID preserved
        constituent1.ResultStatus.ShouldBe(MutantStatus.Pending); // Reset for HOM testing
        constituent1.KillingTests.IsEmpty.ShouldBeTrue(); // Reset for HOM testing
        constituent1.CoveringTests.GetIdentifiers().ShouldBe(new[] { "CoverTest1" }); // Preserved
        constituent1.AssessingTests.GetIdentifiers().ShouldBe(new[] { "AssessTest1", "AssessTest2" }); // Preserved
        constituent1.Mutation.ShouldBe(originalFOM1.Mutation); // Should be same reference
        
        // Verify second constituent is a deep copy with composite ID  
        constituent2.ShouldNotBeNull();
        constituent2.Id.ShouldBe(2002); // New composite ID
        constituent2.OriginalFomId.ShouldBe(2); // Original FOM ID preserved
        constituent2.ResultStatus.ShouldBe(MutantStatus.Pending); // Reset for HOM testing
        constituent2.KillingTests.IsEmpty.ShouldBeTrue(); // Reset for HOM testing
        constituent2.CoveringTests.GetIdentifiers().ShouldBe(new[] { "CoverTest2" }); // Preserved
        constituent2.AssessingTests.GetIdentifiers().ShouldBe(new[] { "AssessTest1", "AssessTest3" }); // Preserved
        
        // Verify they are NOT the same objects as originals
        ReferenceEquals(constituent1, originalFOM1).ShouldBeFalse();
        ReferenceEquals(constituent2, originalFOM2).ShouldBeFalse();
        
        // Verify HOM properties
        hom.Order.ShouldBe(2);
        hom.ConstituentMutantIdsString.ShouldBe("2001,2002"); // Should show constituent IDs
        hom.OriginalConstituentMutantIdsString.ShouldBe("1,2"); // Should show original FOM IDs
        hom.DisplayName.ShouldBe("HOM-1000: 1,2"); // Should use original IDs in display
        
        // Verify HOM's assessing tests are intersection of constituents' assessing tests
        hom.AssessingTests.GetIdentifiers().ShouldBe(new[] { "AssessTest1" }); // Intersection of {AssessTest1, AssessTest2} and {AssessTest1, AssessTest3}
        
        // Verify all IDs were generated as expected
        mockIdProvider.Verify(x => x.NextId(), Times.Exactly(3)); // 1 for HOM ID + 2 for constituent IDs
    }

    [TestMethod]
    public void HigherOrderMutation_MultipleHOMs_ShouldEachHaveUniqueConstituentCopies()
    {
        // Arrange - Test that when a FOM is part of multiple HOMs, each gets its own deep copy
        var mockOptions = new Mock<IStrykerOptions>();
        var mockIdProvider = new Mock<IProvideId>();
        
        // Setup ID provider - 2 HOMs each with 2 constituents = 6 IDs total + 2 HOM IDs = 8 IDs
        var idSequence = new Queue<int>(new[] { 1000, 1001, 1002, 2000, 2001, 2002 });
        mockIdProvider.Setup(x => x.NextId()).Returns(() => idSequence.Dequeue());
        mockOptions.SetupGet(x => x.MutantIdProvider).Returns(mockIdProvider.Object);
        mockOptions.SetupGet(x => x.OptimizationMode).Returns(OptimizationModes.EnableHigherOrderMutants);

        var mockInput = new Mock<MutationTestInput>();
        
        var originalFOM1 = new Mutant { Id = 1, CoveringTests = new TestIdentifierList(new[] { "Test1" }), AssessingTests = new TestIdentifierList(new[] { "Assess1" }) };
        var originalFOM2 = new Mutant { Id = 2, CoveringTests = new TestIdentifierList(new[] { "Test2" }), AssessingTests = new TestIdentifierList(new[] { "Assess2" }) };
        var originalFOM3 = new Mutant { Id = 3, CoveringTests = new TestIdentifierList(new[] { "Test3" }), AssessingTests = new TestIdentifierList(new[] { "Assess3" }) };

        var originalFOMs = new List<IMutant> { originalFOM1, originalFOM2, originalFOM3 };

        var mockAlgorithm = new Mock<IHOMSearchAlgorithm>();
        mockAlgorithm.SetupGet(x => x.Name).Returns("TestAlgorithm");
        
        // Setup algorithm to return HOMs where FOM1 is in both HOMs
        mockAlgorithm.Setup(x => x.GenerateCandidates(It.IsAny<IReadOnlyCollection<IMutant>>(), It.IsAny<IReadOnlyList<IHOMHeuristic>>(), It.IsAny<IStrykerOptions>(), It.IsAny<MutationTestInput>()))
            .Returns(new List<HigherOrderMutant> 
            {
                new HigherOrderMutant(new List<IMutant> { originalFOM1, originalFOM2 }, "TestAlgorithm"), // HOM1: FOM1 + FOM2
                new HigherOrderMutant(new List<IMutant> { originalFOM1, originalFOM3 }, "TestAlgorithm")  // HOM2: FOM1 + FOM3
            });

        var homt = new HigherOrderMutation(mockOptions.Object, mockInput.Object, originalFOMs);
        homt.AddSearchAlgorithm(mockAlgorithm.Object);

        // Act
        var candidateHOMs = homt.CreateCandidateHOMs().ToList();

        // Assert
        candidateHOMs.Count.ShouldBe(2);
        
        var hom1 = candidateHOMs[0];
        var hom2 = candidateHOMs[1];
        
        // Verify each HOM has its own copies of FOM1
        var hom1_constituent1 = hom1.ConstituentMutants[0] as Mutant; // FOM1 copy in HOM1
        var hom2_constituent1 = hom2.ConstituentMutants[0] as Mutant; // FOM1 copy in HOM2
        
        // Both should be copies of original FOM1, but with different IDs
        hom1_constituent1.OriginalFomId.ShouldBe(1); // Both trace back to original FOM1
        hom2_constituent1.OriginalFomId.ShouldBe(1); // Both trace back to original FOM1
        
        hom1_constituent1.Id.ShouldBe(1001); // But have different composite IDs
        hom2_constituent1.Id.ShouldBe(2001); // But have different composite IDs
        
        // They should not be the same object
        ReferenceEquals(hom1_constituent1, hom2_constituent1).ShouldBeFalse("Each HOM should have its own copy of shared FOM");
        ReferenceEquals(hom1_constituent1, originalFOM1).ShouldBeFalse("Should not reference original");
        ReferenceEquals(hom2_constituent1, originalFOM1).ShouldBeFalse("Should not reference original");
        
        // Verify all IDs were used
        mockIdProvider.Verify(x => x.NextId(), Times.Exactly(6)); // 2 HOM IDs + 4 constituent IDs
    }
}
