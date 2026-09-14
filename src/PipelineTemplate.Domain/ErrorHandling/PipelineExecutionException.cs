namespace PipelineTemplate.Domain.ErrorHandling;

/// <summary>
/// Thrown when a pipeline stage faults under a policy that does not continue (e.g.
/// <see cref="FailFastPolicy"/>). The original exception is always the
/// <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class PipelineExecutionException : Exception
{
    /// <summary>The name of the stage that faulted.</summary>
    public string StageName { get; }

    public PipelineExecutionException(string stageName, string message, Exception innerException)
        : base(message, innerException)
    {
        StageName = stageName;
    }
}
