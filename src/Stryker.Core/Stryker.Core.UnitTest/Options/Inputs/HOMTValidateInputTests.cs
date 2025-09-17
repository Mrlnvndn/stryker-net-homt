using Shouldly;
using Stryker.Abstractions.Options.Inputs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stryker.Abstractions.Options;

namespace Stryker.Core.UnitTest.Options.Inputs;

[TestClass]
public class HOMTValidateInputTests : TestBase
{
    [TestMethod]
    public void ShouldHaveHelpText()
    {
        var target = new HOMTValidateInput();
        target.HelpText.ShouldBe(@"Test higher-order mutants alongside all first-order mutants for SSHOM validation and analysis | default: 'False'");
    }

    [TestMethod]
    [DataRow(false, OptimizationModes.None)]
    [DataRow(true, OptimizationModes.HOMTValidate)]
    [DataRow(null, OptimizationModes.None)]
    public void ShouldValidate(bool? input, OptimizationModes expected)
    {
        var target = new HOMTValidateInput { SuppliedInput = input };

        var result = target.Validate();

        result.ShouldBe(expected);
    }
}
