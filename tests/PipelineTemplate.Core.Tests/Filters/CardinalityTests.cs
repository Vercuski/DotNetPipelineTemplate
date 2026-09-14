using System.Runtime.CompilerServices;
using PipelineTemplate.Application.Composition;
using PipelineTemplate.Domain.Filters;
using Xunit;

namespace PipelineTemplate.Core.Tests.Filters;

/// <summary>
/// These tests exist to validate the central claim behind choosing a streaming
/// (<c>IAsyncEnumerable&lt;T&gt;</c>) filter contract in ADR-0005: that 1:1, 1:many, and
/// many:1 filters all compose through the exact same <see cref="IFilter{TIn, TOut}"/>
/// contract and the exact same <c>PipelineBuilder.AddFilter</c> method — with zero
/// changes to the core, per NFR §5's extensibility requirement. Both filters below are
/// authored entirely in the test project, touching no Core Library code.
/// </summary>
public class CardinalityTests
{
    [Fact]
    public async Task OneToMany_Filter_Composes_Through_The_Same_AddFilter_Method()
    {
        // A filter that expands each input string into its individual characters —
        // 1 input item yields many output items.
        var pipeline = Pipeline.Create<string>()
            .AddFilter(new ExplodeToCharsFilter())
            .Build();

        var results = new List<char>();
        await foreach (var c in pipeline.RunAsync(Source("ab", "c")))
        {
            results.Add(c);
        }

        Assert.Equal(new[] { 'a', 'b', 'c' }, results);
    }

    [Fact]
    public async Task ManyToOne_Filter_Composes_Through_The_Same_AddFilter_Method()
    {
        // A filter that batches every 3 input items into 1 output item (their sum) —
        // many input items yield 1 output item. This is the shape a windowing/
        // aggregation filter for real-time analytics would take.
        var pipeline = Pipeline.Create<int>()
            .AddFilter(new SumEveryThreeFilter())
            .Build();

        var results = new List<int>();
        await foreach (var sum in pipeline.RunAsync(Source(1, 2, 3, 4, 5, 6, 7)))
        {
            results.Add(sum);
        }

        // (1+2+3)=6, (4+5+6)=15; the trailing "7" never completes a window of 3
        // and is dropped, exactly as a real windowing filter would do at end-of-stream.
        Assert.Equal(new[] { 6, 15 }, results);
    }

    private sealed class ExplodeToCharsFilter : IFilter<string, char>
    {
        public async IAsyncEnumerable<char> ProcessAsync(
            IAsyncEnumerable<string> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var word in input.WithCancellation(cancellationToken))
            {
                foreach (var c in word)
                {
                    yield return c;
                }
            }
        }
    }

    private sealed class SumEveryThreeFilter : IFilter<int, int>
    {
        public async IAsyncEnumerable<int> ProcessAsync(
            IAsyncEnumerable<int> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var window = new List<int>(capacity: 3);

            await foreach (var item in input.WithCancellation(cancellationToken))
            {
                window.Add(item);
                if (window.Count == 3)
                {
                    yield return window.Sum();
                    window.Clear();
                }
            }
        }
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
