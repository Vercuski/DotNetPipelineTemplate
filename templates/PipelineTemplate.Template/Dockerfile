# Multi-stage build for a scaffolded Pipeline Template consumer project.
#
# Base image: mcr.microsoft.com/dotnet/runtime:10.0 — the standard Debian-based
# ("noble") image, not the Alpine or Ubuntu Chiseled/distroless variants. This is a
# deliberate default, not an oversight: this template's whole point is serving five
# very different, unknown-in-advance consumer domains, and Debian-based images keep
# a shell + package manager, so `docker exec`-based debugging works out of the box —
# valuable for a starting scaffold. Chiseled/distroless images are smaller with a
# reduced attack surface, and are a genuinely good choice once a specific consumer's
# constraints are known; swapping the tag is a one-line change at that point. See
# ADR-0011 for the full reasoning, including why this was actually verified against
# current .NET 10 container guidance rather than carried over from habit.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY *.csproj NuGet.config .
COPY local-nuget-feed ./local-nuget-feed
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
# Run as the non-root "app" user .NET's official Linux container images have
# provided by default since .NET 8 (exposed via the $APP_UID environment variable) —
# a real, current security practice (ADR-0011), not a placeholder comment.
USER $APP_UID
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "PipelineApp.dll"]
