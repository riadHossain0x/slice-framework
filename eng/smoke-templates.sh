#!/usr/bin/env bash
# Template smoke test: pack the framework + templates to a local feed, install them, then scaffold and
# BUILD every `dotnet new` template (including both slice-tenant-api migration modes). This is the heavy,
# end-to-end counterpart to the fast static checks in tests/Slice.Templates.Tests. It needs the network
# (nuget.org for third-party packages). Run from anywhere:
#
#   eng/smoke-templates.sh
#
# Exit code is non-zero if packing, install, scaffolding, or any build fails.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ART="$REPO/artifacts"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "==> Packing framework packages → $ART"
dotnet pack "$REPO/Slice.slnx" -c Release -o "$ART" >/dev/null
echo "==> Packing Slice.Templates"
dotnet pack "$REPO/templates/Slice.Templates.csproj" -o "$ART" >/dev/null

echo "==> Clearing stale Slice.* from the global NuGet cache (so the freshly packed 0.1.0 is used)"
rm -rf "${HOME}/.nuget/packages"/slice.* 2>/dev/null || true

echo "==> Installing templates"
dotnet new uninstall Slice.Templates >/dev/null 2>&1 || true
dotnet new install "$ART/Slice.Templates.0.1.0.nupkg" >/dev/null

# scaffold <name> <shortName + extra args...>
scaffold_and_build() {
  local name="$1"; shift
  echo "==> $name: dotnet new $*"
  ( cd "$WORK" && dotnet new "$@" -n "$name" -o "$name" >/dev/null )
  dotnet nuget add source "$ART" -n slice-local --configfile "$WORK/$name/nuget.config" >/dev/null 2>&1 || true
  echo "    building…"
  ( cd "$WORK/$name" && dotnet build >/dev/null )
  # solutions/multi-project: build any extra projects not reachable from a single root csproj
  for extra in "$WORK/$name"/*.Migrator; do
    [ -d "$extra" ] || continue
    ( cd "$WORK/$name" && dotnet build "$(basename "$extra")" >/dev/null )
  done
  echo "    OK"
}

# Names must be valid C# identifiers / namespaces (no hyphens): dotnet new sanitizes file *contents*
# but not folder names, which diverges for the multi-project (.slnx) templates.
scaffold_and_build Acme.Api          slice-api
scaffold_and_build Acme.ApiPg        slice-api --database postgres
scaffold_and_build Acme.ApiMinimal   slice-api-minimal
scaffold_and_build Billing           slice-module   # module name is used as a class prefix → single identifier (no dots)
scaffold_and_build Acme.Monolith     slice-monolith
scaffold_and_build Acme.Worker       slice-worker
scaffold_and_build Acme.TenantHost   slice-tenant-api
scaffold_and_build Acme.TenantJob    slice-tenant-api --migrations job

echo ""
echo "All templates scaffolded and built successfully."
