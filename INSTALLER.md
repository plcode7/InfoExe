# InfoExe - Instrukcja instalatora

## Budowanie instalatora

### Wymagania wstępne

1. **.NET SDK 10.0** - do budowania projektu
   ```
   winget install Microsoft.DotNet.SDK.10
   ```

2. **Inno Setup 6.0+** - do tworzenia instalatora
   - Pobierz z: https://jrsoftware.org/isdl.php
   - Zainstaluj w domyślnej lokalizacji

### Proces budowania

Uruchom skrypt budowania:

```powershell
.\build-installer.ps1
```

Skrypt wykona następujące kroki:
1. Zbuduje InfoExeGui jako self-contained exe
2. Zbuduje InfoExeApp jako self-contained exe
3. Skompiluje instalator Inno Setup

Wynikowy instalator znajdzie się w katalogu `installer-output/`.

## Używanie instalatora

### Dla użytkownika końcowego

1. Uruchom `InfoExe-Setup-{version}.exe`
2. Postępuj zgodnie z instrukcjami kreatora
3. Wybierz katalog instalacji (domyślnie: `C:\Program Files\InfoExe`)

### Wymagania systemowe

- **System**: Windows 10/11 (x64)
- **.NET**: Runtime 10.0 (wymagany do działania)
- **Opcjonalnie**: ILSpy (ilspycmd) dla funkcji dekompilacji

### Automatyczna instalacja zależności

Po instalacji możesz uruchomić:
- **Menu Start** → **InfoExe** → **InfoExe Setup (Dependencies)**

To uruchomi `setup.ps1`, który zainstaluje:
- .NET 10.0 SDK (jeśli brak)
- ILSpy (ilspycmd) jako global tool
- Zainicjuje bazę SQLite

## Struktura instalatora

### Zawartość

- **InfoExeGui.exe** - główna aplikacja GUI
- **CLI/** - katalog z aplikacją CLI
  - `InfoExeApp.exe` - aplikacja wiersza poleceń
- **setup.ps1** - skrypt instalacji zależności
- **README.md** - dokumentacja projektu

### Skróty

Instalator tworzy następujące skróty w Menu Start:
- **InfoExe GUI** - uruchamia główną aplikację
- **InfoExe CLI** - uruchamia aplikację CLI
- **InfoExe Setup (Dependencies)** - instaluje zależności
- **Odinstaluj** - usuwa program

## Konfiguracja po instalacji

### Baza danych

Baza SQLite jest tworzona automatycznie w:
```
%LOCALAPPDATA%\InfoExe\infoexe.db
```

### Dane użytkownika

Wyniki skanów i raporty są domyślnie zapisywane w katalogu roboczym.

## Rozwiązywanie problemów

### .NET Runtime nie jest zainstalowany

Jeśli aplikacja nie uruchamia się:
1. Uruchom "InfoExe Setup (Dependencies)" z Menu Start
2. Lub ręcznie zainstaluj .NET 10.0 Runtime:
   ```
   winget install Microsoft.DotNet.Runtime.10
   ```

### Funkcja dekompilacji nie działa

ILSpy jest wymagany do dekompilacji:
1. Uruchom "InfoExe Setup (Dependencies)" z Menu Start
2. Lub ręcznie zainstaluj:
   ```
   dotnet tool install --global ilspycmd
   ```

### Problemy z uprawnieniami

Jeśli aplikacja nie może zapisywać w katalogu programu:
- Uruchom jako administrator
- Lub zmień lokalizację danych w ustawieniach

## Odinstalowanie

Użyj skrótu "Odinstaluj" w Menu Start lub:
1. Otwórz **Panel sterowania** → **Programy i funkcje**
2. Znajdź **InfoExe**
3. Kliknij **Odinstaluj**

**Uwaga**: Baza danych w `%LOCALAPPDATA%\InfoExe` NIE jest usuwana przy odinstalowaniu.

## Dalsza pomoc

- Dokumentacja projektu: `README.md`
- Problem z instalatorem: sprawdź logi w `installer-output/`
- Wsparcie: https://github.com/yourusername/InfoExe/issues
