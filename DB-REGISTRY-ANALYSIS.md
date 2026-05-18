# InfoExe — Analiza Baz Danych i Rejestru Windows

## Przegląd

Rozszerzenie InfoExe o możliwość analizy lokalnych baz danych, rejestru Windows oraz plików konfiguracyjnych zawierających connection stringi.

## Komendy CLI

### `dbscan` — Skanowanie lokalnych baz danych

```
InfoExeApp dbscan [--db <ścieżka>]
```

Wykrywa i analizuje:
- **SQLite** — pliki `.db`, `.sqlite`, `.sqlite3` w AppData, ProgramData
- **MySQL** — działające serwisy (`MySQL80`, `MariaDB`), katalogi danych (`C:\ProgramData\MySQL\`)
- **SQL Server** — instancje (`MSSQLSERVER`, `SQLEXPRESS`), pliki `.mdf`
- **PostgreSQL** — serwisy, katalogi danych (`C:\Program Files\PostgreSQL\`)
- **Firebird** — serwisy, pliki `.fdb`
- **Oracle** — serwisy (`OracleServiceORCL`, `OracleServiceXE`)
- **MS Access** — pliki `.mdb`, `.accdb`
- **dBase/Interbase** — pliki `.dbf`, `.ib`, `.gdb`
- **Generic** — pliki `.dat` w katalogach programów

Wyniki zapisywane w tabeli `db_discoveries`.

### `regscan` — Analiza rejestru Windows

```
InfoExeApp regscan [--db <ścieżka>]
```

Analizuje rejestr w poszukiwaniu:

#### Kategorie znalezisk:

| Kategoria | Opis |
|-----------|------|
| `startup` | Wpisy autostartu (HKLM/HKCU Run, RunOnce) |
| `broken_path` | Uszkodzone ścieżki — pliki nie istnieją |
| `com_registration` | Rejestracje COM z brakującymi DLL |
| `shell_extension` | Rozszerzenia powłoki Windows |
| `app_config` | Konfiguracje aplikacji (ODBC, MySQL, MSSQL, Oracle) |
| `connection_string` | Connection stringi w rejestrze |
| `license` | Klucze licencyjne i rejestracyjne |
| `unknown` | Podejrzane wpisy (BHO, Winlogon, Image Hijacks) |

#### Poziomy ważności:

| Poziom | Znaczenie |
|--------|-----------|
| `error` | Plik nie istnieje, uszkodzony wpis |
| `warning` | Potencjalny problem (np. w Temp) |
| `suspicious` | Podejrzany wpis (cmd.exe, powershell, skrypty) |
| `info` | Informacja, bez akcji |

#### Skanowane lokalizacje rejestru:

- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run*`
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run*`
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
- `HKCR\CLSID` (rejestracje COM)
- `HKLM\SYSTEM\CurrentControlSet\Services` (uszkodzone serwisy)
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects`
- `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`
- `HKLM\SOFTWARE\ODBC\ODBC.INI`
- `HKLM\SOFTWARE\Microsoft\MSSQLServer`
- `HKLM\SOFTWARE\MySQL AB`
- `HKLM\SOFTWARE\PostgreSQL`
- `HKLM\SOFTWARE\Oracle`

Wyniki zapisywane w tabeli `registry_keys`.

### `configscan` — Skanowanie plików konfiguracyjnych

```
InfoExeApp configscan --path <katalog> [--db <ścieżka>]
```

Skanuje pliki konfiguracyjne w poszukiwaniu connection stringów.

#### Obsługiwane formaty:

| Format | Rozszerzenia | Metoda parsowania |
|--------|-------------|-------------------|
| JSON | `.json` | `ConnectionStrings` section |
| XML | `.config`, `.xml` | `<connectionStrings><add>` |
| ENV | `.env` | `DB_*`, `DATABASE_*`, `MYSQL_*` zmienne |
| INI/CFG | `.ini`, `.cfg`, `.conf` | Key=value pairs |
| PHP | `.php` | `$db = [...]` arrays |
| Python | `.py` | Dict z kluczami DB |
| YAML | `.yaml`, `.yml` | Klucze `database:`, `host:` |

#### Bezpieczeństwo:

- **Maskowanie haseł** — `Password=***` w connection stringach
- **Hashowanie** — SHA256 hashy haseł do porównywania duplikatów (nie przechowujemy plaintext)
- **Maskowanie userów** — `User ID=***` gdy `--mask-credentials`

Wyniki zapisywane w tabeli `config_connections`.

## Schemat bazy danych

### Nowe tabele (dodane do istniejącego infoexe.db):

```sql
-- Odkryte bazy danych
CREATE TABLE db_discoveries (
    discovery_id      TEXT PRIMARY KEY,
    database_type     TEXT NOT NULL,     -- mysql, sqlite, mssql, postgresql, oracle, firebird
    location          TEXT NOT NULL,     -- ścieżka lub host
    database_name     TEXT,              -- nazwa bazy
    connection_string TEXT,              -- pełny connection string
    source_file       TEXT,              -- plik źródłowy (dla file-based)
    file_size_bytes   INTEGER,          -- rozmiar pliku
    version           TEXT,              -- wykryta wersja
    status            TEXT NOT NULL,     -- accessible, inaccessible, file_only
    error_message     TEXT,              -- błąd jeśli inaccessible
    discovered_at_utc TEXT NOT NULL
);

-- Znaleziska w rejestrze
CREATE TABLE registry_keys (
    finding_id      TEXT PRIMARY KEY,
    hive            TEXT NOT NULL,       -- HKLM, HKCU, HKCR, HKU, HKCC
    key_path        TEXT NOT NULL,       -- pełna ścieżka rejestru
    value_name      TEXT,                -- nazwa wartości
    value_data      TEXT,                -- dane (zamaskowane)
    value_type      TEXT,                -- REG_SZ, REG_DWORD, REG_BINARY...
    severity        TEXT NOT NULL,       -- info, warning, error, suspicious
    category        TEXT NOT NULL,       -- startup, broken_path, com_registration...
    description     TEXT,                -- opis czytelny dla człowieka
    recommendation  TEXT,                -- sugerowana akcja
    discovered_at_utc TEXT NOT NULL
);

-- Połączenia z plików konfiguracyjnych
CREATE TABLE config_connections (
    config_id         TEXT PRIMARY KEY,
    file_path         TEXT NOT NULL,     -- plik konfiguracyjny
    file_type         TEXT NOT NULL,     -- json, xml, env, php, python, yaml
    database_type     TEXT NOT NULL,     -- mysql, mssql, sqlite...
    server            TEXT,              -- host:port
    database_name     TEXT,              -- nazwa bazy
    user_id           TEXT,              -- username (zamaskowany)
    password_hash     TEXT,              -- SHA256 hash hasła
    connection_string TEXT,              -- pełny connection string (zamaskowany)
    extra_parameters  TEXT,
    discovered_at_utc TEXT NOT NULL
);
```

### Integracja z istniejącym schematem

Nowe tabele są tworzone obok istniejących 9 tabel (`scan_jobs`, `scan_files`, itd.) w tej samej bazie `infoexe.db`. Są niezależne — można je skanować osobno bez uruchamiania pełnego skanu .NET.

## Przykładowe użycie

### Skanowanie baz danych:
```powershell
# Podstawowe
InfoExeApp dbscan

# Z własną bazą
InfoExeApp dbscan --db "C:\MyData\analysis.db"
```

### Analiza rejestru:
```powershell
# Pełna analiza
InfoExeApp regscan

# Tylko błędy i podejrzane wpisy
InfoExeApp regscan | findstr /C:"[ERROR]" /C:"[SUSPICIOUS]"
```

### Skanowanie konfiguracji:
```powershell
# Skanuj bieżący katalog
InfoExeApp configscan --path .

# Skanuj konkretny folder projektu
InfoExeApp configscan --path "C:\Projects\MyApp"
```

## Znalezione "dziwne klucze" i błędy

### Przykłady z rzeczywistego skanu:

**Uszkodzone wpisy autostartu:**
- `iTunesHelper` — iTunes odinstalowany, wpis został
- `Docker Desktop` — ścieżka nie istnieje
- `SteelSeriesGG` — aplikacja przeniesiona/usunięta

**Uszkodzone deinstalatory:**
- 7-Zip 21.07, Audacity 3.7.7, Docker Desktop — brak plików deinstalacyjnych

**Podejrzane klucze:**
- Browser Helper Objects (BHO) — potencjalne malware
- Winlogon\Shell — możliwa podmiana shella
- Image File Execution Options — możliwe hijacki procesów

### Wzorce do sprawdzenia:

1. **Ścieżki w `C:\Users\...\AppData\Local\Temp`** — aplikacje uruchamiane z Temp (podejrzane)
2. **`cmd.exe /c` lub `powershell -Command` w autostarcie** — potencjalne backdoory
3. **`wscript` lub `cscript`** — skrypty VBS/JS w autostarcie
4. **Brakujące DLL w COM** — aplikacje odinstalowane niepoprawnie
5. **Serwisy z brakującymi ImagePath** — orphaned services

## Bezpieczeństwo

- Wszystkie operacje są **read-only** — nic nie jest modyfikowane
- Hasła są **maskowane** (`***`) w connection stringach
- Przechowywane są tylko **SHA256 hashe** haseł (do porównań), nigdy plaintext
- Skan rejestru używa tylko `OpenSubKey` (read), nigdy `CreateSubKey`
- Analiza jest **offline** — brak połączeń sieciowych

## Ograniczenia

- Rejestr: tylko Windows (Registry API)
- DatabaseDiscovery: limit ~500 plików na kategorię (wydajność)
- ConfigFileScanner: limit ~2000 plików, max 1MB na plik
- Niektóre ścieżki z spacjami mogą być niepoprawnie parsowane (znany problem)