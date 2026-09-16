namespace PipelineTemplate.Samples.CiCd.Model;

/// <summary>The outcome of one parallel check (unit tests, lint, security scan, ...).</summary>
public sealed record CheckResult(string CheckName, bool Passed);
