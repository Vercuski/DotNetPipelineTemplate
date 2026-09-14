using System.Runtime.CompilerServices;
using PipelineTemplate.Domain.Filters;
using PipelineTemplate.Samples.Telemetry.Model;

namespace PipelineTemplate.Samples.Telemetry.Filters;

/// <summary>
/// Batches every configured window-size of events for the same service into one
/// <see cref="AggregatedMetric"/> — a many:1 filter, the shape a real windowing stage
/// for stream processing/real-time analytics takes. Deliberately implemented against
/// the raw <see cref="IFilter{TIn, TOut}"/> contract rather than
/// <c>TransformFilter</c>, since a 1:1 adapter can't express "consume several inputs,
/// produce one output" — this is exactly the genericity claim from ADR-0005 and the
/// vision's success criteria, proven with a real filter rather than a toy example.
/// </summary>
/// <remarks>
/// Only supports <c>FailFastPolicy</c> — it doesn't implement
/// <see cref="ISkipAndContinueCapable"/>, since "skip this event" doesn't have a clean
/// meaning partway through an in-progress window. A production version could isolate
/// faults per window instead of per event; that refinement is left for when a real
/// consumer needs it.
/// </remarks>
public sealed class WindowAggregationFilter : IFilter<TelemetryEvent, AggregatedMetric>
{
    private readonly int _windowSize;

    public WindowAggregationFilter(int windowSize = 3)
    {
        if (windowSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), windowSize, "Window size must be at least 1.");
        }

        _windowSize = windowSize;
    }

    public async IAsyncEnumerable<AggregatedMetric> ProcessAsync(
        IAsyncEnumerable<TelemetryEvent> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Windows are grouped per service, so one noisy service doesn't skew another's
        // aggregate — a realistic requirement for multi-tenant telemetry.
        var windows = new Dictionary<string, List<TelemetryEvent>>();

        await foreach (var telemetryEvent in input.WithCancellation(cancellationToken))
        {
            if (!windows.TryGetValue(telemetryEvent.ServiceName, out var window))
            {
                window = new List<TelemetryEvent>(_windowSize);
                windows[telemetryEvent.ServiceName] = window;
            }

            window.Add(telemetryEvent);

            if (window.Count == _windowSize)
            {
                yield return Aggregate(telemetryEvent.ServiceName, window);
                window.Clear();
            }
        }

        // Trailing partial windows are intentionally dropped, not flushed — matching
        // real windowing semantics (an incomplete window isn't a valid aggregate) and
        // the CardinalityTests precedent in the Core test suite.
    }

    private static AggregatedMetric Aggregate(string serviceName, IReadOnlyList<TelemetryEvent> window)
    {
        return new AggregatedMetric(
            ServiceName: serviceName,
            EventCount: window.Count,
            AverageValue: window.Average(e => e.Value),
            MaxValue: window.Max(e => e.Value),
            ErrorCount: window.Count(e => string.Equals(e.Severity, "error", StringComparison.OrdinalIgnoreCase)));
    }
}
