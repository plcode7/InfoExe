status: passed
phase: 2

# Verification — Phase 2

## Must-haves

1. Metadane managed assemblies są zapisywane do bazy — **passed**
2. Vendor evidence i vendor result są zapisywane per plik — **passed**
3. Konflikt danych może dać wynik inconclusive — **passed**
4. `status` pokazuje agregaty metadanych/vendor — **passed**

## Evidence

- Build przechodzi (`dotnet build`).
- Przebieg `scan` + `status` zwraca m.in.:
  - `managedMetadata: 8`
  - `vendorAttributed: 4`
  - `vendorInconclusive: 4`
