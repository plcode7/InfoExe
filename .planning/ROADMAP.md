# Roadmap: InfoExe

**Created:** 2026-05-15  
**Mode:** yolo (auto)  
**Granularity:** coarse

## Phase Overview

| Phase | Name | Goal | Requirements |
|---|---|---|---|
| 1 | Foundations & Scan Engine | Zbudować fundament runtime, dane, bezpieczeństwo i podstawowy skan | SCAN-01, SCAN-02, SCAN-04, DECO-03, DATA-01 |
| 2 | Metadata & Vendor Attribution | Dostarczyć wiarygodną analizę metadanych i vendor confidence | META-01, META-02, META-03, VEND-01, VEND-02, VEND-03 |
| 3 | Decompilation & Reliability | Ukończyć dekompilację IL i odporny retry model | DECO-01, DECO-02, SCAN-03 |
| 4 | Reporting & Exports | Dostarczyć komplet raportowy JSON/CSV z provenance | DATA-02, DATA-03, DATA-04 |
| 5 | Optional Python Lane | Dodać opcjonalny pasywny tor analizy Python | PY-01, PY-02 |

## Phase Details

### Phase 1: Foundations & Scan Engine
Goal: Stabilny rdzeń CLI + model danych + pipeline scan w pełni offline.
Requirements: SCAN-01, SCAN-02, SCAN-04, DECO-03, DATA-01
Success criteria:
1. `scan --root` uruchamia pełny przebieg odkrywania plików i zapisuje scanId.
2. Pipeline nie uruchamia analizowanych binariów na żadnym etapie.
3. SQLite przechowuje stany etapów i pozwala wznowić pracę po błędzie.
4. `status` pokazuje stan i agregaty przetwarzania.

**UI hint**: no

### Phase 2: Metadata & Vendor Attribution
Goal: Wiarygodna interpretacja metadanych i producenta z jawnie wyrażoną pewnością.
Requirements: META-01, META-02, META-03, VEND-01, VEND-02, VEND-03
Success criteria:
1. Każdy plik .NET ma wynik klasyfikacji capability (IL/R2R/single-file/AOT-limited).
2. Metadane assembly i referencji zapisują się spójnie do modelu.
3. Vendor result zawiera evidence trail i confidence score.
4. Konfliktujące dowody skutkują stanem inconclusive zamiast fałszywej pewności.

**UI hint**: no

### Phase 3: Decompilation & Reliability
Goal: Niezawodna dekompilacja IL z kontrolą błędów i retry.
Requirements: DECO-01, DECO-02, SCAN-03
Success criteria:
1. IL-capable assembly dekompilują się do artefaktów źródłowych.
2. Unsupported/partial przypadki mają jawny reason code.
3. Retry działa per-stage i respektuje limity prób/backoff.
4. Stuck jobs są wykrywane i eskalowane do trwałego statusu błędu.

**UI hint**: no

### Phase 4: Reporting & Exports
Goal: Gotowe raportowanie operacyjne i audytowe.
Requirements: DATA-02, DATA-03, DATA-04
Success criteria:
1. `export` tworzy poprawny raport JSON dla scanId.
2. `export` tworzy zgodny raport CSV dla scanId.
3. Parzystość danych DB/JSON/CSV jest zachowana dla kluczowych pól.
4. Raport zawiera provenance wymagane do odtworzenia kontekstu.

**UI hint**: no

### Phase 5: Optional Python Lane
Goal: Poszerzyć zakres o opcjonalną analizę artefaktów Python bez naruszenia modelu bezpieczeństwa.
Requirements: PY-01, PY-02
Success criteria:
1. Przełącznik include-python uruchamia analizę `.py`/`.whl`.
2. Python lane jest statyczny/pasywny (brak wykonania kodu).
3. Wyniki Python trafiają do wspólnego modelu i eksportów.

**UI hint**: no

## Coverage

- v1 requirements: 19
- mapped: 19
- unmapped: 0

### Phase 6: Dodaj osobny projekt GUI Avalonia dla Windows do zarządzania parametrami InfoExe, współpracujący z CLI lub działający samodzielnie

**Goal:** Polish and fix the existing InfoExeGui companion app: create a unified solution file, upgrade Avalonia to 11.3.x, and fix the CLI-path auto-discovery bug so the GUI is ready for daily use.
**Requirements**: GUI-01, GUI-02, GUI-03
**Depends on:** Phase 5
**Plans:** 2 plans

Requirements:
- GUI-01: Unified `src/InfoExe.sln` containing both InfoExeApp and InfoExeGui projects
- GUI-02: Avalonia packages at 11.3.15 with a clean build (no NU1903 advisory warning)
- GUI-03: `ResolveDefaultCliPath` correctly resolves InfoExeApp.exe in both dev-layout (4-level relative path) and xcopy-install-layout (same folder as GUI exe)

Plans:
- [ ] 06-01-PLAN.md — Create InfoExe.sln + upgrade Avalonia 11.2.0 → 11.3.15 + suppress NU1903
- [ ] 06-02-PLAN.md — Fix ResolveDefaultCliPath (5→4 levels + xcopy probe) + smoke-test checkpoint
