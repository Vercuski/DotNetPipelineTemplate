namespace PipelineTemplate.Samples.Telemetry.Model;

/// <summary>A single parsed telemetry event.</summary>
public sealed record TelemetryEvent(string ServiceName, string Severity, double Value);

/// <summary>An aggregate produced by windowing several <see cref="TelemetryEvent"/>s together.</summary>
public sealed record AggregatedMetric(
    string ServiceName,
    int EventCount,
    double AverageValue,
    double MaxValue,
    int ErrorCount);
