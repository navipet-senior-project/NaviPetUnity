# Indoor localization baseline

`IndoorArBaseline` records durations and counts from the existing session hooks. It does not change localization, the camera session, or navigation. The device log line is `[IndoorArBaseline]`. It contains no camera frame, pose, token, map payload, or user location.

| Field | Origin | Meaning |
| --- | --- | --- |
| `arStartupMs` | session opened | `BeginSession` until `OnArSupport`. Includes `ARSession.CheckAvailability`. Excludes the Unity splash and the Flutter tap. |
| `requestLatencyMs` | localization dispatched after tracking | Latest dispatched session localization request before the first accepted sign or MultiSet pose, until that pose. Includes any sign hold, authentication or SDK queue wait, but excludes the initial wait for tracking. With no request or accepted pose this stays `na`; it is not an SDK response latency or the 30 s timeout. |
| `timeToFirstLocalizationMs` | session opened | First accepted sign or MultiSet pose. Later poses do not replace it. |
| `arrowShownMs` | session opened | HUD first enters the guide-arrow state, after the 1.5 s “Location found” hold. It records display timing, not a rendered-frame timestamp. |
| `arrowAlignment` | — | Always `unmeasured`. Arrow display is not evidence the arrow matches the real doorway. |
| `attempts` | — | Session localization requests dispatched after tracking through `IndoorArRunner.RequestLocalization`, including requests held for signs or queued for MultiSet. Not SDK calls; the result's `localizationQueries` separately counts actual SDK starts. |
| `retries` | — | Session localization requests after the first, including the timeout retry, tracking-loss restart, and in-place retries. A host relaunch starts a fresh session. [Try again behavior](../../docs/floor-evidence-contract.md#what-unity-does) |
| `trackingLosses` | — | Pinned sessions that left `SessionTracking`. |
| `unansweredAttempts` | — | Requests superseded without a recorded first pose, plus any open request. Only the first accepted pose closes a request in this baseline; this is not the number of outstanding SDK requests. |
| `timeToSignMs` | session opened | OCR first confirms a room sign. Room identity only, not a pose; it never sets `timeToFirstLocalizationMs`. |
| `poseSource` | — | What gave the first accepted pose: `sign`, `multiset`, or `na` before one. |
| `ocrLatencyMs` | frame captured | Capture-to-result time of the session's first OCR read, regardless of match status. |
| `poseEstimateUs` | solve started | Solve time of the session's first sign-pose result with status `Ok`. |

Editor play mode does not emit this line. On a phone, filter logcat or the Xcode console for `IndoorArBaseline`. One line is printed each time a field changes.

Indoor AR also emits `[ArDiag]` XR input counts at an explicit loader restart or active-loader entry and again one second later. `[IndoorAr]` diagnostic lines report frozen-camera changes, floor transitions and eye-height buckets, Limited tracking episodes, waypoint-index churn, route-block causes, and re-pin shift and turn. The localization result line reports `map` (the first eight hex digits of SHA-256 over the returned map codes, or `unavailable` when no identity is supplied), `floor` (the derived floor, `unknown`, or `unsupported`), `deltaM` (camera height relative to that floor in meters, or `unknown`), and `confidence` (or `unknown`). It includes successful MultiSet candidates even when rejected, and sign poses; legacy SDK callbacks and sign poses use `map=unavailable`. These lines do not include positions, raw map codes, images, credentials, or recognized text. Filter for `ArDiag` and `IndoorAr` to capture them alongside the baseline.

`[IndoorAr] pin` lines report the indoor pin mode on first placement and each mode transition: `World`, `Docked`, or `Clipped`. `distM` is a straight-line distance bucket (`0-1`, `1-3`, `3-6`, `6-10`, `10-20`, `20-up`, or `unknown`); `view` is `InView`, `OffScreen`, or `Behind`. These lines contain no coordinates, raw identifiers, images, or recognized text. Device validation of first-entry and transition logs remains outstanding.

`[IndoorAr] localization request` lines report `attempt`, `kind` (`Sign` or `MultiSet`), `elapsedMs`, `outcome`, `lockBusy`, `lockWaitMs`, and `sdkMs`. Sign attempts measure elapsed tick time from the start of the OCR hold through the reported outcome, including time after the hold for a late pose, and use `na` for lock and SDK fields. MultiSet attempts measure time since the request, time behind a live request or queued predecessor, and time in the SDK; `sdkMs` is zero if the SDK call never started. `GenericFailure` is the fixed failure class because the SDK callback provides no detail; `Rejected` means pose validation failed. A `Late` line can follow `Timeout` or `Cancelled` when a result arrives later. These timings do not identify whether a delay came from the network or server. The request lines contain no coordinates, credentials, images, recognized text, or map codes.

In any indoor session state, a frozen camera (frames advance and tracking holds, but the camera pose has not changed for about 2 s) hides the world pin, room label and guide arrow and asks the user to close and reopen AR. The warning clears when the pose moves, tracking is lost, or frames stop arriving. Localization and the bridge result are untouched. Not yet checked on a device.

## What a device run still has to measure

These numbers are not produced by this instrumentation. Do not fill them in from the log.

- Real-world arrow alignment. Compare the arrow with the doorway only while standing in the mapped hallway. Record the door key, floor, and error you observed. `arrowShownMs` stays a display time.
- Pose accuracy against a surveyed point.
- Memory and battery during a scan.
- Splash time, Flutter tap-to-activity time, and camera-permission time. `arStartupMs` starts after the player is already running.

Use one phone for a series. Same door, same network, commit recorded. Stand in a hallway the map contains, start from the chest, one slow sweep. Repeat. Log the attempt index. Do not log the token, the raw pose, or `MultiSetConfig`.

Offline control in `MultiSet` mode: airplane mode should return `NETWORK` before a VPS call, with `attempts=0`. In the optional `SignFirst` calibration mode with OCR enabled, scanning continues without MultiSet authentication; see [fallback behavior](OCR-POC-prerequisites.md#signs-first-multiset-last-signfirst-not-device-verified).
