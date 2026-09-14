namespace PipelineTemplate.Domain.ErrorHandling;

/// <summary>
/// An <see cref="IErrorPolicy"/> under which any filter fault halts the pipeline
/// immediately. This is the safe default for stages where continuing past a failure
/// would be wrong — e.g. a build step in a CI/CD-style pipeline.
/// </summary>
/// <remarks>
/// Unlike <see cref="SkipAndContinuePolicy"/>, this policy requires no cooperation
/// from the filter: letting an exception propagate out of a filter's
/// <c>IAsyncEnumerable&lt;TOut&gt;</c> naturally halts the enclosing
/// <c>await foreach</c>, which is exactly fail-fast semantics. Every
/// <see cref="Filters.IFilter{TIn, TOut}"/> implementation supports this policy,
/// whether or not it implements <see cref="Filters.ISkipAndContinueCapable"/>.
/// </remarks>
public sealed class FailFastPolicy : IErrorPolicy
{
    /// <summary>The shared, stateless instance of this policy.</summary>
    public static readonly FailFastPolicy Instance = new();

    private FailFastPolicy()
    {
    }

    /// <inheritdoc />
    public bool ShouldContinue(Exception exception, ErrorContext context) => false;
}
