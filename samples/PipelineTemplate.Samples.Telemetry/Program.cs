using PipelineTemplate.Application.Composition;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Samples.Telemetry.Filters;

// Deliberately noisy input: real telemetry ingestion sees malformed lines regularly.
// Lines 3 and 6 are broken on purpose to prove SkipAndContinue keeps the pipeline
// running instead of dying on the first bad line.
string[] rawLines =
[
    "checkout-api,info,120",
    "checkout-api,info,95",
    "checkout-api,error,340",   // window of 3 for checkout-api closes here
    "not,enough,fields,here",   // malformed — wrong field count
    "search-api,info,12",
    "search-api,not-a-number",  // malformed — non-numeric value AND wrong field count
    "search-api,info,18",
    "search-api,error,44",      // window of 3 for search-api closes here
];

var pipeline = Pipeline.Create<string>()
    .AddFilter(new ParseTelemetryLineFilter(), errorPolicy: SkipAndContinuePolicy.Instance)
    .AddFilter(new WindowAggregationFilter(windowSize: 3))
    .Build();

await foreach (var metric in pipeline.RunAsync(Source(rawLines)))
{
    Console.WriteLine(
        $"{metric.ServiceName}: {metric.EventCount} events, " +
        $"avg={metric.AverageValue:F1}, max={metric.MaxValue:F1}, errors={metric.ErrorCount}");
}

static async IAsyncEnumerable<string> Source(IEnumerable<string> lines)
{
    foreach (var line in lines)
    {
        await Task.Yield();
        yield return line;
    }
}
