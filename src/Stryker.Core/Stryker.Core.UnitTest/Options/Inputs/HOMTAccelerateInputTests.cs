using Shouldly;
using Stryker.Abstractions.Options.Inputs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stryker.Abstractions.Options;

namespace Stryker.Core.UnitTest.Options.Inputs;

[TestClass]
public class HOMTAccelerateInputTests : TestBase
{
    [TestMethod]
    public void ShouldHaveHelpText()
    {
        var target = new HOMTAccelerateInput();
        target.HelpText.ShouldBe(@"Test higher-order mutants alongside only non-constituent first-order mutants for accelerated mutation testing | default: 'False'");
    }

    [TestMethod]
    [DataRow(false, OptimizationModes.None)]
    [DataRow(true, OptimizationModes.HOMTAccelerate)]
    [DataRow(null, OptimizationModes.None)]
    public void ShouldValidate(bool? input, OptimizationModes expected)
    {
        var target = new HOMTAccelerateInput { SuppliedInput = input };

        var result = target.Validate();

        result.ShouldBe(expected);
    }
}
