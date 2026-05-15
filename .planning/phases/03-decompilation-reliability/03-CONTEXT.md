# Phase 3: Decompilation & Reliability - Context

**Gathered:** 2026-05-15
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Dostarczyć podstawową dekompilację managed assemblies i kontrolowany mechanizm retry dla niepełnych/nieudanych etapów.

</domain>

<decisions>
## Implementation Decisions

### the agent's Discretion
W fazie 3 preferowany jest practical baseline: decompile through `ilspycmd` when available, fallback reason codes otherwise.

</decisions>

<code_context>
## Existing Code Insights

Fazy 1-2 mają już scan/status, metadane i vendor evidence; faza 3 rozbudowuje pipeline o decompile stage i retry flow.

</code_context>

<specifics>
## Specific Ideas

- `retry --id` dla plików z status partial/failed z limitem prób.
- `decompilation_results` jako trwały etap pipeline.

</specifics>

<deferred>
## Deferred Ideas

- Rozszerzona orkiestracja backoff per-stage i dead-letter queue do fazy operacyjnej.

</deferred>
