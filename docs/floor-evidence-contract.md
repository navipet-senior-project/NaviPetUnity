# Floor evidence contract (Flutter to Unity), version 1

Unity implements the floor policy. The Flutter confirmation prompt, pressure capture, and request fields are a separate in-progress dispatch.
Scope limits, stated up front:
- This is a floor VETO and RELEASE signal only. Pressure never names a floor. Unity may narrow only the FIRST MultiSet request with `hintFloorHeight`, a Y band around the trusted floor (a confirmed sign's floor, else the host's unmoved start floor; never the destination `floor`). Every later request is unhinted, so a failed or wrong hint costs one retry (about `NoPoseRetryS`). The band is the walk-grid floor Y minus 0.3 m to plus 3.3 m and is unverified on device: whether the server filters on camera or floor height is not proven, and the band is sized to hold either. `hintMapCodes` is supported but off: the per-map codes are private and nothing in the repo supplies them. `hintPosition` and `hintRadius` are unused.
- No OCR is involved or changed.
- Calibrated on one iPhone 14 Plus in one building over one afternoon (stairs and elevator, seated and standing). Not validated on other devices, buildings, days or Android.
- Unity-only is not a complete feature: the cold-start benefit needs the Flutter work below.

## Transport
Flutter `jsonEncode`s the request (`IndoorArLauncher._launch`), `IndoorArChannel.swift` passes the string untouched to `IndoorArHost`, and Android passes it in the `navipet.indoorAr.request` intent extra. `IndoorArContract.TryParseRequest` uses `JsonUtility` for the request and validates the optional evidence against the original JSON. Older Unity builds ignore the new fields.

## Optional request fields for schemaVersion 2

Send all floor-evidence fields together or omit them. Unity ignores these fields in schema 1 requests, which retain their usual pose and sign policy without native pressure sampling or a start-floor veto.

| Field | Type | Meaning |
|---|---|---|
| `floorEvidenceVersion` | int | Must be `1`. Absent or any other value: Unity ignores every field below. |
| `startFloor` | string | Floor the USER confirmed they are standing on, same format as `floor` (e.g. `"1"`). Never the destination floor, never derived from `floor`, `room` or `doorKey`. Positive integer. |
| `startFloorAgeS` | number | Finite seconds since the user confirmed the starting floor, measured on a monotonic clock. Sampling and the start-floor veto require `0..1200`. |
| `pressureSinceStart` | object | Host barometer summary since the confirmation (below). |
| `pressureSinceStart.settled` | bool | A settled window exists now and a settled anchor was captured at confirmation. |
| `pressureSinceStart.moved` | bool | The settled level moved at least 0.30 hPa from the level at confirmation. |
| `pressureSinceStart.sampleAgeS` | number | Finite age of the newest host barometer sample in seconds. Sampling and the start-floor veto require `0..10`. |

Example:
```json
{"schemaVersion":2,"room":"215","doorKey":"VEC215","floor":"2","position":{"x":1,"y":2,"z":3},"headingDeg":0,
 "mapCode":"<unchanged>","frame":"multiset-mapset","capabilities":["signArrival"],
 "floorEvidenceVersion":1,"startFloor":"1","startFloorAgeS":42,
 "pressureSinceStart":{"settled":true,"moved":false,"sampleAgeS":1.5}}
```
Raw pressure stays on the device. Keep absolute or raw pressure, timestamps, coordinates and location out of the floor-evidence fields. Localize-only requests (`mode: localize`) may carry the same fields.

## Rule the Flutter side must compute (identical to `IndoorFloorBarometer.cs`)
Samples are `sensors_plus` `barometerEventStream` readings (hPa; only differences are used).
- Drop a reading closer than 0.5 s to the previous one. A gap over 10 s, or time going backward, clears the window.
- At confirmation and at each comparison, evaluate only the samples from the last 20 s. A level is settled when the window has at least 8 samples, spans at least 18 s, and has a peak-to-peak range of at most 0.06 hPa. The level is the median.
- Capture the settled level when the user confirms the floor. If pressure is unsettled, omit all floor-evidence fields and ask for a fresh confirmation once pressure settles.
- `moved` means the current settled level differs from the host anchor by at least 0.30 hPa. Omit the floor evidence when the confirmation is older than 1200 s, the newest sample is older than 10 s, or the sensor stream is lost.
- Permission or sensor missing: omit all fields. Unity then behaves as today.

Test vectors (1 s samples, pressure in hPa). Noise of 0.01 around a constant for 60 s is settled and unchanged. A 0.47 step then a 40 s hold is settled and moved. A 0.12 posture shift plus 0.10 drift is unchanged. A 25 s gap then a 40 s hold at a level 0.47 lower is settled and moved. A hold with a range of 0.2 is unsettled. After 11 s of silence the status is unsettled, and the next sample starts a new window.

## What Unity does
- **Sampling consent.** After map validation, Unity starts its native barometer on iOS and Android only for a schema 2 request with complete, well-typed version 1 floor evidence from the user's confirmation of a positive, parseable starting floor. Both age fields must be finite and within their documented bounds, checked on the wire value before any float rounding (`1200.000001` and `10.0000001` are stale). Skipping confirmation or sending stale evidence leaves collection and pressure-based release disabled.
- **Start-floor trust.** A veto additionally requires `settled:true` and `moved:false`.
- While trusted and no confirmed sign: a MultiSet pose on a different supported floor is rejected as a floor conflict and localization retries (the existing sign-conflict path). The pose floor must also be unambiguous (unknown or outside heights is rejected as before).
- Permanently drops the start-floor veto after a confirmed room sign, tracked motion from an accepted pose onto another floor, a settled pressure change of at least 0.30 hPa from Unity's first settled baseline after launch, or two separate attempts that return a valid pose on another floor. The second valid conflicting pose can be accepted immediately.
- Unity anchors the trusted start floor at its first settled window. Moving before that window settles can establish the baseline on a different floor and miss the move.
- Unity's own barometer (iOS Core Motion plugin, Android pressure sensor) also releases a stale confirmed sign after a settled change of at least 0.30 hPa from the pressure at sign confirmation. A sign confirmed before pressure settles must be confirmed again during a settled window for pressure to release it thereafter. Seeing the same still-valid sign again while pressure is unsettled (for example a posture shift) keeps the earlier baseline.
- Continuously tracked motion from an accepted pose can supersede a sign when it reaches a supported floor different from the sign's floor. Tracking loss or elapsed time alone leaves the sign veto in place.
- With no Unity sensor readings, the sign, tracked-motion, and repeated-conflict rules still apply. Floor evidence adds no raw pressure, coordinates, or confirmed start floor to logs.
- When a session reaches Done, Unity stops collecting pressure and clears its pressure window, anchor, and trusted start floor. This includes opening the localization failure card. **Try again** depends on the launch. A session launched **with** valid floor evidence does not retry in place: Unity delivers the existing `localizationFailed` result to the host, and the host must re-prompt for the starting floor and relaunch Unity with new evidence. Unity does not support in-place re-confirmation, and the host must never reuse launch-time evidence. A session launched **without** floor evidence (skipped, schema 1, stale, partial or invalid) retries in place as before, with no pressure. Signs superseded by tracked motion stay superseded.

## Flutter implementation requirements (independent dispatch)
1. Ask "Which floor are you on?" at entry, using explicit confirmation as in `FloorPressureRecorder.confirmFloor`. Send no fields if the user skips or confirms without a settled anchor. Add the four fields to both `toJson` branches and to `withArToken` in `IndoorArRequest` (`lib/data/indoor_ar_contract.dart`).
2. Barometer capture from confirmation to launch (`sensors_plus`), with the rule above ported and unit-tested with the test vectors. Stop the stream when not needed.
3. On iOS, omit the fields if Motion & Fitness access is denied. Use the proposed Motion & Fitness wording in the host app’s `NSMotionUsageDescription`. [iOS export and host disclosure](ios-export.md#floor-change-pressure)
4. Tests: field presence/absence, version value, age bounds, no field on missing sensor, no field when the floor is blank or non-numeric.
5. Ship order is free (absent fields change nothing, old Unity ignores them).

## Known limits and disclosed gaps
- Missed on purpose: a real one-storey step hidden by opposing posture plus drift error above about 0.115 hPa.
- Unity receives hPa as a float. Near 1000 hPa, rounding can make a step of exactly 0.30 hPa read just under the threshold for either anchor.
- EditMode tests exercise timestamp forwarding and capture gaps using delayed reader output. Android polling with the event capture timestamp and the iOS Core Motion measurement timestamp require device verification.
- **Unity anchor lifetime.** Drift was observed for up to 20 minutes. Unity stops comparing against a pressure anchor once more than 20 minutes have elapsed since Unity captured it, whether at its first settled launch window or at sign confirmation. This timer starts when Unity captures the anchor.
- Even with a valid pressure anchor, tracking loss during an elevator ride can leave a stale sign vetoing poses on the new floor until pressure settles. MultiSet can exhaust its automatic retry first. The user may need to retry localization manually.
- Cold starts without valid host evidence have no start-floor veto. A wrong confirmation can cost one retry when both attempts return valid conflicting poses.
- Device behavior, iOS export linking CoreMotion, Android hardware, privacy and Motion & Fitness permission behavior, and the Flutter integration remain unverified.
