# Windows 11 cloud control

This diagnostic branch uses the public GitHub-hosted [Windows 11 ARM runner](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) and runs the unchanged win-x64 target through Windows 11's [x64 emulation](https://learn.microsoft.com/en-us/windows/arm/apps-on-arm-x86-emulation). It never targets a self-hosted runner or the laptop.

The native ARM64 SDK cross-publishes a self-contained x64 test executable so the test does not depend on a separately installed x64 Desktop Runtime. The normal framework-dependent x64 package and distribution targets are unchanged. Only JSON evidence is retained, not these large test binaries. This does not introduce an ARM64 distribution or certify native x64 Windows 11 hardware.

One job exercises the warm-cache Shell probe, including all supported extensions, the unsupported control, multiple selection and cleanup. Two isolated resident jobs compare current source with untouched 842847154b0357e0e6abae5220f6bb593b09fe32, each with 1,800 seconds idle and the same cycle counts and 5-second/200-MiB/32-MiB limits. The wrapper still requires the executable to report X64; no architecture assertion is relaxed.

The source checkout is checked before publication. Provenance records both workflow and application revisions, ARM64 host architecture, x64 target and test-only self-contained packaging. The reports record actual Windows build and HWND DPI. Emulation performance, VM display configuration and available shell/session features are limitations, not native x64 or physical-DPI verification. No manual Explorer or Windows testing is claimed.

All source changes are confined to the hosted test workflow and this record on the separate diagnostic branch. Phase 11 remains unmerged while the original blockers are unresolved. Review this branch's actual CI outcome and retained logs; a job being scheduled is not a passed check.
