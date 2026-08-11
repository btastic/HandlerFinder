using BenchmarkDotNet.Running;

namespace Konseben.HandlerFinder.Benchmarks
{
    public static class Program
    {
        // Run everything:      dotnet build -c Release && bin\Release\net48\Konseben.HandlerFinder.Benchmarks.exe
        // Filter, e.g. warm:   ...Benchmarks.exe --filter *Warm*
        // Point at a solution: set HANDLERFINDER_BENCHMARK_DIR (and optionally HANDLERFINDER_BENCHMARK_REQUEST)
        public static void Main(string[] args) =>
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
