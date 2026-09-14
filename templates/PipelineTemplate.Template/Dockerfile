# Multi-stage build for a scaffolded Pipeline Template consumer project.
#
# PLACEHOLDER base images / conventions — architecture-vision.md §10 lists the final
# base image and multi-stage build conventions as a still-open decision, tracked in
# infrastructure-as-code-notes.md. This is a reasonable, working first pass (ADR-0003:
# containerization is included by default), not a final answer.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY *.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "PipelineApp.dll"]
