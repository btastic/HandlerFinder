using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
            string requestedCommandOrRequest,
            CancellationToken cancellationToken = default)
        {
            var results = new List<(string, int, int)>();

            if (solution == null || string.IsNullOrWhiteSpace(requestedCommandOrRequest))
            {
                return results;
            }

            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<Document> documents = solution.Projects
                .SelectMany(p => p.Documents)
                .Where(doc => doc.SupportsSyntaxTree);

            IEnumerable<MethodDeclarationSyntax> methodDeclarationSyntaxes =
                (await documents
                .Select(async doc =>
                {
                    SourceText text = await doc.GetTextAsync(cancellationToken);

                    if (!SourceTextContains(text, requestedCommandOrRequest)
                        || !SourceTextContains(text, "Handle"))
                    {
                        return Enumerable.Empty<MethodDeclarationSyntax>();
                    }

                    var syntaxRoot = await doc.GetSyntaxRootAsync(cancellationToken);

                    return syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>();
                })
                .WhenAllAsync())
                .SelectMany(x => x)
                .Where(x => x.Identifier.Text == "Handle");

            foreach (MethodDeclarationSyntax method in methodDeclarationSyntaxes)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                    FileLinePositionSpan lineSpan = method.Identifier.GetLocation().GetLineSpan();
                    var lineIndex = lineSpan.StartLinePosition.Line + 1;
                    var columnIndex = lineSpan.StartLinePosition.Character + 1;

                    results.Add((file, lineIndex, columnIndex));
                }
            }

            return results;
        }

        private static bool SourceTextContains(SourceText text, string value)
        {
            int length = text.Length;
            int needleLength = value.Length;

            if (needleLength == 0)
            {
                return true;
            }

            if (needleLength > length)
            {
                return false;
            }

            const int chunkSize = 16384;
            int overlap = needleLength - 1;
            ReadOnlySpan<char> needle = value.AsSpan();

            char[] buffer = ArrayPool<char>.Shared.Rent(chunkSize + overlap);

            try
            {
                int carried = 0;
                int position = 0;

                while (position < length)
                {
                    int toCopy = Math.Min(chunkSize, length - position);
                    text.CopyTo(position, buffer, carried, toCopy);

                    int available = carried + toCopy;

                    if (new ReadOnlySpan<char>(buffer, 0, available).IndexOf(needle) >= 0)
                    {
                        return true;
                    }

                    // Keep the last (needleLength - 1) chars so a match spanning this chunk
                    // and the next is not missed.
                    carried = Math.Min(overlap, available);
                    if (carried > 0)
                    {
                        Array.Copy(buffer, available - carried, buffer, 0, carried);
                    }

                    position += toCopy;
                }

                return false;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
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
