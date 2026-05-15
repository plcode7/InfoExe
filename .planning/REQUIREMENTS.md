# Requirements: InfoExe

**Defined:** 2026-05-15  
**Core Value:** Użytkownik może bezpiecznie i offline uzyskać wiarygodny, powtarzalny raport o składzie i pochodzeniu aplikacji .NET oraz dekompilowane źródła.

## v1 Requirements

### Scan & Orchestration

- [ ] **SCAN-01**: User can start a scan for any provided `rootPath`.
- [ ] **SCAN-02**: User can see scan status with counts of discovered, processed, failed, and partial files.
- [ ] **SCAN-03**: User can retry failed work by stage for a selected scan.
- [ ] **SCAN-04**: Scan runs fully offline without external service dependency.

### .NET Classification & Metadata

- [ ] **META-01**: User can scan `.dll` and `.exe` files and detect managed .NET artifacts.
- [ ] **META-02**: User can get assembly metadata (name, version, target framework, references, token/signing facts) per artifact.
- [ ] **META-03**: User can see explicit capability flags for single-file, ReadyToRun, and Native AOT-limited binaries.

### Decompilation

- [ ] **DECO-01**: User can decompile IL-capable assemblies into source artifacts.
- [ ] **DECO-02**: User gets explicit partial/unsupported reason codes when full decompilation is not possible.
- [ ] **DECO-03**: Decompilation pipeline never executes analyzed binaries.

### Vendor Attribution

- [ ] **VEND-01**: User gets vendor evidence from multiple sources (metadata/signature/package context when available).
- [ ] **VEND-02**: User sees confidence level and provenance for vendor attribution results.
- [ ] **VEND-03**: System supports inconclusive vendor outcome instead of forced attribution when evidence conflicts.

### Persistence & Export

- [ ] **DATA-01**: User can persist scan artifacts and stage states in local SQLite.
- [ ] **DATA-02**: User can export scan report to JSON.
- [ ] **DATA-03**: User can export scan report to CSV.
- [ ] **DATA-04**: Exported outputs include key provenance fields needed for audit/replay.

### Optional Python Lane

- [ ] **PY-01**: User can optionally include `.py` and `.whl` files in the scan pipeline.
- [ ] **PY-02**: Python lane performs static/passive parsing only.

## v2 Requirements

### Advanced Analysis

- **ADV-01**: User can compare two scans and get a structured diff of dependencies/vendor findings.
- **ADV-02**: User can use rule packs to customize vendor inference confidence behavior.
- **ADV-03**: User can export CycloneDX SBOM when project metadata allows it.

## Out of Scope

| Feature | Reason |
|---------|--------|
| Executing analyzed binaries | Violates passive-analysis safety model |
| Automated DRM/obfuscation bypass | Legal and ethical risk outside project scope |
| Mandatory cloud sync/upload | Conflicts with strict offline requirement |
| Full GUI product in v1 | CLI-first delivery is priority for core value |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| SCAN-01 | Phase 1 | Pending |
| SCAN-02 | Phase 1 | Pending |
| SCAN-03 | Phase 3 | Pending |
| SCAN-04 | Phase 1 | Pending |
| META-01 | Phase 2 | Pending |
| META-02 | Phase 2 | Pending |
| META-03 | Phase 2 | Pending |
| DECO-01 | Phase 3 | Pending |
| DECO-02 | Phase 3 | Pending |
| DECO-03 | Phase 1 | Pending |
| VEND-01 | Phase 2 | Pending |
| VEND-02 | Phase 2 | Pending |
| VEND-03 | Phase 2 | Pending |
| DATA-01 | Phase 1 | Pending |
| DATA-02 | Phase 4 | Pending |
| DATA-03 | Phase 4 | Pending |
| DATA-04 | Phase 4 | Pending |
| PY-01 | Phase 5 | Pending |
| PY-02 | Phase 5 | Pending |

**Coverage:**
- v1 requirements: 19 total
- Mapped to phases: 19
- Unmapped: 0

---
*Requirements defined: 2026-05-15*  
*Last updated: 2026-05-15 after auto initialization*
