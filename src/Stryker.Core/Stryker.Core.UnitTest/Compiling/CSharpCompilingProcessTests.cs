using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Exceptions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.Core.Compiling;
using Stryker.Core.MutationTest;
using Stryker.Core.ProjectComponents.Csharp;
using Stryker.Core.ProjectComponents.SourceProjects;
using Stryker.Core.ProjectComponents.TestProjects;

namespace Stryker.Core.UnitTest.Compiling;

[TestClass]
public class CSharpCompilingProcessTests : TestBase
{
    [TestMethod]
    public void CompilingProcessTests_ShouldCompile()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"using System;

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(int first, int second)
    {
        return first - second;
    }
}
}");
        var input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(
                    projectFilePath: "/c/project.csproj",
                    properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "" },
                        { "AssemblyName", "AssemblyName"},
                        { "TargetFileName", "TargetFileName.dll"},
                    },
                    // add a reference to system so the example code can compile
                    references: new[] { typeof(object).Assembly.Location }
                ).Object
            }
        };
        var rollbackProcessMock = new Mock<ICSharpRollbackProcess>(MockBehavior.Strict);

        var target = new CsharpCompilingProcess(input, rollbackProcessMock.Object);

        using var ms = new MemoryStream();
        using var symbol = new MemoryStream();
        var result = target.Compile(new Collection<SyntaxTree>() { syntaxTree }, ms, symbol);
        result.Success.ShouldBe(true);
        ms.Length.ShouldBeGreaterThan(100, "No value was written to the MemoryStream by the compiler");
    }

    [TestMethod]
    public void CompilingProcessTests_ShouldSupportPackageReferenceAliases()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"
extern alias TheAlias;
using TheAlias::System;

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(int first, int second)
    {
        return first - second;
    }
}
}");
        var alias = new Dictionary<string, ImmutableArray<string>>();
        var immutableArray = ImmutableArray.Create("TheAlias");
        alias[typeof(object).Assembly.Location]=immutableArray;

        var input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(
                    projectFilePath: "/c/project.csproj",
                    properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "" },
                        { "AssemblyName", "AssemblyName" },
                        { "TargetFileName", "TargetFileName.dll" },
                    },
                    // add a reference to system so the example code can compile
                    references: [typeof(object).Assembly.Location],
                    aliases: alias.ToImmutableDictionary()
                ).Object
            }
        };
        var rollbackProcessMock = new Mock<ICSharpRollbackProcess>(MockBehavior.Strict);


        var target = new CsharpCompilingProcess(input, rollbackProcessMock.Object);

        using (var ms = new MemoryStream())
        {
            var result = target.Compile(new Collection<SyntaxTree>() { syntaxTree }, ms, null);
            result.Success.ShouldBe(true);
            ms.Length.ShouldBeGreaterThan(100, "No value was written to the MemoryStream by the compiler");
        }
    }

    [TestMethod]
    public void CompilingProcessTests_ShouldCallRollbackProcess_OnCompileError()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"using System;

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(string first, string second)
    {
        return first - second;
    }
}
}");
        var input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(
                    projectFilePath: "/c/project.csproj",
                    properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "" },
                        { "AssemblyName", "AssemblyName"},
                        { "TargetFileName", "TargetFileName.dll"},
                    },
                    // add a reference to system so the example code can compile
                    references: new string[] { typeof(object).Assembly.Location }
                ).Object
            }
        };
        var rollbackProcessMock = new Mock<ICSharpRollbackProcess>(MockBehavior.Strict);
        rollbackProcessMock.Setup(x => x.Start(It.IsAny<CSharpCompilation>(), It.IsAny<ImmutableArray<Diagnostic>>(), It.IsAny<bool>(), false))
                        .Returns((CSharpCompilation compilation, ImmutableArray<Diagnostic> diagnostics, bool _, bool _) =>
                        new(compilation, null));

        var target = new CsharpCompilingProcess(input, rollbackProcessMock.Object, new StrykerOptions());

        using (var ms = new MemoryStream())
        {
            Should.Throw<CompilationException>(() => target.Compile(new Collection<SyntaxTree>() { syntaxTree }, ms, null));
        }
        rollbackProcessMock.Verify(x => x.Start(It.IsAny<CSharpCompilation>(), It.IsAny<ImmutableArray<Diagnostic>>(), false, false),
            Times.AtLeast(2));
    }

    [TestMethod]
    public void CompilingProcessTests_ShouldOnlyRollbackErrors()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"using System;

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(int first, int second)
    {
        return first - second;
    }
}
}");
        var input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(
                    projectFilePath: "/c/project.csproj",
                    properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "" },
                        { "AssemblyName", "AssemblyName"},
                        { "TargetFileName", "TargetFileName.dll"},
                    },
                    // add a reference to system so the example code can compile
                    references: new string[] { typeof(object).Assembly.Location }
                ).Object
            }
        };
        var rollbackProcessMock = new Mock<ICSharpRollbackProcess>(MockBehavior.Strict);

        var target = new CsharpCompilingProcess(input, rollbackProcessMock.Object);

        using (var ms = new MemoryStream())
        {
            target.Compile(new Collection<SyntaxTree>() { syntaxTree }, ms, null);

            ms.Length.ShouldBeGreaterThan(100, "No value was written to the MemoryStream by the compiler");
        }
    }

    [TestMethod]
    public void CompilingProcessTests_SignedAssembliesMustBeSigned()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(int first, int second)
    {
        return first - second;
    }
}
}");
        var input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(
                    properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "" },
                        { "AssemblyName", "AssemblyName" },
                        { "TargetFileName", "TargetFileName.dll" },
                        { "SignAssembly", "true" },
                        { "AssemblyOriginatorKeyFile", Path.GetFullPath(Path.Combine("TestResources", "StrongNameKeyFile.snk")) }
                    },
                    // add a reference to system so the example code can compile
                    references: new string[] { typeof(object).Assembly.Location },
                    projectFilePath: "TestResources"
                ).Object
            }
        };
        var rollbackProcessMock = new Mock<ICSharpRollbackProcess>(MockBehavior.Strict);

        var target = new CsharpCompilingProcess(input, rollbackProcessMock.Object);

        using (var ms = new MemoryStream())
        {
            var result = target.Compile(new Collection<SyntaxTree>() { syntaxTree }, ms, null);
            result.Success.ShouldBe(true);

            var key = Assembly.Load(ms.ToArray()).GetName().GetPublicKey();
            key.Length.ShouldBe(160, "Assembly was not signed");
            ms.Length.ShouldBeGreaterThan(100, "No value was written to the MemoryStream by the compiler");
        }
    }

    [TestMethod]
    public void CompilingProcessTests_AssemblyShouldCompileUnsigned_WhenSignAssemblyTrue_ButKeyFileMissing()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(int first, int second)
    {
        return first - second;
    }
}
}");
        var input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(
                    properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "" },
                        { "AssemblyName", "AssemblyName"},
                        { "TargetFileName", "TargetFileName.dll"},
                        { "SignAssembly", "true" }
                    },
                    // add a reference to system so the example code can compile
                    references: new string[] { typeof(object).Assembly.Location },
                    projectFilePath: "TestResources"
                ).Object
            }

        };
        var rollbackProcessMock = new Mock<ICSharpRollbackProcess>(MockBehavior.Strict);

        var target = new CsharpCompilingProcess(input, rollbackProcessMock.Object);

        using (var ms = new MemoryStream())
        {
            var result = target.Compile(new Collection<SyntaxTree>() { syntaxTree }, ms, null);
            result.Success.ShouldBe(true);

            var key = Assembly.Load(ms.ToArray()).GetName().GetPublicKey();
            key.Length.ShouldBe(0, "Assembly was signed");
            ms.Length.ShouldBeGreaterThan(100, "No value was written to the MemoryStream by the compiler");
        }
    }

    [TestMethod]
    public void CompilingProcessTests_ProperlyFailsWhenSigningKeyIsNotFound()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(int first, int second)
    {
        return first - second;
    }
}
}");
        var input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "" },
                        { "TargetFileName", "TargetFileName.dll"},
                        { "AssemblyName", "AssemblyName"},
                        { "SignAssembly", "true" },
                        { "AssemblyOriginatorKeyFile", "DoesNotExist.snk" }
                    },
                    projectFilePath: "project.csproj",
                    // add a reference to system so the example code can compile
                    references: new string[] { typeof(object).Assembly.Location }
                ).Object
            }

        };
        var rollbackProcessMock = new Mock<ICSharpRollbackProcess>(MockBehavior.Strict);

        var target = new CsharpCompilingProcess(input, rollbackProcessMock.Object);

        using (var ms = new MemoryStream())
        {
            Should.Throw<CompilationException>(() => target.Compile(new Collection<SyntaxTree>() { syntaxTree }, ms, null));
        }
    }

    [TestMethod]
    public void CompilingProcessTests_MustIncludeVersionInfo()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"using System;

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(int first, int second)
    {
        return first - second;
    }
}
}");
        var input = new MutationTestInput()
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(
                    projectFilePath: "/c/project.csproj",
                    properties: new Dictionary<string, string>()
                    {
                        { "TargetDir", "" },
                        { "TargetFileName", "TargetFileName.dll" },
                        { "AssemblyName", "AssemblyName"},
                    },
                    // add a reference to system so the example code can compile
                    references: new string[] { typeof(object).Assembly.Location }
                ).Object
            }
        };
        var rollbackProcessMock = new Mock<ICSharpRollbackProcess>(MockBehavior.Strict);

        var target = new CsharpCompilingProcess(input, rollbackProcessMock.Object);

        using (var ms = new MemoryStream())
        {
            var result = target.Compile(new Collection<SyntaxTree>() { syntaxTree }, ms, null);
            result.Success.ShouldBe(true);

            Assembly.Load(ms.ToArray()).GetName().Version.ToString().ShouldBe("0.0.0.0");
        }
    }

    [TestMethod]
    public void ShouldCompileAndRollbackErrorWhenUninitializedVariable()
    {
        var sourceFile = @"using System;
using System.Collections.Generic;

namespace ExampleProject
{
public class Calculator
{
    public int Dummy()
    {
        int z;
        int y;
        if (true)
        {
            if (true)
            {
                z = 1;
                y = 0;
            }
            else
            {
                z = 0;
                y = 1;
            }
        }
        return z + y;
    }
}
}";
        var projectContentsMutants = MutateAndCompileSource(sourceFile);
        // those results can change if mutators are added.
        projectContentsMutants.Count(t => t.ResultStatus == MutantStatus.CompileError).ShouldBe(9);
        projectContentsMutants.Count(t => t.ResultStatus == MutantStatus.Pending).ShouldBe(0);
    }

    [TestMethod]
    public void ShouldCompileAndRollbackErrorsForEventHandler()
    {
        var sourceFile = @"using System;

namespace ExampleProject
{
public class Calculator
{
    public int Subtract(int first, int second)
    {
        return first - second;
    }
    private event Action SendCompleted;

    void TestMethod(){
        Action<Action> unsubscribe = (handler) => SendCompleted -= handler;
    }
}
}";
        var projectContentsMutants = MutateAndCompileSource(sourceFile);
        // those results can change if mutators are added.
        projectContentsMutants.Count(t => t.ResultStatus == MutantStatus.CompileError).ShouldBe(1);
        projectContentsMutants.Count(t => t.ResultStatus == MutantStatus.Pending).ShouldBe(3);
    }

    private static IEnumerable<IMutant> MutateAndCompileSource(string sourceFile)
    {
        var filesystemRoot = Path.GetPathRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

        var inputFile = new CsharpFileLeaf()
        {
            SourceCode = sourceFile,
            SyntaxTree = CSharpSyntaxTree.ParseText(sourceFile)
        };

        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            {
                Path.Combine(filesystemRoot, "ExampleProject", "Calculator.cs"),
                new MockFileData(sourceFile)
            },
            {
                Path.Combine(filesystemRoot, "ExampleProject.Test", "bin", "Debug", "netcoreapp2.0", "ExampleProject.dll"),
                new MockFileData("Bytecode")
            },
            {
                Path.Combine(filesystemRoot, "ExampleProject.Test", "obj", "Release", "netcoreapp2.0",
                    "ExampleProject.dll"),
                new MockFileData("Bytecode")
            }
        });

        var input = new MutationTestInput
        {
            SourceProjectInfo = new SourceProjectInfo
            {
                AnalyzerResult = TestHelper.SetupProjectAnalyzerResult(
                    projectFilePath: "/c/project.csproj",
                    properties: new Dictionary<string, string>
                    {
                        { "TargetDir", "Project" },
                        { "AssemblyName", "AssemblyName" },
                        { "TargetFileName", "TargetFileName.dll" },
                    },
                    // add a reference to system so the example code can compile
                    references: new[] { typeof(object).Assembly.Location }
                ).Object,
                TestProjectsInfo = new TestProjectsInfo(fileSystem)
                {
                    TestProjects = new List<TestProject> {
                        new TestProject(fileSystem, TestHelper.SetupProjectAnalyzerResult(
                            projectFilePath: "/c/test.csproj",
                            properties: new Dictionary<string, string>
                            {
                                { "TargetDir", "Project" },
                                { "AssemblyName", "AssemblyName" },
                                { "TargetFileName", "TargetFileName.dll" },
                            },
                            // add a reference to system so the example code can compile
                            references: new[] { typeof(object).Assembly.Location }
                        ).Object),
                    }
                }
            },

            TestRunner = new Mock<ITestRunner>(MockBehavior.Default).Object
        };
        var folder = new CsharpFolderComposite();
        var injector = input.SourceProjectInfo.CodeInjector;
        folder.Add(inputFile);
        foreach (var (name, code) in injector.MutantHelpers)
        {
            folder.AddCompilationSyntaxTree(CSharpSyntaxTree.ParseText(code, path: name, encoding: Encoding.UTF32));
        }

        input.SourceProjectInfo.ProjectContents = folder;

        var options = new StrykerOptions
        {
            MutationLevel = MutationLevel.Complete,
            OptimizationMode = OptimizationModes.CoverageBasedTest,
        };
        var process = new CsharpMutationProcess(fileSystem, options);
        process.Mutate(input);

        var projectContentsMutants = input.SourceProjectInfo.ProjectContents.Mutants;
        return projectContentsMutants;
    }
}
