using System;
using System.IO;
using Xunit;

namespace Konseben.HandlerFinder.Tests
{
    /// <summary>
    /// Marks a manual, opt-in benchmark test. xUnit has no [Explicit], so instead this
    /// [Fact] skips itself unless it is explicitly enabled. This keeps a normal
    /// "dotnet test" fast and green on every machine, including dev boxes that happen to
    /// have the benchmark solution checked out.
    ///
    /// To run it, set the opt-in flag (any non-empty value) and execute, e.g.:
    ///   $env:HANDLERFINDER_RUN_BENCHMARK = "1"
    ///   dotnet test --filter Category=Benchmark -l "console;verbosity=detailed"
    ///
    /// The solution root defaults to <see cref="DefaultBenchmarkDirectory"/> and can be
    /// overridden with HANDLERFINDER_BENCHMARK_DIR to point at any large solution.
    /// </summary>
    public sealed class SolutionBenchmarkFactAttribute : FactAttribute
    {
        public const string DefaultBenchmarkDirectory = @"C:\dev\CentralHub\netgo.centralhub.Monorepo";
        public const string DirectoryEnvironmentVariable = "HANDLERFINDER_BENCHMARK_DIR";
        public const string EnableEnvironmentVariable = "HANDLERFINDER_RUN_BENCHMARK";

        public SolutionBenchmarkFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnableEnvironmentVariable)))
            {
                Skip = $"Manual benchmark. Set {EnableEnvironmentVariable}=1 to enable.";
                return;
            }

            string directory = BenchmarkDirectory;

            if (!Directory.Exists(directory))
            {
                Skip = $"Benchmark directory not found: '{directory}'. " +
                       $"Set {DirectoryEnvironmentVariable} to a large solution's root to run this benchmark.";
            }
        }

        public static string BenchmarkDirectory =>
            Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable) ?? DefaultBenchmarkDirectory;
    }
}
