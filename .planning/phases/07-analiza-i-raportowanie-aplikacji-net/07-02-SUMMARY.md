# Phase 7 - Plan 2 Summary: Enhanced `analyze` command

## What changed

- Enhanced `AnalysisReportBuilder.BuildReport()`:
  - Framework classification from TFM strings (.NET 10 → .NET Framework 4.x)
  - Assembly reference extraction via `Assembly.LoadFrom().GetReferencedAssemblies()`
  - Dependency storage to `assembly_references` table
  - NuGet package detection from assembly names (known packages) and .csproj parsing
  - License detection from LICENSE files in assembly directories
  - Build file detection (.sln, .csproj, Dockerfile, CI/CD configs)
  - Code metrics from decompiled artifacts (file count, line count, project count)
- Enhanced `MarkdownReportFormatter.Format()`:
  - Technology Stack section with frameworks, compilation modes, packages
  - Code Metrics table
  - Enhanced Assemblies table with Type, License, Size columns
  - Dependencies section grouped by assembly
  - Licenses Detected section
  - Build Files section
- Enhanced `HtmlReportFormatter.Format()`:
  - Dark theme matching modern dev tools (GitHub dark style)
  - Metric cards in responsive grid
  - Technology badges (badge-net, badge-pkg, badge-lic)
  - Scrollable dependency section
  - Responsive viewport meta tag

## Build result

- `dotnet build src/InfoExe.sln` succeeded (0 warnings, 0 errors)

## Verification

- Tested on GitExtensions scan (66 assemblies):
  - Technology Stack: detected C#, RestSharp, Newtonsoft.Json
  - Code Metrics: 66 assemblies, 8126 decompiled files, 681,413 LOC, 66 projects
  - Dependencies extracted for 30+ assemblies
  - MD report: 381+ lines, well-structured
  - HTML report: generated successfully with dark theme