# ADR-0006: Bake into ISO writes only registry values and service start types into the offline image

Status: accepted · Date: 2026-11-13

## Context

3.19 adds Bake into ISO: the chosen tweaks are written into a copy of a Windows setup ISO, so a fresh install
starts tuned. An offline image has no running services, task scheduler, power manager or devices. Many tweaks
run commands (bcdedit, powercfg, PowerShell) or change scheduled tasks, power plans, devices or the NVIDIA
profile; none of that can happen offline.

## Decision

- A tweak qualifies only when every op is a registry value (VOp), an added key (KeyAddOp) or a service start
  type (SvcOp), and it has no commands and no built-in action (`IsoBake.Unbakeable`).
- HKLM\SOFTWARE and HKLM\SYSTEM map to the image's SOFTWARE and SYSTEM hives (CurrentControlSet → the control
  set in the image's Select\Current); HKCU maps to Users\Default\NTUSER.DAT, so every new account gets it;
  HKCR maps to SOFTWARE\Classes. Anything else is refused.
- The writes go to `reg load`ed hives with plain RegistryKey calls, not through `Reg.Set`: they change the
  image, not this PC, so they must not enter the undo journal (ADR-0002).
- No first-boot script (SetupComplete.cmd) for the rest: it would run changes on hardware ArchTweaker has never
  seen, with no journal to undo them.
- The ISO is built with oscdimg.exe from the Windows ADK path, or a copy the user picked into the admin-owned
  ProgramData\ArchTweaker\bin (same trust rule as PresentMon, ADR-0005). Without it, the output is a folder.
- Every failure after `dism /Mount-Wim` unloads the hives and runs `/Unmount-Wim /Discard`. The work folder is
  deleted only after the ISO was written, and it is always a folder this run created.

## Consequences

Fewer tweaks qualify than are applied on a tuned PC; the page lists the applied ones that stay out and why.
The installed system shows baked tweaks as applied (their values are there), and their Undo writes the
Windows defaults, since there is no journal entry from before.
