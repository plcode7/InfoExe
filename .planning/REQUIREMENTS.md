# Requirements: InfoExe v1.1

**Milestone:** v1.1 — Samodzielna aplikacja z auto-konfiguracją
**Created:** 2026-05-16

## Active Requirements

### Setup & Auto-Configuration

- [ ] **SETUP-01**: Użytkownik uruchamia `setup.ps1`, który pobiera i instaluje ILSpy (ilspycmd) jako narzędzie globalne
- [ ] **SETUP-02**: `setup.ps1` weryfikuje obecność .NET SDK 10.0 i instaluje go jeśli brak (winget lub dotnet-install.ps1)
- [ ] **SETUP-03**: `setup.ps1` wykonuje `dotnet restore` dla wszystkich projektów w solution
- [ ] **SETUP-04**: `setup.ps1` tworzy strukturę katalogów i inicjalizuje bazę SQLite

### Standalone GUI

- [ ] **GUI-11**: Logika skanowania i analizy jest wbudowana bezpośrednio w InfoExeGui (brak subprocess do CLI)
- [ ] **GUI-12**: Progress bar pokazuje postęp skanowania (pliki przetworzone / całkowite)
- [ ] **GUI-13**: Live output stream pokazuje logi dekompilacji i analizy w czasie rzeczywistym
- [ ] **GUI-14**: Przycisk "Analizuj" dostępny od razu po skanie, bez ręcznego wybierania scanId
- [ ] **GUI-15**: Przycisk "Otwórz raport" otwiera wygenerowany plik w domyślnej przeglądarce/aplikacji

### Tool Discovery

- [ ] **TOOLS-01**: Aplikacja automatycznie wykrywa ścieżkę do ILSpy po instalacji przez setup.ps1
- [ ] **TOOLS-02**: Aplikacja weryfikuje dostępność .NET SDK przy starcie
- [ ] **TOOLS-03**: Przy braku narzędzi GUI pokazuje przyjazny komunikat z linkiem do `setup.ps1`

## Future Requirements (Deferred)

- Porównywanie raportów między skanami
- Custom templates dla raportów
- SBOM export

## Out of Scope

- Dynamiczne uruchamianie analizowanych binariów — ryzyko bezpieczeństwa
- PDF export — kompleksowa biblioteka + wersjonowanie
- Cross-platform (Linux/macOS) — Windows-first dla v1.1

## Traceability

| REQ-ID | Phase | Status |
|--------|-------|--------|
| SETUP-01 | Phase 8 | Not started |
| SETUP-02 | Phase 8 | Not started |
| SETUP-03 | Phase 8 | Not started |
| SETUP-04 | Phase 8 | Not started |
| GUI-11 | Phase 9 | Not started |
| GUI-12 | Phase 9 | Not started |
| GUI-13 | Phase 9 | Not started |
| GUI-14 | Phase 9 | Not started |
| GUI-15 | Phase 9 | Not started |
| TOOLS-01 | Phase 9 | Not started |
| TOOLS-02 | Phase 9 | Not started |
| TOOLS-03 | Phase 9 | Not started |