using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Konseben.HandlerFinder.Benchmarks
{
    /// <summary>
    /// Shared loader for the benchmarks. Enumerates and reads all .cs files under a large
    /// solution root once, then builds Roslyn solutions from the cached text on demand.
    ///
    /// Building the search is purely syntactic (no semantic model / compilation), so an
    /// AdhocWorkspace of all documents faithfully reproduces the parse + node-walk cost the
    /// extension pays inside Visual Studio, without any MSBuild restore or design-time build.
    /// </summary>
    public sealed class SolutionSource
    {
        public const string DirectoryEnvironmentVariable = "HANDLERFINDER_BENCHMARK_DIR";
        public const string RequestEnvironmentVariable = "HANDLERFINDER_BENCHMARK_REQUEST";
        public const string DefaultDirectory = @"C:\dev\CentralHub\netgo.centralhub.Monorepo";
        public const string DefaultRequest = "GetOrderIdsQuery";

        private string[] _filePaths;
        private string[] _fileContents;

        public string Root { get; private set; }
        public string Request { get; private set; }
        public int DocumentCount => _filePaths?.Length ?? 0;

        /// <summary>
        /// Enumerates and reads every .cs file under the configured root into memory.
        /// Call once from a [GlobalSetup]; the file I/O is deliberately excluded from timings.
        /// </summary>
        public void Load()
        {
            Root = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable) ?? DefaultDirectory;
            Request = Environment.GetEnvironmentVariable(RequestEnvironmentVariable) ?? DefaultRequest;

            if (!Directory.Exists(Root))
            {
                throw new DirectoryNotFoundException(
                    $"Benchmark directory not found: '{Root}'. " +
                    $"Set {DirectoryEnvironmentVariable} to a large solution's root.");
            }

            _filePaths = Directory
                .EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsBuildOutput(f))
                .ToArray();

            _fileContents = new string[_filePaths.Length];
            for (int i = 0; i < _filePaths.Length; i++)
            {
                _fileContents[i] = File.ReadAllText(_filePaths[i]);
            }
        }

        /// <summary>
        /// Builds a fresh solution from the cached file text. Each call produces new syntax
        /// trees (nothing parsed yet), so the first search over it pays the full cold cost.
        /// </summary>
        public Solution BuildSolution()
        {
            var projectId = ProjectId.CreateNewId();

            var documents = new List<DocumentInfo>(_filePaths.Length);
            for (int i = 0; i < _filePaths.Length; i++)
            {
                string path = _filePaths[i];
                documents.Add(DocumentInfo.Create(
                    DocumentId.CreateNewId(projectId),
                    name: Path.GetFileName(path),
                    filePath: path,
                    loader: TextLoader.From(
                        TextAndVersion.Create(
                            SourceText.From(_fileContents[i]),
                            VersionStamp.Default,
                            path))));
            }

            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                name: "BenchmarkProject",
                assemblyName: "BenchmarkProject",
                language: LanguageNames.CSharp,
                documents: documents)
                .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

            var workspace = new AdhocWorkspace();
            return workspace.CurrentSolution.AddProject(projectInfo);
        }

        private static bool IsBuildOutput(string path)
        {
            return path.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
