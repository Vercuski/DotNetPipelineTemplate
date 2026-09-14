using PipelineTemplate.Application.Filters;
using PipelineTemplate.Samples.Telemetry.Model;

namespace PipelineTemplate.Samples.Telemetry.Filters;

/// <summary>
/// Parses a raw "serviceName,severity,value" line into a <see cref="TelemetryEvent"/>.
/// Real telemetry ingestion is noisy — a single malformed line (wrong field count,
/// non-numeric value) is expected, not exceptional, so this filter is meant to run
/// under <c>SkipAndContinuePolicy</c>: one bad line shouldn't take down ingestion of
/// everything after it. It derives from <see cref="TransformFilter{TIn, TOut}"/>
/// specifically so that skip-and-continue isolation is safe (see ADR-0006 /
/// <c>ISkipAndContinueCapable</c>).
/// </summary>
public sealed class ParseTelemetryLineFilter : TransformFilter<string, TelemetryEvent>
{
    protected override Task<TelemetryEvent> TransformAsync(string item, CancellationToken cancellationToken)
    {
        var fields = item.Split(',');
        if (fields.Length != 3)
        {
            throw new FormatException($"Expected 3 comma-separated fields, got {fields.Length}: '{item}'");
        }

        if (!double.TryParse(fields[2], out var value))
        {
            throw new FormatException($"Value field is not numeric: '{fields[2]}'");
        }

        return Task.FromResult(new TelemetryEvent(fields[0], fields[1], value));
    }
}
