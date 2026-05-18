# InfoExe — Podsumowanie instalatora

## ✅ Utworzone pliki

### 1. InfoExeInstaller.iss
Skrypt instalatora Inno Setup z następującymi funkcjami:
- Instalacja InfoExe GUI i CLI
- Sprawdzanie wymagań (.NET 10.0 Runtime, ILSpy)
- Tworzenie skrótów w Menu Start
- Odinstalator
- Wsparcie dla języka polskiego i angielskiego

### 2. build-installer.ps1
Automatyczny skrypt budowania, który:
- Buduje InfoExeGui jako self-contained exe
- Buduje InfoExeApp jako self-contained exe
- Kompiluje instalator Inno Setup

### 3. LICENSE.txt
Plik licencji MIT z informacjami o zależnościach.

### 4. README.md
Podstawowa dokumentacja dla użytkowników.

### 5. INSTALLER.md
Szczegółowa instrukcja budowania i używania instalatora.

## ✅ Zbudowane aplikacje

Projekty zostały pomyślnie zbudowane:

### InfoExeGui (GUI)
- **Lokalizacja**: `src/InfoExeGui/bin/Release/net10.0/win-x64/publish/`
- **Plik główny**: `InfoExeGui.exe` (~20 MB)
- **Zależności**: Avalonia UI, SkiaSharp, SQLite
- **Self-contained**: Tak (nie wymaga .NET Runtime)

### InfoExeApp (CLI)
- **Lokalizacja**: `src/InfoExeApp/bin/Release/net10.0/win-x64/publish/`
- **Plik główny**: `InfoExeApp.exe` (~14 MB)
- **Zależności**: Microsoft.Data.Sqlite
- **Self-contained**: Tak (nie wymaga .NET Runtime)
- **Uwaga**: Trimming wyłączony ze względu na użycie refleksji

## 📋 Następne kroki

### Aby utworzyć instalator:

1. **Zainstaluj Inno Setup 6.0+**
   - Pobierz z: https://jrsoftware.org/isdl.php
   - Zainstaluj w domyślnej lokalizacji

2. **Uruchom skrypt budowania**
   ```powershell
   .\build-installer.ps1
   ```

3. **Znajdź instalator**
   - Wynik: `installer-output/InfoExe-Setup-1.1.0.exe`

### Alternatywa - ręczna kompilacja:

Jeśli wolisz użyć Inno Setup bezpośrednio:
```powershell
ISCC.exe InfoExeInstaller.iss
```

## 🎯 Funkcje instalatora

### Dla użytkownika końcowego:
- ✅ Prosty kreator instalacji
- ✅ Wybór katalogu instalacji
- ✅ Skróty w Menu Start
- ✅ Opcjonalny skrót na pulpicie
- ✅ Sprawdzanie wymagań (.NET, ILSpy)
- ✅ Łatwe odinstalowanie
- ✅ Integracja z setup.ps1 dla zależności

### Dla dewelopera:
- ✅ Automatyczne budowanie
- ✅ Self-contained executables
- ✅ Obsługa języków (PL/EN)
- ✅ Kompresja LZMA2
- ✅ Solid compression
- ✅ Tylko x64

## 📝 Uwagi

### Wymagania użytkownika:
- Windows 10/11 (x64)
- .NET 10.0 Runtime (lub uruchomienie setup.ps1)
- ILSpy (ilspycmd) dla dekompilacji (opcjonalnie)

### Dane użytkownika:
- Baza SQLite: `%LOCALAPPDATA%\InfoExe\infoexe.db`
- Nie jest usuwana przy odinstalowaniu

### Skrypt setup.ps1:
- Jest dołączony do instalatora
- Może być uruchomiony z Menu Start
- Instaluje .NET SDK i ILSpy
- Inicjalizuje bazę danych

## 🔧 Rozwiązywanie problemów

### Jeśli build-installer.ps1 nie znajduje ISCC.exe:
Upewnij się, że Inno Setup jest zainstalowany w jednej z lokalizacji:
- `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
- `C:\Program Files\Inno Setup 6\ISCC.exe`
- `C:\Program Files (x86)\Inno Setup 5\ISCC.exe`
- `C:\Program Files\Inno Setup 5\ISCC.exe`

### Jeśli budowanie projektu nie działa:
Upewnij się, że masz .NET SDK 10.0:
```powershell
dotnet --version
```

## 📚 Dokumentacja

- `INSTALLER.md` - szczegółowa instrukcja
- `README.md` - dokumentacja użytkownika
- `LICENSE.txt` - licencja
- `setup.ps1` - skrypt zależności

## ✨ Podsumowanie

Instalator jest gotowy do użycia po zainstalowaniu Inno Setup. Wszystkie pliki zostały utworzone i skonfigurowane. Projekty są już zbudowane jako self-contained executables, gotowe do pakowania.
