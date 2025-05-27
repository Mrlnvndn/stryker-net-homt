using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stryker.Abstractions;

namespace Stryker.Core.MutationTest;
public class HigherOrderMutation
{
    public IEnumerable<List<IMutant>> CreateHigherOrderMutants()
    {
        yield return new List<IMutant>();
    }
}
