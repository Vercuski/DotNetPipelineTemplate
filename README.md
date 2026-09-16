# DotNet Pipeline Template

A generic, domain-agnostic **Pipes and Filters** implementation for .NET 10,
packaged as an importable Visual Studio 2026 project template.

Full architecture rationale — vision, requirements, C4 diagrams, and every ADR
and trade study referenced in code comments below — lives in the companion
Obsidian vault, not in this repo. This README covers only what's needed to
build, test, and try this code. API documentation generated from the source's
own XML doc comments lives in
[`Pipeline_Template_Documentation.md`](./Pipeline_Template_Documentation.md),
regenerated automatically on every push to `main` (or on demand via the
Actions tab).

## Status

**Phases 1, 2, and 3 are complete.** The core library (filter contract,
fluent composition API, error-handling policies, fan-out/fan-in) is
implemented and tested, organized as Clean Architecture layers. The
Template Package installs and scaffolds a working project. Not yet done:
general (non-1:1) fan-out/fan-in topologies, a declarative authoring
layer, and the two still-open decisions noted below.

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
tests/PipelineTemplate.Samples.Tests/ Validates the two sample pipelines below
                                       against architecture-vision.md §6
samples/PipelineTemplate.Samples.Telemetry/  A telemetry-processing sample
samples/PipelineTemplate.Samples.CiCd/       A CI/CD-style build pipeline sample
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

26 tests currently cover: multi-filter composition, sync and async delegate
filters, FailFast propagation, SkipAndContinue fault isolation (and the
`NotSupportedException` guard for filters that haven't opted into it), 1:many
and many:1 cardinality (proving the core contract's genericity claim), DI
resolution composing with the existing API, explicit stage-name overrides,
fan-out/fan-in (merge correctness, error-policy integration, cardinality
guards, and real concurrency — not just sequential execution dressed up),
three architecture tests enforcing the Domain → Application → Infrastructure
dependency direction, and six tests validating the sample pipelines below.

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

# 3. Scaffold a project and restore explicitly against the local feed + nuget.org
dotnet new pipefilter -n MyPipeline
cd MyPipeline
dotnet restore --source ../local-nuget-feed --source https://api.nuget.org/v3/index.json
dotnet run
```

There's deliberately no repo-root `NuGet.config` for this — a config file at
the repo root would apply to *every* project's restore (via NuGet's directory
walk-up), including this repo's own CI builds, which don't need the local
feed and don't have it populated. An earlier version of this README had one,
and it broke the `generate-docs` GitHub Action on every run with
`NU1301: The local source '.../local-nuget-feed' doesn't exist` — a
freshly-cloned CI runner never has that folder populated, only a local dev
machine that's run the `dotnet pack` commands above. The explicit `--source`
flags above scope the local feed to only the one restore that actually needs
it.

**Gotcha worth knowing**: if you change a layer's public API and re-pack
without bumping the version, NuGet's global package cache
(`~/.nuget/packages/`) will keep serving the *old* cached copy under the same
package ID + version, silently. If a rebuilt package doesn't seem to take
effect, either bump the version or clear the relevant folder(s) under
`~/.nuget/packages/dotnetpipelinetemplate.*` and restore with `--force`. This
bit us once already during development — a "successful" build turned out to
be silently testing stale, pre-refactor code.

## Trying the sample pipelines

Two samples live under `samples/`, validated by `tests/PipelineTemplate.Samples.Tests/`
— they exist specifically to prove the genericity and mixed-policy claims in
`architecture-vision.md` §6, not just to look nice:

```bash
dotnet run --project samples/PipelineTemplate.Samples.Telemetry
dotnet run --project samples/PipelineTemplate.Samples.CiCd
```

- **Telemetry** (`ParseTelemetryLineFilter` + `WindowAggregationFilter`): a
  `SkipAndContinue`-driven parser tolerating malformed input lines, feeding a
  hand-written many:1 windowing filter — real windowing/aggregation against
  the raw `IFilter<TIn,TOut>` contract, not the toy int-summing example in
  the core test suite.
- **CI/CD** (`BuildStepFilter` + `ParallelCheckFilter` via `CiCdPipelineFactory`):
  `Restore → Build → ParallelChecks → Package → Notify`. `ParallelChecks` fans
  out to three concurrent checks (`FanOutFilter`, ADR-0009) and fans back in
  via "all must pass"; `Notify` is overridden to `SkipAndContinue`. One
  pipeline demonstrating fan-out/fan-in, `FailFast`, and `SkipAndContinue`
  all coexisting — exactly the ADR-0006/ADR-0009 scenario the vision's
  success criteria call for.

Building these surfaced a real gap: `PipelineBuilder.AddFilter` never
actually implemented the explicit stage-name override that
`ErrorContext.StageName`'s own XML doc already promised — every stage
silently fell back to the filter's type name, which broke the moment one
filter class got reused for several named stages. Fixed by adding an
optional `stageName` parameter to all three `AddFilter` overloads.

`FanOutFilter` (Phase 3, ADR-0009) is worth calling out specifically: it
required zero changes to `PipelineBuilder`, `Pipeline`, or `IFilter` — it's
a new filter type that plugs into the existing `AddFilter(...)` call like
any other filter, inheriting error-policy resolution and observability for
free. That's the concrete proof of ADR-0004's forward-compatibility promise,
not just a claim about it. Branches are scoped to 1:1 cardinality in this
version — see the type's XML doc remarks for why the general case is a
substantially harder problem deliberately left for if a real need shows up.

## Performance benchmarks

`benchmarks/PipelineTemplate.Benchmarks` uses BenchmarkDotNet to answer
the questions ADR-0005 and `non-functional-requirements.md` §1 left as
TBD: how much overhead the pipeline framework adds per filter hop, and
what real throughput looks like against the actual Telemetry sample
(not a synthetic toy). Run it with:

```bash
dotnet run --project benchmarks/PipelineTemplate.Benchmarks -c Release -- --filter "*"
```

BenchmarkDotNet requires Release; it refuses to run otherwise. Current
results (measured on a single shared, weak logical core — see the NFR
doc for the full numbers and the measurement-environment caveat):
framework overhead is ≈100 ns and ≈72 bytes allocated per item per hop,
constant regardless of chain length, and attaching a real observer
costs nothing statistically distinguishable from noise. The Telemetry
sample sustains ≈655,000 items/sec (mean) end to end. Re-run on real
target hardware before treating the throughput figure as a capacity
number rather than a sanity check.

## Generated API documentation

`Pipeline_Template_Documentation.md` is generated from the XML doc comments on
every public type in the Domain, Application, and Infrastructure layers — not
hand-written, and not meant to be edited directly. `.github/workflows/generate-docs.yml`
regenerates and commits it automatically on every push to `main`, and can
also be run on demand from the Actions tab (`workflow_dispatch`). To run it
locally:

```bash
dotnet run --project tools/PipelineTemplate.DocGenerator -- "$(pwd)"
```

It's built on Roslyn's semantic model (`Microsoft.CodeAnalysis.CSharp`) rather
than reflecting over compiled assemblies and pattern-matching XML doc IDs —
the latter is a well-known fragile approach (getting the ID string exactly
right for every generic method/constructor shape is its own small compiler),
whereas `ISymbol.GetDocumentationCommentXml()` gets the correct, fully-resolved
doc comment for any symbol directly from the compiler, with no ID matching of
its own to get wrong.

Samples and the Core facade are deliberately out of scope for this doc: Core
has no source of its own, and samples exist to validate the template, not to
be part of its distributed public API.

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
