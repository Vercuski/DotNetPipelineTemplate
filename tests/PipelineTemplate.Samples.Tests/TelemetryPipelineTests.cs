using PipelineTemplate.Application.Composition;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Samples.Telemetry.Filters;
using Xunit;

namespace PipelineTemplate.Samples.Tests;

public class TelemetryPipelineTests
{
    [Fact]
    public async Task Malformed_Lines_Are_Skipped_Without_Breaking_The_Pipeline()
    {
        string[] lines =
        [
            "svc-a,info,10",
            "this-line-has-too-many,comma,separated,fields",
            "svc-a,info,20",
            "svc-a,not-a-number,value-should-be-numeric",
            "svc-a,error,30", // completes the first window of 3 valid svc-a events
        ];

        var pipeline = Pipeline.Create<string>()
            .AddFilter(new ParseTelemetryLineFilter(), errorPolicy: SkipAndContinuePolicy.Instance)
            .AddFilter(new WindowAggregationFilter(windowSize: 3))
            .Build();

        var results = new List<Samples.Telemetry.Model.AggregatedMetric>();
        await foreach (var metric in pipeline.RunAsync(Source(lines)))
        {
            results.Add(metric);
        }

        // 2 malformed lines were skipped; the 3 valid svc-a events still formed one
        // complete window, proving a bad line doesn't corrupt or halt aggregation of
        // the good ones around it.
        var aggregate = Assert.Single(results);
        Assert.Equal("svc-a", aggregate.ServiceName);
        Assert.Equal(3, aggregate.EventCount);
        Assert.Equal(1, aggregate.ErrorCount);
        Assert.Equal(20, aggregate.AverageValue, precision: 5); // (10+20+30)/3
        Assert.Equal(30, aggregate.MaxValue, precision: 5);
    }

    [Fact]
    public async Task Windowing_Groups_Independently_Per_Service()
    {
        // Interleaved services must not bleed into each other's windows — a realistic
        // multi-tenant telemetry requirement.
        string[] lines =
        [
            "svc-a,info,10",
            "svc-b,info,100",
            "svc-a,info,20",
            "svc-b,info,200",
            "svc-a,info,30", // closes svc-a's window
            "svc-b,info,300", // closes svc-b's window
        ];

        var pipeline = Pipeline.Create<string>()
            .AddFilter(new ParseTelemetryLineFilter())
            .AddFilter(new WindowAggregationFilter(windowSize: 3))
            .Build();

        var results = new List<Samples.Telemetry.Model.AggregatedMetric>();
        await foreach (var metric in pipeline.RunAsync(Source(lines)))
        {
            results.Add(metric);
        }

        Assert.Equal(2, results.Count);
        Assert.Contains(results, m => m.ServiceName == "svc-a" && m.AverageValue == 20);
        Assert.Contains(results, m => m.ServiceName == "svc-b" && m.AverageValue == 200);
    }

    [Fact]
    public async Task Without_SkipAndContinue_A_Malformed_Line_Halts_The_Pipeline()
    {
        // The counterpart to the first test: proves the noisy-input tolerance is a
        // deliberate policy choice, not an accident of the filter's own design — the
        // default FailFast policy still halts on the exact same bad input.
        string[] lines = ["svc-a,info,10", "malformed"];

        var pipeline = Pipeline.Create<string>()
            .AddFilter(new ParseTelemetryLineFilter()) // no override — default FailFast applies
            .AddFilter(new WindowAggregationFilter(windowSize: 3))
            .Build();

        await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
        {
            await foreach (var _ in pipeline.RunAsync(Source(lines)))
            {
                // draining
            }
        });
    }

    private static async IAsyncEnumerable<T> Source<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
