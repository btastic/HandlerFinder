using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.CodeAnalysis;

namespace Konseben.HandlerFinder.Benchmarks
{
    /// <summary>
    /// Steady-state cost of a search when the syntax trees are already parsed and cached
    /// (a repeat "Find handler" within the same VS session). The workspace is built and
    /// primed once; every measured invocation reuses the cached trees.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class WarmSearchBenchmark
    {
        private readonly SolutionSource _source = new SolutionSource();
        private Solution _solution;

        [GlobalSetup]
        public void Setup()
        {
            _source.Load();
            _solution = _source.BuildSolution();

            // Prime: force every tree to parse once so the measured runs are truly warm.
            var primed = HandlerSearch.FindHandlersAsync(_solution, _source.Request).GetAwaiter().GetResult();
            System.Console.Error.WriteLine(
                $"[warm] root='{_source.Root}' request='{_source.Request}' docs={_source.DocumentCount} matches={primed.Count}");
        }

        [Benchmark]
        public async Task<int> FindHandlers_Warm()
        {
            var results = await HandlerSearch.FindHandlersAsync(_solution, _source.Request);
            return results.Count;
        }
    }

    /// <summary>
    /// Cold cost of the first search in a session: every document is parsed from scratch.
    /// A one-shot operation, so this uses RunStrategy.Monitoring with a single invocation
    /// per iteration and a fresh (unparsed) workspace rebuilt in [IterationSetup].
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 10, invocationCount: 1)]
    public class ColdSearchBenchmark
    {
        private readonly SolutionSource _source = new SolutionSource();
        private Solution _solution;

        [GlobalSetup]
        public void Setup()
        {
            _source.Load();
        }

        [IterationSetup]
        public void RebuildWorkspace()
        {
            // Fresh workspace => new syntax trees => the search below pays the full parse cost.
            // This runs between measured iterations and is excluded from the timing.
            _solution = _source.BuildSolution();
        }

        [Benchmark]
        public async Task<int> FindHandlers_Cold()
        {
            var results = await HandlerSearch.FindHandlersAsync(_solution, _source.Request);
            return results.Count;
        }
    }
}
