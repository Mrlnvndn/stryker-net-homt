using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stryker.Abstractions.Options.Inputs;
public class EnableHigherOrderMutantsInput : Input<bool?>
{
    public override bool? Default => false;

    protected override string Description => "Test mutation operators together by forming higher order mutants";

    public OptimizationModes Validate()
    {
        if (SuppliedInput is { })
        {
            return SuppliedInput.Value ? OptimizationModes.EnableHigherOrderMutants : OptimizationModes.None;
        }
        return OptimizationModes.None;
    }
}

