using PipelineTemplate.Application.Filters;
using PipelineTemplate.Samples.CiCd.Model;

namespace PipelineTemplate.Samples.CiCd.Filters;

/// <summary>
/// A single named check (unit tests, lint, security scan, ...) run as one branch of
/// the fan-out stage in <see cref="CiCdPipelineFactory"/>. Real checks would run the
/// actual tool; this sample just reports pass/fail, with an optional simulated
/// failure so the fan-out/fan-in "all must pass before deploy" behavior (ADR-0004's
/// own example, implemented via <c>FanOutFilter</c> per ADR-0009) can be demonstrated.
/// </summary>
public sealed class ParallelCheckFilter : TransformFilter<BuildContext, CheckResult>
{
    private readonly string _checkName;
    private readonly bool _simulateFailure;

    public ParallelCheckFilter(string checkName, bool simulateFailure = false)
    {
        _checkName = checkName;
        _simulateFailure = simulateFailure;
    }

    protected override Task<CheckResult> TransformAsync(BuildContext item, CancellationToken cancellationToken)
        => Task.FromResult(new CheckResult(_checkName, Passed: !_simulateFailure));
}
