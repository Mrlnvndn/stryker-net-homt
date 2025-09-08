using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;
public class LocationInfo
{
    public string Namespace { get; set; }
    public string ClassName { get; set; }
    public string MethodName { get; set; }
    public string PropertyName { get; set; }
    public int LineNumber { get; set; }
    public int CharacterPosition { get; set; }
    public bool IsInStaticContext { get; set; }
    public bool IsInNestedClass { get; set; }
    public string ContainingMemberType { get; set; } // "Method", "Property", "Constructor", etc.
}
