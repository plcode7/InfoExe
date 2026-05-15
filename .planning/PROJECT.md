# InfoExe — lokalny analizator i dekompilator .NET

## What This Is

InfoExe to lokalne narzędzie CLI do analizy binariów .NET (`.dll`, `.exe`) z opcjonalnym torem dla artefaktów Python (`.py`, `.whl`). Aplikacja działa offline, skanuje wskazany katalog, zbiera metadane, identyfikuje producentów bibliotek i wykonuje dekompilację IL do projektu C# oraz raportów JSON/CSV.

## Core Value

Użytkownik może bezpiecznie i offline uzyskać wiarygodny, powtarzalny raport o składzie i pochodzeniu aplikacji .NET oraz dekompilowane źródła.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] Użytkownik uruchamia skan dla dowolnego `rootPath` i dostaje status zadania.
- [ ] Narzędzie rozpoznaje pliki .NET i wykonuje pasywną analizę metadanych bez uruchamiania obcego kodu.
- [ ] Narzędzie dekompiluje assembly IL do artefaktów źródłowych i zapisuje wynik.
- [ ] Raport zawiera identyfikację producenta na bazie metadanych i podpisów.
- [ ] Wyniki są zapisywane lokalnie w SQLite oraz eksportowane do JSON/CSV.
- [ ] CLI wspiera co najmniej komendy: `scan`, `status`, `export`, `retry`.

### Out of Scope

- Dynamiczne uruchamianie analizowanych binariów — ryzyko bezpieczeństwa, niezgodne z założeniem analizy pasywnej.
- Automatyczne omijanie DRM/ochron licencyjnych — poza zakresem legalnym i bezpieczeństwa.
- Rozbudowany GUI jako część v1 — priorytetem jest stabilny pipeline CLI.

## Context

Projekt bazuje na raporcie analitycznym z 2026-05-15. Rekomendowany rdzeń to ILSpy/ICSharpCode.Decompiler, analiza metadanych przez Mono.Cecil/dnlib/AsmResolver i lokalna persystencja w SQLite. Pipeline ma przetwarzać pliki wsadowo, używać kolejkowania in-memory z retry i utrzymywać pełny tryb offline. Kluczowe ograniczenia techniczne dotyczą plików AOT/obfuskowanych oraz przypadków ReadyToRun/single-file.

## Constraints

- **Security**: Analiza wyłącznie offline i pasywna — brak wykonywania badanych plików.
- **Legal**: Użycie wyłącznie dla legalnie posiadanych artefaktów i do celów interoperacyjności/diagnostyki.
- **Platform**: Główny scenariusz uruchomienia to lokalny laptop i CLI.
- **Persistence**: Wyniki muszą być trwałe lokalnie (SQLite + raporty plikowe).

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| ILSpy jako domyślny dekompilator | aktywny OSS, CLI + biblioteka, dobre pokrycie .NET | — Pending |
| SQLite jako magazyn wyników | prosty offline storage z dobrym queryability | — Pending |
| Kolejkowanie in-memory z retry | brak zależności od brokera i prostszy deployment lokalny | — Pending |
| CLI jako interfejs główny | najszybsza droga do działającego v1 i automatyzacji | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-05-15 after initialization*
