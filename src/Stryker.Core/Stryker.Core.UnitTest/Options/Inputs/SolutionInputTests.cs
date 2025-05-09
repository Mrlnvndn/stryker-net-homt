using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Shouldly;
using Stryker.Abstractions.Exceptions;
using Stryker.Abstractions.Options.Inputs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stryker.Core.UnitTest;

namespace Stryker.Core.UnitTest.Options.Inputs;

[TestClass]
public class SolutionInputTests : TestBase
{
    [TestMethod]
    public void ShouldHaveHelpText()
    {
        var target = new SolutionInput();
        target.HelpText.ShouldBe(@"Full path to your solution file. Required on dotnet framework.");
    }

    [TestMethod]
    public void ShouldReturnSolutionPathIfExists()
    {
        var dir = Directory.GetCurrentDirectory();
        var path = Path.Combine(dir, "solution.sln");
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(dir);
        fileSystem.AddFile(path, new MockFileData(""));

        var input = new SolutionInput { SuppliedInput = path };

        input.Validate(dir, fileSystem).ShouldBe(path);
    }

    [TestMethod]
    public void ShouldReturnFullPathWhenRelativePathGiven()
    {
        var dir = Directory.GetCurrentDirectory();
        var relativePath = "./solution.sln";
        var fullPath = Path.Combine(dir, "solution.sln");
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(dir);
        fileSystem.AddFile(fullPath, new MockFileData(""));
        fileSystem.Directory.SetCurrentDirectory(dir);

        var input = new SolutionInput { SuppliedInput = relativePath };

        input.Validate(dir, fileSystem).ShouldBe(fullPath);
    }

    [TestMethod]
    public void ShouldDiscoverSolutionFileIfSolutionPathIsNotSupplied()
    {
        var input = new SolutionInput { SuppliedInput = null };
        var dir = Directory.GetCurrentDirectory();
        var fileSystem = new MockFileSystem();
        var fullPath = Path.Combine(dir, "solution.sln");
        fileSystem.AddDirectory(dir);
        fileSystem.AddFile(fullPath, new MockFileData(""));
        fileSystem.Directory.SetCurrentDirectory(dir);

        input.Validate(dir, fileSystem).ShouldBe(fullPath);
    }

    [TestMethod]
    public void ShouldThrowWhenMultipleSolutionFilesAreDiscovered()
    {
        var input = new SolutionInput { SuppliedInput = null };
        var dir = Directory.GetCurrentDirectory();
        var fileSystem = new MockFileSystem();
        var solution1 = Path.Combine(dir, "solution1.sln");
        var solution2 = Path.Combine(dir, "solution2.sln");
        fileSystem.AddDirectory(dir);
        fileSystem.AddFile(solution1, new MockFileData(""));
        fileSystem.AddFile(solution2, new MockFileData(""));
        fileSystem.Directory.SetCurrentDirectory(dir);
        var errorMessage =
$@"Expected exactly one .sln file, found more than one:
{solution1}
{solution2}
";

        var ex = Should.Throw<InputException>(() =>
        {
            input.Validate(dir, fileSystem);
        });

        ex.Message.ShouldBe(errorMessage);
    }

    [TestMethod]
    public void ShouldBeEmptyWhenNullAndCantBeDiscovered()
    {
        var input = new SolutionInput { SuppliedInput = null };
        var dir = Directory.GetCurrentDirectory();
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(dir);
        fileSystem.Directory.SetCurrentDirectory(dir);

        input.Validate(dir, fileSystem).ShouldBeNull();
    }

    [TestMethod]
    public void ShouldThrowWhenNotExists()
    {
        var input = new SolutionInput { SuppliedInput = "/c/root/bla/solution.sln" };
        var dir = Directory.GetCurrentDirectory();
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(dir);

        var ex = Should.Throw<InputException>(() =>
        {
            input.Validate(dir, fileSystem);
        });

        ex.Message.ShouldBe("Given path does not exist: /c/root/bla/solution.sln");
    }

    [TestMethod]
    public void ShouldThrowWhenPathIsNoSolutionFile()
    {
        var input = new SolutionInput { SuppliedInput = "/c/root/bla/solution.csproj" };
        var dir = Directory.GetCurrentDirectory();
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(dir);

        var ex = Should.Throw<InputException>(() =>
        {
            input.Validate(dir, fileSystem);
        });

        ex.Message.ShouldBe("Given path is not a solution file: /c/root/bla/solution.csproj");
    }
}
