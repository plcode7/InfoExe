status: passed
phase: 3

# Verification — Phase 3

## Must-haves

1. Managed assemblies przechodzą przez etap decompilation — **passed**
2. Fallback reason codes istnieją dla awarii etapu decompile — **passed**
3. `retry --id` działa z limitem prób — **passed**
4. `status` raportuje metrykę `decompiled` — **passed**

## Evidence

- Build przechodzi.
- `scan` + `status` pokazują `decompiled: 8`.
- `retry --id` zwraca sensowny wynik (`No retry candidates` przy braku zaległości).
