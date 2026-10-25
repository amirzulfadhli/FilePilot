# Phase 11 cloud-only investigation

This follow-up uses GitHub repository APIs and disposable GitHub-hosted Windows runners only. It does not read or execute the laptop checkout, control its desktop, change its registry, or use a self-hosted runner. Existing failure evidence in [the acceptance record](phase-11-acceptance-results.md) remains valid until a justified fix passes the matching checks.

## Initial diagnostics

The opt-in memory sampler now records process/GUI handles, allocated bytes, generation counts and last-GC heap/commit information without forcing collection. Explicit workloads record checkpoints every ten popup cycles and after each closed Settings window, plus separate post-popup/post-Settings snapshots. The final post-cycle sample, startup/idle intervals, cycle counts, and 5-second / 200-MiB / 32-MiB performance limits are unchanged. Normal use does not instantiate this runner.

The wrapper's optional Mode selects one mode for parallel, isolated hosted jobs; its default still runs both. Each hosted mode requests 1,800 seconds idle, 180 samples, 100 popup and ten Settings cycles. Both jobs must be reviewed; fail-fast is disabled so a failure in one preserves the other's evidence. New 45-minute job deadlines accommodate this explicit half-hour workload; existing build/test deadlines are unchanged.

A separate hosted-only script registers the actual published app under HKCU, then calls the native Shell IShellFolder/IContextMenu APIs for all five supported extensions and an unsupported TXT control. An owned all-file control verb distinguishes association-specific failures from a broader menu-enumeration failure. It never invokes any menu command, changes defaults, modifies HKLM, or restarts Explorer. It refuses pre-existing product/control entries and unregisters its owned entries in finally. Its guard refuses non-hosted execution before any mutation.

Native menu enumeration on hosted Windows Server is an automated integration check, not manual Windows 10/11 Explorer acceptance. Unicode/spaced executable and selected-file paths are included. A fresh-session Windows 11 classic-menu launch, Windows 10, physical monitors and assistive-technology testing remain unverified unless independently performed.

## Status

The initial change adds diagnosis and regression coverage, not a claimed root-cause fix. No limit is relaxed and neither blocker is declared resolved. Retained hosted logs and JSON must identify the failing phase before product changes are selected. Phase 11 must remain unmerged while either blocker is unresolved; Phase 12 and public release remain gated.
