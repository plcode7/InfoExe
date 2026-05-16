# InfoExe — lokalny analizator i dekompilator .NET

## What This Is

InfoExe to lokalne narzędzie CLI i GUI (Avalonia) do analizy binariów .NET (`.dll`, `.exe`) z opcjonalnym torem dla artefaktów Python (`.py`, `.whl`). Aplikacja działa offline, skanuje wskazany katalog, zbiera metadane, identyfikuje producentów bibliotek, wykonuje dekompilację IL do projektu C#, generuje raporty JSON/CSV oraz kompleksowe raporty analityczne MD/HTML z technologiami, zależnościami, licencjami i metrykami kodu.

## Core Value

Użytkownik może bezpiecznie i offline uzyskać wiarygodny, powtarzalny raport o składzie, technologiach, zależnościach i pochodzeniu aplikacji .NET oraz dekompilowane źródła.

## Requirements

### Validated

- ✓ Uruchamianie skanu dla dowolnego katalogu — v1.0
- ✓ Pasywna analiza metadanych .NET bez uruchamiania obcego kodu — v1.0
- ✓ Dekompilacja IL do artefaktów źródłowych — v1.0
- ✓ Identyfikacja producenta na bazie metadanych i podpisów — v1.0
- ✓ Wyniki w SQLite + eksport JSON/CSV — v1.0
- ✓ Komendy CLI: `scan`, `status`, `export`, `retry`, `analyze` — v1.0
- ✓ GUI Avalonia do zarządzania parametrami i uruchamiania CLI — v1.0
- ✓ Raportowanie analityczne MD/HTML z technologiami, zależnościami, licencjami — v1.0

### Active

- [ ] Porównywanie raportów między skanami
- [ ] Custom templates dla raportów
- [ ] SBOM export

### Out of Scope

- Dynamiczne uruchamianie analizowanych binariów — ryzyko bezpieczeństwa
- Automatyczne omijanie DRM/ochron licencyjnych — poza zakresem legalnym
- PDF export — kompleksowa biblioteka + wersjonowanie

## Current Milestone: v1.1 Samodzielna aplikacja z auto-konfiguracją

**Goal:** Aplikacja Avalonia działa w pełni samodzielnie — po `git clone` użytkownik uruchamia `setup.ps1`, który pobiera ILSpy, instaluje zależności, konfiguruje środowisko. GUI obsługuje wszystko w procesie (bez zewnętrznego CLI).

**Target features:**
- Skrypt `setup.ps1` pobierający i konfigurujący ILSpy, .NET SDK, NuGet, SQLite
- Wbudowana logika skanowania/analizy w GUI (brak zależności od InfoExeApp.exe)
- Progress bar i live output podczas skanowania i analizy
- Automatyczne wykrywanie i konfiguracja ścieżek do narzędzi

## Context

**Current state (v1.0 shipped):**
- 2 projekty w unified solution (`src/InfoExe.sln`)
- `InfoExeApp` — CLI (net10.0, Microsoft.Data.Sqlite 10.0.0), ~2000 LOC w Program.cs
- `InfoExeGui` — GUI Avalonia 11.3.15, ~600 LOC + 2 pliki AXAML
- SQLite schema: scan_jobs, scan_files, assembly_metadata, vendor_evidence, vendor_results, decompilation_results, analyze_reports, assembly_references, license_detections
- Pipeline: scan → classify → metadata → vendor → decompile → analyze
- ILSpy jako dekompilator zewnętrzny (wymagany w PATH)
- Pełny tryb offline, brak zależności sieciowych

## Constraints

- **Security**: Analiza wyłącznie offline i pasywna — brak wykonywania badanych plików.
- **Legal**: Użycie wyłącznie dla legalnie posiadanych artefaktów i do celów interoperacyjności/diagnostyki.
- **Platform**: Windows (CLI + GUI Avalonia).
- **Persistence**: Wyniki muszą być trwałe lokalnie (SQLite + raporty plikowe).

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| ILSpy jako domyślny dekompilator | Aktywny OSS, CLI + biblioteka, dobre pokrycie .NET | ✓ Good — działa z retry |
| SQLite jako magazyn wyników | Prosty offline storage z dobrym queryability | ✓ Good — schema 9 tabel |
| Kolejkowanie in-memory z retry | Brak zależności od brokera, prostszy deployment lokalny | ✓ Good |
| CLI jako interfejs główny | Najszybsza droga do działającego v1 i automatyzacji | ✓ Good — 6 komend |
| Avalonia 11.3.15 dla GUI | Nowoczesny, cross-platform UI framework dla .NET | ✓ Good — unified solution |
| Analiza technologii przez refleksję | Assembly.LoadFrom + GetReferencedAssemblies | ✓ Good — wykrywa pakiety NuGet |

## Evolution

This document evolves at phase transitions and milestone boundaries.

---
*Last updated: 2026-05-16 — v1.1 milestone started*