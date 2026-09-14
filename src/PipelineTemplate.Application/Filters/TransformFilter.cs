using System.Runtime.CompilerServices;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Domain.Filters;
using PipelineTemplate.Domain.Observability;

namespace PipelineTemplate.Application.Filters;

/// <summary>
/// A base class for ordinary one-in-one-out filters, so authoring a simple transform
/// doesn't require hand-writing an async iterator against the Domain layer's streaming
/// <see cref="IFilter{TIn, TOut}"/> contract directly. This is the ADR-0005
/// developer-experience mitigation, and it is also the supported, safe way to get
/// per-item <see cref="SkipAndContinuePolicy"/> semantics: this class isolates faults
/// around its own per-item work — catching around the call to
/// <see cref="TransformAsync"/> and continuing its own <c>await foreach</c> over the
/// upstream input — which never requires resuming a foreign enumerator after an
/// exception.
/// </summary>
/// <remarks>
/// This lives in the Application layer, not Domain, because it orchestrates policy
/// application and observability reporting around the pure <see cref="IFilter{TIn, TOut}"/>
/// contract — that orchestration is a use-case-level concern, not a rule of the
/// pattern itself.
/// </remarks>
/// <typeparam name="TIn">The type of items consumed.</typeparam>
/// <typeparam name="TOut">The type of items produced.</typeparam>
public abstract class TransformFilter<TIn, TOut> : IFilter<TIn, TOut>, ISkipAndContinueCapable
{
    private IErrorPolicy _policy = FailFastPolicy.Instance;
    private IPipelineObserver _observer = Application.Observability.NullPipelineObserver.Instance;
    private string? _stageName;

    /// <summary>
    /// Transforms a single input item into a single output item.
    /// </summary>
    protected abstract Task<TOut> TransformAsync(TIn item, CancellationToken cancellationToken);

    /// <inheritdoc />
    void ISkipAndContinueCapable.Configure(IErrorPolicy policy, IPipelineObserver observer, string stageName)
    {
        _policy = policy;
        _observer = observer;
        _stageName = stageName;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TOut> ProcessAsync(
        IAsyncEnumerable<TIn> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stageName = _stageName ?? GetType().Name;

        await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var start = DateTime.UtcNow;
            TOut result;

            try
            {
                result = await TransformAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var context = new ErrorContext(stageName);
                var shouldContinue = _policy.ShouldContinue(ex, context);
                _observer.OnItemFaulted(stageName, ex, shouldContinue);

                if (shouldContinue)
                {
                    // Safe: we're just moving to the next item of our own, well-behaved
                    // upstream `await foreach` loop — not resuming a foreign enumerator
                    // that just threw.
                    continue;
                }

                throw new PipelineExecutionException(
                    stageName,
                    $"Filter '{stageName}' faulted and the active error policy requested the pipeline halt.",
                    ex);
            }

            _observer.OnItemProcessed(stageName, DateTime.UtcNow - start);
            yield return result;
        }
    }
}
