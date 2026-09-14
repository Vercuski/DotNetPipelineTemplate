using PipelineTemplate.Application.Composition;
using Xunit;

namespace PipelineTemplate.Core.Tests.Composition;

public class PipelineBuilderTests
{
    [Fact]
    public async Task Pipeline_Composes_Multiple_Filters_In_Order()
    {
        // Arrange: a 3-stage linear pipeline, each stage a simple delegate filter.
        var pipeline = Pipeline.Create<int>()
            .AddFilter(x => x + 1)
            .AddFilter(x => x * 2)
            .AddFilter(x => x.ToString())
            .Build();

        // Act
        var results = await Collect(pipeline.RunAsync(Source(1, 2, 3)));

        // Assert: (1+1)*2="4", (2+1)*2="6", (3+1)*2="8"
        Assert.Equal(new[] { "4", "6", "8" }, results);
    }

    [Fact]
    public async Task Pipeline_Supports_AsyncTransform_Filters()
    {
        var pipeline = Pipeline.Create<int>()
            .AddFilter(async (x, ct) =>
            {
                await Task.Delay(1, ct);
                return x * 10;
            })
            .Build();

        var results = await Collect(pipeline.RunAsync(Source(1, 2, 3)));

        Assert.Equal(new[] { 10, 20, 30 }, results);
    }

    [Fact]
    public async Task Pipeline_With_No_Filters_Passes_Input_Through_Unchanged()
    {
        var pipeline = Pipeline.Create<int>().Build();

        var results = await Collect(pipeline.RunAsync(Source(1, 2, 3)));

        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    private static async IAsyncEnumerable<T> Source<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
