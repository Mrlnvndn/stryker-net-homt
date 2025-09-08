using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics;

namespace Stryker.Core.UnitTest.MutationTest.HigherOrderMutationTest.Heuristics;

[TestClass]
public class CodeLocationHeuristicTests : TestBase
{
    private CodeLocationHeuristic _target;
    private Mock<IStrykerOptions> _optionsMock;
    private Mock<MutationTestInput> _inputMock;

    [TestInitialize]
    public void TestInitialize()
    {
        _target = new CodeLocationHeuristic();
        _optionsMock = new Mock<IStrykerOptions>();
        _inputMock = new Mock<MutationTestInput>();
    }

    #region ExtractLocationInfo Tests

    [TestMethod]
    public void ExtractLocationInfo_WithMethodDeclaration_ShouldExtractMethodInfo()
    {
        // Arrange
        var source = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            var x = 1 + 2; // This is our target node
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.Namespace.ShouldBe("TestNamespace");
        locationInfo.ClassName.ShouldBe("TestClass");
        locationInfo.MethodName.ShouldBe("TestMethod");
        locationInfo.ContainingMemberType.ShouldBe("Method");
        locationInfo.IsInStaticContext.ShouldBeFalse();
        locationInfo.IsInNestedClass.ShouldBeFalse();
        locationInfo.LineNumber.ShouldBe(7); // 1-based line number
    }

    [TestMethod]
    public void ExtractLocationInfo_WithStaticMethod_ShouldDetectStaticContext()
    {
        // Arrange
        var source = @"
namespace TestNamespace
{
    public class TestClass
    {
        public static void StaticMethod()
        {
            var x = 1 + 2; // This is our target node
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.MethodName.ShouldBe("StaticMethod");
        locationInfo.IsInStaticContext.ShouldBeTrue();
        locationInfo.ContainingMemberType.ShouldBe("Method");
    }

    [TestMethod]
    public void ExtractLocationInfo_WithConstructor_ShouldExtractConstructorInfo()
    {
        // Arrange
        var source = @"
namespace TestNamespace
{
    public class TestClass
    {
        public TestClass()
        {
            var x = 1 + 2; // This is our target node
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.MethodName.ShouldBe(".ctor");
        locationInfo.ContainingMemberType.ShouldBe("Constructor");
        locationInfo.IsInStaticContext.ShouldBeFalse();
    }

    [TestMethod]
    public void ExtractLocationInfo_WithStaticConstructor_ShouldDetectStaticConstructor()
    {
        // Arrange
        var source = @"
namespace TestNamespace
{
    public class TestClass
    {
        static TestClass()
        {
            var x = 1 + 2; // This is our target node
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.MethodName.ShouldBe(".ctor");
        locationInfo.ContainingMemberType.ShouldBe("Constructor");
        locationInfo.IsInStaticContext.ShouldBeTrue();
    }

    [TestMethod]
    public void ExtractLocationInfo_WithProperty_ShouldExtractPropertyInfo()
    {
        // Arrange
        var source = @"
namespace TestNamespace
{
    public class TestClass
    {
        public int TestProperty
        {
            get { return 1 + 2; } // This is our target node
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.PropertyName.ShouldBe("TestProperty");
        locationInfo.ContainingMemberType.ShouldBe("Property");
        locationInfo.IsInStaticContext.ShouldBeFalse();
    }

    [TestMethod]
    public void ExtractLocationInfo_WithStaticProperty_ShouldDetectStaticProperty()
    {
        // Arrange
        var source = @"
namespace TestNamespace
{
    public class TestClass
    {
        public static int StaticProperty
        {
            get { return 1 + 2; } // This is our target node
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.PropertyName.ShouldBe("StaticProperty");
        locationInfo.IsInStaticContext.ShouldBeTrue();
    }

    [TestMethod]
    public void ExtractLocationInfo_WithNestedClass_ShouldDetectNestedClass()
    {
        // Arrange
        var source = @"
namespace TestNamespace
{
    public class OuterClass
    {
        public class NestedClass
        {
            public void TestMethod()
            {
                var x = 1 + 2; // This is our target node
            }
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.ClassName.ShouldBe("NestedClass");
        locationInfo.IsInNestedClass.ShouldBeTrue();
    }

    [TestMethod]
    public void ExtractLocationInfo_WithFileScopedNamespace_ShouldExtractNamespace()
    {
        // Arrange
        var source = @"
namespace TestNamespace;

public class TestClass
{
    public void TestMethod()
    {
        var x = 1 + 2; // This is our target node
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.Namespace.ShouldBe("TestNamespace");
        locationInfo.ClassName.ShouldBe("TestClass");
    }

    [TestMethod]
    public void ExtractLocationInfo_WithStruct_ShouldExtractStructInfo()
    {
        // Arrange
        var source = @"
namespace TestNamespace
{
    public struct TestStruct
    {
        public void TestMethod()
        {
            var x = 1 + 2; // This is our target node
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.ClassName.ShouldBe("TestStruct");
    }

    [TestMethod]
    public void ExtractLocationInfo_WithNoNamespace_ShouldUseGlobal()
    {
        // Arrange
        var source = @"
public class TestClass
{
    public void TestMethod()
    {
        var x = 1 + 2; // This is our target node
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        // Act
        LocationInfo locationInfo = InvokeExtractLocationInfo(targetNode);

        // Assert
        locationInfo.Namespace.ShouldBe("Global");
    }

    #endregion

    #region Scoring Tier Tests

    [TestMethod]
    public void ScoreCandidate_WithSingleMutant_ShouldReturnPerfectScore()
    {
        // Arrange
        var mutant = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var candidate = new List<IMutant> { mutant };
        InitializeHeuristic(new List<IMutant> { mutant });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(1.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithSameMemberMutants_ShouldReturnTier1Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var candidate = new List<IMutant> { mutant1, mutant2 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(1.0); // Tier 1: Same method
    }

    [TestMethod]
    public void ScoreCandidate_WithSameClassDifferentMethods_ShouldReturnTier2Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method1");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method2");
        var candidate = new List<IMutant> { mutant1, mutant2 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeGreaterThanOrEqualTo(0.85);
        score.ShouldBeLessThan(1.0); // Tier 2: Same class
    }

    [TestMethod]
    public void ScoreCandidate_WithSameNamespaceDifferentClasses_ShouldReturnTier3Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "Class1", "TestMethod");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "Class2", "TestMethod");
        var candidate = new List<IMutant> { mutant1, mutant2 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.6); // Tier 3: Same namespace
    }

    [TestMethod]
    public void ScoreCandidate_WithSameStaticContext_ShouldReturnTier4Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("Namespace1", "Class1", "Method1", isStatic: true);
        var mutant2 = CreateMutantWithLocation("Namespace2", "Class2", "Method2", isStatic: true);
        var candidate = new List<IMutant> { mutant1, mutant2 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.4); // Tier 4: Same static context
    }

    [TestMethod]
    public void ScoreCandidate_WithScatteredMutants_ShouldReturnTier5Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("Namespace1", "Class1", "Method1", isStatic: false);
        var mutant2 = CreateMutantWithLocation("Namespace2", "Class2", "Method2", isStatic: true);
        var candidate = new List<IMutant> { mutant1, mutant2 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeLessThan(0.4); // Tier 5: Scattered
        score.ShouldBeGreaterThan(0.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithCloseLineNumbers_ShouldGetProximityBonus()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method1");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method2", lineOffset: 5);
        var candidate = new List<IMutant> { mutant1, mutant2 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.95);
    }

    [TestMethod]
    public void ScoreCandidate_WithDistantLineNumbers_ShouldGetLowerProximityBonus()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method1");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method2", lineOffset: 40);
        var candidate = new List<IMutant> { mutant1, mutant2 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeGreaterThan(0.85); 
        score.ShouldBeLessThan(0.95); 
    }

    [TestMethod]
    public void ScoreCandidate_WithThreeSameMemberMutants_ShouldReturnTier1Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var mutant3 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2, mutant3 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(1.0); // Tier 1: Same method
    }

    [TestMethod]
    public void ScoreCandidate_WithThreeSameClassDifferentMethods_ShouldReturnTier2Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method1");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method2");
        var mutant3 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method3");
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2, mutant3 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeGreaterThanOrEqualTo(0.85);
        score.ShouldBeLessThan(1.0); // Tier 2: Same class
    }

    [TestMethod]
    public void ScoreCandidate_WithThreeMixedNamespaceClasses_ShouldReturnMixedScore()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "Class1", "TestMethod");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "Class2", "TestMethod");
        var mutant3 = CreateMutantWithLocation("DifferentNamespace", "Class3", "TestMethod");
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2, mutant3 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeGreaterThan(0.3); // Better than scattered
        score.ShouldBeLessThan(0.6); // Worse than same namespace
    }

    [TestMethod]
    public void ScoreCandidate_WithFourSameMemberMutants_ShouldReturnTier1Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var mutant3 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var mutant4 = CreateMutantWithLocation("TestNamespace", "TestClass", "TestMethod");
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3, mutant4 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2, mutant3, mutant4 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(1.0); // Tier 1: Same method
    }

    [TestMethod]
    public void ScoreCandidate_WithFourSameClassDifferentMethods_ShouldReturnTier2Score()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method1");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method2");
        var mutant3 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method3");
        var mutant4 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method4");
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3, mutant4 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2, mutant3, mutant4 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeGreaterThanOrEqualTo(0.85);
        score.ShouldBeLessThan(1.0); // Tier 2: Same class
    }

    [TestMethod]
    public void ScoreCandidate_WithFourCompletelyScatteredMutants_ShouldReturnLowScore()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("Namespace1", "Class1", "Method1", isStatic: false);
        var mutant2 = CreateMutantWithLocation("Namespace2", "Class2", "Method2", isStatic: true);
        var mutant3 = CreateMutantWithLocation("Namespace3", "Class3", "Method3", isStatic: false);
        var mutant4 = CreateMutantWithLocation("Namespace4", "Class4", "Method4", isStatic: true);
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3, mutant4 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2, mutant3, mutant4 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeLessThan(0.4); // Tier 5: Scattered
        score.ShouldBeGreaterThan(0.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithThreeCloseLineNumbers_ShouldGetProximityBonus()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method1");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method2", lineOffset: 3);
        var mutant3 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method3", lineOffset: 6);
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3 };
        InitializeHeuristic([mutant1, mutant2, mutant3]);

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeGreaterThan(0.85); // Should get proximity bonus
        score.ShouldBeLessThan(1.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithFourVaryingLineNumbers_ShouldGetModeratProximityBonus()
    {
        // Arrange
        var mutant1 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method1");
        var mutant2 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method2", lineOffset: 5);
        var mutant3 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method3", lineOffset: 15);
        var mutant4 = CreateMutantWithLocation("TestNamespace", "TestClass", "Method4", lineOffset: 25);
        var candidate = new List<IMutant> { mutant1, mutant2, mutant3, mutant4 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2, mutant3, mutant4 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBeGreaterThan(0.85); // Should get some proximity bonus
        score.ShouldBeLessThan(0.95); // But not maximum due to line spread
    }

    [TestMethod]
    public void ScoreCandidate_WithNullCandidate_ShouldReturnPerfectScore()
    {
        // Arrange
        InitializeHeuristic(new List<IMutant>());

        // Act
        var score = _target.ScoreCandidate(null);

        // Assert
        score.ShouldBe(1.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithEmptyCandidate_ShouldReturnPerfectScore()
    {
        // Arrange
        var candidate = new List<IMutant>();
        InitializeHeuristic(new List<IMutant>());

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(1.0);
    }

    [TestMethod]
    public void ScoreCandidate_WithMutantsWithoutLocationInfo_ShouldReturnLowScore()
    {
        // Arrange
        var mutant1 = CreateMutantWithoutLocation();
        var mutant2 = CreateMutantWithoutLocation();
        var candidate = new List<IMutant> { mutant1, mutant2 };
        InitializeHeuristic(new List<IMutant> { mutant1, mutant2 });

        // Act
        var score = _target.ScoreCandidate(candidate);

        // Assert
        score.ShouldBe(0.1); // No location info available
    }

    #endregion

    #region Heuristic Properties Tests

    [TestMethod]
    public void Name_ShouldReturnCodeLocation()
    {
        // Act & Assert
        _target.Name.ShouldBe("CodeLocation");
    }

    [TestMethod]
    public void Weight_ShouldReturnExpectedValue()
    {
        // Act & Assert
        _target.Weight.ShouldBe(2.0);
    }

    [TestMethod]
    public void IsSearchStrategyHeuristic_ShouldReturnFalse()
    {
        // Act & Assert
        _target.IsSearchStrategyHeuristic.ShouldBeFalse();
    }

    [TestMethod]
    public void IsFitnessScoringHeuristic_ShouldReturnTrue()
    {
        // Act & Assert
        _target.IsFitnessScoringHeuristic.ShouldBeTrue();
    }

    #endregion

    #region Helper Methods

    private void InitializeHeuristic(IReadOnlyCollection<IMutant> mutants)
    {
        _target.Initialize(mutants, _optionsMock.Object, _inputMock.Object);
    }

    private IMutant CreateMutantWithLocation(string namespaceName, string className, string methodName, 
        bool isStatic = false, int lineOffset = 0)
    {
        // Calculate whitespace offset - the binary expression is normally on line 7
        // so we add extra newlines to offset it to the desired line number
        var baseLineNumber = 7; // The line where "var x = 1 + 2;" normally appears
        var whitespaceOffset = lineOffset > 0 ? Math.Max(0, lineOffset - baseLineNumber) : 0;
        var extraWhitespace = string.Concat(Enumerable.Repeat("\n", whitespaceOffset));

        var source = $@"
namespace {namespaceName}
{{
    public class {className}
    {{
        public {(isStatic ? "static " : "")}void {methodName}()
        {{{extraWhitespace}
            var x = 1 + 2; // Line {lineOffset}
        }}
    }}
}}";

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetNode = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First();

        var mutation = new Mutation
        {
            OriginalNode = targetNode,
            ReplacementNode = targetNode, // For testing purposes, use same node
            DisplayName = "Test Mutation",
            Type = Mutator.Arithmetic,
            Description = "Test mutation for location heuristic"
        };

        var mutant = new Mutant
        {
            Id = GetNextMutantId(),
            Mutation = mutation
        };

        return mutant;
    }

    private IMutant CreateMutantWithoutLocation()
    {
        var mutation = new Mutation
        {
            OriginalNode = null,
            ReplacementNode = null,
            DisplayName = "Test Mutation Without Location",
            Type = Mutator.Arithmetic,
            Description = "Test mutation without location info"
        };

        var mutant = new Mutant
        {
            Id = GetNextMutantId(),
            Mutation = mutation
        };

        return mutant;
    }

    private static int _nextMutantId = 1;
    private static int GetNextMutantId() => _nextMutantId++;

    private dynamic InvokeExtractLocationInfo(SyntaxNode node)
    {
        var method = typeof(CodeLocationHeuristic)
            .GetMethod("ExtractLocationInfo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return method?.Invoke(null, new object[] { node });
    }

    #endregion
}
