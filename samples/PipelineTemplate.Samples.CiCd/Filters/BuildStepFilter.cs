using PipelineTemplate.Application.Filters;
using PipelineTemplate.Samples.CiCd.Model;

namespace PipelineTemplate.Samples.CiCd.Filters;

/// <summary>
/// A single named build step. Real steps (restore/build/test/package) would shell out
/// to <c>dotnet</c>; this sample just records that the step ran, with an optional
/// simulated failure so both error-handling policies can be demonstrated against the
/// same pipeline shape. One reusable class, several stage names — showing that a
/// stage's identity (for error messages and observability) comes from how it's
/// configured, not from writing a new class per step.
/// </summary>
public sealed class BuildStepFilter : TransformFilter<BuildContext, BuildContext>
{
    private readonly bool _simulateFailure;

    public BuildStepFilter(string stepName, bool simulateFailure = false)
    {
        StepName = stepName;
        _simulateFailure = simulateFailure;
    }

    /// <summary>
    /// This step's name — reused as the pipeline stage name when composing (see
    /// <see cref="CiCdPipelineFactory"/>), so the same reusable filter class still
    /// shows up distinctly per step in exception messages and observability.
    /// </summary>
    public string StepName { get; }

    protected override Task<BuildContext> TransformAsync(BuildContext item, CancellationToken cancellationToken)
    {
        if (_simulateFailure)
        {
            throw new InvalidOperationException($"Step '{StepName}' failed (simulated).");
        }

        return Task.FromResult(item.WithStepCompleted(StepName));
    }
}
