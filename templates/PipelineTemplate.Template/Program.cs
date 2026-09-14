using PipelineTemplate.Application.Composition;
#if (IncludeSampleFilter)
using PipelineApp.Filters;
#endif

#if (IncludeSampleFilter)
// A minimal pipeline demonstrating the fluent composition API (ADR-0007) with a
// trivial pass-through filter (PassThroughFilter). Replace this with your own
// filters — telemetry processing, ETL, stream processing, CI/CD, image/video
// processing, or whatever pipeline-shaped problem you're solving.
var pipeline = Pipeline.Create<string>()
    .AddFilter(new PassThroughFilter())
    .Build();

await foreach (var item in pipeline.RunAsync(Source("hello", "pipeline")))
{
    Console.WriteLine(item);
}

static async IAsyncEnumerable<string> Source(params string[] items)
{
    foreach (var item in items)
    {
        await Task.Yield();
        yield return item;
    }
}
#else
// IncludeSampleFilter was set to false — start by composing your own pipeline here:
//
// var pipeline = Pipeline.Create<TIn>()
//     .AddFilter(/* your first filter */)
//     .Build();
//
// await foreach (var item in pipeline.RunAsync(yourInputSource))
// {
//     // ...
// }
Console.WriteLine("Pipeline scaffold ready — add your filters in Program.cs.");
#endif
