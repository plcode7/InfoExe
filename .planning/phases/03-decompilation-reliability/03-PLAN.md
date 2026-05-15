# Phase 3 Plan — Decompilation & Reliability

**Phase:** 3  
**Mode:** Auto (YOLO)  
**Status:** Completed

## Plan

1. Dodać etap dekompilacji i trwały zapis wyniku.
2. Zaimplementować fallback reason codes dla przypadków bez narzędzia/timeout/fail.
3. Dodać komendę `retry --id` z limitem prób i aktualizacją statusu.
4. Rozszerzyć `status` o metrykę `decompiled`.
