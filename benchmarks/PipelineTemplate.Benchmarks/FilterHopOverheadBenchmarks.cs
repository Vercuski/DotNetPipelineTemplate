using BenchmarkDotNet.Attributes;
using PipelineTemplate.Application.Composition;
using PipelineTemplate.Domain.Observability;

namespace PipelineTemplate.Benchmarks;

/// <summary>
/// Isolates the pipeline framework's own per-item overhead from the cost of async
/// iteration itself and from the cost of the transform work a filter does. Answers
/// the question <c>non-functional-requirements.md</c> §1 leaves as TBD: how much does
/// composing filters through <c>PipelineBuilder</c>/<c>TransformFilter</c> cost, on
/// top of what a bare <c>IAsyncEnumerable&lt;T&gt;</c> loop already costs.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class FilterHopOverheadBenchmarks
{
    private const int ItemCount = 100_000;

    [Benchmark(Baseline = true, Description = "Raw sync loop — no async, no framework")]
    public long RawSyncLoop()
    {
        long sum = 0;
        for (var i = 0; i < ItemCount; i++)
        {
            sum += Transform(i);
        }

        return sum;
    }

    [Benchmark(Description = "Raw IAsyncEnumerable — async iteration, no framework")]
    public async Task<long> RawAsyncEnumerable()
    {
        long sum = 0;
        await foreach (var item in Source())
        {
            sum += Transform(item);
        }

        return sum;
    }

    [Benchmark(Description = "1-stage pipeline — PipelineBuilder + TransformFilter")]
    public async Task<long> OneStagePipeline()
    {
        var pipeline = Pipeline.Create<int>()
            .AddFilter(Transform)
            .Build();

        long sum = 0;
        await foreach (var item in pipeline.RunAsync(Source()))
        {
            sum += item;
        }

        return sum;
    }

    [Benchmark(Description = "5-stage pipeline — same transform chained 5x")]
    public async Task<long> FiveStagePipeline()
    {
        var pipeline = Pipeline.Create<int>()
            .AddFilter(Transform)
            .AddFilter(Transform)
            .AddFilter(Transform)
            .AddFilter(Transform)
            .AddFilter(Transform)
            .Build();

        long sum = 0;
        await foreach (var item in pipeline.RunAsync(Source()))
        {
            sum += item;
        }

        return sum;
    }

    [Benchmark(Description = "5-stage pipeline — with a real (non-null) observer attached")]
    public async Task<long> FiveStagePipelineWithObserver()
    {
        var observer = new CountingObserver();
        var pipeline = Pipeline.Create<int>(observer: observer)
            .AddFilter(Transform)
            .AddFilter(Transform)
            .AddFilter(Transform)
            .AddFilter(Transform)
            .AddFilter(Transform)
            .Build();

        long sum = 0;
        await foreach (var item in pipeline.RunAsync(Source()))
        {
            sum += item;
        }

        return sum + observer.Processed;
    }

    private static int Transform(int x) => x + 1;

    private static async IAsyncEnumerable<int> Source()
    {
        await Task.CompletedTask; // satisfies the compiler's "async method needs an await"
                                  // once, up front — not per item, so it doesn't distort
                                  // the per-item cost this benchmark is trying to isolate.
        for (var i = 0; i < ItemCount; i++)
        {
            yield return i;
        }
    }

    private sealed class CountingObserver : IPipelineObserver
    {
        public long Processed;

        public void OnItemProcessed(string stageName, TimeSpan duration) => Processed++;

        public void OnItemFaulted(string stageName, Exception exception, bool willContinue)
        {
        }
    }
}
