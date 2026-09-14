namespace PipelineTemplate.Application.Filters;

/// <summary>
/// A <see cref="TransformFilter{TIn, TOut}"/> backed by a delegate, for filters simple
/// enough not to warrant their own class. Used by
/// <c>PipelineBuilder.AddFilter(Func&lt;TIn,TOut&gt;)</c> and its async overload.
/// </summary>
public sealed class DelegateTransformFilter<TIn, TOut> : TransformFilter<TIn, TOut>
{
    private readonly Func<TIn, CancellationToken, Task<TOut>> _transform;

    public DelegateTransformFilter(Func<TIn, CancellationToken, Task<TOut>> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        _transform = transform;
    }

    public DelegateTransformFilter(Func<TIn, TOut> transform)
        : this(WrapSynchronous(transform))
    {
    }

    private static Func<TIn, CancellationToken, Task<TOut>> WrapSynchronous(Func<TIn, TOut> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return (item, _) => Task.FromResult(transform(item));
    }

    protected override Task<TOut> TransformAsync(TIn item, CancellationToken cancellationToken)
        => _transform(item, cancellationToken);
}
