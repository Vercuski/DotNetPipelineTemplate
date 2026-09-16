using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Samples.CiCd.Filters;
using PipelineTemplate.Samples.CiCd.Model;

var solution = new BuildContext("MyPipeline.sln", CompletedSteps: []);

Console.WriteLine("=== Scenario 1: healthy build, flaky notification service ===");
var healthyBuildPipeline = CiCdPipelineFactory.Create(simulateNotifyFailure: true);
await foreach (var result in healthyBuildPipeline.RunAsync(Source(solution)))
{
    // Won't actually print — Notify's failure means this item is skipped, not
    // produced. See the comment below for why that's still the correct outcome.
    Console.WriteLine($"Pipeline completed all steps: {string.Join(", ", result.CompletedSteps)}");
}
Console.WriteLine(
    "Pipeline finished without throwing — Restore/Build/ParallelChecks/Package all " +
    "ran for real; only the Notify step's failure was swallowed (SkipAndContinue), " +
    "matching how a real CI system treats a failed Slack webhook as a warning, not a " +
    "build failure. No item is printed above because this simple sample only " +
    "surfaces the final BuildContext, and that one build's context was 'skipped' at " +
    "the last stage.");

Console.WriteLine();
Console.WriteLine("=== Scenario 2: one of three parallel checks fails (fan-out/fan-in, ADR-0009) ===");
var brokenChecksPipeline = CiCdPipelineFactory.Create(simulateChecksFailure: true);
try
{
    await foreach (var _ in brokenChecksPipeline.RunAsync(Source(solution)))
    {
        // Never reached — UnitTests/Lint/SecurityScan run concurrently via
        // FanOutFilter; the merge function throws because not all three passed,
        // and FailFast halts the pipeline before Package or Notify run.
    }
}
catch (PipelineExecutionException ex)
{
    Console.WriteLine($"Pipeline halted as expected: {ex.Message}");
}

static async IAsyncEnumerable<T> Source<T>(T item)
{
    await Task.Yield();
    yield return item;
}
