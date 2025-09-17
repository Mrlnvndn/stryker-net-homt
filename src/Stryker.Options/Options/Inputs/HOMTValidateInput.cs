using Stryker.Abstractions.Options;

namespace Stryker.Abstractions.Options.Inputs;

public class HOMTValidateInput : Input<bool?>
{
    public override bool? Default => false;

    protected override string Description => "Test higher-order mutants alongside all first-order mutants for SSHOM validation and analysis";

    public OptimizationModes Validate()
    {
        if (SuppliedInput is { })
        {
            return SuppliedInput.Value ? OptimizationModes.HOMTValidate : OptimizationModes.None;
        }
        return OptimizationModes.None;
    }
}
