# Research Summary

**Project:** InfoExe  
**Date:** 2026-05-15

## Key Findings

- **Stack:** .NET 10 LTS + ILSpy/ICSharpCode.Decompiler + Mono.Cecil (+dnlib fallback) + SQLite + System.CommandLine + Channels.
- **Table stakes:** scan/classify, metadata analysis, IL decompilation where possible, vendor evidence with confidence, retry/status, JSON/CSV export.
- **Architecture:** modular local pipeline with checkpointed stage model and bounded concurrency.
- **Main risks:** vendor misattribution, AOT/obfuscation limits, memory pressure, schema/export drift, retry deadlocks, compliance ambiguity.

## Prescriptive Guidance for Planning

1. Build metadata + persistence contracts before decompilation details.
2. Make decompilation capability-aware (IL vs R2R vs Native AOT).
3. Treat vendor attribution as evidence-based scoring, never as single-field truth.
4. Lock one canonical output schema and validate parity across DB/JSON/CSV.
5. Keep legal-safe messaging explicit in CLI and documentation from phase 1.
