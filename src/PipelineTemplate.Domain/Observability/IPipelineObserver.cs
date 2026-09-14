namespace PipelineTemplate.Domain.Observability;

/// <summary>
/// Receives per-filter telemetry as a pipeline runs — throughput, latency, and error
/// rate per stage, per <c>non-functional-requirements.md</c> §6. Implementations are
/// expected to be cheap and non-blocking; the pipeline calls these hooks inline on the
/// processing path.
/// </summary>
/// <remarks>
/// This is a Domain-layer port: the contract every layer agrees on. Concrete adapters
/// belong outward — <c>PipelineTemplate.Application.Observability.NullPipelineObserver</c>
/// is the default, dependency-free no-op; a real backend (OpenTelemetry, Serilog, etc.)
/// would be an Infrastructure-layer adapter, supplied by the consumer via
/// <c>PipelineBuilder.WithObserver(...)</c>.
/// </remarks>
public interface IPipelineObserver
{
    /// <summary>Called after a filter successfully produces an item.</summary>
    void OnItemProcessed(string stageName, TimeSpan duration);

    /// <summary>
    /// Called whenever a filter faults while processing an item, regardless of which
    /// error policy is active — this is what makes
    /// <see cref="ErrorHandling.SkipAndContinuePolicy"/> observable rather than a
    /// silent data sink.
    /// </summary>
    /// <param name="stageName">The display name of the stage that faulted.</param>
    /// <param name="exception">The exception the filter raised.</param>
    /// <param name="willContinue">
    /// Whether the active policy decided the pipeline should continue past this fault.
    /// </param>
    void OnItemFaulted(string stageName, Exception exception, bool willContinue);
}
