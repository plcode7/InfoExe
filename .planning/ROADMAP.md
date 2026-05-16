# Roadmap: InfoExe

**Created:** 2026-05-15
**Updated:** 2026-05-16
**Mode:** yolo (auto)

## Milestones

- ✅ **v1.0 milestone** — Phases 1-7 (shipped 2026-05-16)
- 🚧 **v1.1 Samodzielna aplikacja** — Phases 8-9 (in progress)

## Phases

<details>
<summary>✅ v1.0 milestone (Phases 1-7) — SHIPPED 2026-05-16</summary>

- [x] Phase 1: Foundations & Scan Engine — completed 2026-05-15
- [x] Phase 2: Metadata & Vendor Attribution — completed 2026-05-15
- [x] Phase 3: Decompilation & Reliability — completed 2026-05-15
- [x] Phase 4: Reporting & Exports — completed 2026-05-15
- [x] Phase 5: Optional Python Lane — completed 2026-05-15
- [x] Phase 6: GUI Avalonia — completed 2026-05-16
- [x] Phase 7: Analyze & Reports — completed 2026-05-16

</details>

### v1.1 Phases

### Phase 8: PowerShell bootstrap i auto-konfiguracja

**Goal:** Stworzyć skrypt `setup.ps1`, który po jednorazowym uruchomieniu pobiera i konfiguruje wszystkie zależności (ILSpy, .NET SDK, NuGet, SQLite), tak aby aplikacja była gotowa do pracy bez manualnej konfiguracji.

**Requirements:** SETUP-01, SETUP-02, SETUP-03, SETUP-04
**Depends on:** Phase 7

**Success criteria:**
1. `setup.ps1` pobiera ILSpy jako `dotnet tool` i weryfikuje dostępność `ilspycmd`
2. `setup.ps1` sprawdza .NET SDK 10.0, instaluje przez winget jeśli brak
3. `setup.ps1` wykonuje `dotnet restore src/InfoExe.sln` — wszystkie pakiety przywrócone
4. `setup.ps1` tworzy `%LOCALAPPDATA%/InfoExe/` i inicjalizuje infoexe.db
5. Skrypt jest idempotentny — można go uruchomić wielokrotnie bez błędów

**UI hint:** no

---

### Phase 9: GUI standalone — wbudowana logika i live progress

**Goal:** Przenieść logikę skanowania i analizy bezpośrednio do InfoExeGui, eliminując zależność od zewnętrznego procesu CLI. Dodać progress bar, live output stream i automatyczne wykrywanie narzędzi.

**Requirements:** GUI-11, GUI-12, GUI-13, GUI-14, GUI-15, TOOLS-01, TOOLS-02, TOOLS-03
**Depends on:** Phase 8

**Success criteria:**
1. Kliknięcie "Skanuj" uruchamia skanowanie w procesie GUI (nie przez subprocess)
2. Progress bar aktualizuje się w czasie rzeczywistym (pliki/total)
3. Output box pokazuje logi dekompilacji na żywo (stdout/stderr ILSpy)
4. Po skanie przycisk "Generuj raport" jest automatycznie dostępny z wybranym scanId
5. Przy starcie GUI weryfikuje obecność ILSpy i .NET SDK — pokazuje komunikat jeśli brak
6. Ścieżka do ILSpy wykrywana automatycznie (dotnet tool list + PATH fallback)

**UI hint:** yes