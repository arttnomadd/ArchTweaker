# ADR-0007: Registry snapshots hold only the values an apply touches

Status: accepted · Date: 2026-11-16

## Context

Before each apply (and each Tuning assistant run) ArchTweaker saves a .reg snapshot in
`ProgramData\ArchTweaker\snapshots`. Up to 3.31.0 it ran `reg.exe export` on every key the apply touched.
`reg export` copies the key with every subkey, so a value directly under `HKLM\SYSTEM\CurrentControlSet\Control`
saved about 30 MB. Change history read the newest snapshot in full each time it opened, which took seconds.

## Decision

- A snapshot lists only the values the apply touches, read with RegistryKey and written with `RegText.Line`.
- A value that did not exist is written as `"name"=-`, so importing the snapshot deletes it again.
- A key that did not exist is only a comment. A snapshot never contains `[-key]`, so importing it can never
  delete a key with everything under it.
- Change history reads only the keys it shows, keeps a parsed snapshot while its file is unchanged, and
  loads in the background.
- Once, at the first start of 3.32, ArchTweaker deletes snapshots larger than 1 MB. Undo uses the journal
  (ADR-0002), not snapshots.

## Alternatives considered

- **Keep `reg export`, parse faster:** still 30 MB on disk per apply, and restoring such a file puts back a
  whole subtree, not what the apply changed.
- **Drop snapshots, use the journal only:** loses the double-click restore that works without ArchTweaker.

## Consequences

- Snapshots are a few KB, and double-clicking one restores exactly the values the apply changed.
- A snapshot no longer shows unrelated values under the same key.
