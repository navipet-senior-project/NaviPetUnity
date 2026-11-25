# NaviPetUnity
The Unity Component of the NaviPet App.

Indoor AR starts a fresh navigation session for each launch, including after arrival, cancellation, or closing AR. Build and host setup: [Android library](Assets/IndoorAr/HUD-preview.md), [iOS export](docs/ios-export.md). See [OCR localization and route prerequisites](Assets/IndoorAr/OCR-POC-prerequisites.md) before using sign-based alignment; physical calibration and device validation remain pending.

## Indoor navigation architecture (MultiSet)

This repository contains the Unity AR runtime embedded by NaviPet Flutter and the Unity editor tools for local navigation assets. Flutter, the backend and the hosted MultiSet service have separate roles.

| Part | Repository | Owns |
| --- | --- | --- |
| Flutter app | NaviPetFlutter | Search and room selection, camera permission, fetching the MultiSet launch token, building the launch request, launching Unity, showing the result. |
| Backend | NavipetBackend | Campus and room data, the saved door pose for each indoor destination, and the authenticated token endpoint. Holds the client credentials for token issuance. Unity's SDK fallback can also use credentials from its local build config. |
| MultiSet | External service | Visual positioning (VPS) against the hosted VEC scan map. |
| Unity (this repo) | NaviPetUnity | The AR camera session, localization, floor decision, hallway routing and the AR display. Editor tools export scene markers and generate the walk grid. |

Unity does not call the backend or Supabase. It also has no door database of its own for navigation targets: the destination door pose arrives in the request. Outdoor AR (`Assets/OutdoorAr`) shares the host bridge but is a separate flow and is not described here.

```
Flutter ──GET /places/:id──▶ Backend          Flutter ──POST /indoor/ar-token──▶ Backend ──▶ MultiSet (token only)
   │  request JSON (door pose, floor, optional short-lived token)
   ▼
Unity: AR camera → MultiSet VPS request (Unity ↔ MultiSet, camera frame) → pose in MapSet frame
       → floor check → AR HUD (walk-grid guidance after the hold, with a usable route)
   ├── optional progress status ──▶ native host status handler
   │  one final result JSON
   ▼
Flutter
```

### Interfaces

- **Flutter → Unity request.** Flutter JSON-encodes the request and passes it untouched through the native channel (`navipet/indoor_ar`). On Android it is the `navipet.indoorAr.request` intent extra; on iOS it goes through `IndoorArHost`. `IndoorArContract.TryParseRequest` accepts `schemaVersion` 1 or 2. A navigation request carries the room, door key, floor, door position and heading, build map code, and frame name. Either mode may include `arToken` with `token` and `expiresOn`. A `mode: localize` request has no door and only locates the phone. The host supplies door position and heading directly in the MultiSet MapSet frame. A request whose frame or map code does not match the build is refused as `MAP_MISMATCH`.
- **Unity → Flutter result.** `IndoorArBridge.Deliver` sends one result per session: `arrived`, `cancelled`, `localizationFailed`, `unavailable` or `error`, with counts and duration, and optional distance, confidence and `signRoom`. A localize-only pose updates optional native status and normally shows **Location found**. Closing returns `cancelled`. Manual arrival reports horizontal `checkDistanceM`, even across floors. Sign arrival includes `signRoom` without confidence or measured distance. Hosts declaring `signArrival` get no `checkDistanceM`, while legacy hosts get `0`. Error codes include `BAD_REQUEST`, `MAP_MISMATCH`, `ARCORE_UNSUPPORTED`, `MULTISET_AUTH`, `NETWORK` and `STEP_FREE_UNVERIFIED`.
- **Unity → native host status.** Optional progress goes to Android's `postStatus(String)` or a registered iOS `IndoorArHost` status handler. Without the handler, progress is dropped and the final result is still delivered. The shipped Flutter hosts register no status handler on either platform, so progress is currently dropped. Messages include state, tracking, recognized room and floor when available, localization and fallback availability, and the raw `mapCode` and frame. Localized status includes the camera position, quaternion rotation and pose source in the MapSet frame. Keep status JSON out of logs.
- **Backend token.** `POST /indoor/ar-token` uses the app's signed-in session. The backend uses its client credentials to obtain and cache a short-lived MultiSet token, and Flutter fetches it with its expiry per launch. After device AR support and connectivity checks pass, a nonempty token pair seeds the SDK cache and signals startup success without validation or an authentication exchange. If either field is missing or empty, Unity loads SDK configuration, which can use the gitignored `MultiSetConfig.asset`. Normal Editor entry skips authentication. Offline startup returns `NETWORK` before token handling. Never commit or print the asset. See **Privacy** for Android and iOS exports built with it.
- **Door poses and bundled markers.** The backend owns the MapSet door pose that Flutter forwards as the route target. Without a door pose, the app can request localization only. Unity's editor exports scene markers relative to `IndoorMapOrigin`. The bundled directory (`vec_floors123.json`) uses that exporter frame. `IndoorMapFrame` converts marker positions and door headings into MapSet coordinates for room landmarks and floor connectors.

Detailed field tables: [floor evidence contract](docs/floor-evidence-contract.md), [host bridge](docs/ios-export.md#host-bridge).

### Localization

1. The session opens and waits for AR support and camera tracking.
2. After device AR support and connectivity checks pass, Unity seeds the SDK from the supplied pair or loads SDK configuration as described above. A nonempty expired token can still reach VPS. Unity sends selected camera frames to MultiSet for VPS, one request at a time across the process. Each localization cycle has one automatic retry, including a new cycle after tracking loss from a pinned pose. The retry becomes due at the 30 s localization deadline or 3 s after a reply without an accepted pose, whichever comes first. After such a reply, a confirmed sign in view can make the retry due immediately. Dispatch still requires AR tracking and a free SDK lock. At 30 s in flight or queued behind a live request, that session's MultiSet fallback becomes unavailable. Only a success or failure callback releases the process-wide lock. A late reply releases the lock without applying the stale pose. If the callback never arrives, even a fresh Unity launch in the same process cannot start a VPS request until the app process restarts.
3. A returned pose is only a candidate. Before moving the map, Unity checks that the camera is 1 to 2.2 m above exactly one supported floor and agrees with any active sign or start-floor evidence. A second otherwise valid conflict on a separate attempt clears the start-floor veto, so Unity can accept that candidate immediately if it passes the remaining checks. A rejected pose is discarded. Unity retries only if the cycle still has its automatic retry available.
4. An accepted pose aligns the navigation root. **Location found** is the default while localized for localize-only requests (no route or pin) and during navigation's initial 1.5 s hold. Route warnings can override it. Guidance starts when the hold has elapsed and a route is usable. A step-free request that reaches routing returns `unavailable` with `STEP_FREE_UNVERIFIED`.

Room-sign OCR runs alongside, on the AR camera's own frames, using the bundled ML Kit Latin recognizer on both platforms. A room is confirmed after two matching reads within four seconds. A sign identifies a room and a floor. A sign can pose the camera only through `SignFirst`, which needs a surveyed, device-validated calibration entry. The calibration ships empty, so sign poses are inert today. When the localization budget runs out, the shipped MultiSet mode shows the localization failure card even with OCR enabled. Only OCR-enabled `SignFirst` keeps scanning with room-only guidance after that budget expires. See [OCR and sign prerequisites](Assets/IndoorAr/OCR-POC-prerequisites.md).

### Pressure floor hints

The barometer is advisory evidence for which floor the user is on. It never produces a position.

- **Host evidence (schema 2).** After the user confirms their starting floor, the host may send `floorEvidenceVersion: 1`, a positive floor number as a `startFloor` string, `startFloorAgeS` (0–1200 s), and `pressureSinceStart` with booleans `settled` and `moved` and `sampleAgeS` (0–10 s). The Flutter launch request carries only a relative pressure summary.
- **Unity's own barometer.** With valid launch evidence, Unity reads absolute pressure on-device (Core Motion on iOS, pressure sensor on Android). A settled change of at least 0.30 hPa releases the sign or start-floor veto only against a settled anchor at most 20 minutes old. Unity stops sampling and clears its window at session end.
- **Start-floor veto.** Valid launch evidence with `settled: true` and `moved: false` makes the confirmed start floor trusted. Unity rejects the first otherwise valid MultiSet pose on another supported floor and retries if it can. A second conflict on a separate attempt clears the veto before Unity rechecks that pose. A confirmed sign, tracked motion onto another floor, or pressure meeting the anchor rule above also clears it.
- **First-request height hint.** Height hinting additionally requires a valid `requestSentAtBootS` from the device's boot clock and a valid signed `pressureSinceStart.deltaHpa`. At SDK dispatch Unity adds the elapsed time since the host sent the request to both supplied ages, including time spent waiting for tracking or the SDK lock. If the ages are still within bounds and the evidence identifies a supported floor, Unity may narrow the first MultiSet request to a height band for floors 1 to 3 (the candidate floor's surface Y minus 0.3 m to plus 3.3 m). The band is advisory and the retry is unhinted. Missing or invalid hint fields still allow sampling and the veto when their launch requirements are met.

Without valid launch evidence Unity leaves pressure sampling, the start-floor veto and height hinting off.

### Routing and AR display

- **Walk grid.** `Resources/IndoorWalkGrid.json` is generated in the editor from the scanned floor mesh: 0.25 m cells in the MapSet frame, walkable where a floor surface exists and nothing sits 0.4 to 1.4 m above it. Floors 1 to 3 are covered; the scan has hallways only, not room interiors.
- **Route.** A* runs over the grid from the localized camera to the door, with endpoint snapping within 1.6 m and smoothing that respects blocked cells. On another floor, Unity chooses the shortest reachable grid route to a stairs or elevator marker with a matching destination-floor landing and a clear onward route to the door. The HUD names and pins the connector when its route can provide guidance. If no connector route is usable, the HUD shows **Route unavailable** and hides the pin. The path through the connector is not verified.
- **Step-free.** When a `stepFree` navigation request reaches route generation, Unity returns `unavailable` with `STEP_FREE_UNVERIFIED` because the grid has no verified accessibility data.
- **HUD.** During active guidance, the chevron ribbon and screen arrow share a clear waypoint. The target advances when the current waypoint is within 1 m and the line to the next is clear. The arrow takes over if the ribbon fails clearance, otherwise cannot be drawn, or is out of view. The door or usable connector pin docks or clips to the screen when off-screen. **Try again** returns `localizationFailed` for fresh floor confirmation and relaunch when the original launch had valid floor evidence, or retries in place otherwise. Both paths remain subject to the process-wide VPS lock. See [HUD preview](Assets/IndoorAr/HUD-preview.md).
- **Arrival.** After the hold, localized non-step-free navigation can offer **I'm here** even with **Route unavailable** or on a different floor. **Done** confirms arrival. Confirming the destination sign also opens the arrival card, including for `stepFree` before routing, using the `signArrival` convention above.

### Privacy

- Unity sends selected camera frames to MultiSet through its SDK for VPS. ML Kit processes OCR images and text on the device and reports usage metrics to Google.
- When Unity uses the supplied token, it stores the token and expiry in the SDK's `PlayerPrefs` entry and keeps them out of logs. When built with local MultiSet client credentials, Android AARs and iOS frameworks can embed them. Share those artifacts privately and keep them out of source control. See [Android AAR build](tools/build-indoor-ar-aar.sh) and [iOS export](docs/ios-export.md).
- Unity diagnostics (`[IndoorAr]`, `[IndoorArBaseline]`, `[ArDiag]`) keep full camera poses, raw map codes, images, raw OCR text and credentials out of logs. Map identity uses a short hash. A VPS candidate on a supported floor can log its floor and height above that floor rounded to 0.01 m. Floor transitions can log the floors and a 0.5 m eye-height bucket. Sign diagnostics can include normalized room IDs, and Unity's logged final result can include `signRoom`. Host status JSON is a separate payload carrying the raw map code and, when localized, the camera pose. Keep it out of host logs.
- The Flutter launch request carries only a relative pressure summary. Absolute readings from Unity's own iOS and Android sensors stay on the device. The iOS host must supply a Motion & Fitness usage description. See the proposed wording in [the iOS export notes](docs/ios-export.md#floor-change-pressure).
- A frozen-camera guard hides world content and asks the user to reopen AR instead of showing misplaced guidance.

### Verification status

- **Automated.** Unity EditMode tests cover the contract, the session state machine, localizer gating, floor evidence, grid routing, sign tracking and HUD logic. CI runs them on pull requests when Unity license secrets are set ([CI notes](docs/CI.md)); it does not build the Android library.
- **Not verified on a device:** the exported Android AAR and iOS framework with the Flutter host; physical route and arrow alignment with real doorways; MultiSet pose accuracy against surveyed points; ML Kit OCR on phones; the native barometer on both platforms; world pin and ribbon behavior; memory and battery.
- **Not available yet:** sign-based poses (empty calibration), floors 4 and 5, verified step-free routing, and a distance-based arrival check. The Flutter app's main branch sends floor evidence after the user confirms a start floor, but end-to-end height hinting and the start-floor veto have not been verified on a device, and the hint's server and SDK behavior is unconfirmed.
- Metrics and the device checks still required are listed in the [baseline](Assets/IndoorAr/BASELINE.md).

## Building Directory Index Guide:
- The abbreviation for building is the same format as classroom names. (Ex. VEC202)
- Entrances are `entr`
- Elevators are `elev`
- Stairs are `stairs`
- Bathrooms are `bath`
- Normal classrooms are in their ordinary format.
