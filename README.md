# DotNet Pipeline Template

A generic, domain-agnostic **Pipes and Filters** implementation for .NET 10,
packaged as an importable Visual Studio 2026 project template.

Full architecture rationale — vision, requirements, C4 diagrams, and every ADR
and trade study referenced in code comments below — lives in the companion
Obsidian vault, not in this repo. This README covers only what's needed to
build, test, and try this code.

## Status

**Phase 1 (Foundation & Core Pattern)** — in progress. The core library
(filter contract, fluent composition API, error-handling policies) is
implemented and tested, organized as Clean Architecture layers. The Template
Package installs and scaffolds a working project. Not yet done: fan-out/fan-in
(Phase 3), a declarative authoring layer, and the three still-open decisions
noted below.

## Structure

The library is organized as Clean Architecture layers, dependencies pointing
one direction only — enforced by both MSBuild (a reverse reference would be a
circular dependency, which the build refuses outright) and by NetArchTest
checks in `PipelineTemplate.Core.Tests/Architecture/`:

```
src/PipelineTemplate.Domain/          Pure contracts and rules — IFilter,
                                       IErrorPolicy, IPipelineObserver.
                                       Zero package dependencies, by design.
src/PipelineTemplate.Application/     Orchestration — TransformFilter,
                                       PipelineBuilder/Pipeline, the default
                                       no-op observer. Depends on Domain only.
src/PipelineTemplate.Infrastructure/  Reserved for real technology-specific
                                       adapters (e.g. an OpenTelemetry-backed
                                       observer). Currently empty. Depends on
                                       Domain + Application.
src/PipelineTemplate.Core/            A thin facade — no source of its own,
                                       just references to the three layers
                                       above, so consumers reference one
                                       package/name.

tests/PipelineTemplate.Core.Tests/    Unit + integration tests, plus
                                       Architecture/ (NetArchTest layer checks)
templates/PipelineTemplate.Template/  The dotnet new / VS2026 template package
.github/workflows/ci.yml              This repo's own build (not the workflow
                                       scaffolded into a generated solution —
                                       see templates/.../.github/workflows/ for
                                       that)
local-nuget-feed/                     Phase 1 only — see below
```

## Building and testing this repo

```bash
dotnet build
dotnet test
```

13 tests currently cover: multi-filter composition, sync and async delegate
filters, FailFast propagation, SkipAndContinue fault isolation (and the
`NotSupportedException` guard for filters that haven't opted into it), 1:many
and many:1 cardinality (proving the core contract's genericity claim), DI
resolution composing with the existing API, and three architecture tests
enforcing the Domain → Application → Infrastructure dependency direction.

## Trying the template locally

The library isn't published to NuGet yet (the final package identity is one
of the open decisions below), so local testing goes through a throwaway local
feed instead of nuget.org. All four layer packages need packing, since Core's
package now declares them as real dependencies:

```bash
# 1. Pack all four layers into a local feed
dotnet pack src/PipelineTemplate.Domain/PipelineTemplate.Domain.csproj -c Release -o local-nuget-feed
dotnet pack src/PipelineTemplate.Application/PipelineTemplate.Application.csproj -c Release -o local-nuget-feed
dotnet pack src/PipelineTemplate.Infrastructure/PipelineTemplate.Infrastructure.csproj -c Release -o local-nuget-feed
dotnet pack src/PipelineTemplate.Core/PipelineTemplate.Core.csproj -c Release -o local-nuget-feed

# 2. Install the template
dotnet new install ./templates/PipelineTemplate.Template

# 3. Scaffold a project (copy the repo-root NuGet.config into it so the
#    PackageReference resolves against the local feed)
dotnet new pipefilter -n MyPipeline
cp NuGet.config MyPipeline/
cd MyPipeline
dotnet run
```

**Gotcha worth knowing**: if you change a layer's public API and re-pack
without bumping the version, NuGet's global package cache
(`~/.nuget/packages/`) will keep serving the *old* cached copy under the same
package ID + version, silently. If a rebuilt package doesn't seem to take
effect, either bump the version or clear the relevant folder(s) under
`~/.nuget/packages/dotnetpipelinetemplate.*` and restore with `--force`. This
bit us once already during development — a "successful" build turned out to
be silently testing stale, pre-refactor code.

## Known open decisions (deliberately deferred, not forgotten)

These are tracked in `architecture-vision.md` §10 in the Obsidian vault, not
duplicated here in full — but they show up as literal `PLACEHOLDER` comments
in this code wherever they matter:

- Final `dotnet new` short name / package identity (currently `pipefilter` /
  `DotNetPipelineTemplate.*` for all four packages)
- Docker base image / multi-stage build conventions (a working first pass
  exists in the template's `Dockerfile`)
- Whether to include a reference sample filter — implemented as an actual
  template parameter (`IncludeSampleFilter`, default `true`) rather than a
  fixed yes/no, so the question doesn't need a single answer

## A design note worth reading before extending error handling

`IFilter<TIn,TOut>` implementations that want to support the
`SkipAndContinue` error policy must isolate their own per-item faults
internally (see the XML doc comments on `IFilter<TIn,TOut>` and
`ISkipAndContinueCapable`, both in the Domain layer). This isn't a style
preference — it's because an `IAsyncEnumerator` isn't safely resumable after
throwing, so the pipeline can't safely inject fault isolation from outside an
arbitrary filter. `TransformFilter<TIn,TOut>` (Application layer) does this
correctly for the common one-in-one-out case; a custom filter that hasn't
implemented `ISkipAndContinueCapable` will get a clear
`NotSupportedException` at pipeline-build time if you try to pair it with
`SkipAndContinue`, rather than undefined behavior at runtime.

## A design note worth reading before adding architecture tests

NetArchTest's dependency checks only see a type's own *direct* member
signatures (fields, method parameters/returns) — they do **not** see a
dependency that exists only through inheriting a base class in another
namespace, or through an unconstrained generic type parameter. See the class
remarks on `LayerDependencyTests` for a concrete example of a false negative
this produced during development, and why the real enforcement for the
Domain/Application/Infrastructure boundary is actually MSBuild's circular
project-reference detection, not NetArchTest — NetArchTest's genuinely
necessary contribution is catching a stray third-party `PackageReference` on
Domain, which no project-cycle check would ever catch.
