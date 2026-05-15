# Phase 3 Summary — Decompilation & Reliability

## Delivered

- Dodano tabelę `decompilation_results`.
- Skan wykonuje etap dekompilacji managed assemblies:
  - używa `ilspycmd`, jeśli dostępny
  - fallback z reason code, jeśli narzędzie niedostępne lub fail/timeout
- Dodano komendę:
  - `retry --id <scanId> [--db <file>]`
- Dodano limit retry (`retry_count < 3`) i aktualizację statusu po retry.
- `status` raportuje `decompiled`.
