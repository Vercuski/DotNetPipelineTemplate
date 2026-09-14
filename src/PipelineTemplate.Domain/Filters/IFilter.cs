namespace PipelineTemplate.Domain.Filters;

/// <summary>
/// The core filter contract in the Pipes and Filters pattern. A filter consumes a
/// stream of <typeparamref name="TIn"/> items and produces a stream of
/// <typeparamref name="TOut"/> items.
/// </summary>
/// <remarks>
/// <para>
/// This is a Domain-layer contract: it has no dependency on how filters are composed,
/// executed, or observed (those are Application-layer concerns — see
/// <c>PipelineTemplate.Application.Composition</c>). It's the one thing every layer
/// and every consumer agrees on.
/// </para>
/// <para>
/// The contract is intentionally cardinality-flexible: an implementation may yield
/// zero, one, or many output items per input item (or per several input items), which
/// is what lets it serve 1:1 transforms, filtering/sampling, windowing or aggregation
/// (many:1), and fan-out-style expansion (1:many) — all without a different interface
/// per shape. See ADR-0005 for the full reasoning behind choosing a streaming contract
/// over a synchronous or item-at-a-time asynchronous one.
/// </para>
/// <para>
/// <b>Fault-isolation contract for implementers:</b> the pipeline's error-handling
/// policy (ADR-0006) can only safely apply <c>SkipAndContinue</c> semantics to a
/// filter that isolates its own per-item faults internally (catching around the
/// processing of one logical unit of work and continuing its own iteration) rather
/// than letting an exception escape from advancing its output enumerator. See
/// <see cref="ISkipAndContinueCapable"/> for how a filter opts into this, and
/// <c>PipelineTemplate.Application.Filters.TransformFilter&lt;TIn, TOut&gt;</c> for the
/// supported-out-of-the-box way to get this right for ordinary one-in-one-out
/// filters.
/// </para>
/// </remarks>
/// <typeparam name="TIn">The type of items this filter consumes.</typeparam>
/// <typeparam name="TOut">The type of items this filter produces.</typeparam>
public interface IFilter<in TIn, out TOut>
{
    /// <summary>
    /// Processes a stream of input items, producing a stream of output items.
    /// </summary>
    /// <param name="input">The upstream sequence of items to process.</param>
    /// <param name="cancellationToken">A token to cancel processing.</param>
    /// <returns>The resulting sequence of output items.</returns>
    IAsyncEnumerable<TOut> ProcessAsync(
        IAsyncEnumerable<TIn> input,
        CancellationToken cancellationToken = default);
}