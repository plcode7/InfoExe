# Architecture Patterns

**Project:** InfoExe  
**Date:** 2026-05-15

## Recommended Shape

Single-process modular pipeline with durable checkpoints in SQLite.

## Component Boundaries

| Component | Responsibility |
|---|---|
| CLI Layer | Komendy użytkownika, parametry, prezentacja statusu |
| Orchestrator | Tworzy scan job i zarządza cyklem życia |
| Scanner | Enumeracja plików i podstawowe fakty o plikach |
| Classifier | Rozpoznanie .NET/Python/unsupported + capability flags |
| Analyze Worker | Metadane PE/.NET, referencje, TFM, podpisy |
| Decompile Worker | Dekompilacja IL (gdy możliwa) |
| Vendor Worker | Agregacja dowodów i confidence scoring |
| Persist Layer | Transakcyjne zapisy i stany etapów |
| Exporter | JSON/CSV (opcjonalnie SBOM) |

## Data Flow

`scan -> classify -> analyze -> decompile -> vendor infer -> persist -> export`

## Retry + State Model

Per-file finite state machine:

`Discovered -> Classified -> Analyzed -> Decompiled -> VendorInferred -> Persisted -> Exported`

Failure path:
- `FailedTransient`: retry z backoff i limitem prób
- `FailedPermanent`: bez dalszych retry (np. unsupported/AOT limitation)

## Build Order

1. Schema + state machine + contracts
2. Scanner/classifier
3. Persistence + queue orchestration
4. Metadata analyzer
5. Decompiler
6. Vendor inference
7. Exporter + CLI polish
8. Optional Python lane

## Edge Cases

- **Single-file bundle:** wykrycie i częściowa analiza z lineage.
- **ReadyToRun:** prefer metadata-first i częściowa dekompilacja.
- **Native AOT:** jawne `NotApplicable` dla dekompilacji, bez blokowania całego skanu.
