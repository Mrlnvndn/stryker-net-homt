using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Abstractions;
using Stryker.Core.Mutants;

namespace Stryker.Core.MutationTest.HigherOrderMutationTest.Heuristics
{
    /// <summary>
    /// A heuristic that prioritizes certain mutation types that research suggests are more likely to form SSHOMs,
    /// such as relational operation removals and statement block removals.
    /// This is primarily a FITNESS SCORING heuristic.
    /// </summary>
    public class MutatorTypeHeuristic : BaseHOMHeuristic
    {
        /// <summary>
        /// The set of preferred mutation types
        /// </summary>
        private readonly HashSet<Mutator> _preferredMutationTypes;
        
        public override string Name => "MutatorType";
        
        public override double Weight => 1.5;
        
        public override bool IsFitnessScoringHeuristic => true;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="MutatorTypeHeuristic"/> class with default
        /// preferred mutation types based on research.
        /// </summary>
        public MutatorTypeHeuristic()
        {

        }
        
        public override double ScoreCandidate(List<IMutant> candidate)
        {
            if (candidate == null || candidate.Count == 0)
            {
                return 0.0;
            }
            
            double totalScore = 0;
            
            foreach (var mutant in candidate)
            {
                if(IsExpressionRemoval(mutant as Mutant))
                {
                    totalScore += 0.5;
                    continue;
                }
                if(IsRelationalOrEqualityReplacement(mutant as Mutant))
                {
                    totalScore += 0.5;
                    continue;
                }               
            }          
            
            var normalizedScore = NormalizeScore(totalScore);
            
            // Add NaN protection
            return double.IsNaN(normalizedScore) || double.IsInfinity(normalizedScore) ? 0.0 : normalizedScore;
        }

        private static bool IsExpressionRemoval(Mutant m)
        {
            var orig = m.Mutation.OriginalNode;
            var repl = m.Mutation.ReplacementNode;

            bool IsExprStmt(StatementSyntax s)
            {
                return s is ExpressionStatementSyntax es &&
                (es.Expression is AssignmentExpressionSyntax
                 || es.Expression is InvocationExpressionSyntax
                 || es.Expression is PostfixUnaryExpressionSyntax
                 || es.Expression is PrefixUnaryExpressionSyntax);
            }

            bool IsEmptyReplacement(SyntaxNode n) =>
                n is null
                || n.IsKind(SyntaxKind.EmptyStatement)
                || n is BlockSyntax b && b.Statements.Count == 0;

            if (orig is StatementSyntax s && IsExprStmt(s) && IsEmptyReplacement(repl))
            {
                return true;
            }

            // Fallback: a block became empty and previously contained only expr statements
            if (orig is BlockSyntax ob && repl is BlockSyntax rb && rb.Statements.Count == 0)
            {
                return ob.Statements.Any(ss => ss is ExpressionStatementSyntax);
            }

            return false;
        }

        private static bool IsRelationalOrEqualityReplacement(Mutant m)
        {
            static bool IsRelOrEq(SyntaxKind k)
            {
                return k is SyntaxKind.LessThanExpression
                  or SyntaxKind.LessThanOrEqualExpression
                  or SyntaxKind.GreaterThanExpression
                  or SyntaxKind.GreaterThanOrEqualExpression
                  or SyntaxKind.EqualsExpression
                  or SyntaxKind.NotEqualsExpression;
            }

            if (m.Mutation.OriginalNode is BinaryExpressionSyntax o &&
                m.Mutation.ReplacementNode is BinaryExpressionSyntax r &&
                IsRelOrEq(o.Kind()) && IsRelOrEq(r.Kind()) &&
                o.Kind() != r.Kind())
            {
                return true;
            }

            return false;

        }

    }
}
