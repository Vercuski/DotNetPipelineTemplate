namespace PipelineTemplate.Samples.CiCd.Model;

/// <summary>The state threaded through a build pipeline, one step at a time.</summary>
public sealed record BuildContext(string SolutionName, IReadOnlyList<string> CompletedSteps)
{
    public BuildContext WithStepCompleted(string stepName)
        => this with { CompletedSteps = [.. CompletedSteps, stepName] };
}
