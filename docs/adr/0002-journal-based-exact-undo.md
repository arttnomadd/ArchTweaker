# ADR-0002: Journal-based exact-undo for every change

**Date**: 2026-10-31
**Status**: accepted
**Deciders**: Archie

## Context

A tweaker that changes registry values, services, scheduled tasks, power plans and boot flags is only safe
if every change can be reversed to the user's *exact* prior state — not to a generic "Windows default" that
may differ from what they had.

## Decision

Every operation records its prior state to a journal before applying. Registry deletes export a `.reg`
backup first. "Undo everything" reverts non-registry changes (power plan, bcdedit, tasks, commands) that
ArchTweaker itself applied, then replays the registry journal, removes the hosts block, and clears history.

## Alternatives Considered

### Alternative 1: System Restore only
- **Pros**: built into Windows, no custom code.
- **Cons**: coarse, user-disableable, not per-tweak, slow.
- **Why not**: can't offer per-change or per-session undo. (ArchTweaker still offers a restore point as a belt-and-suspenders.)

### Alternative 2: Re-apply "Windows default" values on revert
- **Pros**: simpler, no journal.
- **Cons**: the "default" may not equal the user's prior value.
- **Why not**: not a faithful undo; could change behavior the user relied on.

## Consequences

### Positive
- Faithful per-tweak and per-session undo; the product's core trust guarantee.
- Delete operations are reversible via the exported `.reg` (enforced: the delete aborts if the export fails — see 2.0.1).

### Negative
- Every op type must implement capture + revert; more code per feature.

### Risks
- A missing journal entry would strand a change — mitigated by routing all deletes through `DeleteTreeBackedUp` and verifying the export wrote.
