# Feature Landscape

**Domain:** Lokalny offline analyzer/decompiler .NET  
**Project:** InfoExe  
**Date:** 2026-05-15

## Table Stakes (must-have)

| Feature | Complexity | Why |
|---|---|---|
| `scan --root` + klasyfikacja plików | Low-Med | Punkt wejścia całego workflow |
| Pasywna analiza metadanych .NET | Medium | Fundament vendor + stack mapping |
| Dekompilacja IL do artefaktów źródłowych | High | Główna wartość produktu |
| Vendor evidence collection | High | Kluczowe dla identyfikacji producenta |
| Vendor confidence scoring | High | Ogranicza fałszywe przypisania |
| Status jobów + retry etapów | Medium | Niezawodność dla skanów wsadowych |
| Eksport JSON/CSV | Medium | Integracja z narzędziami użytkownika |
| Trwałość danych lokalnie (SQLite) | Medium | Wznawialność i audytowalność |

## Differentiators

| Feature | Complexity | Value |
|---|---|---|
| Explainability śladu dowodowego | High | Transparentne decyzje vendor confidence |
| Adaptive decompile strategy (IL/R2R/single-file) | High | Lepsza jakość dla realnych binariów |
| Partial-success reporting | Medium | Większa użyteczność operacyjna |
| Re-run diffing między skanami | High | Forensics / supply-chain review |
| Opcjonalny CycloneDX | Medium | Integracja SBOM |

## Anti-Features (explicitly out)

| Anti-feature | Reason |
|---|---|
| Uruchamianie analizowanych binariów | Narusza model bezpieczeństwa |
| Wymuszone uploady/chmura | Narusza założenie offline |
| Funkcje omijania DRM/obfuskacji | Ryzyka prawne i etyczne |
| GUI-first scope dla v1 | Odsuwa dostarczenie rdzenia |

## Dependencies

`scan` -> `classify` -> `analyze` -> (`decompile` + `vendor inference`) -> `persist` -> `export`  
Cross-cutting: legal-safe posture + passive-only processing.
