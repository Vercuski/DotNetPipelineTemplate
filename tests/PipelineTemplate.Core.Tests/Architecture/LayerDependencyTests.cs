using NetArchTest.Rules;
using Xunit;

namespace PipelineTemplate.Core.Tests.Architecture;

/// <summary>
/// Enforces the Clean Architecture dependency rule across the layer projects.
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>HaveDependencyOnAny</c>, not <c>HaveDependencyOnAll</c>: the latter
/// requires a type to reference every listed namespace simultaneously to be
/// flagged, which makes it practically a no-op for an isolation check like this (a
/// documented, previously hard-won gotcha — see the Onion template's own
/// architecture-learnings).
/// </para>
/// <para>
/// Worth being honest about what actually enforces what here: in this project's
/// current reference graph (Domain ← Application ← Infrastructure ← Core, each
/// pointing one direction only), <b>MSBuild's circular-reference detection is the
/// real, airtight enforcement</b> for the inter-project tests below — Domain can't
/// reference anything in Application without first adding a ProjectReference, and
/// that reference would create a cycle the build refuses outright, before
/// NetArchTest ever runs. The layer tests here are defense-in-depth for that case.
/// The one test doing genuinely non-redundant work is
/// <see cref="Domain_Should_Not_Depend_On_Any_Third_Party_Package"/>: nothing about
/// project-reference cycles stops Domain from picking up an unwanted
/// PackageReference directly, since that introduces no cycle at all.
/// </para>
/// </remarks>
public class LayerDependencyTests
{
    private const string DomainNamespace = "PipelineTemplate.Domain";
    private const string ApplicationNamespace = "PipelineTemplate.Application";
    private const string InfrastructureNamespace = "PipelineTemplate.Infrastructure";

    [Fact]
    public void Domain_Should_Not_Depend_On_Application_Or_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Domain.Filters.IFilter<,>).Assembly)
            .Should()
            .NotHaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, DescribeFailures(result));
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Application.Composition.Pipeline).Assembly)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, DescribeFailures(result));
    }

    [Fact]
    public void Domain_Should_Not_Depend_On_Any_Third_Party_Package()
    {
        // The innermost layer stays dependency-free by design — see the comment in
        // PipelineTemplate.Domain.csproj. This test catches an accidental
        // PackageReference addition, not just a project-reference violation.
        var result = Types.InAssembly(typeof(Domain.Filters.IFilter<,>).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.Extensions", "System.Text.Json", "Newtonsoft")
            .GetResult();

        Assert.True(result.IsSuccessful, DescribeFailures(result));
    }

    [Fact]
    public void Application_Does_Depend_On_Domain()
    {
        // A positive control: proves NetArchTest is actually inspecting real IL
        // dependencies here, not trivially passing because the check never fires.
        //
        // Finding worth recording: NetArchTest's dependency detection only sees a
        // type's own direct member signatures (fields, method parameters/returns) —
        // it does NOT see a dependency that only exists via inheriting from a base
        // class in another namespace, or via an unconstrained generic type parameter.
        // An earlier version of this test targeted Pipeline<TIn,TOut> (whose only
        // members are generic over TIn/TOut, with no direct Domain reference of its
        // own) and DelegateTransformFilter (whose only Domain exposure is inherited
        // from TransformFilter) — both came back as "no dependency found", which
        // would have been a false negative if used as the real enforcement check.
        // PipelineBuilder<,> is used here instead because it holds actual
        // IErrorPolicy/IPipelineObserver fields directly.
        var result = Types.InAssembly(typeof(Application.Composition.Pipeline).Assembly)
            .That()
            .HaveNameStartingWith("PipelineBuilder")
            .Should()
            .HaveDependencyOnAny(DomainNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, DescribeFailures(result));
    }

    private static string DescribeFailures(TestResult result)
    {
        if (result.IsSuccessful)
        {
            return string.Empty;
        }

        var offenders = result.FailingTypes?.Select(t => t.FullName) ?? Enumerable.Empty<string>();
        return "Violating types: " + string.Join(", ", offenders);
    }
}
