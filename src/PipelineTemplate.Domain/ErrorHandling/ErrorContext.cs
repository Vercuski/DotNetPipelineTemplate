namespace PipelineTemplate.Domain.ErrorHandling;

/// <summary>
/// Describes the stage a fault occurred in, for use by an <see cref="IErrorPolicy"/>
/// and by observability hooks.
/// </summary>
/// <param name="StageName">
/// The display name of the filter/stage that faulted — the filter's type name by
/// default, or an explicit name supplied when the filter was added to the pipeline.
/// </param>
public readonly record struct ErrorContext(string StageName);
