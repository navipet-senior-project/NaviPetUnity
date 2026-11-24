# Floor evidence contract (Flutter to Unity), version 1

Status: Unity side implemented. Flutter side NOT implemented. Until Flutter sends these fields, floor behaviour is unchanged.
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
| `startFloorAgeS` | number | Seconds since the user confirmed it (monotonic clock, not wall time). Must be `0..1200`. |
| `pressureSinceStart` | object | Host barometer summary since the confirmation (below). |
| `pressureSinceStart.settled` | bool | A settled window exists now. |
| `pressureSinceStart.moved` | bool | The settled level moved at least 0.30 hPa from the level at confirmation. |
| `pressureSinceStart.sampleAgeS` | number | Age of the newest barometer sample, `0..10`. |

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
- Keep the last 20 s. A level is settled when the window has at least 8 samples, spans at least 18 s, its newest sample is at most 10 s old, and its peak-to-peak range is at most 0.06 hPa. The level is the median.
- The anchor is the settled level at the moment the user confirms the floor. If not settled at confirmation, wait for the first settled window and anchor there (do not anchor while moving); until then send `settled:false`.
- `moved` = current settled level differs from the anchor by at least 0.30 hPa. After 1200 s since confirmation, or any sensor loss or denial, send `settled:false` (or omit the object): never `moved:false` on missing data.
- Permission or sensor missing: omit all fields. Unity then behaves as today.

Test vectors (1 s samples unless noted): 0.01 noise around a constant for 60 s => settled, not moved. A 0.47 step then 40 s hold => settled, moved. 0.22 posture shift plus 0.1 drift => not moved. 25 s with no samples then 40 s hold at a 0.47 lower level => settled, moved. Hold with range 0.2 => not settled. Reading stream silent for 11 s => window cleared, not settled.

## What Unity does
- Trusts `startFloor` only if version is 1, floor > 0, age 0..1200 s, `settled`, not `moved`, `sampleAgeS` 0..10. Anything else: ignored.
- While trusted and no confirmed sign: a MultiSet pose on a different supported floor is rejected as a floor conflict and localization retries (the existing sign-conflict path). The pose floor must also be unambiguous (unknown or outside heights is rejected as before).
- Gives way, permanently for the session: a confirmed room sign (the sign wins and the start floor never returns); tracked motion from an accepted pose onto another floor; Unity's own barometer settling at a level 0.30 hPa from its launch level; two separate localization attempts landing on another floor (a wrong confirmation costs two retries, not the session).
- Unity's own barometer (iOS Core Motion plugin, Android pressure sensor) also releases a stale confirmed sign after a settled 0.30 hPa move since the sign.
- No sensor on the Unity side keeps the host's claim. Logs carry no pressure values and no coordinates.

## Flutter implementation requirements (independent dispatch)
1. Entry-path prompt: ask "Which floor are you on?" (explicit confirm, same pattern as `FloorPressureRecorder.confirmFloor`). Skipping sends no fields. Use `IndoorArRequest` (`lib/data/indoor_ar_contract.dart`): add the four fields to both `toJson` branches and to `withArToken`.
2. Barometer capture from confirmation to launch (`sensors_plus`), with the rule above ported and unit-tested with the test vectors. Stop the stream when not needed.
3. iOS: barometer needs Motion & Fitness access; `NSMotionUsageDescription` currently says steps only. Wording/consent is a product call. Denied means omit fields.
4. Tests: field presence/absence, version value, age bounds, no field on missing sensor, no field when the floor is blank or non-numeric.
5. Ship order is free (absent fields change nothing, old Unity ignores them).

## Known limits and disclosed gaps
- Missed on purpose: a real one-storey step hidden by opposing posture plus drift error above about 0.115 hPa.
- Drift only observed to 20 minutes; confirmation older than 20 minutes is ignored.
- Cold start without a user-confirmed floor, a user who confirms the wrong floor (limited to two retries), and any MultiSet hinting remain unsolved.
- Not verified: device, iOS export linking CoreMotion, Android hardware, the Flutter side.
