using System.Runtime.CompilerServices;
using PipelineTemplate.Domain.Filters;

namespace PipelineApp.Filters;

/// <summary>
/// A trivial no-op filter included only to demonstrate the shape of a raw
/// <see cref="IFilter{TIn, TOut}"/> implementation. For ordinary 1:1 transforms,
/// prefer deriving from <see cref="TransformFilter{TIn, TOut}"/> (or just passing a
/// delegate to <c>PipelineBuilder.AddFilter</c>) instead — see the Core Library's
/// documentation for why this contract also supports 1:many and many:1 filters,
/// which this sample deliberately doesn't need to show.
/// </summary>
public sealed class PassThroughFilter : IFilter<string, string>
{
    public async IAsyncEnumerable<string> ProcessAsync(
        IAsyncEnumerable<string> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in input.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
