using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Stryker.Abstractions.ProjectComponents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stryker.Core.Reporters;
using Stryker.Core.Mutants;
using Stryker.Core.ProjectComponents.Csharp;
using Stryker.Abstractions;

namespace Stryker.Core.UnitTest.Reporters;

[TestClass]
public class StatusReporterTests : TestBase
{
    private Mock<ILogger<FilteredMutantsLogger>> _loggerMock = new Mock<ILogger<FilteredMutantsLogger>>();

    public StatusReporterTests()
    {
    }

    [TestMethod]
    public void ShouldPrintNoMutations()
    {
        var target = new FilteredMutantsLogger(_loggerMock.Object);

        var folder = new CsharpFolderComposite();
        folder.Add(new CsharpFileLeaf()
        {
            Mutants = new Collection<IMutant>()
            {
            }
        });

        target.OnMutantsCreated(folder);

        _loggerMock.Verify(LogLevel.Information, "0     total mutants will be tested", Times.Once);
        _loggerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void ShouldPrintIgnoredStatus()
    {
        var target = new FilteredMutantsLogger(_loggerMock.Object);

        var folder = new CsharpFolderComposite();
        folder.Add(new CsharpFileLeaf()
        {
            Mutants = new Collection<IMutant>()
            {
                new Mutant() { ResultStatus = MutantStatus.Ignored, ResultStatusReason = "In excluded file" },
            }
        });

        target.OnMutantsCreated(folder);

        _loggerMock.Verify(LogLevel.Information, "1     mutants got status Ignored.      Reason: In excluded file", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "1     total mutants are skipped for the above mentioned reasons", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "0     total mutants will be tested", Times.Once);
        _loggerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void ShouldPrintEachReasonWithCount()
    {
        var target = new FilteredMutantsLogger(_loggerMock.Object);

        var folder = new CsharpFolderComposite();
        folder.Add(new CsharpFileLeaf()
        {
            Mutants = new Collection<IMutant>()
            {
                new Mutant() { ResultStatus = MutantStatus.Ignored, ResultStatusReason = "In excluded file" },
                new Mutant() { ResultStatus = MutantStatus.Ignored, ResultStatusReason = "In excluded file" },
                new Mutant() { ResultStatus = MutantStatus.Ignored, ResultStatusReason = "Mutator excluded" },
                new Mutant() { ResultStatus = MutantStatus.Ignored, ResultStatusReason = "Mutator excluded" },
                new Mutant() { ResultStatus = MutantStatus.Ignored, ResultStatusReason = "Mutator excluded" },
                new Mutant() { ResultStatus = MutantStatus.CompileError, ResultStatusReason = "CompileError" },
                new Mutant() { ResultStatus = MutantStatus.Ignored, ResultStatusReason = "In excluded file" },
                new Mutant() { ResultStatus = MutantStatus.Pending },
            }
        });

        target.OnMutantsCreated(folder);

        _loggerMock.Verify(LogLevel.Information, "1     mutants got status CompileError. Reason: CompileError", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "3     mutants got status Ignored.      Reason: In excluded file", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "3     mutants got status Ignored.      Reason: Mutator excluded", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "7     total mutants are skipped for the above mentioned reasons", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "1     total mutants will be tested", Times.Once);
        _loggerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void ShouldPrintHigherOrderMutantInformation()
    {
        var target = new FilteredMutantsLogger(_loggerMock.Object);

        var folder = new CsharpFolderComposite();
        
        // Create some first-order mutants
        var fom1 = new Mutant() { Id = 1, ResultStatus = MutantStatus.Pending };
        var fom2 = new Mutant() { Id = 2, ResultStatus = MutantStatus.Pending };
        var fom3 = new Mutant() { Id = 3, ResultStatus = MutantStatus.Pending };
        
        // Create a higher-order mutant
        var hom = new HigherOrderMutant(new List<IMutant> { fom1, fom2 })
        {
            Id = 100,
            ResultStatus = MutantStatus.Pending
        };

        folder.Add(new CsharpFileLeaf()
        {
            Mutants = new Collection<IMutant>()
            {
                fom1,
                fom2, 
                fom3,
                hom
            }
        });

        target.OnMutantsCreated(folder);

        _loggerMock.Verify(LogLevel.Information, "3     first-order mutants will be tested", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "1     higher-order mutants will be tested", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "Higher-Order Mutants: Average order 2.0, Maximum order 2", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "Higher-Order Mutants cover 2 unique first-order mutants", Times.Once);
        _loggerMock.Verify(LogLevel.Information, "4     total mutants will be tested", Times.Once);
        _loggerMock.VerifyNoOtherCalls();
    }
}
