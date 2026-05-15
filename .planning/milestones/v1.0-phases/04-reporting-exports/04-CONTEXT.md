# Phase 4: Reporting & Exports - Context

**Gathered:** 2026-05-15
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Udostępnić eksport wyników skanu w JSON i CSV z polami provenance.

</domain>

<decisions>
## Implementation Decisions

### the agent's Discretion
Eksport ma używać danych już zapisanych w SQLite i utrzymać spójny kontrakt pól między JSON i CSV.

</decisions>

<code_context>
## Existing Code Insights

Scan/status/retry + metadata/vendor/decompile są gotowe, więc eksport buduje warstwę raportową nad obecnym modelem.

</code_context>

<specifics>
## Specific Ideas

- `export --id --format json|csv|json,csv --output`.
- W JSON i CSV te same kluczowe pola: status, reason, vendor, decompile, retry_count.

</specifics>

<deferred>
## Deferred Ideas

- Dedykowane wersjonowanie schemy exportów.

</deferred>
