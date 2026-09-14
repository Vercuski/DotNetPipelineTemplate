using PipelineTemplate.Application.Composition;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Samples.CiCd.Model;

namespace PipelineTemplate.Samples.CiCd.Filters;

/// <summary>
/// Builds the sample build pipeline: Restore → Build → Test → Package (all
/// <c>FailFast</c>, the pipeline-wide default — a real build shouldn't continue past
/// a broken step) → Notify (explicitly overridden to <c>SkipAndContinue</c> — a
/// flaky notification channel shouldn't fail an otherwise-good build). This mix in
/// one pipeline is exactly the ADR-0006 layered-policy scenario the architecture
/// vision's success criteria call for.
/// </summary>
public static class CiCdPipelineFactory
{
    public static Pipeline<BuildContext, BuildContext> Create(
        bool simulateTestFailure = false,
        bool simulateNotifyFailure = false,
        Domain.Observability.IPipelineObserver? observer = null)
    {
        var restore = new BuildStepFilter("Restore");
        var build = new BuildStepFilter("Build");
        var test = new BuildStepFilter("Test", simulateFailure: simulateTestFailure);
        var package = new BuildStepFilter("Package");
        var notify = new BuildStepFilter("Notify", simulateFailure: simulateNotifyFailure);

        return Pipeline.Create<BuildContext>(defaultErrorPolicy: FailFastPolicy.Instance, observer: observer)
            .AddFilter(restore, stageName: restore.StepName)
            .AddFilter(build, stageName: build.StepName)
            .AddFilter(test, stageName: test.StepName)
            .AddFilter(package, stageName: package.StepName)
            .AddFilter(notify, errorPolicy: SkipAndContinuePolicy.Instance, stageName: notify.StepName)
            .Build();
    }
}
