using BenchmarkDotNet.Attributes;
using PipelineTemplate.Application.Composition;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Samples.Telemetry.Filters;

namespace PipelineTemplate.Benchmarks;

/// <summary>
/// Sustained-throughput benchmark against the real Telemetry sample's filters (not a
/// reimplementation), per <c>non-functional-requirements.md</c> §1's requirement to
/// validate against "a stream-processing-style sample pipeline" — the most demanding
/// of the five target domains for latency/throughput.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class TelemetryThroughputBenchmarks
{
    private const int ItemCount = 100_000;
    private string[] _lines = [];

    [GlobalSetup]
    public void Setup()
    {
        // ~2% malformed, matching the noisy-input shape the sample itself is built to
        // demonstrate tolerating — SkipAndContinue's cost is part of what's measured.
        _lines = new string[ItemCount];
        var random = new Random(Seed: 42);
        string[] services = ["checkout-api", "search-api", "inventory-api"];

        for (var i = 0; i < ItemCount; i++)
        {
            if (i % 50 == 0)
            {
                _lines[i] = "malformed,line"; // wrong field count — triggers SkipAndContinue
            }
            else
            {
                var service = services[i % services.Length];
                var severity = i % 20 == 0 ? "error" : "info";
                var value = random.NextDouble() * 500;
                _lines[i] = $"{service},{severity},{value:F2}";
            }
        }
    }

    [Benchmark(Description = "Parse (SkipAndContinue) + many:1 window, 100k lines")]
    public async Task<int> ParseAndWindow()
    {
        var pipeline = Pipeline.Create<string>()
            .AddFilter(new ParseTelemetryLineFilter(), errorPolicy: SkipAndContinuePolicy.Instance)
            .AddFilter(new WindowAggregationFilter(windowSize: 10))
            .Build();

        var count = 0;
        await foreach (var _ in pipeline.RunAsync(Source(_lines)))
        {
            count++;
        }

        return count;
    }

    private static async IAsyncEnumerable<string> Source(string[] lines)
    {
        await Task.CompletedTask;
        foreach (var line in lines)
        {
            yield return line;
        }
    }
}
