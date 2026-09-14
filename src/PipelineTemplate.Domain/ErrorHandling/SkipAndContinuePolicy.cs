namespace PipelineTemplate.Domain.ErrorHandling;

/// <summary>
/// An <see cref="IErrorPolicy"/> under which a filter fault is isolated to the item
/// that caused it — the pipeline logs and skips that item (via
/// <see cref="Observability.IPipelineObserver"/>, never silently) and continues
/// processing subsequent items.
/// </summary>
/// <remarks>
/// This policy can only be safely applied to a filter that implements
/// <see cref="Filters.ISkipAndContinueCapable"/> — see the fault-isolation contract
/// documented on <see cref="Filters.IFilter{TIn, TOut}"/> for why. Attempting to apply
/// it to a filter that hasn't opted in throws <see cref="NotSupportedException"/> when
/// the pipeline is built, rather than risking undefined behavior from resuming a
/// foreign enumerator after a fault.
/// </remarks>
public sealed class SkipAndContinuePolicy : IErrorPolicy
{
    /// <summary>The shared, stateless instance of this policy.</summary>
    public static readonly SkipAndContinuePolicy Instance = new();

    private SkipAndContinuePolicy()
    {
    }

    /// <inheritdoc />
    public bool ShouldContinue(Exception exception, ErrorContext context) => true;
}
