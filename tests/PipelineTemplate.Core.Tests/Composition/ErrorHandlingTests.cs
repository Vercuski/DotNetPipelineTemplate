using PipelineTemplate.Application.Composition;
using PipelineTemplate.Application.Filters;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Domain.Filters;
using PipelineTemplate.Domain.Observability;
using Xunit;

namespace PipelineTemplate.Core.Tests.Composition;

public class ErrorHandlingTests
{
    [Fact]
    public async Task FailFast_Propagates_The_Fault_And_Halts_The_Pipeline()
    {
        var pipeline = Pipeline.Create<int>(defaultErrorPolicy: FailFastPolicy.Instance)
            .AddFilter(x =>
            {
                if (x == 2)
                {
                    throw new InvalidOperationException("boom");
                }

                return x;
            })
            .Build();

        var ex = await Assert.ThrowsAsync<PipelineExecutionException>(
            async () =>
            {
                await foreach (var _ in pipeline.RunAsync(Source(1, 2, 3)))
                {
                    // draining the stream
                }
            });

        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public async Task SkipAndContinue_Isolates_The_Faulted_Item_And_Continues()
    {
        var observer = new RecordingObserver();

        var pipeline = Pipeline.Create<int>(defaultErrorPolicy: SkipAndContinuePolicy.Instance, observer: observer)
            .AddFilter(x =>
            {
                if (x == 2)
                {
                    throw new InvalidOperationException("boom on 2");
                }

                return x * 10;
            })
            .Build();

        var results = new List<int>();
        await foreach (var item in pipeline.RunAsync(Source(1, 2, 3)))
        {
            results.Add(item);
        }

        // Item 2 was skipped; 1 and 3 made it through untouched by the fault.
        Assert.Equal(new[] { 10, 30 }, results);
        Assert.Single(observer.Faults);
        Assert.True(observer.Faults[0].WillContinue);
        Assert.Equal(2, observer.ItemsProcessed);
    }

    [Fact]
    public void SkipAndContinue_On_A_Filter_That_Does_Not_Opt_In_Throws_At_Build_Time()
    {
        // The exception is thrown eagerly when the filter is added, not lazily when
        // the pipeline runs — fail fast on a misconfiguration, don't wait for a
        // confusing runtime failure mid-stream.
        Assert.Throws<NotSupportedException>(() =>
            Pipeline.Create<int>().AddFilter(new NonCooperativeDoublingFilter(), SkipAndContinuePolicy.Instance));
    }

    /// <summary>
    /// A hand-written filter implementing <see cref="IFilter{TIn, TOut}"/> directly,
    /// without going through <see cref="TransformFilter{TIn, TOut}"/> and without
    /// implementing <see cref="ISkipAndContinueCapable"/> — representing a filter
    /// author who has not (or cannot safely) isolate their own per-item faults.
    /// </summary>
    private sealed class NonCooperativeDoublingFilter : IFilter<int, int>
    {
        public async IAsyncEnumerable<int> ProcessAsync(
            IAsyncEnumerable<int> input,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in input.WithCancellation(cancellationToken))
            {
                yield return item * 2;
            }
        }
    }

    private sealed class RecordingObserver : IPipelineObserver
    {
        public int ItemsProcessed { get; private set; }
        public List<(Exception Exception, bool WillContinue)> Faults { get; } = new();

        public void OnItemProcessed(string stageName, TimeSpan duration) => ItemsProcessed++;

        public void OnItemFaulted(string stageName, Exception exception, bool willContinue)
            => Faults.Add((exception, willContinue));
    }

    private static async IAsyncEnumerable<T> Source<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
