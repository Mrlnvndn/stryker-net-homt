using Microsoft.CodeAnalysis;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Core.MutationTest;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// Enhanced heuristic that scores candidates based on structural code location proximity.
    /// Uses syntax tree analysis to determine precise location relationships.
    /// </summary>
    public class CodeLocationHeuristic : BaseHOMHeuristic
    {
        private readonly Dictionary<int, LocationInfo> _locationMap = [];

        public override string Name => "CodeLocation";
        public override double Weight => 2.0; // Increased weight due to better precision
        public override bool IsSearchStrategyHeuristic => false;
        public override bool IsFitnessScoringHeuristic => true;

        

        protected override void OnInitialized()
        {
            foreach (var fom in AvailableFOMs)
            {
                if (fom?.Mutation?.OriginalNode != null)
                {
                    _locationMap[fom.Id] = ExtractLocationInfo(fom.Mutation.OriginalNode);
                }
            }
        }

        private static LocationInfo ExtractLocationInfo(SyntaxNode originalNode)
        {
            var location = originalNode.GetLocation();
            var lineSpan = location.GetLineSpan();
            
            var info = new LocationInfo
            {
                LineNumber = lineSpan.StartLinePosition.Line,
                CharacterPosition = lineSpan.StartLinePosition.Character
            };

            // Extract namespace information
            var namespaceDecl = originalNode.Ancestors()
                .OfType<NamespaceDeclarationSyntax>()
                .FirstOrDefault();
            if (namespaceDecl != null)
            {
                info.Namespace = namespaceDecl.Name.ToString();
            }
            else
            {
                // Check for file-scoped namespace (C# 10+)
                var fileScopedNamespace = originalNode.Ancestors()
                    .OfType<FileScopedNamespaceDeclarationSyntax>()
                    .FirstOrDefault();
                info.Namespace = fileScopedNamespace?.Name.ToString() ?? "Global";
            }

            // Extract class information
            var classDecl = originalNode.Ancestors()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();
            if (classDecl != null)
            {
                info.ClassName = classDecl.Identifier.Text;
                info.IsInNestedClass = classDecl.Ancestors().OfType<ClassDeclarationSyntax>().Any();
            }
            else
            {
                // Check for struct, interface, record, etc.
                var typeDecl = originalNode.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault();
                info.ClassName = typeDecl?.Identifier.Text ?? "Unknown";
            }

            // Extract method information
            var methodDecl = originalNode.Ancestors()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();
            if (methodDecl != null)
            {
                info.MethodName = methodDecl.Identifier.Text;
                info.ContainingMemberType = "Method";
                info.IsInStaticContext = methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            }
            else
            {
                // Check for other member types
                var constructorDecl = originalNode.Ancestors()
                    .OfType<ConstructorDeclarationSyntax>()
                    .FirstOrDefault();
                if (constructorDecl != null)
                {
                    info.MethodName = ".ctor";
                    info.ContainingMemberType = "Constructor";
                    info.IsInStaticContext = constructorDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                }
                else
                {
                    var propertyDecl = originalNode.Ancestors()
                        .OfType<PropertyDeclarationSyntax>()
                        .FirstOrDefault();
                    if (propertyDecl != null)
                    {
                        info.PropertyName = propertyDecl.Identifier.Text;
                        info.ContainingMemberType = "Property";
                        info.IsInStaticContext = propertyDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    }
                }
            }

            return info;
        }

        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count <= 1)
            {
                return 1.0; // Single mutant gets perfect score
            }

            var locations = candidate.Select(m => _locationMap.GetValueOrDefault(m.Id)).Where(l => l != null).ToList();
            if (locations.Count == 0)
            {
                return 0.1; // No location info available
            }

            // Multi-tier scoring based on location proximity
            return CalculateLocationProximityScore(locations);
        }

        private double CalculateLocationProximityScore(List<LocationInfo> locations)
        {
            // Tier 1: Same method/property (highest score)
            if (AreSameMember(locations))
            {
                return 1.0;
            }

            // Tier 2: Same class, different methods (high score)
            if (AreSameClass(locations))
            {
                // Bonus for methods that are close together
                var proximityBonus = CalculateLineProximityBonus(locations);
                return 0.85 + (proximityBonus * 0.1);
            }

            // Tier 3: Same namespace, different classes (medium score)
            if (AreSameNamespace(locations))
            {
                return 0.6;
            }

            // Tier 4: Different namespaces but same static context (lower score)
            if (AreSameStaticContext(locations))
            {
                return 0.4;
            }

            // Tier 5: Scattered across different contexts (lowest score)
            return NormalizeScore(0.1 + CalculateScatterPenalty(locations));
        }

        private static bool AreSameMember(List<LocationInfo> locations)
        {
            var first = locations.First();
            return locations.All(l => 
                l.ClassName == first.ClassName && 
                l.MethodName == first.MethodName &&
                l.PropertyName == first.PropertyName);
        }

        private static bool AreSameClass(List<LocationInfo> locations)
        {
            var first = locations.First();
            return locations.All(l => 
                l.Namespace == first.Namespace && 
                l.ClassName == first.ClassName);
        }

        private static bool AreSameNamespace(List<LocationInfo> locations)
        {
            var first = locations.First();
            return locations.All(l => l.Namespace == first.Namespace);
        }

        private static bool AreSameStaticContext(List<LocationInfo> locations)
        {
            var first = locations.First();
            return locations.All(l => l.IsInStaticContext == first.IsInStaticContext);
        }

        private static double CalculateLineProximityBonus(List<LocationInfo> locations)
        {
            if (locations.Count < 2)
            {
                return 0;
            }

            var lines = locations.Select(l => l.LineNumber).OrderBy(l => l).ToList();
            var maxDistance = lines.Last() - lines.First();
            
            // Closer lines get higher bonus (max 10 line spread gets full bonus)
            return maxDistance <= 10 ? 1.0 : Math.Max(0, 1.0 - (maxDistance - 10) / 50.0);
        }

        private static double CalculateScatterPenalty(List<LocationInfo> locations)
        {
            // Calculate how scattered the mutants are across different contexts
            var namespaceCount = locations.Select(l => l.Namespace).Distinct().Count();
            var classCount = locations.Select(l => l.ClassName).Distinct().Count();
            
            // More scattered = lower score
            var scatterFactor = 1.0 / (namespaceCount + classCount);
            return Math.Min(0.3, scatterFactor);
        }
    }
}
