# Floor evidence contract (Flutter to Unity), version 1

Unity implements the floor policy. The Flutter confirmation prompt, pressure capture, and request fields are a separate in-progress dispatch.
Scope limits, stated up front:
- Unity may send an advisory `hintFloorHeight` on the first MultiSet request while an unhinted retry remains available. The hint uses the confirmed start floor with unchanged pressure or a unique pressure-based candidate. Both the confirmation and sample must still be fresh at SDK dispatch. Only floors 1 to 3 are eligible for a hint. Floors 4 and 5 stay unhinted. The band runs from the candidate's walk-grid surface Y minus 0.3 m to that surface plus 3.3 m. The 3.3 m top is an assumed bound, not a measured ceiling (the mesh has none): portal measurements on one pass put floor-to-floor slab spacing at 3.91 to 4.32 m, so the top stays at least 0.6 m below the next slab. Unverified on device and server: MultiSet documents a filter on frame Y but does not say whether it compares the camera or the floor under a stored frame, or what an empty band returns. Map-code hinting is deferred because the codes are private and need a configuration delivery decision. Unity clears `hintMapCodes` once when it takes ownership of the SDK manager. No position hint is sent, because no trusted horizontal prior exists.
- No OCR is involved or changed.
- Calibrated on one iPhone 14 Plus in one building over one afternoon (stairs and elevator, seated and standing). Not validated on other devices, buildings, days or Android.
- Unity-only is not a complete feature: the cold-start benefit needs the Flutter work below.

## Transport
Flutter `jsonEncode`s the request (`IndoorArLauncher._launch`), `IndoorArChannel.swift` passes the string untouched to `IndoorArHost`, and Android passes it in the `navipet.indoorAr.request` intent extra. `IndoorArContract.TryParseRequest` uses `JsonUtility` for the request and validates the optional evidence against the original JSON. Older Unity builds ignore the new fields.

The first Unity read gives a lower bound on elapsed time because a request can wait in the host or native bridge. A height hint requires `requestSentAtBootS` from the same device boot clock as Unity. Compute both evidence ages and the stamp from that one boot clock (Android `SystemClock.elapsedRealtimeNanos()`, iOS `mach_continuous_time()`). A clock that excludes sleep can report a stale sample as fresh after the phone sleeps, and Unity trusts the supplied ages. Stamp it when computing the two evidence ages for send, before handing off the request. Unity checks that the stamp precedes or equals its first read, preserves that read across XR initialization, and adds the full send-to-SDK elapsed time to both ages. A missing, malformed, or future stamp leaves the request unhinted. Existing start-floor pose checks and sampling consent still use the launch summary.

## Optional request fields for schemaVersion 2

Send the required floor-evidence fields together or omit them. Height hints also require both optional inputs below. Unity ignores these fields in schema 1 requests, which retain their usual pose and sign policy without native pressure sampling or a start-floor veto.

| Field | Type | Meaning |
|---|---|---|
| `floorEvidenceVersion` | int | Must be `1`. Absent or any other value: Unity ignores every field below. |
| `startFloor` | string | Floor the USER confirmed they are standing on, same format as `floor` (e.g. `"1"`). Never the destination floor, never derived from `floor`, `room` or `doorKey`. Positive integer. |
| `startFloorAgeS` | number | Finite seconds since the user confirmed the starting floor, measured on the same suspend-inclusive boot clock as `requestSentAtBootS`, never a clock that excludes sleep. Sampling and the start-floor veto require `0..1200`. |
| `requestSentAtBootS` | number, optional | Device monotonic time in seconds when the request ages were computed. Required for height hints. Use Android `SystemClock.elapsedRealtimeNanos() / 1e9` or iOS `mach_continuous_time()` converted with `mach_timebase_info` to seconds. Both clocks include time spent asleep. |
| `pressureSinceStart` | object | Host barometer summary since the confirmation (below). |
| `pressureSinceStart.settled` | bool | A settled window exists now and a settled anchor was captured at confirmation. |
| `pressureSinceStart.moved` | bool | The settled level moved at least 0.30 hPa from the level at confirmation. |
| `pressureSinceStart.sampleAgeS` | number | Finite age of the newest host barometer sample in seconds, measured on the same suspend-inclusive boot clock as `requestSentAtBootS`. Sampling and the start-floor veto require `0..10`. |
| `pressureSinceStart.deltaHpa` | number, optional for the start-floor veto and sampling consent. Required for all height hints. | Signed difference, current settled level minus the level at confirmation, in hPa. Negative means the pressure fell as the user went up. Send only with `settled:true`. A supplied delta must be finite, satisfy `abs <= 3`, and agree with `moved` (`abs >= 0.30` exactly when `moved`). When the delta is missing or invalid, the request is unhinted. Valid legacy evidence still governs sampling consent, the start-floor veto, and Try Again. |

Legacy payload example. Unity localizes without a height hint.
```json
{"schemaVersion":2,"room":"215","doorKey":"VEC215","floor":"2","position":{"x":1,"y":2,"z":3},"headingDeg":0,
 "mapCode":"<unchanged>","frame":"multiset-mapset","capabilities":["signArrival"],
 "floorEvidenceVersion":1,"startFloor":"1","startFloorAgeS":42,
 "pressureSinceStart":{"settled":true,"moved":false,"sampleAgeS":1.5}}
```
Raw pressure stays on the device. Keep absolute or raw pressure, sensor timestamps, coordinates and location out of the floor-evidence fields. Localize-only requests (`mode: localize`) may carry the same fields.

## Rule the Flutter side must compute (identical to `IndoorFloorBarometer.cs`)
Samples are `sensors_plus` `barometerEventStream` readings (hPa; only differences are used).
- Drop a reading closer than 0.5 s to the previous one. A gap over 10 s, or time going backward, clears the window.
- At confirmation and at each comparison, evaluate only the samples from the last 20 s. A level is settled when the window has at least 8 samples, spans at least 18 s, and has a peak-to-peak range of at most 0.06 hPa. The level is the median.
- Capture the settled level when the user confirms the floor. If pressure is unsettled, omit all floor-evidence fields and ask for a fresh confirmation once pressure settles.
- `moved` means the current settled level differs from the host anchor by at least 0.30 hPa. Omit the floor evidence when the confirmation is older than 1200 s, the newest sample is older than 10 s, or the sensor stream is lost.
- Permission or sensor missing: omit all fields. Unity then behaves as today.

Test vectors (1 s samples, pressure in hPa). Noise of 0.01 around a constant for 60 s is settled and unchanged. A 0.47 step then a 40 s hold is settled and moved. A 0.12 posture shift plus 0.10 drift is unchanged. A 25 s gap then a 40 s hold at a level 0.47 lower is settled and moved. A hold with a range of 0.2 is unsettled. After 11 s of silence the status is unsettled, and the next sample starts a new window.

## What Unity does
- **Pressure candidate floor (advisory).** With `moved:true` and a valid `deltaHpa`, a change of `c` hPa fits `k` storeys when `0.40k - 0.10 <= c <= 0.55k + 0.10`, for `k` from 1 to 4. Unity sends a height hint only if exactly one count fits and the resulting floor meets the eligibility limits above and has a walk grid. A change below 0.30, above 3, between counts, or in an overlap leaves the request unhinted. A confirmed sign supersedes host hint evidence. The candidate estimate has no pose-veto authority. Replay of the estimator before the overlap check included the true floor in the estimated range for all 72 floor changes across seven measured runs. The same runs calibrated the estimator, using one device in one building over one afternoon.
- **Sampling consent.** After map validation, Unity starts its native barometer on iOS and Android only for a schema 2 request with complete, well-typed version 1 floor evidence from the user's confirmation of a positive, parseable starting floor. Both age fields must be finite and within their documented bounds, checked on the wire value (`1200.000001` and `10.0000001` are stale). Skipping confirmation or sending stale evidence leaves collection and pressure-based release disabled.
- **Start-floor trust.** A veto additionally requires `settled:true` and `moved:false`.
- While trusted and no confirmed sign: a MultiSet pose on a different supported floor is rejected as a floor conflict and localization retries (the existing sign-conflict path). The pose floor must also be unambiguous (unknown or outside heights is rejected as before).
- Permanently drops the start-floor veto after a confirmed room sign, tracked motion from an accepted pose onto another floor, a settled pressure change of at least 0.30 hPa from Unity's first settled baseline after launch, or two separate attempts that return a valid pose on another floor. The second valid conflicting pose can be accepted immediately.
- A failed hinted request does not drop the start-floor veto. The pinned SDK reports a connectivity failure before the request is sent and a server no-pose as the same failure, so Unity cannot prove the hint was applied and rejected. The unhinted retry still requires a supported, unambiguous pose floor, agrees with any trusted sign and with the start floor, until one of the drops above applies. Releasing the veto after a failed hinted request would need a result-bearing SDK integration that tells a connectivity failure from a server no-pose; until then a generic SDK failure must never be treated as evidence of a server no-pose. Known limit: a wrong confirmed start floor then needs a second conflicting pose before the retry is accepted.
- Unity anchors the trusted start floor at its first settled window. Moving before that window settles can establish the baseline on a different floor and miss the move.
- Unity's own barometer (iOS Core Motion plugin, Android pressure sensor) also releases a stale confirmed sign after a settled change of at least 0.30 hPa from the pressure at sign confirmation. A sign confirmed before pressure settles must be confirmed again during a settled window for pressure to release it thereafter. Seeing the same still-valid sign again while pressure is unsettled (for example a posture shift) keeps the earlier baseline.
- Continuously tracked motion from an accepted pose can supersede a sign when it reaches a supported floor different from the sign's floor. Tracking loss or elapsed time alone leaves the sign veto in place.
- With no Unity sensor readings, the sign, tracked-motion, and repeated-conflict rules still apply. Floor evidence adds no raw pressure, coordinates, or confirmed start floor to logs.
- When a session reaches Done, Unity stops collecting pressure and clears its pressure window, anchor, and trusted start floor. This includes opening the localization failure card. **Try again** depends on the launch. A session launched **with** valid floor evidence does not retry in place: Unity delivers the existing `localizationFailed` result to the host, and the host must re-prompt for the starting floor and relaunch Unity with new evidence. Unity does not support in-place re-confirmation, and the host must never reuse launch-time evidence. A session launched **without** floor evidence (skipped, schema 1, stale, partial or invalid) retries in place as before, with no pressure. Signs superseded by tracked motion stay superseded.

## Flutter implementation requirements (independent dispatch)
1. Ask "Which floor are you on?" at entry, using explicit confirmation as in `FloorPressureRecorder.confirmFloor`. Send no fields if the user skips or confirms without a settled anchor. Preserve floor evidence in both `toJson` branches and in `withArToken` in `IndoorArRequest` (`lib/data/indoor_ar_contract.dart`).
2. Capture barometer readings from confirmation to launch (`sensors_plus`), using the rule and test vectors above. For height hinting, serialize both `requestSentAtBootS` and `pressureSinceStart.deltaHpa`. Compute `startFloorAgeS`, `sampleAgeS` and the stamp from the same suspend-inclusive boot clock, and stamp the request when computing the evidence ages for send. Include the signed change in hPa only when settled and `abs(deltaHpa) <= 3`. It is negative when going up. Set `moved == (abs(deltaHpa) >= 0.30)` and send a sample age from 0 to 10 s. Stop the stream when no longer needed.
3. On iOS, omit the fields if Motion & Fitness access is denied. Use the proposed Motion & Fitness wording in the host app’s `NSMotionUsageDescription`. [iOS export and host disclosure](ios-export.md#floor-change-pressure)
4. Test field presence and omission, including `requestSentAtBootS` and `pressureSinceStart.deltaHpa`, the version value, the age bounds, and omission when the sensor is missing or the floor is blank or non-numeric.
5. Ship order is free (absent fields change nothing, old Unity ignores them).

## Known limits and disclosed gaps
- Height hinting stays inert until the companion Flutter host emits both a valid `requestSentAtBootS` and a valid `pressureSinceStart.deltaHpa`. This applies to both the confirmed-start-floor hint and the pressure-based candidate hint. Unity’s own barometer starts after launch and cannot supply that first-request delta. End-to-end height hinting remains unverified.
- Missed on purpose: a real one-storey step hidden by opposing posture plus drift error above about 0.115 hPa.
- Unity receives hPa as a float. Near 1000 hPa, rounding can make a step of exactly 0.30 hPa read just under the threshold for either anchor.
- EditMode tests exercise timestamp forwarding and capture gaps using delayed reader output. Android polling with the event capture timestamp and the iOS Core Motion measurement timestamp require device verification.
- **Unity anchor lifetime.** Drift was observed for up to 20 minutes. Unity stops comparing against a pressure anchor once more than 20 minutes have elapsed since Unity captured it, whether at its first settled launch window or at sign confirmation. This timer starts when Unity captures the anchor.
- Even with a valid pressure anchor, tracking loss during an elevator ride can leave a stale sign vetoing poses on the new floor until pressure settles. MultiSet can exhaust its automatic retry first. The user may need to retry localization manually.
- Cold starts without valid host evidence have no start-floor veto. A wrong confirmation can cost one retry when both attempts return valid conflicting poses.
- Device behavior, iOS export linking CoreMotion, Android hardware, privacy and Motion & Fitness permission behavior, and the Flutter integration remain unverified.
