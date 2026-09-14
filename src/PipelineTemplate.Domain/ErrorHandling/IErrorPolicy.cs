namespace PipelineTemplate.Domain.ErrorHandling;

/// <summary>
/// Decides how a pipeline stage responds when a filter faults while processing an
/// item. Modeled as an interface (not a closed enum) so additional policies — a
/// retry-then-skip policy, for instance — can be added later without changing the
/// core. See ADR-0006 for the full design rationale, including why this is a
/// pipeline-level default with an optional per-filter override rather than a single
/// pipeline-wide setting.
/// </summary>
public interface IErrorPolicy
{
    /// <summary>
    /// Decides whether the pipeline should continue processing after a fault.
    /// </summary>
    /// <param name="exception">The exception the filter raised.</param>
    /// <param name="context">Context about where the fault occurred.</param>
    /// <returns>
    /// <see langword="true"/> if the pipeline should skip the faulted item and
    /// continue; <see langword="false"/> if the pipeline should halt.
    /// </returns>
    /// <remarks>
    /// This method must be a pure decision — any side effect (logging, metrics) is
    /// the pipeline's responsibility via <see cref="Observability.IPipelineObserver"/>,
    /// not the policy's, so that observability happens consistently regardless of
    /// which policy is active.
    /// </remarks>
    bool ShouldContinue(Exception exception, ErrorContext context);
}
