using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using Stryker.DataCollector;

namespace Stryker.DataCollector.UnitTest;

/// <summary>
/// Tests for the enhanced dual-mode XML generation in CoverageCollector
/// Validates the correct generation of XML relationships between HOMs and FOMs
/// </summary>
[TestClass]
public class CoverageCollectorDualModeXmlTests
{
    [TestMethod]
    public void GetVsTestSettings_WithRegularFOMs_ShouldGenerateStandardXml()
    {
        // Arrange
        var mutantTestsMap = new[]
        {
            (1, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),
            (2, new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") }.AsEnumerable())
        };
        var helperNameSpace = "TestNamespace";
        var isHomt = false;
        var homToFomMapping = new Dictionary<int, List<int>>();

        // Act
        var result = CoverageCollector.GetVsTestSettings(false, mutantTestsMap, helperNameSpace, isHomt, homToFomMapping);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldContain("type='fom'");
        result.ShouldContain("id='1'");
        result.ShouldContain("id='2'");
        result.ShouldNotContain("type='hom'");
        result.ShouldNotContain("parent=");
        result.ShouldNotContain("constituents=");

        // Validate XML structure
        ValidateXmlStructure(result);
    }

    [TestMethod]
    public void GetVsTestSettings_WithHOMAndFOMs_ShouldGenerateDualModeXml()
    {
        // Arrange - HOM (100) with two constituent FOMs (1, 2) plus regular FOM (5)
        var mutantTestsMap = new[]
        {
            (100, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()), // HOM
            (1, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),   // FOM 1 (constituent)
            (2, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),   // FOM 2 (constituent)
            (5, new[] { Guid.Parse("55555555-5555-5555-5555-555555555555") }.AsEnumerable())    // Regular FOM
        };
        var helperNameSpace = "TestNamespace";
        var isHomt = true;
        var homToFomMapping = new Dictionary<int, List<int>>
        {
            { 100, new List<int> { 1, 2 } }
        };

        // Act
        var result = CoverageCollector.GetVsTestSettings(false, mutantTestsMap, helperNameSpace, isHomt, homToFomMapping);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldContain("<Homt/>");
        
        // Should contain HOM entry with constituents
        result.ShouldContain("id='100'");
        result.ShouldContain("type='hom'");
        result.ShouldContain("constituents='1,2'");
        
        // Should contain constituent FOM entries with parent reference
        result.ShouldContain("id='1'");
        result.ShouldContain("id='2'");
        result.ShouldContain("type='fom' parent='100'");
        
        // Should contain regular FOM entry
        result.ShouldContain("id='5'");
        result.ShouldContain("type='fom'/>");

        ValidateXmlStructure(result);
        ValidateHOMFOMRelationships(result, homToFomMapping);
    }

    [TestMethod]
    public void GetVsTestSettings_WithMultipleHOMs_ShouldHandleComplexScenarios()
    {
        // Arrange - Multiple HOMs with different constituent counts
        var mutantTestsMap = new[]
        {
            (100, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()), // HOM 1
            (1, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),   // HOM 1 constituent 1
            (2, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),   // HOM 1 constituent 2
            (200, new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") }.AsEnumerable()), // HOM 2
            (3, new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") }.AsEnumerable()),   // HOM 2 constituent 1
            (4, new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") }.AsEnumerable()),   // HOM 2 constituent 2
            (5, new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") }.AsEnumerable()),   // HOM 2 constituent 3
            (99, new[] { Guid.Parse("99999999-9999-9999-9999-999999999999") }.AsEnumerable())    // Regular FOM
        };
        var helperNameSpace = "TestNamespace";
        var isHomt = true;
        var homToFomMapping = new Dictionary<int, List<int>>
        {
            { 100, new List<int> { 1, 2 } },
            { 200, new List<int> { 3, 4, 5 } }
        };

        // Act
        var result = CoverageCollector.GetVsTestSettings(false, mutantTestsMap, helperNameSpace, isHomt, homToFomMapping);

        // Assert
        result.ShouldNotBeNull();
        
        // Validate HOM 1
        result.ShouldContain("id='100'");
        result.ShouldContain("type='hom'");
        result.ShouldContain("constituents='1,2'");
        
        // Validate HOM 2
        result.ShouldContain("id='200'");
        result.ShouldContain("constituents='3,4,5'");
        
        // Validate constituent FOMs
        result.ShouldContain("id='1'");
        result.ShouldContain("id='2'");
        result.ShouldContain("type='fom' parent='100'");
        result.ShouldContain("id='3'");
        result.ShouldContain("id='4'");
        result.ShouldContain("id='5'");
        result.ShouldContain("type='fom' parent='200'");
        
        // Validate regular FOM
        result.ShouldContain("id='99'");
        var regularFomPattern = @"id='99'[^>]*type='fom'[^>]*/>|id='99'[^>]*type='fom'[^>]*tests=";
        System.Text.RegularExpressions.Regex.IsMatch(result, regularFomPattern).ShouldBeTrue();

        ValidateXmlStructure(result);
        ValidateHOMFOMRelationships(result, homToFomMapping);
    }

    [TestMethod]
    public void GetVsTestSettings_WithEmptyHOMMapping_ShouldTreatAsRegularFOMs()
    {
        // Arrange
        var mutantTestsMap = new[]
        {
            (1, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),
            (2, new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") }.AsEnumerable())
        };
        var helperNameSpace = "TestNamespace";
        var isHomt = true;
        var homToFomMapping = new Dictionary<int, List<int>>(); // Empty mapping

        // Act
        var result = CoverageCollector.GetVsTestSettings(false, mutantTestsMap, helperNameSpace, isHomt, homToFomMapping);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldContain("<Homt/>");
        result.ShouldContain("type='fom'");
        result.ShouldNotContain("type='hom'");
        result.ShouldNotContain("parent=");
        result.ShouldNotContain("constituents=");

        ValidateXmlStructure(result);
    }

    [TestMethod]
    public void GetVsTestSettings_WithNullHOMMapping_ShouldHandleGracefully()
    {
        // Arrange
        var mutantTestsMap = new[]
        {
            (1, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable())
        };
        var helperNameSpace = "TestNamespace";
        var isHomt = false;
        Dictionary<int, List<int>> homToFomMapping = null;

        // Act
        var result = CoverageCollector.GetVsTestSettings(false, mutantTestsMap, helperNameSpace, isHomt, homToFomMapping);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldContain("type='fom'");
        result.ShouldNotContain("type='hom'");

        ValidateXmlStructure(result);
    }

    [TestMethod]
    public void GetVsTestSettings_CoverageMode_ShouldNotIncludeMutantInformation()
    {
        // Arrange
        var mutantTestsMap = new[]
        {
            (100, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),
            (1, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable())
        };
        var helperNameSpace = "TestNamespace";
        var isHomt = true;
        var homToFomMapping = new Dictionary<int, List<int>>
        {
            { 100, new List<int> { 1 } }
        };

        // Act
        var result = CoverageCollector.GetVsTestSettings(true, mutantTestsMap, helperNameSpace, isHomt, homToFomMapping);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldContain("<Coverage/>");
        result.ShouldNotContain("id='100'");
        result.ShouldNotContain("id='1'");
        result.ShouldNotContain("type='hom'");
        result.ShouldNotContain("type='fom'");
    }

    [TestMethod]
    public void GetVsTestSettings_XmlStructure_ShouldBeValidAndWellFormed()
    {
        // Arrange
        var mutantTestsMap = new[]
        {
            (100, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),
            (1, new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }.AsEnumerable()),
            (2, new[] { Guid.Parse("22222222-2222-2222-2222-222222222222") }.AsEnumerable())
        };
        var helperNameSpace = "TestNamespace";
        var isHomt = true;
        var homToFomMapping = new Dictionary<int, List<int>>
        {
            { 100, new List<int> { 1 } }
        };

        // Act
        var result = CoverageCollector.GetVsTestSettings(false, mutantTestsMap, helperNameSpace, isHomt, homToFomMapping);

        // Assert
        result.ShouldNotBeNull();
        ValidateXmlStructure(result);

        // Parse XML and validate structure
        var doc = new XmlDocument();
        doc.LoadXml(result);

        // Should have root InProcDataCollectionRunSettings
        doc.DocumentElement?.Name.ShouldBe("InProcDataCollectionRunSettings");

        // Should have Parameters section
        var parameters = doc.SelectSingleNode("//Parameters");
        parameters.ShouldNotBeNull();

        // Should have Homt flag
        var homtNode = doc.SelectSingleNode("//Parameters/Homt");
        homtNode.ShouldNotBeNull();

        // Should have mutant entries with correct attributes
        var mutantNodes = doc.SelectNodes("//Parameters/Mutant");
        mutantNodes?.Count.ShouldBe(3);

        // Should have MutantControl entry
        var mutantControlNode = doc.SelectSingleNode("//Parameters/MutantControl");
        mutantControlNode.ShouldNotBeNull();
        mutantControlNode?.Attributes?["name"]?.Value.ShouldBe("TestNamespace.MutantControl");
    }

    #region Helper Methods

    private static void ValidateXmlStructure(string xml)
    {
        // Should be valid XML
        var doc = new XmlDocument();
        Should.NotThrow(() => doc.LoadXml(xml));

        // Should contain basic structure
        xml.ShouldContain("<InProcDataCollectionRunSettings>");
        xml.ShouldContain("<InProcDataCollectors>");
        xml.ShouldContain("<InProcDataCollector");
        xml.ShouldContain("<Configuration>");
        xml.ShouldContain("<Parameters>");
        xml.ShouldContain("</Parameters>");
        xml.ShouldContain("</Configuration>");
        xml.ShouldContain("</InProcDataCollector>");
        xml.ShouldContain("</InProcDataCollectors>");
        xml.ShouldContain("</InProcDataCollectionRunSettings>");
    }

    private static void ValidateHOMFOMRelationships(string xml, Dictionary<int, List<int>> expectedMappings)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var mutantNodes = doc.SelectNodes("//Parameters/Mutant");

        foreach (var mapping in expectedMappings)
        {
            var homId = mapping.Key;
            var fomIds = mapping.Value;

            // Validate HOM node
            var homNode = mutantNodes?.Cast<XmlNode>()
                .FirstOrDefault(n => n.Attributes?["id"]?.Value == homId.ToString());
            homNode.ShouldNotBeNull($"HOM node with ID {homId} should exist");
            homNode?.Attributes?["type"]?.Value.ShouldBe("hom");
            
            var constituents = homNode?.Attributes?["constituents"]?.Value;
            constituents.ShouldNotBeNull();
            var constituentIds = constituents!.Split(',').Select(int.Parse).ToList();
            constituentIds.ShouldBeEquivalentTo(fomIds);

            // Validate FOM nodes
            foreach (var fomId in fomIds)
            {
                var fomNode = mutantNodes?.Cast<XmlNode>()
                    .FirstOrDefault(n => n.Attributes?["id"]?.Value == fomId.ToString());
                fomNode.ShouldNotBeNull($"FOM node with ID {fomId} should exist");
                fomNode?.Attributes?["type"]?.Value.ShouldBe("fom");
                fomNode?.Attributes?["parent"]?.Value.ShouldBe(homId.ToString());
            }
        }
    }

    #endregion
}
