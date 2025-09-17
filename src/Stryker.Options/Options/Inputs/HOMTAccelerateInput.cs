using Stryker.Abstractions.Options;

namespace Stryker.Abstractions.Options.Inputs;

public class HOMTAccelerateInput : Input<bool?>
{
    public override bool? Default => false;

    protected override string Description => "Test higher-order mutants alongside only non-constituent first-order mutants for accelerated mutation testing";

    public OptimizationModes Validate()
    {
        if (SuppliedInput is { })
        {
            return SuppliedInput.Value ? OptimizationModes.HOMTAccelerate : OptimizationModes.None;
        }
        return OptimizationModes.None;
    }
}
