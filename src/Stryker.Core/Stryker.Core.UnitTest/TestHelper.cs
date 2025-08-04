using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Buildalyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moq;
using Stryker.Abstractions;
using Stryker.Abstractions.Testing;
using Stryker.Core.Mutants;
using Stryker.TestRunner.Tests;

namespace Stryker.Core.UnitTest;

public static class TestHelper
{
    public static Mock<IAnalyzerResult> SetupProjectAnalyzerResult(Dictionary<string, string> properties = null,
        string projectFilePath = null,
        string[] sourceFiles = null,
        IEnumerable<string> projectReferences = null,
        string targetFramework = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> packageReferences = null,
        string[] references = null,
        string[] preprocessorSymbols = null,
        string[] analyzers = null,
        ImmutableDictionary<string,  ImmutableArray<string>> aliases = null
    )
    {
        var analyzerResultMock = new Mock<IAnalyzerResult>();

        if (properties != null)
        {
            analyzerResultMock.Setup(x => x.Properties).Returns(properties);
        }
        else
        {
            properties = new Dictionary<string, string>();
            analyzerResultMock.Setup(x => x.Properties).Returns(properties);
        }
        if (projectFilePath != null)
        {
            analyzerResultMock.Setup(x => x.ProjectFilePath).Returns(projectFilePath);
            if (!properties.ContainsKey("TargetDir"))
            {
                properties["TargetDir"] = Path.Combine(Path.GetFullPath(projectFilePath), "bin", "Debug", targetFramework ?? "net");
                properties["TargetFileName"] = Path.GetFileNameWithoutExtension(projectFilePath) + ".dll";
            }
        }
        if (sourceFiles != null)
        {
            analyzerResultMock.Setup(x => x.SourceFiles).Returns(sourceFiles);
        }
        if (projectReferences != null)
        {
            analyzerResultMock.Setup(x => x.ProjectReferences).Returns(projectReferences);
        }
        if (targetFramework != null)
        {
            analyzerResultMock.Setup(x => x.TargetFramework).Returns(targetFramework);
        }
        if (packageReferences != null)
        {
            analyzerResultMock.Setup(x => x.PackageReferences).Returns(packageReferences);
        }
        if (references != null)
        {
            analyzerResultMock.Setup(x => x.References).Returns(references);
        }
        if (preprocessorSymbols is not null)
        {
            analyzerResultMock.Setup(x => x.PreprocessorSymbols).Returns(preprocessorSymbols);
        }

        if (analyzers is not null)
        {
            analyzerResultMock.Setup(x => x.AnalyzerReferences).Returns(analyzers);
        }
        aliases ??= ImmutableDictionary<string, ImmutableArray<string>>.Empty;
        analyzerResultMock.Setup(x => x.Items).Returns(new Dictionary<string, IProjectItem[]>());
        analyzerResultMock.Setup(x => x.ReferenceAliases).Returns(aliases);

        return analyzerResultMock;
    }

    public static IMutant CreateMutant(string fileName, int id, MutantStatus status, string[] killingTests = null)
    {
        killingTests ??= new[] { "Test1" };
        
        var mutant = new Mutant
        {
            Id = id,
            ResultStatus = status,
            Mutation = new Mutation
            {
                DisplayName = $"Test mutation {id}",
                Type = Mutator.Arithmetic,
                Description = "Replace + with -",
                OriginalNode = SyntaxFactory.ParseSyntaxTree(
                    text: "public int Add(int a, int b) { return a + b; }",
                    path: fileName).GetRoot(),
                ReplacementNode = SyntaxFactory.ParseSyntaxTree(
                    text: "public int Add(int a, int b) { return a - b; }").GetRoot()
            },
            CoveringTests = new TestIdentifierList(killingTests),
            KillingTests = new TestIdentifierList(killingTests),
            AssessingTests = new TestIdentifierList(killingTests)
        };
        return mutant;
    }
}
