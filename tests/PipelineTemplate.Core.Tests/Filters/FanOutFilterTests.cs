using System.Diagnostics;
using PipelineTemplate.Application.Composition;
using PipelineTemplate.Application.Filters;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Domain.Filters;
using Xunit;

namespace PipelineTemplate.Core.Tests.Filters;

/// <summary>
/// Validates <see cref="FanOutFilter{TIn, TBranchOut, TOut}"/> — the Phase 3
/// fan-out/fan-in capability ADR-0004 deferred and ADR-0009 records the
/// implementation of. These tests exist independently of any sample, so the
/// mechanism is proven even if a sample's own tests change.
/// </summary>
public class FanOutFilterTests
{
    [Fact]
    public async Task Merges_Every_Branchs_Result_For_Each_Item()
    {
        var doubleIt = new DelegateTransformFilter<int, int>(x => x * 2);
        var addTen = new DelegateTransformFilter<int, int>(x => x + 10);

        var fanOut = new FanOutFilter<int, int, int>(
            branches: [doubleIt, addTen],
            merge: (_, results) => results.Sum());

        var pipeline = Pipeline.Create<int>().AddFilter(fanOut).Build();

        var results = new List<int>();
        await foreach (var item in pipeline.RunAsync(Source(1, 2, 3)))
        {
            results.Add(item);
        }

        // item 1: double=2, addTen=11, sum=13. item 2: 4+12=16. item 3: 6+13=19.
        Assert.Equal([13, 16, 19], results);
    }

    [Fact]
    public async Task Merge_Function_Receives_The_Original_Input_Item()
    {
        var passThrough1 = new DelegateTransformFilter<string, bool>(_ => true);
        var passThrough2 = new DelegateTransformFilter<string, bool>(_ => true);

        var fanOut = new FanOutFilter<string, bool, string>(
            branches: [passThrough1, passThrough2],
            merge: (original, results) => $"{original}:{results.Count(r => r)}");

        var pipeline = Pipeline.Create<string>().AddFilter(fanOut).Build();

        var results = new List<string>();
        await foreach (var item in pipeline.RunAsync(Source("a", "b")))
        {
            results.Add(item);
        }

        Assert.Equal(["a:2", "b:2"], results);
    }

    [Fact]
    public async Task Requires_At_Least_Two_Branches()
    {
        var onlyOne = new DelegateTransformFilter<int, int>(x => x);

        Assert.Throws<ArgumentException>(() =>
            new FanOutFilter<int, int, int>([onlyOne], (_, results) => results[0]));
    }

    [Fact]
    public async Task A_Branch_That_Yields_The_Wrong_Cardinality_Throws_A_Clear_Error()
    {
        var oneToOne = new DelegateTransformFilter<int, int>(x => x);
        var oneToMany = new ExplodingCardinalityFilter();

        var fanOut = new FanOutFilter<int, int, int>(
            branches: [oneToOne, oneToMany],
            merge: (_, results) => results.Sum());

        var pipeline = Pipeline.Create<int>().AddFilter(fanOut).Build();

        var ex = await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
        {
            await foreach (var _ in pipeline.RunAsync(Source(1)))
            {
                // draining
            }
        });

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("exactly one output", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Merge_Function_Failure_Is_Governed_By_The_Configured_Error_Policy()
    {
        var alwaysTrue = new DelegateTransformFilter<int, bool>(_ => true);
        var alwaysFalse = new DelegateTransformFilter<int, bool>(_ => false);

        var fanOut = new FanOutFilter<int, bool, int>(
            branches: [alwaysTrue, alwaysFalse],
            merge: (item, results) => results.All(r => r)
                ? item
                : throw new InvalidOperationException("not all checks passed"));

        // FailFast (the default): the merge exception halts the pipeline.
        var failFastPipeline = Pipeline.Create<int>().AddFilter(fanOut).Build();
        await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
        {
            await foreach (var _ in failFastPipeline.RunAsync(Source(1)))
            {
            }
        });

        // SkipAndContinue: the same failure is isolated to that item instead.
        var skipFanOut = new FanOutFilter<int, bool, int>(
            branches: [alwaysTrue, alwaysFalse],
            merge: (item, results) => results.All(r => r)
                ? item
                : throw new InvalidOperationException("not all checks passed"));
        var skipPipeline = Pipeline.Create<int>(defaultErrorPolicy: SkipAndContinuePolicy.Instance)
            .AddFilter(skipFanOut)
            .Build();

        var results = new List<int>();
        await foreach (var item in skipPipeline.RunAsync(Source(1, 2)))
        {
            results.Add(item);
        }

        Assert.Empty(results); // both items fail the "all checks pass" merge, both skipped
    }

    [Fact]
    public async Task Branches_Run_Concurrently_Not_Sequentially()
    {
        // Two branches that each "take" 150ms. If they ran sequentially, one item
        // would take >= 300ms; if concurrent, it should take close to 150ms.
        var slowA = new DelegateTransformFilter<int, int>(async (x, ct) =>
        {
            await Task.Delay(150, ct);
            return x;
        });
        var slowB = new DelegateTransformFilter<int, int>(async (x, ct) =>
        {
            await Task.Delay(150, ct);
            return x;
        });

        var fanOut = new FanOutFilter<int, int, int>(
            branches: [slowA, slowB],
            merge: (_, results) => results.Sum());

        var pipeline = Pipeline.Create<int>().AddFilter(fanOut).Build();

        var stopwatch = Stopwatch.StartNew();
        await foreach (var _ in pipeline.RunAsync(Source(1)))
        {
        }

        stopwatch.Stop();

        // Generous upper bound to avoid CI flakiness, but well under the ~300ms a
        // sequential implementation would take.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 250,
            $"Expected concurrent branches to finish in well under 300ms; took {stopwatch.ElapsedMilliseconds}ms.");
    }

    private sealed class ExplodingCardinalityFilter : IFilter<int, int>
    {
        public async IAsyncEnumerable<int> ProcessAsync(
            IAsyncEnumerable<int> input,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in input.WithCancellation(cancellationToken))
            {
                yield return item;
                yield return item; // deliberately wrong — 2 outputs for 1 input
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
