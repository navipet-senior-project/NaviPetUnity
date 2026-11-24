# Floor evidence contract (Flutter to Unity), version 1

Unity implements the floor policy. The Flutter confirmation prompt, pressure capture, and request fields are a separate in-progress dispatch. Unity uses its own barometer only for indoor launches that carry completed floor confirmation.
Scope limits, stated up front:
- This is a floor VETO and RELEASE signal only. It does not name a floor from pressure and it does not send any MultiSet hint. **No MultiSet hints are implemented** (`hintFloorHeight`, `hintMapCodes`, `hintPosition` are unused); they are blocked on a server-keyframe height check and portal map codes.
- No OCR is involved or changed.
- Calibrated on one iPhone, one building, one afternoon (stairs and elevator, seated and standing). Not validated on other devices, buildings, days or Android.
- Unity-only is not a complete feature: the cold-start benefit needs the Flutter work below.

## Transport
No native host change. Flutter `jsonEncode`s the request (`IndoorArLauncher._launch`), `IndoorArChannel.swift` passes the string untouched to `IndoorArHost`, and Android passes it in the `navipet.indoorAr.request` intent extra. Unity parses it with `JsonUtility` (`IndoorArContract.TryParseRequest`). Unknown fields are ignored, so old Unity builds accept the new request.

## Request fields (additive to schemaVersion 2; all optional, send all together or none)

| Field | Type | Meaning |
|---|---|---|
| `floorEvidenceVersion` | int | Must be `1`. Absent or any other value: Unity ignores every field below. |
| `startFloor` | string | Floor the USER confirmed they are standing on, same format as `floor` (e.g. `"1"`). Never the destination floor, never derived from `floor`, `room` or `doorKey`. Positive integer. |
| `startFloorAgeS` | number | Finite seconds since the user confirmed it, measured on a monotonic clock. Must be `0..1200`. |
| `pressureSinceStart` | object | Host barometer summary since the confirmation (below). |
| `pressureSinceStart.settled` | bool | A settled window exists now and a settled anchor was captured at confirmation. |
| `pressureSinceStart.moved` | bool | The settled level moved at least 0.30 hPa from the level at confirmation. |
| `pressureSinceStart.sampleAgeS` | number | Finite age of the newest barometer sample in seconds, `0..10`. |

Example:
```json
{"schemaVersion":2,"room":"215","doorKey":"VEC215","floor":"2","position":{"x":1,"y":2,"z":3},"headingDeg":0,
 "mapCode":"<unchanged>","frame":"multiset-mapset","capabilities":["signArrival"],
 "floorEvidenceVersion":1,"startFloor":"1","startFloorAgeS":42,
 "pressureSinceStart":{"settled":true,"moved":false,"sampleAgeS":1.5}}
```
Never send absolute or raw pressure, timestamps, coordinates or location. Localize-only requests (`mode: localize`) may carry the same fields.

## Rule the Flutter side must compute (identical to `IndoorFloorBarometer.cs`)
Samples are `sensors_plus` `barometerEventStream` readings (hPa; only differences are used).
- Drop a reading closer than 0.5 s to the previous one. A gap over 10 s, or time going backward, clears the window.
- At confirmation and at each comparison, evaluate only the samples from the last 20 s. A level is settled when the window has at least 8 samples, spans at least 18 s, its newest sample is at most 10 s old, and its peak-to-peak range is at most 0.06 hPa. The level is the median.
- Capture the settled level when the user confirms the floor. If pressure is unsettled, omit all floor-evidence fields and ask for a fresh confirmation once pressure settles.
- `moved` = current settled level differs from the anchor by at least 0.30 hPa. After 1200 s since confirmation, or any sensor loss or denial, send `settled:false` (or omit the object): never `moved:false` on missing data.
- Permission or sensor missing: omit all fields. Unity then behaves as today.

Test vectors (1 s samples, pressure in hPa). Noise of 0.01 around a constant for 60 s is settled and unchanged. A 0.47 step then a 40 s hold is settled and moved. A 0.12 posture shift plus 0.10 drift is unchanged. A 25 s gap then a 40 s hold at a level 0.47 lower is settled and moved. A hold with a range of 0.2 is unsettled. After 11 s of silence the status is unsettled, and the next sample starts a new window.

## What Unity does
- Starts its native barometer on iOS and Android only after map validation and only with complete version 1 evidence containing a positive, parseable starting floor. Skipping confirmation sends no evidence and disables Unity-side barometer collection and pressure-based release. A complete confirmation can enable sampling even if its pressure summary is too old, unsettled, or moved to support a start-floor veto.
- Trusts `startFloor` only when all fields are present with the types above, version is 1, floor > 0, age is 0..1200 s, `settled` is true, `moved` is false, and `sampleAgeS` is 0..10 s.
- While trusted and no confirmed sign: a MultiSet pose on a different supported floor is rejected as a floor conflict and localization retries (the existing sign-conflict path). The pose floor must also be unambiguous (unknown or outside heights is rejected as before).
- Permanently drops the start-floor veto after a confirmed room sign, tracked motion from an accepted pose onto another floor, a settled pressure change of at least 0.30 hPa from Unity's first settled baseline after launch, or two separate attempts that return a valid pose on another floor. The second valid conflicting pose can be accepted immediately.
- Unity anchors the trusted start floor at its first settled window. Moving before that window settles can establish the baseline on a different floor and miss the move.
- Unity's own barometer (iOS Core Motion plugin, Android pressure sensor) also releases a stale confirmed sign after a settled change of at least 0.30 hPa from the pressure at sign confirmation. A sign confirmed before pressure settles must be confirmed again during a settled window for pressure to release it thereafter.
- With no Unity sensor readings, the sign, tracked-motion, and repeated-conflict rules still apply. Floor evidence adds no raw pressure, coordinates, or confirmed start floor to logs.

## Flutter implementation requirements (independent dispatch)
1. Ask "Which floor are you on?" at entry, using explicit confirmation as in `FloorPressureRecorder.confirmFloor`. Send no fields if the user skips or confirms without a settled anchor. Add the four fields to both `toJson` branches and to `withArToken` in `IndoorArRequest` (`lib/data/indoor_ar_contract.dart`).
2. Barometer capture from confirmation to launch (`sensors_plus`), with the rule above ported and unit-tested with the test vectors. Stop the stream when not needed.
3. On iOS, omit the fields if Motion & Fitness access is denied. Use the proposed Motion & Fitness wording in the host app’s `NSMotionUsageDescription`. [iOS export and host disclosure](ios-export.md#floor-change-pressure)
4. Tests: field presence/absence, version value, age bounds, no field on missing sensor, no field when the floor is blank or non-numeric.
5. Ship order is free (absent fields change nothing, old Unity ignores them).

## Known limits and disclosed gaps
- Missed on purpose: a real one-storey step hidden by opposing posture plus drift error above about 0.115 hPa.
- Drift only observed to 20 minutes; confirmation older than 20 minutes is ignored.
- Even with a valid pressure anchor, tracking loss during an elevator ride can leave a stale sign vetoing poses on the new floor until pressure settles. MultiSet can exhaust its automatic retry first. The user may need to retry localization manually.
- Cold starts without valid host evidence have no start-floor veto. A wrong confirmation can cost one retry when both attempts return valid conflicting poses.
- Device behavior, iOS export linking CoreMotion, Android hardware, privacy and Motion & Fitness permission behavior, and the Flutter integration remain unverified.
