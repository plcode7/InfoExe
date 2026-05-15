# Phase 1 Plan — Foundations & Scan Engine

**Phase:** 1  
**Name:** Foundations & Scan Engine  
**Mode:** Auto (YOLO)  
**Status:** Ready

## Goal

Dostarczyć minimalny, stabilny fundament aplikacji: CLI `scan/status`, stan skanu w SQLite i gwarancję pasywnej analizy (bez uruchamiania obcych binariów).

## Inputs

- `.planning/ROADMAP.md` (Phase 1)
- `.planning/REQUIREMENTS.md` (SCAN-01, SCAN-02, SCAN-04, DECO-03, DATA-01)
- `.planning/phases/01-foundations-scan-engine/01-CONTEXT.md`

## Execution Plan

1. **Bootstrap projektu**
   - Utworzyć szkielet projektu .NET CLI i strukturę modułów (`cli`, `scan`, `storage`, `domain`).
   - Skonfigurować podstawowe logowanie lokalne.

2. **Model danych + SQLite**
   - Zdefiniować tabele `scan_jobs`, `scan_files`, `stage_events`.
   - Dodać repozytorium zapisujące/odczytujące stan skanu.

3. **Pipeline skanu (v1)**
   - `scan --root <path>`: enumeracja plików i zapis `scanId`.
   - Detekcja podstawowych typów plików i zapis statusów.

4. **Status API CLI**
   - `status --id <scanId>`: agregaty discovered/processed/failed/partial.

5. **Guardrails bezpieczeństwa**
   - Wymusić pasywny tryb przetwarzania (brak uruchamiania analizowanych binariów).
   - Dodać jawne reason codes dla pominięć/ograniczeń.

## Deliverables

- Działający szkielet CLI z komendami `scan`, `status`
- Lokalny store SQLite z minimalnym modelem postępu
- Podstawowy pipeline skanu
- Dokumentacja statusów i reason codes

## Verification Targets

1. `scan --root` tworzy scan job i zwraca scanId.
2. `status --id` pokazuje prawidłowe agregaty.
3. Dane skanu są trwałe po restarcie procesu.
4. Kod nie uruchamia analizowanych plików.

## Risks & Mitigation

- **Brak kodu bazowego:** zacząć od minimalnego pionowego slice (scan + status + DB).
- **Drift modelu danych:** trzymać spójny kontrakt statusów od początku.
