using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Domain.Observability;

namespace PipelineTemplate.Domain.Filters;

/// <summary>
/// Implemented by filters that isolate their own per-item faults internally and can
/// therefore safely support the <c>SkipAndContinuePolicy</c> error policy.
/// </summary>
/// <remarks>
/// <para>
/// <c>PipelineTemplate.Application.Filters.TransformFilter&lt;TIn, TOut&gt;</c>
/// implements this automatically for the common one-in-one-out case. A hand-written
/// filter that produces or consumes multiple items per logical unit of work
/// (windowing, aggregation, fan-out) can implement this too, but only if it genuinely
/// catches and isolates faults around its own per-item work — see the fault-isolation
/// contract documented on <see cref="IFilter{TIn, TOut}"/>.
/// </para>
/// <para>
/// A filter that does <b>not</b> implement this interface can still be used in a
/// pipeline, but only under a <c>FailFastPolicy</c> — the pipeline builder rejects an
/// attempt to apply <c>SkipAndContinuePolicy</c> to a filter that hasn't opted in,
/// rather than risking undefined behavior from resuming a foreign enumerator after a
/// fault.
/// </para>
/// </remarks>
public interface ISkipAndContinueCapable
{
    /// <summary>
    /// Called by the pipeline builder when this filter is added to a pipeline, supplying
    /// the resolved error policy, observer, and a display name for observability and
    /// exception messages.
    /// </summary>
    void Configure(IErrorPolicy policy, IPipelineObserver observer, string stageName);
}
