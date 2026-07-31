# Local development

## SQL Server (verification oracle)

```
cp .env.example .env      # override SILENTSCAN_SA_PASSWORD if you want
docker compose up -d
```

Connects on `localhost,14330`, user `sa`. Used by `SilentScan.Verify` (lineage
oracle against `sys.columns`, plan-XML `CONVERT_IMPLICIT` confirmation) and
`SilentScan.Bench`. Compat level is pinned to 160 per-database by the tooling,
not at the server level — each spike/bench database sets it explicitly after
`CREATE DATABASE`.

## Build & test

```
dotnet build
dotnet test
```

`Directory.Build.props` treats warnings as errors and enables recommended
analyzers solution-wide; a red build is a real defect, not noise to suppress.

## Sonar

```
pwsh ./sonar-scan.ps1              # build + test + coverage + upload
./sonar-check-issues.sh            # print open issues + quality gate status
```

The SonarQube MCP server is disabled in this session (it pages full issue
objects and burns context). `sonar-check-issues.sh` hits the REST API
directly with `curl`/`jq` and prints a compact table instead — use that to
check gate status before every commit, per CLAUDE.md.
