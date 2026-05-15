# Phase 2 Plan — Metadata & Vendor Attribution

**Phase:** 2  
**Name:** Metadata & Vendor Attribution  
**Mode:** Auto (YOLO)  
**Status:** Ready

## Goal

Zapisać metadane .NET i vendor evidence w modelu danych oraz wystawić wynik vendor attribution z confidence i stanem inconclusive.

## Execution Plan

1. Rozszerzyć schemat SQLite o tabele metadata i vendor.
2. Podczas skanu dla managed .NET zapisywać:
   - assembly name/version/token
   - capability flags (bazowe)
3. Dla każdego pliku tworzyć vendor evidence (file-version/assembly token) i agregować vendor result.
4. Utrzymać kompatybilność istniejących komend `scan` i `status`.

## Verification Targets

1. Po skanie powstają rekordy metadanych dla managed assemblies.
2. Dla plików istnieją wpisy vendor evidence i wynik vendor_result.
3. Konfliktujące dowody dają status inconclusive.
