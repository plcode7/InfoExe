<!-- GSD:project-start source:PROJECT.md -->
## Project

**InfoExe — lokalny analizator i dekompilator .NET**

InfoExe to lokalne narzędzie CLI do analizy binariów .NET (`.dll`, `.exe`) z opcjonalnym torem dla artefaktów Python (`.py`, `.whl`). Aplikacja działa offline, skanuje wskazany katalog, zbiera metadane, identyfikuje producentów bibliotek i wykonuje dekompilację IL do projektu C# oraz raportów JSON/CSV.

**Core Value:** Użytkownik może bezpiecznie i offline uzyskać wiarygodny, powtarzalny raport o składzie i pochodzeniu aplikacji .NET oraz dekompilowane źródła.

### Constraints

- **Security**: Analiza wyłącznie offline i pasywna — brak wykonywania badanych plików.
- **Legal**: Użycie wyłącznie dla legalnie posiadanych artefaktów i do celów interoperacyjności/diagnostyki.
- **Platform**: Główny scenariusz uruchomienia to lokalny laptop i CLI.
- **Persistence**: Wyniki muszą być trwałe lokalnie (SQLite + raporty plikowe).
<!-- GSD:project-end -->

<!-- GSD:stack-start source:research/STACK.md -->
## Technology Stack

## Recommended Stack
| Area | Choice | Notes |
|---|---|---|
| Runtime | .NET 10 LTS | Stabilny horyzont wsparcia, nowoczesne API |
| CLI | System.CommandLine 2.x | Główny interfejs `scan/status/export/retry` |
| Decompilation | ICSharpCode.Decompiler + `ilspycmd` | Spójny silnik ILSpy dla biblioteki i CLI |
| Metadata | Mono.Cecil (primary), dnlib (fallback) | Szerokie pokrycie przypadków .NET |
| Persistence | SQLite + Microsoft.Data.Sqlite | Lokalny, offline, prosty deployment |
| Queue/Retry | System.Threading.Channels | In-memory pipeline z kontrolą backpressure |
| JSON | System.Text.Json | Wbudowane, szybkie |
| CSV | CsvHelper | Stabilny eksport tabelaryczny |
| Logging | Serilog (file sink) | Diagnostyka lokalna |
## Architecture-Level Decisions
## What Not To Use
- Preview toolchain w produkcyjnym MVP.
- Zewnętrzne brokery kolejek (overkill dla lokalnego scenariusza).
- Podejścia wymagające wykonywania analizowanych binariów.
- GUI-first jako priorytet v1 (opóźnia dostarczenie rdzenia).
## Confidence
- **High:** .NET 10 + ILSpy + SQLite + CLI baseline
- **Medium-High:** Cecil + dnlib split
- **Medium:** Tuning wydajności i limity konkurencji zależne od realnych danych
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->
## Project Skills

No project skills found. Add skills to any of: `.github/skills/`, `.agents/skills/`, `.cursor/skills/`, or `.github/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->



<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
