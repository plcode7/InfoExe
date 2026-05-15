# Phase 2: Metadata & Vendor Attribution - Context

**Gathered:** 2026-05-15
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Rozszerzyć pipeline o odczyt metadanych .NET i podstawowy model vendor attribution z confidence oraz wynikiem inconclusive.

</domain>

<decisions>
## Implementation Decisions

### the agent's Discretion
Implementacja ma preferować pasywne API metadanych i model dowodowy, bez uruchamiania analizowanych binariów.

</decisions>

<code_context>
## Existing Code Insights

Phase 1 delivers scan/status and SQLite state. Phase 2 should extend schema and scan flow without łamania kompatybilności istniejących komend.

</code_context>

<specifics>
## Specific Ideas

- Dodać tabelę metadanych assembly.
- Dodać tabelę vendor evidence i wynik vendor_result.
- W statusie utrzymać agregaty i zgodność CLI.

</specifics>

<deferred>
## Deferred Ideas

- Zaawansowane heurystyki R2R/AOT i głęboka analiza referencji do kolejnych faz.

</deferred>
