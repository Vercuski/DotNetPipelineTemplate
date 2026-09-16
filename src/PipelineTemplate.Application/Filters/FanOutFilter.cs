using System.Runtime.CompilerServices;
using PipelineTemplate.Application.Observability;
using PipelineTemplate.Domain.ErrorHandling;
using PipelineTemplate.Domain.Filters;
using PipelineTemplate.Domain.Observability;

namespace PipelineTemplate.Application.Filters;

/// <summary>
/// Fans a single input item out to several branch filters running concurrently, then
/// merges their per-item results back into one output item. This is the Phase 3
/// (fan-out/fan-in) capability promised — but not built — in ADR-0004.
/// </summary>
/// <remarks>
/// <para>
/// <b>The design point worth calling out:</b> this type requires zero changes to
/// <see cref="Composition.PipelineBuilder{TIn, TCurrent}"/>, <see cref="Composition.Pipeline{TIn, TOut}"/>,
/// or the <see cref="IFilter{TIn, TOut}"/> contract. It implements
/// <see cref="IFilter{TIn, TOut}"/> like any other filter and slots directly into
/// the existing <c>PipelineBuilder.AddFilter(...)</c> call, inheriting that stage's
/// error-policy resolution, observability reporting, and stage naming for free. This
/// is exactly the forward-compatibility ADR-0004 asked the core contract to support —
/// see ADR-0009 for the full record of this decision.
/// </para>
/// <para>
/// <b>v1 scope, and why:</b> each branch must be 1:1 — it must produce exactly one
/// output item for the single input item it's given, or this filter throws. That
/// rules out a branch that is itself a many:1 (windowing) or 1:many (expansion)
/// filter; supporting that would mean correlating independently-paced branch outputs
/// against a shared upstream, which is a substantially harder problem (closer to a
/// general reactive multicast) than the "parallel checks converging before a
/// decision" shape this is built for (ADR-0004's own CI/CD example). Branches built
/// from <see cref="TransformFilter{TIn, TOut}"/> or a raw 1:1
/// <see cref="IFilter{TIn, TOut}"/> satisfy this automatically.
/// </para>
/// <para>
/// Branch faults and the merge function's own exceptions are both treated as a fault
/// of this fan-out *stage* — governed by whichever error policy this filter is
/// configured with (<see cref="FailFastPolicy"/> or <see cref="SkipAndContinuePolicy"/>),
/// exactly like a <see cref="TransformFilter{TIn, TOut}"/>'s own per-item fault
/// handling. A branch's *internal* error policy (if it has its own, e.g. because it's
/// itself a <see cref="TransformFilter{TIn, TOut}"/> configured with one) still
/// applies first, inside that branch, before anything reaches this level.
/// </para>
/// </remarks>
/// <typeparam name="TIn">The type of the single input item fanned out to every branch.</typeparam>
/// <typeparam name="TBranchOut">The type each branch produces.</typeparam>
/// <typeparam name="TOut">The type produced by merging every branch's result.</typeparam>
public sealed class FanOutFilter<TIn, TBranchOut, TOut> : IFilter<TIn, TOut>, ISkipAndContinueCapable
{
    private readonly IReadOnlyList<IFilter<TIn, TBranchOut>> _branches;
    private readonly Func<TIn, IReadOnlyList<TBranchOut>, TOut> _merge;
    private IErrorPolicy _policy = FailFastPolicy.Instance;
    private IPipelineObserver _observer = NullPipelineObserver.Instance;
    private string? _stageName;

    /// <param name="branches">
    /// At least two branch filters, each run concurrently against the same input
    /// item. Every branch must produce exactly one output per item — see the
    /// v1-scope remarks on this type.
    /// </param>
    /// <param name="merge">
    /// Combines the original input item and every branch's result (in the same order
    /// as <paramref name="branches"/>) into the stage's output. May throw to signal
    /// the item as faulted — e.g. "not all checks passed" — which this filter's
    /// configured error policy then decides how to handle.
    /// </param>
    public FanOutFilter(IEnumerable<IFilter<TIn, TBranchOut>> branches, Func<TIn, IReadOnlyList<TBranchOut>, TOut> merge)
    {
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(merge);

        _branches = branches.ToList();
        if (_branches.Count < 2)
        {
            throw new ArgumentException("Fan-out requires at least two branches — with one, just chain a filter normally.", nameof(branches));
        }

        _merge = merge;
    }

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
                var branchResults = await RunBranchesAsync(item, cancellationToken).ConfigureAwait(false);
                result = _merge(item, branchResults);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var context = new ErrorContext(stageName);
                var shouldContinue = _policy.ShouldContinue(ex, context);
                _observer.OnItemFaulted(stageName, ex, shouldContinue);

                if (shouldContinue)
                {
                    continue;
                }

                throw new PipelineExecutionException(
                    stageName,
                    $"Fan-out stage '{stageName}' faulted and the active error policy requested the pipeline halt.",
                    ex);
            }

            _observer.OnItemProcessed(stageName, DateTime.UtcNow - start);
            yield return result;
        }
    }

    private async Task<IReadOnlyList<TBranchOut>> RunBranchesAsync(TIn item, CancellationToken cancellationToken)
    {
        var tasks = new Task<TBranchOut>[_branches.Count];
        for (var i = 0; i < _branches.Count; i++)
        {
            tasks[i] = RunBranchAsync(_branches[i], item, cancellationToken);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<TBranchOut> RunBranchAsync(IFilter<TIn, TBranchOut> branch, TIn item, CancellationToken cancellationToken)
    {
        var count = 0;
        var result = default(TBranchOut);

        await foreach (var output in branch.ProcessAsync(SingleItem(item), cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            result = output;
            count++;
        }

        if (count != 1)
        {
            throw new InvalidOperationException(
                $"Fan-out branches must produce exactly one output per input item; " +
                $"'{branch.GetType().Name}' produced {count}. Branches with many:1 or " +
                "1:many cardinality aren't supported by FanOutFilter — see its XML " +
                "doc remarks on v1 scope.");
        }

        return result!;
    }

    private static async IAsyncEnumerable<TIn> SingleItem(TIn item)
    {
        await Task.Yield();
        yield return item;
    }
}
