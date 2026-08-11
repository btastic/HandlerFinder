using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Konseben.HandlerFinder
{
    /// <summary>
    /// Host-independent handler search logic. Contains the pure Roslyn-based matching
    /// so it can be exercised without any Visual Studio shell services.
    /// </summary>
    public static class HandlerSearch
    {
        /// <summary>
        /// Finds all MediatR "Handle" methods across the solution whose parameter type name
        /// matches <paramref name="requestedCommandOrRequest"/>.
        /// </summary>
        public static async Task<IReadOnlyList<(string file, int lineIndex, int columnIndex)>> FindHandlersAsync(
            Solution solution,
            string requestedCommandOrRequest)
        {
            var results = new List<(string, int, int)>();

            if (solution == null || string.IsNullOrWhiteSpace(requestedCommandOrRequest))
            {
                return results;
            }

            IEnumerable<MethodDeclarationSyntax> methodDeclarationSyntaxes =
                (await solution.Projects.Select(p => p.Documents)
                .SelectMany(x => x)
                .Select(async doc =>
                {
                    var syntaxRoot = await doc.GetSyntaxRootAsync();

                    return new
                    {
                        MethodDeclarations = syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>(),
                    };
                })
                .WhenAllAsync())
                .Where(x => x.MethodDeclarations.Any())
                .SelectMany(x => x.MethodDeclarations)
                .Where(x => x.Identifier.Text == "Handle");

            foreach (MethodDeclarationSyntax method in methodDeclarationSyntaxes)
            {
                foreach (ParameterSyntax typeArgument in method.ParameterList.Parameters)
                {
                    string identifierText = GetIdentifierNameByNode(typeArgument);

                    if (string.IsNullOrWhiteSpace(identifierText))
                    {
                        continue;
                    }

                    if (identifierText != requestedCommandOrRequest)
                    {
                        continue;
                    }

                    var file = method.SyntaxTree.FilePath;
                    var lineIndex = method.SyntaxTree.GetLineSpan(method.Span).StartLinePosition.Line + 1;
                    var columnIndex =
                        method
                            .ToFullString()
                            .Replace(Environment.NewLine, string.Empty)
                            .IndexOf("Handle") + 1;

                    results.Add((file, lineIndex, columnIndex));
                }
            }

            return results;
        }

        /// <summary>
        /// Checks if the syntax node type is supported as an invocation target for "Find handler".
        /// </summary>
        public static bool IsSyntaxNodeSupported(SyntaxNode node)
        {
            bool isSupported = node != null
                && ((node is IdentifierNameSyntax) ||
                    (node is RecordDeclarationSyntax) ||
                    (node is ClassDeclarationSyntax) ||
                    (node is ConstructorDeclarationSyntax));

            isSupported = isSupported && GetIdentifierNameByNode(node) != string.Empty;

            return isSupported;
        }

        /// <summary>
        /// Extracts the type/identifier name from a supported syntax node.
        /// </summary>
        public static string GetIdentifierNameByNode(SyntaxNode node)
        {
            string name = string.Empty;

            switch (node)
            {
                case ParameterSyntax parameterSyntax:
                    switch (parameterSyntax.Type)
                    {
                        case QualifiedNameSyntax qualifiedNameSyntax:
                            name = qualifiedNameSyntax.Right.Identifier.ToFullString().Trim();
                            break;
                        default:
                            name = parameterSyntax.Type.ToFullString().Trim();
                            break;
                    }
                    break;
                case RecordDeclarationSyntax recordDeclarationSyntax:
                    name = recordDeclarationSyntax.Identifier.Text;
                    break;
                case IdentifierNameSyntax identifierNameSyntax:
                    name = identifierNameSyntax.Identifier.Text;
                    break;
                case ClassDeclarationSyntax classDeclarationSyntax:
                    name = classDeclarationSyntax.Identifier.Text;
                    break;
                case GenericNameSyntax genericNameSyntax:
                    name = genericNameSyntax.Identifier.Text;
                    break;
                case ConstructorDeclarationSyntax constructorDeclarationSyntax:
                    name = constructorDeclarationSyntax.Identifier.Text;
                    break;
            }

            if (name != "var")
            {
                return name;
            }

            return string.Empty;
        }
    }
}
