using Stryker.Abstractions.Options;
using Stryker.Abstractions.Exceptions;

namespace Stryker.Abstractions.Options.Inputs;

public class HOMTAlgorithmInput : Input<string>
{
    public override string Default => "genetic";
    protected override string Description => "Select HOMT search algorithm: genetic | localv2 | both.";

    public HOMTAlgorithmKind Validate()
    {
        var value = (SuppliedInput ?? Default).Trim().ToLowerInvariant();
        return value switch
        {
            "genetic" or "geneticsearch" => HOMTAlgorithmKind.Genetic,
            "localv2" or "local" or "localsearchv2" => HOMTAlgorithmKind.Local,
            "both" => HOMTAlgorithmKind.Both,
            _ => throw new InputException($"Unknown homt-algorithm '{SuppliedInput}'. Allowed: genetic, localv2, both.")
        };
    }
}
