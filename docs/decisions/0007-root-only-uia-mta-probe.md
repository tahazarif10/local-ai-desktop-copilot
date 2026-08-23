# ADR 0007: Root-only UIA probe on an application-owned COM MTA worker

- Status: Proposed
- Date: 2026-08-21
- Supersedes: none

## Context

M3.1 needs to prove that a packaged .NET/WinUI client can resolve the current foreground window's UI Automation root without coupling cross-process COM work to the UI thread, widening privacy grants, introducing traversal/text collection, or prematurely adding a helper process.

Microsoft requires desktop UIA client calls to run from a non-UI COM MTA thread that owns no windows. `CUIAutomation8` exposes `IUIAutomation2`, whose provider connection and transaction timeouts bound expected unresponsive-provider calls. A cancellation token alone cannot interrupt a blocked COM call, and UIA interfaces should not be passed across apartments.

The interop choices considered were:

1. `System.Windows.Automation`, which would import the WindowsDesktop/WPF automation stack into the WinUI app and obscure the required COM ownership boundary.
2. A generated COM reference, whose output depends on a machine-installed type library/tooling step and adds an interop assembly.
3. CsWin32, which is authoritative and attractive for a broader Windows adapter, but adds source-generator/package surface for a probe that uses only COM initialization, activation, two timeout setters, and one root-resolution slot.
4. A narrow local ABI adapter over the stable Windows SDK interface IDs/vtable layout, isolated to one file and validated by the strict Windows build plus physical runtime evidence.
5. A restartable helper process, which provides harder isolation but is not yet justified by measured root-probe behavior and would add packaging, protocol, lifetime, and privacy surface.

## Decision

For M3.1 only:

- Keep the worker in `LocalCopilot.App`; do not introduce a process boundary yet.
- Start one application-owned background thread lazily after an allowed manual probe request.
- Set the thread to MTA and explicitly call `CoInitializeEx(COINIT_MULTITHREADED)` there.
- Activate `CUIAutomation8` as `IUIAutomation2`, set connection and transaction timeouts to 1.5 seconds, and apply a 2.5-second end-to-end request deadline.
- Expose a diagnostic-only forced-expiry command that uses the same admitted queue/worker and must be followed by a normal probe. This proves typed request-timeout recovery deterministically but does not claim that cancellation can interrupt every hostile provider.
- Use a narrow local ABI adapter for `ElementFromHandle` and the two timeout setters. Keep the CLSID, IID, and inherited vtable slots named, documented, and contained in that adapter.
- Permit at most one active request and one coalesced newest pending request. Complete a replaced pending request as `Cancelled/Superseded`.
- Require `ReadUiStructure` before queueing, revalidate HWND/PID on the worker, and fail closed before UIA if target integrity is unreadable or above the client.
- Immediately release the returned root pointer on the same worker. Do not query properties, children, views, cache requests, patterns, names, values, text, or actions.
- Publish only typed outcome/reason/timing/HRESULT/thread/identity metadata after the current epoch and capability are checked again.
- Keep the application manifest without `uiAccess`; never elevate or attempt secure-desktop access.

## Consequences

- Normal product launches keep `ReadUiStructure` denied and never start the COM worker.
- Diagnostic validation can prove the ABI and provider behavior without exposing UI content.
- Pure queue, HRESULT-classification, privacy-separation, and stale-publication logic remains portable and deterministic.
- A manual ABI surface is acceptable only while it stays this small. Any expansion to cache requests, element properties, traversal, subscriptions, or patterns must reevaluate CsWin32/generated interop before proceeding.
- The platform timeouts bound expected provider failures but are not a hard process-isolation guarantee. M3.1 requires physical timeout/recovery and teardown evidence; M3.4 decides whether continuous UIA needs a restartable helper process.

## References

- [UI Automation threading issues](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading)
- [Creating the CUIAutomation object](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-creatingcuiautomation)
- [`IUIAutomation::ElementFromHandle`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation-elementfromhandle)
- [`IUIAutomation2`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nn-uiautomationclient-iuiautomation2)
- [`CUIAutomation8`](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/legacy/hh448746(v=vs.85))
- [UI Automation error codes](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-error-codes)
- [Security considerations for assistive technologies](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)
