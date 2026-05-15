# Phase 2 Summary — Metadata & Vendor Attribution

## Delivered

- Rozszerzono model danych SQLite o:
  - `assembly_metadata`
  - `vendor_evidence`
  - `vendor_results`
- Rozszerzono `scan`:
  - zapis metadanych managed assemblies (name/version/token + capability flags)
  - zapis evidence dla vendor attribution
  - wyliczenie `attributed` vs `inconclusive`
- Rozszerzono `status` o:
  - `managedMetadata`
  - `vendorAttributed`
  - `vendorInconclusive`

## Notes

- Model confidence jest celowo prosty i evidence-based; bardziej zaawansowane heurystyki zostają na kolejne fazy.
