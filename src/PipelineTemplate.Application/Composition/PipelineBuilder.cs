using PipelineTemplate.Application.Filters;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Domain.Filters;
using PipelineTemplate.Domain.Observability;

namespace PipelineTemplate.Application.Composition;

/// <summary>
/// A strongly-typed, immutable fluent builder for composing a linear chain of filters
/// into a runnable <see cref="Pipeline{TIn, TOut}"/>. See ADR-0007 for why this is
/// code-first rather than declarative/config-driven, and ADR-0004 for why v1
/// composition is linear only.
/// </summary>
/// <remarks>
/// Each call to <see cref="AddFilter{TNext}(IFilter{TCurrent, TNext}, IErrorPolicy?, string?)"/>
/// returns a <em>new</em> builder rather than mutating this one, so a partially-built
/// pipeline can be safely reused as a starting point for more than one variant. This
/// is Application-layer orchestration: it composes Domain-layer <see cref="IFilter{TIn, TOut}"/>
/// and <see cref="IErrorPolicy"/> contracts into a runnable use case, but defines none
/// of the pattern's core rules itself.
/// </remarks>
/// <typeparam name="TIn">The type the overall pipeline accepts as input.</typeparam>
/// <typeparam name="TCurrent">
/// The type the pipeline currently produces — i.e. what the next
/// <see cref="AddFilter{TNext}(IFilter{TCurrent, TNext}, IErrorPolicy?, string?)"/> call must
/// accept.
/// </typeparam>
public sealed class PipelineBuilder<TIn, TCurrent>
{
    private readonly Func<IAsyncEnumerable<TIn>, CancellationToken, IAsyncEnumerable<TCurrent>> _compose;
    private readonly IErrorPolicy _defaultPolicy;
    private readonly IPipelineObserver _observer;

    private PipelineBuilder(
        Func<IAsyncEnumerable<TIn>, CancellationToken, IAsyncEnumerable<TCurrent>> compose,
        IErrorPolicy defaultPolicy,
        IPipelineObserver observer)
    {
        _compose = compose;
        _defaultPolicy = defaultPolicy;
        _observer = observer;
    }

    internal static PipelineBuilder<TIn, TIn> Start(IErrorPolicy defaultPolicy, IPipelineObserver observer)
        => new((input, _) => input, defaultPolicy, observer);

    /// <summary>
    /// Adds a filter to the pipeline. The pipeline's default error policy applies
    /// unless <paramref name="errorPolicy"/> overrides it for this stage — see
    /// ADR-0006. The stage's display name (used in exception messages and reported to
    /// the observer) defaults to the filter's type name; supply
    /// <paramref name="stageName"/> to override it — useful when the same filter type
    /// is reused for several differently-named stages in one pipeline.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The resolved policy is <see cref="SkipAndContinuePolicy"/> and
    /// <paramref name="filter"/> does not implement
    /// <see cref="ISkipAndContinueCapable"/> — see the fault-isolation contract on
    /// <see cref="IFilter{TIn, TOut}"/> for why this can't be done safely for an
    /// arbitrary filter.
    /// </exception>
    public PipelineBuilder<TIn, TNext> AddFilter<TNext>(
        IFilter<TCurrent, TNext> filter,
        IErrorPolicy? errorPolicy = null,
        string? stageName = null)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var policy = errorPolicy ?? _defaultPolicy;
        var resolvedStageName = stageName ?? filter.GetType().Name;

        if (policy is SkipAndContinuePolicy && filter is not ISkipAndContinueCapable)
        {
            throw new NotSupportedException(
                $"The filter '{resolvedStageName}' cannot use SkipAndContinue because it does not " +
                $"implement {nameof(ISkipAndContinueCapable)}. Derive from " +
                $"{nameof(TransformFilter<TCurrent, TNext>)}<{typeof(TCurrent).Name},{typeof(TNext).Name}> " +
                "for ordinary one-in-one-out filters, which supports this automatically, or implement " +
                $"{nameof(ISkipAndContinueCapable)} yourself if this filter isolates its own per-item " +
                "faults. Only FailFast is safe for arbitrary filters — see IFilter's fault-isolation " +
                "contract.");
        }

        if (filter is ISkipAndContinueCapable capable)
        {
            capable.Configure(policy, _observer, resolvedStageName);
        }

        var upstreamCompose = _compose;

        IAsyncEnumerable<TNext> NewCompose(IAsyncEnumerable<TIn> input, CancellationToken cancellationToken)
        {
            var upstream = upstreamCompose(input, cancellationToken);
            return filter.ProcessAsync(upstream, cancellationToken);
        }

        return new PipelineBuilder<TIn, TNext>(NewCompose, _defaultPolicy, _observer);
    }

    /// <summary>Adds a synchronous 1:1 transform as a filter, via <see cref="DelegateTransformFilter{TIn, TOut}"/>.</summary>
    public PipelineBuilder<TIn, TNext> AddFilter<TNext>(
        Func<TCurrent, TNext> transform,
        IErrorPolicy? errorPolicy = null,
        string? stageName = null)
        => AddFilter(new DelegateTransformFilter<TCurrent, TNext>(transform), errorPolicy, stageName);

    /// <summary>Adds an asynchronous 1:1 transform as a filter, via <see cref="DelegateTransformFilter{TIn, TOut}"/>.</summary>
    public PipelineBuilder<TIn, TNext> AddFilter<TNext>(
        Func<TCurrent, CancellationToken, Task<TNext>> transform,
        IErrorPolicy? errorPolicy = null,
        string? stageName = null)
        => AddFilter(new DelegateTransformFilter<TCurrent, TNext>(transform), errorPolicy, stageName);

    /// <summary>
    /// Returns a builder with a new default error policy, applied to filters added
    /// after this call. Does not change stages already added.
    /// </summary>
    public PipelineBuilder<TIn, TCurrent> WithDefaultErrorPolicy(IErrorPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new PipelineBuilder<TIn, TCurrent>(_compose, policy, _observer);
    }

    /// <summary>Returns a builder with a new observer, applied to filters added after this call.</summary>
    public PipelineBuilder<TIn, TCurrent> WithObserver(IPipelineObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return new PipelineBuilder<TIn, TCurrent>(_compose, _defaultPolicy, observer);
    }

    /// <summary>Builds the runnable pipeline.</summary>
    public Pipeline<TIn, TCurrent> Build() => new(_compose);
}
