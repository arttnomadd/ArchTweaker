# ADR-0004: Registry scanner limited to dead uninstall/App Paths refs (no CLSID, no defrag)

**Date**: 2026-11-03
**Status**: deprecated (2026-11-06, v2.7.3: the registry scan was removed; it found too little to be useful)
**Deciders**: Archie

## Context

"Registry cleaners" are notorious for breaking Windows by deleting keys they wrongly judge unused (COM
servers registered for on-demand install, shell extensions, etc.). ArchTweaker wants a registry scan that is
useful without being one of those tools.

## Decision

Scan only two high-signal, low-risk categories: orphaned **Uninstall** entries (install folder *and*
uninstaller both missing) and **App Paths** entries whose target `.exe` is missing. Skip Microsoft/MSI
entries and anything on an offline drive. Every removal exports a `.reg` backup first and is Undo-covered.
Do **not** sweep CLSID/InprocServer32 or shell extensions, and do **not** "defrag/compact" the registry.

## Alternatives Considered

### Alternative 1: Full registry clean (CLSID, file associations, MUICache, shared DLLs…)
- **Pros**: removes more entries; matches commercial cleaners.
- **Cons**: high false-positive rate; breaks on-demand COM and shell integration.
- **Why not**: the risk/benefit is bad; "more entries removed" is not a real user benefit.

### Alternative 2: Offline registry hive defragmentation
- **Pros**: can shrink bloated hives.
- **Cons**: can't be done live or reversibly; a failed compaction can corrupt a hive.
- **Why not**: violates the exact-undo guarantee (ADR-0002); deliberately out of scope.

## Consequences

### Positive
- Only entries that provably point at nothing get removed.
- Fully reversible; consistent with the product's trust model.

### Negative
- Leaves "clutter" other cleaners would remove (by design).

### Risks
- An unquoted/offline path could be misjudged — mitigated by proper path parsing and a drive-ready check (2.0.1).
