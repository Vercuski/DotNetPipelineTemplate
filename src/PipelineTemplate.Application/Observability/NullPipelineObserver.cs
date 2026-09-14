using PipelineTemplate.Domain.Observability;

namespace PipelineTemplate.Application.Observability;

/// <summary>
/// An <see cref="IPipelineObserver"/> that does nothing. The default observer for a
/// pipeline that hasn't been given one — see NFR §8 ("observability is opt-out, not
/// omitted": the hooks always exist, but cost nothing until a real observer is
/// supplied). A real, technology-specific observer (OpenTelemetry, Serilog, etc.)
/// belongs in the Infrastructure layer, not here.
/// </summary>
public sealed class NullPipelineObserver : IPipelineObserver
{
    /// <summary>The shared, stateless instance of this observer.</summary>
    public static readonly NullPipelineObserver Instance = new();

    private NullPipelineObserver()
    {
    }

    /// <inheritdoc />
    public void OnItemProcessed(string stageName, TimeSpan duration)
    {
        // Intentionally empty.
    }

    /// <inheritdoc />
    public void OnItemFaulted(string stageName, Exception exception, bool willContinue)
    {
        // Intentionally empty.
    }
}
