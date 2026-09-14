using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Domain.Observability;

namespace PipelineTemplate.Application.Composition;

/// <summary>
/// A composed, runnable pipeline produced by <see cref="PipelineBuilder{TIn, TCurrent}.Build"/>.
/// </summary>
/// <typeparam name="TIn">The type the pipeline accepts as input.</typeparam>
/// <typeparam name="TOut">The type the pipeline produces as output.</typeparam>
public sealed class Pipeline<TIn, TOut>
{
    private readonly Func<IAsyncEnumerable<TIn>, CancellationToken, IAsyncEnumerable<TOut>> _compose;

    internal Pipeline(Func<IAsyncEnumerable<TIn>, CancellationToken, IAsyncEnumerable<TOut>> compose)
    {
        _compose = compose;
    }

    /// <summary>Runs the pipeline over the given input, producing the resulting output stream.</summary>
    public IAsyncEnumerable<TOut> RunAsync(IAsyncEnumerable<TIn> input, CancellationToken cancellationToken = default)
        => _compose(input, cancellationToken);
}

/// <summary>
/// Entry point for composing a new pipeline. See ADR-0007 for the composition-style
/// decision and ADR-0006 for the default error-policy model.
/// </summary>
public static class Pipeline
{
    /// <summary>
    /// Starts building a new pipeline accepting <typeparamref name="TIn"/> as input.
    /// </summary>
    /// <param name="defaultErrorPolicy">
    /// The pipeline-wide default error policy, overridable per filter via
    /// <see cref="PipelineBuilder{TIn, TCurrent}.AddFilter{TNext}(Domain.Filters.IFilter{TCurrent, TNext}, IErrorPolicy?, string?)"/>.
    /// Defaults to <see cref="FailFastPolicy"/> if not supplied.
    /// </param>
    /// <param name="observer">
    /// The observer to report per-filter telemetry to. Defaults to
    /// <see cref="Observability.NullPipelineObserver"/> (no-op) if not supplied.
    /// </param>
    public static PipelineBuilder<TIn, TIn> Create<TIn>(
        IErrorPolicy? defaultErrorPolicy = null,
        IPipelineObserver? observer = null)
        => PipelineBuilder<TIn, TIn>.Start(
            defaultErrorPolicy ?? FailFastPolicy.Instance,
            observer ?? Observability.NullPipelineObserver.Instance);
}
