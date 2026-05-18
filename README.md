# InfoExe — Lokalny analizator i dekompilator .NET

InfoExe to narzędzie do analizy binariów .NET (`.dll`, `.exe`) z możliwością dekompilacji. Działa offline, skanuje katalogi, zbiera metadane, identyfikuje producentów bibliotek i generuje raporty.

## Funkcje

- **Skanowanie katalogów** — analiza plików .NET w wybranym katalogu
- **Metadane** — zbieranie informacji o zależnościach, wersjach, technologii
- **Identyfikacja producenta** — rozpoznawanie bibliotek i ich twórców
- **Dekompilacja** — konwersja IL do kodu C# (wymaga ILSpy)
- **Raporty** — eksport do JSON, CSV, Markdown, HTML
- **GUI** — interfejs graficzny (Avalonia)
- **CLI** — interfejs wiersza poleceń dla automatyzacji

## Wymagania

- Windows 10/11 (x64)
- .NET 10.0 Runtime
- ILSpy (ilspycmd) — opcjonalnie, dla dekompilacji

## Pobieranie

**[Pobierz najnowszą wersję](https://github.com/passcode2026/InfoExe/releases/latest)**

[![Release](https://img.shields.io/github/v/release/passcode2026/InfoExe?label=latest)](https://github.com/passcode2026/InfoExe/releases/latest)

### Instalacja

1. Pobierz `InfoExe-Setup-1.1.0.exe` z [zakładki Releases](https://github.com/passcode2026/InfoExe/releases)
2. Uruchom instalator i postępuj zgodnie z instrukcjami
3. Uruchom "InfoExe Setup (Dependencies)" z Menu Start (opcjonalnie)

### Użytkowanie

**GUI:**
- Menu Start → InfoExe → InfoExe GUI

**CLI:**
```powershell
cd "C:\Program Files\InfoExe\CLI"
.\InfoExeApp.exe scan "C:\path\to\scan"
.\InfoExeApp.exe status
.\InfoExeApp.exe export json "output.json"
```

## Dokumentacja

Pełna dokumentacja dostępna w `.planning/PROJECT.md`

## Licencja

MIT License — zobacz LICENSE.txt

## Wsparcie

https://github.com/yourusername/InfoExe/issues
