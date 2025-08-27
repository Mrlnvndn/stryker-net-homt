using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;
using Stryker.Configuration;
using Moq;

// Demonstration script to show the deep copy functionality
namespace Stryker.Core.DeepCopyDemo
{
    public class DeepCopyDemonstration
    {
        public static void DemonstrateDeepCopyFunctionality()
        {
            Console.WriteLine("=== Higher-Order Mutant Deep Copy Demonstration ===");
            
            // Create original First-Order Mutants (FOMs)
            var originalFOM1 = new Mutant
            {
                Id = 10,
                ResultStatus = MutantStatus.Killed,
                KillingTests = new TestIdentifierList(new[] { "TestA", "TestB" }),
                CoveringTests = new TestIdentifierList(new[] { "TestA", "TestB", "TestC" }),
                AssessingTests = new TestIdentifierList(new[] { "TestA", "TestB" }),
                Mutation = new Mutation { DisplayName = "x + y -> x - y", Type = Mutator.Arithmetic }
            };
            
            var originalFOM2 = new Mutant
            {
                Id = 20,
                ResultStatus = MutantStatus.Survived,
                CoveringTests = new TestIdentifierList(new[] { "TestA", "TestD" }),
                AssessingTests = new TestIdentifierList(new[] { "TestA", "TestE" }),
                Mutation = new Mutation { DisplayName = "true -> false", Type = Mutator.Boolean }
            };

            Console.WriteLine("\n--- Original FOMs ---");
            Console.WriteLine($"FOM1: ID={originalFOM1.Id}, Status={originalFOM1.ResultStatus}, KillingTests=[{string.Join(", ", originalFOM1.KillingTests.GetIdentifiers())}]");
            Console.WriteLine($"FOM2: ID={originalFOM2.Id}, Status={originalFOM2.ResultStatus}, KillingTests=[{string.Join(", ", originalFOM2.KillingTests.GetIdentifiers())}]");
            
            // Create a Higher-Order Mutant
            var hom = new HigherOrderMutant(new List<IMutant> { originalFOM1, originalFOM2 }, "LocalSearch");
            
            // Assign HOM ID (normally done by the mutation test process)
            hom.Id = 1000;
            
            // Setup mock ID provider for generating composite IDs
            var mockIdProvider = new Mock<IProvideId>();
            mockIdProvider.SetupSequence(x => x.NextId())
                .Returns(1010) // Composite ID for FOM1 copy 
                .Returns(1020); // Composite ID for FOM2 copy
            
            Console.WriteLine($"\n--- Before Deep Copy ---");
            Console.WriteLine($"HOM ID: {hom.Id}");
            Console.WriteLine($"HOM Order: {hom.Order}");
            Console.WriteLine($"Original FOM IDs: {hom.OriginalConstituentMutantIdsString}");
            Console.WriteLine($"Mutant IDs: {hom.ConstituentMutantIdsString}");
            
            // Create deep copies with composite IDs
            Console.WriteLine($"\n--- Creating Deep Copies ---");
            hom.CreateDeepCopies(mockIdProvider.Object);
            
            Console.WriteLine($"\n--- After Deep Copy ---");
            Console.WriteLine($"HOM ID: {hom.Id}");
            Console.WriteLine($"HOM Order: {hom.Order}");
            Console.WriteLine($"Original FOM IDs: {hom.OriginalConstituentMutantIdsString}");
            Console.WriteLine($"Constituent IDs: {hom.ConstituentMutantIdsString}");
            Console.WriteLine($"Display Name: {hom.DisplayName}");
            
            // Show constituent details
            Console.WriteLine($"\n--- Constituent Mutant Details ---");
            for (int i = 0; i < hom.ConstituentMutants.Count; i++)
            {
                var constituent = hom.ConstituentMutants[i] as Mutant;
                var original = i == 0 ? originalFOM1 : originalFOM2;
                
                Console.WriteLine($"\nConstituent {i + 1}:");
                Console.WriteLine($"  New ID: {constituent.Id} (Original: {constituent.OriginalFomId})");
                Console.WriteLine($"  Status: {constituent.ResultStatus} (Original: {original.ResultStatus})");
                Console.WriteLine($"  KillingTests: [{string.Join(", ", constituent.KillingTests.GetIdentifiers())}] (Original: [{string.Join(", ", original.KillingTests.GetIdentifiers())}])");
                Console.WriteLine($"  CoveringTests: [{string.Join(", ", constituent.CoveringTests.GetIdentifiers())}] (Same as original: {constituent.CoveringTests.GetIdentifiers().SequenceEqual(original.CoveringTests.GetIdentifiers())})");
                Console.WriteLine($"  Is same object as original: {ReferenceEquals(constituent, original)}");
                Console.WriteLine($"  Mutation reference same: {ReferenceEquals(constituent.Mutation, original.Mutation)}");
            }
            
            // Show HOM-level test calculations
            Console.WriteLine($"\n--- HOM Test Calculations ---");
            Console.WriteLine($"HOM AssessingTests: [{string.Join(", ", hom.AssessingTests.GetIdentifiers())}] (intersection of constituent assessing tests)");
            Console.WriteLine($"HOM CoveringTests: [{string.Join(", ", hom.CoveringTests.GetIdentifiers())}] (union of constituent covering tests)");
            
            // Simulate mutation testing on the HOM
            Console.WriteLine($"\n--- Simulating HOM Testing ---");
            Console.WriteLine("Simulating test run where 'TestA' fails...");
            
            var failedTests = new TestIdentifierList(new[] { "TestA" });
            var ranTests = new TestIdentifierList(new[] { "TestA", "TestB", "TestC", "TestD", "TestE" });
            var timedOutTests = TestIdentifierList.NoTest();
            
            hom.AnalyzeTestRun(failedTests, ranTests, timedOutTests, false);
            
            Console.WriteLine($"HOM Result: {hom.ResultStatus}");
            Console.WriteLine($"HOM KillingTests: [{string.Join(", ", hom.KillingTests.GetIdentifiers())}]");
            
            // Show that constituent results are independent
            Console.WriteLine($"\n--- Verifying Independence ---");
            Console.WriteLine("Original FOMs remain unchanged:");
            Console.WriteLine($"  Original FOM1 Status: {originalFOM1.ResultStatus}");
            Console.WriteLine($"  Original FOM2 Status: {originalFOM2.ResultStatus}");
            Console.WriteLine("Constituent copies have fresh results:");
            foreach (var constituent in hom.ConstituentMutants.OfType<Mutant>())
            {
                Console.WriteLine($"  Constituent {constituent.OriginalFomId} Status: {constituent.ResultStatus}");
            }
            
            Console.WriteLine($"\n=== Deep Copy Demonstration Complete ===");
        }
    }
}
