using Microsoft.Extensions.DependencyInjection;
using PipelineTemplate.Application.Composition;
using PipelineTemplate.Application.Filters;
using PipelineTemplate.Domain.Filters;
using Xunit;

namespace PipelineTemplate.Core.Tests.Composition;

/// <summary>
/// Validates the ADR-0007 implementation note about integrating with
/// <c>Microsoft.Extensions.DependencyInjection</c>. The finding: no bespoke
/// extension methods are needed in the Core Library at all — a filter resolved from
/// any <see cref="IServiceProvider"/> is just an <see cref="IFilter{TIn, TOut}"/>
/// instance, and composes with the existing
/// <c>PipelineBuilder.AddFilter(IFilter&lt;TCurrent,TNext&gt;)</c> overload with no
/// help required from Core beyond <see cref="IServiceProvider"/>, which is already
/// part of the BCL. Deliberately not adding a wrapper here keeps the Core Library
/// free of any dependency on a specific DI container or its abstractions package.
/// </summary>
public class DependencyInjectionIntegrationTests
{
    private interface IGreetingService
    {
        string Greet(string name);
    }

    private sealed class GreetingService : IGreetingService
    {
        public string Greet(string name) => $"Hello, {name}!";
    }

    /// <summary>A filter with a constructor-injected dependency, registered like any other service.</summary>
    private sealed class GreetingFilter : TransformFilter<string, string>
    {
        private readonly IGreetingService _greetingService;

        public GreetingFilter(IGreetingService greetingService)
        {
            _greetingService = greetingService;
        }

        protected override Task<string> TransformAsync(string item, CancellationToken cancellationToken)
            => Task.FromResult(_greetingService.Greet(item));
    }

    [Fact]
    public async Task Filter_Resolved_From_DI_Composes_With_The_Existing_AddFilter_Overload()
    {
        // Arrange: a standard DI container, with the filter and its dependency
        // registered the ordinary way — nothing pipeline-specific about this setup.
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService, GreetingService>();
        services.AddTransient<GreetingFilter>();
        await using var provider = services.BuildServiceProvider();

        var filter = provider.GetRequiredService<GreetingFilter>();

        // Act: the resolved instance is just an IFilter<TIn,TOut> — no DI-specific
        // overload of AddFilter was needed to use it.
        var pipeline = Pipeline.Create<string>()
            .AddFilter(filter)
            .Build();

        var results = new List<string>();
        await foreach (var item in pipeline.RunAsync(Source("Scott", "World")))
        {
            results.Add(item);
        }

        Assert.Equal(new[] { "Hello, Scott!", "Hello, World!" }, results);
    }

    private static async IAsyncEnumerable<T> Source<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
