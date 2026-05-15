# Domain Pitfalls

**Project:** InfoExe  
**Date:** 2026-05-15

## Critical Pitfalls

| Pitfall | Warning signs | Prevention | Phase |
|---|---|---|---|
| Fałszywa pewność vendor attribution | Konflikty między podpisem i metadanymi; niska liczba `unknown`, wysoki błąd ręczny | Evidence hierarchy + provenance + `inconclusive` outcome | 2 |
| AOT/obfuscation traktowane jak zwykły błąd | Wysoki odsetek `decompile failed` bez klasyfikacji przyczyny | Capability gating + `completed_partial` z reason codes | 3 |
| Presja pamięci przy dużych skanach | Spadek throughput, OOM, długie GC | Bounded concurrency + streaming zapisów + limity | 4 |
| Brak traceability danych | Brak odtworzenia wyników dla tego samego pliku | Hash pliku + config hash + wersje narzędzi w DB | 1 |
| Drift modelu danych vs eksport | Różne liczby/kolumny między DB a JSON/CSV | Canonical schema + contract parity tests | 5 |
| Retry loops i stuck jobs | Joby w `processing` bez końca | Jawna FSM + max attempts + heartbeat + dead-letter | 3/6 |
| Luki legal/compliance messaging | Niejasne użycie narzędzia przez użytkownika | Jasne disclaimery i granice użycia w CLI/docs | 1/6 |

## Additional Risks

- Nadmierne związanie z jednym backendem bez abstrakcji diagnostycznej.
- Niespójna semantyka statusów między CLI, DB i raportami.
- Problemy path handling (long path, unicode, reparse points).
