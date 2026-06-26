using BenchmarkDotNet.Running;

namespace DedupSharp.Benchmarks;

// Run all benchmarks:   dotnet run -c Release --project src/DedupSharp.Benchmarks
// Filter:               dotnet run -c Release --project src/DedupSharp.Benchmarks -- --filter *ExactScanMode*
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
