using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Domain.Observability;
using PipelineTemplate.Samples.CiCd.Filters;
using PipelineTemplate.Samples.CiCd.Model;
using Xunit;

namespace PipelineTemplate.Samples.Tests;

/// <summary>
/// This class exists specifically to prove two success criteria from
/// architecture-vision.md §6: "a sample pipeline demonstrates both a fail-fast stage
/// and a skip-and-continue stage coexisting in the same pipeline," and — since
/// ADR-0009 — that fan-out/fan-in (ParallelChecks) coexists with both of those in the
/// same pipeline too. All tests build the pipeline from
/// <see cref="CiCdPipelineFactory"/> — the exact same factory, only the
/// simulated-failure flags differ.
/// </summary>
public class CiCdPipelineTests
{
    [Fact]
    public async Task FailFast_Checks_Stage_Halts_The_Pipeline_Before_Later_Steps_Run()
    {
        var observer = new RecordingObserver();
        var pipeline = CiCdPipelineFactory.Create(simulateChecksFailure: true, observer: observer);
        var solution = new BuildContext("MyPipeline.sln", CompletedSteps: []);

        var ex = await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
        {
            await foreach (var _ in pipeline.RunAsync(Source(solution)))
            {
                // draining
            }
        });

        // Restore and Build completed; the fan-out stage (one failed branch out of
        // three) faulted as a whole and halted everything after it. Package/Notify
        // never ran.
        Assert.Equal(["Restore", "Build"], observer.ProcessedStages);
        Assert.Equal(["ParallelChecks"], observer.FaultedStages);
        Assert.Contains("UnitTests", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Fan_Out_Fan_In_Succeeds_When_All_Parallel_Checks_Pass()
    {
        var observer = new RecordingObserver();
        var pipeline = CiCdPipelineFactory.Create(observer: observer); // no simulated failures at all
        var solution = new BuildContext("MyPipeline.sln", CompletedSteps: []);

        var results = new List<BuildContext>();
        await foreach (var result in pipeline.RunAsync(Source(solution)))
        {
            results.Add(result);
        }

        // Every stage — including the fan-out/fan-in one — ran and produced output;
        // nothing faulted anywhere in this run.
        Assert.Equal(["Restore", "Build", "ParallelChecks", "Package", "Notify"], observer.ProcessedStages);
        Assert.Empty(observer.FaultedStages);

        var finalResult = Assert.Single(results);
        Assert.Equal(
            ["Restore", "Build", "ParallelChecks", "Package", "Notify"],
            finalResult.CompletedSteps);
    }

    [Fact]
    public async Task SkipAndContinue_Override_Lets_The_Pipeline_Finish_Despite_A_Failed_Notify_Step()
    {
        var observer = new RecordingObserver();
        var pipeline = CiCdPipelineFactory.Create(simulateNotifyFailure: true, observer: observer);
        var solution = new BuildContext("MyPipeline.sln", CompletedSteps: []);

        // The key assertion: this does NOT throw, unlike the FailFast case above,
        // even though a step genuinely failed.
        var results = new List<BuildContext>();
        await foreach (var result in pipeline.RunAsync(Source(solution)))
        {
            results.Add(result);
        }

        // Restore, Build, ParallelChecks, and Package all genuinely ran — real work
        // happened — before Notify's isolated failure. The item itself is dropped at
        // the Notify stage (a 1:1 transform that faults has no output for that
        // item), which is why `results` is empty; that's the honest limitation noted
        // in Program.cs, not a bug in this test.
        Assert.Equal(["Restore", "Build", "ParallelChecks", "Package"], observer.ProcessedStages);
        Assert.Equal(["Notify"], observer.FaultedStages);
        Assert.Empty(results);
    }

    private static async IAsyncEnumerable<T> Source<T>(T item)
    {
        await Task.Yield();
        yield return item;
    }

    private sealed class RecordingObserver : IPipelineObserver
    {
        public List<string> ProcessedStages { get; } = new();
        public List<string> FaultedStages { get; } = new();

        public void OnItemProcessed(string stageName, TimeSpan duration) => ProcessedStages.Add(stageName);

        public void OnItemFaulted(string stageName, Exception exception, bool willContinue)
            => FaultedStages.Add(stageName);
    }
}
