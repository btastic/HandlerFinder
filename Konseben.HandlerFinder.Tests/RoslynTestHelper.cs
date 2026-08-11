using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Konseben.HandlerFinder.Tests
{
    internal static class RoslynTestHelper
    {
        /// <summary>
        /// Builds an in-memory single-project solution from the given (fileName, source) pairs,
        /// with each document's FilePath set to its file name so the search can report it.
        /// </summary>
        public static Solution CreateSolution(params (string fileName, string source)[] files)
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();

            Solution solution = workspace.CurrentSolution
                .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
                .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Latest));

            foreach (var (fileName, source) in files)
            {
                var documentId = DocumentId.CreateNewId(projectId);
                solution = solution.AddDocument(
                    documentId,
                    fileName,
                    SourceText.From(source),
                    folders: null,
                    filePath: fileName);
            }

            return solution;
        }

        /// <summary>
        /// Parses a snippet and returns the first descendant node of the requested type.
        /// </summary>
        public static T FirstNode<T>(string source) where T : SyntaxNode
        {
            SyntaxNode root = CSharpSyntaxTree
                .ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))
                .GetRoot();

            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (node is T typed)
                {
                    return typed;
                }
            }

            return null;
        }
    }
}
