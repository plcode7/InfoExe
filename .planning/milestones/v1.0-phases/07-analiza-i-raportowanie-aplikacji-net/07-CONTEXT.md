# Phase 7 Context: Analiza i raportowanie aplikacji .NET

**Gathered:** 2026-05-16  
**Status:** Ready for planning  
**Mode:** User-driven (auto-mode, requirements specified)

## Phase Boundary

Rozszerz InfoExe o nowy pipeline analizy i raportowania. Po zakończonym skanie (faza 1-5, teraz dostępne w GUI fazy 6) użytkownik może:
1. Wygenerować raport analizy zeskanowanej aplikacji
2. Raport zawiera szczegółowe info o assemblies, DLL-kach, zależnościach i kodowaniu
3. Raport dostępny w formatach MD (Markdown) i HTML (przeglądarka)
4. GUI umożliwia wybór skanu i wygenerowanie raportu przez interfejs

## Implementation Decisions (Locked)

### 1. New CLI command: `analyze`
- **Command**: `infoexeapp analyze --id <scanId> --format <md|html> [--output <path>]`
- **Output**: Domyślnie `./scan_<scanId>_report.{md|html}`
- **Działanie**: Odczytuje wyniki skanu z DB, generuje strukturyzowany raport

### 2. Report Contents (Required)
- **Assembly Metadata**: Nazwa, wersja, target framework, signing info
- **Dependency Graph**: Lista referencji assembly (w tabeli MD/HTML)
- **DLL Analysis**: Path, size, version, culture, public key token
- **Encoding Detection**: Jeśli dodetektowane, typ kodowania (UTF-8, ASCII, itp.)
- **Summary**: Podsumowanie całości aplikacji

### 3. Format Strategy
- **MD (Markdown)**: Strukturalny markdown z tabelami, headings, code blocks
- **HTML**: Statyczny HTML (bez JavaScript), wygenerowany z szablonu, przeglądarka-friendly
- Biblioteka: Można użyć `MarkdownBuilder` (custom) lub biblioteki
- HTML: Generować za pomocą string-buildera lub simple template engine (np. Scriban lite)

### 4. Database Schema Extension
- **analyze_reports table**: id, scan_id, format, generated_at, output_path, status
- Przechowywanie metadanych raportu (nie treści — zbyt duże)

### 5. GUI Extensions
- **New form section**: "Generate Report"
- **Inputs**: Dropdown wyboru scan ID, radio buttons (MD/HTML), text field output path
- **Buttons**: "Generate Report", "Open in Explorer", "Open in Browser" (jeśli HTML)
- **Live preview**: TextBox pokazujący status generacji

### 6. Scope Decisions
- **CLI-first**: analyze command działa standalone, niezależnie od GUI
- **No PDF**: Tylko MD i HTML (PDF to kompleks library + wersjonowanie)
- **No cloud**: 100% offline, raporty w lokalnym FS
- **Deferred**: Porównywanie raportów, custom templates, export do SBOM — Phase 8+

## Code Reuse & Patterns

- **Database access**: Istniejące `OpenConnection()` pattern z Program.cs
- **Metadata extraction**: Reuse z `ExtractManagedMetadata()` z Phase 1-2
- **CLI argument parsing**: Reuse z `GetOption()`, `GetOptionWithAliases()`
- **GUI subprocess model**: Reuse z `RunCliAsync()` pattern z InfoExeGui
- **Polish/English localization**: Reuse z `IsPolishHelp()` i prompts

## Success Criteria

1. ✓ `analyze --id <scanId> --format md` generuje raport MD
2. ✓ `analyze --id <scanId> --format html` generuje raport HTML
3. ✓ Raport zawiera wszystkie wymagane sekcje (assembly, deps, DLL, encoding, summary)
4. ✓ GUI form do konfiguracji analizy i otwierania raportów
5. ✓ Raport jest czytelny i użyteczny dla nauki (formatowanie, linki, tabele)

## Out of Scope (Phase 7)

- PDF export
- Szablony do customizacji
- Porównywanie raportów
- SBOM export
- Interactive web viewer
