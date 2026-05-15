# Technology Stack

**Project:** InfoExe  
**Date:** 2026-05-15

## Recommended Stack

| Area | Choice | Notes |
|---|---|---|
| Runtime | .NET 10 LTS | Stabilny horyzont wsparcia, nowoczesne API |
| CLI | System.CommandLine 2.x | Główny interfejs `scan/status/export/retry` |
| Decompilation | ICSharpCode.Decompiler + `ilspycmd` | Spójny silnik ILSpy dla biblioteki i CLI |
| Metadata | Mono.Cecil (primary), dnlib (fallback) | Szerokie pokrycie przypadków .NET |
| Persistence | SQLite + Microsoft.Data.Sqlite | Lokalny, offline, prosty deployment |
| Queue/Retry | System.Threading.Channels | In-memory pipeline z kontrolą backpressure |
| JSON | System.Text.Json | Wbudowane, szybkie |
| CSV | CsvHelper | Stabilny eksport tabelaryczny |
| Logging | Serilog (file sink) | Diagnostyka lokalna |

## Architecture-Level Decisions

1. Jeden główny silnik dekompilacji (ILSpy) dla przewidywalności wyników.
2. Metadata-first: najpierw analiza/metadane, potem warunkowa dekompilacja.
3. Persistencja przez jawny SQL/ADO.NET zamiast ciężkiej warstwy ORM.
4. Tryb offline i pasywny jako niezmienny kontrakt bezpieczeństwa.

## What Not To Use

- Preview toolchain w produkcyjnym MVP.
- Zewnętrzne brokery kolejek (overkill dla lokalnego scenariusza).
- Podejścia wymagające wykonywania analizowanych binariów.
- GUI-first jako priorytet v1 (opóźnia dostarczenie rdzenia).

## Confidence

- **High:** .NET 10 + ILSpy + SQLite + CLI baseline
- **Medium-High:** Cecil + dnlib split
- **Medium:** Tuning wydajności i limity konkurencji zależne od realnych danych
