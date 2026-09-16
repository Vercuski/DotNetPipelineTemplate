using PipelineTemplate.Application.Composition;
using PipelineTemplate.Application.Filters;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Samples.CiCd.Model;

namespace PipelineTemplate.Samples.CiCd.Filters;

/// <summary>
/// Builds the sample build pipeline: Restore → Build → ParallelChecks (fan-out to
/// UnitTests/Lint/SecurityScan, fan-in via "all must pass" — ADR-0004's own example,
/// implemented as <c>FanOutFilter</c> per ADR-0009) → Package → Notify. Restore,
/// Build, and ParallelChecks are all <c>FailFast</c> (the pipeline-wide default — a
/// real build shouldn't continue past a broken step or a failed check); Notify is
/// explicitly overridden to <c>SkipAndContinue</c> (a flaky notification channel
/// shouldn't fail an otherwise-good build). This mix — fan-out/fan-in coexisting
/// with layered fail-fast/skip-and-continue policies in one pipeline — is exactly
/// the scenario the architecture vision's success criteria call for.
/// </summary>
public static class CiCdPipelineFactory
{
    public static Pipeline<BuildContext, BuildContext> Create(
        bool simulateChecksFailure = false,
        bool simulateNotifyFailure = false,
        Domain.Observability.IPipelineObserver? observer = null)
    {
        var restore = new BuildStepFilter("Restore");
        var build = new BuildStepFilter("Build");
        var unitTests = new ParallelCheckFilter("UnitTests", simulateFailure: simulateChecksFailure);
        var lint = new ParallelCheckFilter("Lint");
        var securityScan = new ParallelCheckFilter("SecurityScan");
        var package = new BuildStepFilter("Package");
        var notify = new BuildStepFilter("Notify", simulateFailure: simulateNotifyFailure);

        var parallelChecks = new FanOutFilter<BuildContext, CheckResult, BuildContext>(
            branches: [unitTests, lint, securityScan],
            merge: (context, results) =>
            {
                var failed = results.Where(r => !r.Passed).Select(r => r.CheckName).ToList();
                return failed.Count == 0
                    ? context.WithStepCompleted("ParallelChecks")
                    : throw new InvalidOperationException($"Checks failed: {string.Join(", ", failed)}");
            });

        return Pipeline.Create<BuildContext>(defaultErrorPolicy: FailFastPolicy.Instance, observer: observer)
            .AddFilter(restore, stageName: restore.StepName)
            .AddFilter(build, stageName: build.StepName)
            .AddFilter(parallelChecks, stageName: "ParallelChecks")
            .AddFilter(package, stageName: package.StepName)
            .AddFilter(notify, errorPolicy: SkipAndContinuePolicy.Instance, stageName: notify.StepName)
            .Build();
    }
}
