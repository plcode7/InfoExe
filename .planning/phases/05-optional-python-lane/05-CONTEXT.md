# Phase 5: Optional Python Lane - Context

**Gathered:** 2026-05-15
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Dodać opcjonalne, pasywne przetwarzanie artefaktów Python (`.py`, `.whl`) bez naruszenia bezpieczeństwa.

</domain>

<decisions>
## Implementation Decisions

### the agent's Discretion
Włączenie toru Python tylko przez jawny przełącznik CLI.

</decisions>

<code_context>
## Existing Code Insights

Pipeline już rozpoznaje Python artifacts; ta faza domyka opcjonalność i kontrakt CLI.

</code_context>

<specifics>
## Specific Ideas

- `scan` domyślnie przetwarza tylko `.dll/.exe`.
- `--include-python` aktywuje `.py/.whl`.

</specifics>

<deferred>
## Deferred Ideas

- Głębsza analiza zależności Python w kolejnych iteracjach.

</deferred>
