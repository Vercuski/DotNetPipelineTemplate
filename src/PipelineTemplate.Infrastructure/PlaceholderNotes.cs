namespace PipelineTemplate.Infrastructure;

// Intentionally no concrete types here yet.
//
// This project is reserved for technology-specific adapters that implement the ports
// defined in the Domain layer (e.g. PipelineTemplate.Domain.Observability.IPipelineObserver)
// using a real external dependency — an OpenTelemetry-backed observer, a Serilog sink,
// or similar. The default adapters that ship in the Application layer
// (PipelineTemplate.Application.Observability.NullPipelineObserver) are dependency-free
// on purpose; anything that pulls in a real package belongs here instead, so the Core
// Library's consumers only take on that dependency if they actually reference this
// project/package.
//
// See ADR-0007's DI-integration finding for why no bespoke dependency-injection
// wrapper code lives here either: a filter or observer resolved from any
// IServiceProvider already composes with the existing public API without help.
