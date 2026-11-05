# Dependency and licence review

Every NuGet package that reaches the restore graph must appear in
[`allowed-packages.txt`](allowed-packages.txt). CI runs
`python3 scripts/sbom/generate.py --check`, which fails with the package names that are missing,
and uploads a CycloneDX 1.5 SBOM as a build artifact.

This is deliberately a **review gate**, not a licence scanner: the build does not guess a
licence from a nuspec URL, because a wrong guess is worse than no guess. A human records the
licence when a package is added.

## Licences in the current graph

| Family | Licence | Notes |
|---|---|---|
| `Microsoft.*`, `System.*` | MIT | .NET runtime and extensions |
| `Npgsql`, `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL licence | permissive, MIT-like |
| `OpenIddict.*` | Apache-2.0 | OIDC server/validation |
| `Serilog.*` | Apache-2.0 | logging |
| `OpenTelemetry.*` | Apache-2.0 | telemetry (shipped by the ASP.NET defaults) |
| `Polly*` | BSD-3-Clause | resilience |
| `xunit*` | Apache-2.0 | test-only |
| `AngleSharp`, `HtmlSanitizer`, `Humanizer`, `Newtonsoft.Json`, `StackExchange.Redis`, `RESPite`, `coverlet.collector`, `Mono.TextTemplating` | MIT | |

No package in the graph is copyleft. Nothing here is redistributed as source; the container
ships compiled assemblies plus their licence files.

## Adding a package

1. Read the licence on nuget.org and confirm it is permissive or already budgeted.
2. Add the exact package id (case-insensitive, one per line) under the right heading in
   `allowed-packages.txt`.
3. Note anything that is not MIT/Apache-2.0/BSD in the table above.
4. `python3 scripts/sbom/generate.py --check` must pass before the PR is merged.

## What is out of scope

- Transitive packages pulled in by the .NET SDK itself (`Microsoft.AspNetCore.App.Ref`) are in
  the graph and therefore in the allowlist, but are not shipped as separate redistributables.
- JavaScript packages: `tailwindcss` is a build-time dependency only; no JavaScript runtime
  package is shipped to browsers apart from the Blazor framework in the shared runtime image.
