using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using Stryker.Abstractions.Exceptions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Options.Inputs;

namespace Stryker.Core.UnitTest.Options.Inputs;

[TestClass]
public class HOMTHeuristicsInputTests : TestBase
{
    [TestMethod]
    public void ShouldHaveHelpText()
    {
        var target = new HOMTHeuristicsInput();
        target.HelpText.ShouldContain("Select HOMT heuristics to use");
        target.HelpText.ShouldContain("all");
        target.HelpText.ShouldContain("CodeLocation");
        target.HelpText.ShouldContain("EmptyAssessingTests");
        target.HelpText.ShouldContain("MutatorType");
        target.HelpText.ShouldContain("MaxSizeLimit");
        target.HelpText.ShouldContain("OverlappingTests");
        target.HelpText.ShouldContain("SyntaxNodeConflict");
    }

    [TestMethod]
    public void ShouldReturnAllHeuristicsWhenSpecifyingAll()
    {
        var target = new HOMTHeuristicsInput { SuppliedInput = new[] { "all" } };

        var result = target.Validate();

        result.Count().ShouldBe(6);
        result.ShouldContain(HOMTHeuristicKind.CodeLocation);
        result.ShouldContain(HOMTHeuristicKind.EmptyAssessingTests);
        result.ShouldContain(HOMTHeuristicKind.MutatorType);
        result.ShouldContain(HOMTHeuristicKind.MaxSizeLimit);
        result.ShouldContain(HOMTHeuristicKind.OverlappingTests);
        result.ShouldContain(HOMTHeuristicKind.SyntaxNodeConflict);
    }

    [TestMethod]
    public void ShouldReturnSpecificHeuristics()
    {
        var target = new HOMTHeuristicsInput { SuppliedInput = new[] { "CodeLocation", "MutatorType" } };

        var result = target.Validate();

        result.Count().ShouldBe(2);
        result.ShouldContain(HOMTHeuristicKind.CodeLocation);
        result.ShouldContain(HOMTHeuristicKind.MutatorType);
    }

    [TestMethod]
    public void ShouldHandleCaseInsensitiveInput()
    {
        var target = new HOMTHeuristicsInput { SuppliedInput = new[] { "codelocation", "MUTATORTYPE", "OverlappingTests" } };

        var result = target.Validate();

        result.Count().ShouldBe(3);
        result.ShouldContain(HOMTHeuristicKind.CodeLocation);
        result.ShouldContain(HOMTHeuristicKind.MutatorType);
        result.ShouldContain(HOMTHeuristicKind.OverlappingTests);
    }

    [TestMethod]
    public void ShouldRemoveDuplicates()
    {
        var target = new HOMTHeuristicsInput { SuppliedInput = new[] { "CodeLocation", "CodeLocation", "MutatorType" } };

        var result = target.Validate();

        result.Count().ShouldBe(2);
        result.ShouldContain(HOMTHeuristicKind.CodeLocation);
        result.ShouldContain(HOMTHeuristicKind.MutatorType);
    }

    [TestMethod]
    public void ShouldThrowOnInvalidHeuristic()
    {
        var target = new HOMTHeuristicsInput { SuppliedInput = new[] { "InvalidHeuristic" } };

        var ex = Should.Throw<InputException>(() => target.Validate());
        ex.Message.ShouldContain("Unknown HOMT heuristic 'InvalidHeuristic'");
        ex.Message.ShouldContain("Available:");
    }

    [TestMethod]
    public void ShouldUseDefaultWhenNull()
    {
        var target = new HOMTHeuristicsInput { SuppliedInput = null };

        var result = target.Validate();

        result.Count().ShouldBe(6);
        result.ShouldContain(HOMTHeuristicKind.CodeLocation);
        result.ShouldContain(HOMTHeuristicKind.EmptyAssessingTests);
        result.ShouldContain(HOMTHeuristicKind.MutatorType);
        result.ShouldContain(HOMTHeuristicKind.MaxSizeLimit);
        result.ShouldContain(HOMTHeuristicKind.OverlappingTests);
        result.ShouldContain(HOMTHeuristicKind.SyntaxNodeConflict);
    }

    [TestMethod]
    public void ShouldReturnAllWhenAllIsSpecifiedWithOtherHeuristics()
    {
        var target = new HOMTHeuristicsInput { SuppliedInput = new[] { "all", "CodeLocation" } };

        var result = target.Validate();

        // When "all" is specified, it should return all heuristics regardless of other specific ones
        result.Count().ShouldBe(6);
    }
}
