# Phase 6 Context

## Decision Summary

This phase creates a separate Windows desktop GUI in Avalonia for InfoExe.
The GUI is a companion app, not a replacement for the CLI.

## Locked Decisions

1. **Scope is scan-only**
   - The GUI will provide a form for scan parameters only.
   - In scope: root folder, program name, search path, program arguments, Python toggle, CLI path, DB path, run scan, show help, show live output.
   - Out of scope: status history browser, retry UI, export UI, and any broader redesign of the CLI workflow.

2. **GUI stays separate from CLI logic**
   - The GUI launches the existing `InfoExeApp` as a subprocess.
   - No shared library extraction is required in this phase.
   - CLI behavior remains the source of truth.

3. **Stable paths**
   - The GUI must use a stable DB location under the user profile, not the repo-local dev DB.
   - The GUI must make the CLI executable path configurable and provide a sensible default for development layouts.

4. **Implementation style**
   - Keep the GUI simple and pragmatic.
   - Prefer code-behind over introducing a new MVVM stack unless a specific binding need forces it.

5. **Windows-first delivery**
   - Target Windows as the primary supported desktop platform.
   - Avalonia is the chosen UI framework.

## Notes

- The GUI should generate a preview of the exact CLI command before running it.
- The GUI should stream stdout/stderr from the CLI so the user can see scan progress.
- The GUI should not require the user to remember CLI flags.
