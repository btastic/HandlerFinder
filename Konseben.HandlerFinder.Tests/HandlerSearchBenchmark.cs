using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Xunit.Abstractions;

namespace Konseben.HandlerFinder.Tests
{
    /// <summary>
    /// Manual, opt-in performance baseline for <see cref="HandlerSearch.FindHandlersAsync"/>.
    ///
    /// It builds a Roslyn solution directly from the .cs files on disk under a large solution
    /// root. That is a faithful proxy for what the extension does inside Visual Studio: the
    /// search is purely syntactic (no semantic model / compilation), so a workspace containing
    /// every document reproduces the exact parse + node-walk cost, on the same Roslyn version.
    ///
    /// Run with:
    ///   dotnet test --filter Category=Benchmark -l "console;verbosity=detailed"
    /// </summary>
    [Trait("Category", "Benchmark")]
    public class HandlerSearchBenchmark
    {
        private const string RequestEnvironmentVariable = "HANDLERFINDER_BENCHMARK_REQUEST";
        private const string DefaultRequest = "GetOrderIdsQuery";

        private readonly ITestOutputHelper _output;

        public HandlerSearchBenchmark(ITestOutputHelper output)
        {
            _output = output;
        }

        [SolutionBenchmarkFact]
        public async Task Baseline_FindHandlers_OverLargeSolution()
        {
            string root = SolutionBenchmarkFactAttribute.BenchmarkDirectory;
            string request = Environment.GetEnvironmentVariable(RequestEnvironmentVariable) ?? DefaultRequest;

            // --- Setup (NOT part of what we optimize): enumerate + load all .cs into a workspace. ---
            var setupStopwatch = Stopwatch.StartNew();

            string[] files = Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsInBuildOutput(f))
                .ToArray();

            Solution solution = BuildSolution(files);
            setupStopwatch.Stop();

            _output.WriteLine($"Root:            {root}");
            _output.WriteLine($"Request type:    {request}");
            _output.WriteLine($"C# documents:    {files.Length:N0}");
            _output.WriteLine($"Workspace build: {setupStopwatch.ElapsedMilliseconds:N0} ms (setup, not measured)");
            _output.WriteLine(string.Empty);

            // --- Cold run: first call, forces every document to be parsed. ---
            var cold = Stopwatch.StartNew();
            IReadOnlyList<(string file, int lineIndex, int columnIndex)> results =
                await HandlerSearch.FindHandlersAsync(solution, request);
            cold.Stop();

            // --- Warm runs: syntax trees are cached; isolates the match/walk cost. ---
            const int warmIterations = 3;
            var warm = Stopwatch.StartNew();
            for (int i = 0; i < warmIterations; i++)
            {
                await HandlerSearch.FindHandlersAsync(solution, request);
            }
            warm.Stop();

            _output.WriteLine($"Matches found:   {results.Count}");
            _output.WriteLine($"COLD run:        {cold.ElapsedMilliseconds:N0} ms  (includes parsing all trees)");
            _output.WriteLine($"WARM run (avg):  {warm.ElapsedMilliseconds / (double)warmIterations:N1} ms  (over {warmIterations} iterations)");

            foreach (var (file, line, column) in results.Take(10))
            {
                _output.WriteLine($"  -> {file}({line},{column})");
            }

            // The benchmark asserts nothing about timing; it exists to produce numbers.
            // We only sanity-check that the workspace actually contained code to scan.
            Assert.NotEmpty(files);
        }

        private static Solution BuildSolution(string[] files)
        {
            var projectId = ProjectId.CreateNewId();

            var documents = files.Select(file => DocumentInfo.Create(
                DocumentId.CreateNewId(projectId),
                name: Path.GetFileName(file),
                filePath: file,
                loader: TextLoader.From(
                    TextAndVersion.Create(
                        SourceText.From(File.ReadAllText(file)),
                        VersionStamp.Default,
                        file))));

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

        private static bool IsInBuildOutput(string path)
        {
            return path.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
