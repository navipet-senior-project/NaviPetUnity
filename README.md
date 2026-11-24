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
Flutter ──GET door pose──▶ Backend            Flutter ──POST /indoor/ar-token──▶ Backend ──▶ MultiSet (token only)
   │  request JSON (door pose, floor, optional short-lived token)
   ▼
Unity: AR camera → MultiSet VPS request (Unity ↔ MultiSet, camera frame) → pose in MapSet frame
       → floor check → AR HUD (walk-grid route for navigation requests)
   │  one final result JSON + optional progress status via native host
   ▼
Flutter
```

### Interfaces

- **Flutter → Unity request.** Flutter JSON-encodes the request and passes it untouched through the native channel (`navipet/indoor_ar`). On Android it is the `navipet.indoorAr.request` intent extra; on iOS it goes through `IndoorArHost`. `IndoorArContract.TryParseRequest` accepts `schemaVersion` 1 or 2. A navigation request carries the room, door key, floor, door position and heading, build map code, and frame name. Either mode may include `arToken` with `token` and `expiresOn`. A `mode: localize` request has no door and only locates the phone. The host supplies door position and heading directly in the MultiSet MapSet frame. A request whose frame or map code does not match the build is refused as `MAP_MISMATCH`.
- **Unity → Flutter result.** One result per session through `IndoorArBridge.Deliver`: `arrived`, `cancelled`, `localizationFailed`, `unavailable` or `error`, with counts and duration, and an optional distance, confidence and `signRoom`. Sign-confirmed arrival includes `signRoom` and omits confidence. Unity measures no distance for this arrival. It omits `checkDistanceM` for hosts declaring the `signArrival` capability and sends `checkDistanceM: 0` for legacy hosts. Error codes include `BAD_REQUEST`, `MAP_MISMATCH`, `ARCORE_UNSUPPORTED`, `MULTISET_AUTH`, `NETWORK` and `STEP_FREE_UNVERIFIED`.
- **Unity → native host status.** Optional progress goes to Android's `postStatus(String)` or a registered iOS `IndoorArHost` status handler. Without the handler, progress is dropped and the final result is still delivered. Flutter decoding is follow-up work. Messages include state, tracking, recognized room and floor when available, localization and fallback availability, and the raw `mapCode` and frame. Localized status includes the camera position, quaternion rotation and pose source in the MapSet frame. Keep status JSON out of logs.
- **Backend token.** `POST /indoor/ar-token` needs the app's normal signed-in session and returns a short-lived MultiSet token and its expiry. The backend uses its client credentials and caches the token. Flutter fetches the token for each launch. When both token fields are nonempty, Unity seeds the SDK's token cache and signals startup authentication success without validating the token or expiry. This path skips SDK configuration and the startup authentication exchange. If either field is missing or empty, the editor and players fall back to the SDK configuration, which can use a project-local, gitignored `MultiSetConfig.asset`. Never commit or print that asset. See **Privacy** for how to handle Android and iOS exports built with it.
- **Door poses and bundled markers.** The backend owns the MapSet door pose that Flutter forwards as the route target. Without a door pose, the app can request localization only. Unity's editor exports scene markers relative to `IndoorMapOrigin`. The bundled directory (`vec_floors123.json`) uses that exporter frame. `IndoorMapFrame` converts marker positions and door headings into MapSet coordinates for room landmarks and floor connectors.

Detailed field tables: [floor evidence contract](docs/floor-evidence-contract.md), [host bridge](docs/ios-export.md#host-bridge).

### Localization

1. The session opens and waits for AR support and camera tracking.
2. A supplied token pair seeds the SDK and skips the startup authentication exchange, so an expired token can reach the VPS path. Without a nonempty pair Unity uses the SDK configuration. Unity sends selected camera frames to MultiSet for VPS, one request at a time across the process. Each localization cycle has one automatic retry, including a new cycle after tracking loss from a pinned pose. The retry becomes due at the 30 s localization deadline or 3 s after a reply without an accepted pose, whichever comes first. After such a reply, a confirmed sign in view can make the retry due immediately. Dispatch still requires AR tracking and a free SDK lock. At 30 s in flight or queued behind a live request, that session's MultiSet fallback becomes unavailable. Only a success or failure callback releases the process-wide lock. A late reply releases the lock without applying the stale pose. If the callback never arrives, even a fresh Unity launch in the same process cannot start a VPS request until the app process restarts.
3. A returned pose is only a candidate. Before moving the map, Unity checks that the camera is 1 to 2.2 m above exactly one supported floor and agrees with any active sign or start-floor evidence. A second otherwise valid conflict on a separate attempt clears the start-floor veto, so Unity can accept that candidate immediately if it passes the remaining checks. A rejected pose is discarded. Unity retries only if the cycle still has its automatic retry available.
4. An accepted pose aligns the navigation root and shows **Location found**. Navigation requests proceed to guidance after a short hold. Localize-only requests stay on **Location found** while localized, without a route or pin.

Room-sign OCR runs alongside, on the AR camera's own frames, using the bundled ML Kit Latin recognizer on both platforms. A room is confirmed after two matching reads within four seconds. A sign identifies a room and a floor. A sign can pose the camera only through `SignFirst`, which needs a surveyed, device-validated calibration entry. The calibration ships empty, so sign poses are inert today. When the localization budget runs out, the shipped MultiSet mode shows the localization failure card even with OCR enabled. Only OCR-enabled `SignFirst` keeps scanning with room-only guidance after that budget expires. See [OCR and sign prerequisites](Assets/IndoorAr/OCR-POC-prerequisites.md).

### Pressure floor hints

The barometer is advisory evidence for which floor the user is on. It never produces a position.

- **Host evidence (schema 2).** After the user confirms their starting floor, the host may send `floorEvidenceVersion: 1`, a positive floor number as a `startFloor` string, `startFloorAgeS` (0–1200 s), and `pressureSinceStart` with booleans `settled` and `moved` and `sampleAgeS` (0–10 s). The Flutter launch request carries only a relative pressure summary.
- **Unity's own barometer.** Only for a request carrying such evidence, Unity reads absolute pressure on-device through Core Motion on iOS or the pressure sensor on Android. Unity compares its own samples to a settled baseline. A change of at least 0.30 hPa releases a stale confirmed sign or the start-floor veto. Unity stops sampling and clears its window when the session ends.
- **Start-floor veto.** Valid launch evidence with `settled: true` and `moved: false` makes the confirmed start floor trusted. Unity rejects the first otherwise valid MultiSet pose on another supported floor and uses the automatic retry if it remains. A second such conflict on a separate attempt clears the veto before Unity rechecks that same pose. A confirmed sign, tracked motion onto another floor, or a settled pressure change also clears the veto.
- **First-request height hint.** Height hinting additionally requires a valid `requestSentAtBootS` from the device's boot clock and a valid signed `pressureSinceStart.deltaHpa`. At SDK dispatch Unity adds the elapsed time since the host sent the request to both supplied ages, including time spent waiting for tracking or the SDK lock. If the ages are still within bounds and the evidence identifies a supported floor, Unity may narrow the first MultiSet request to a height band for floors 1 to 3 (the candidate floor's surface Y minus 0.3 m to plus 3.3 m). The band is advisory and the retry is unhinted. Missing or invalid hint fields still allow sampling and the veto when their launch requirements are met.

Without valid launch evidence Unity leaves pressure sampling, the start-floor veto and height hinting off.

### Routing and AR display

- **Walk grid.** `Resources/IndoorWalkGrid.json` is generated in the editor from the scanned floor mesh: 0.25 m cells in the MapSet frame, walkable where a floor surface exists and nothing sits 0.4 to 1.4 m above it. Floors 1 to 3 are covered; the scan has hallways only, not room interiors.
- **Route.** A* runs over the grid from the localized camera to the door, with endpoint snapping within 1.6 m and smoothing that respects blocked cells. On another floor, Unity chooses the shortest reachable grid route to a stairs or elevator marker with a matching destination-floor landing and a clear onward route to the door. The HUD names and pins the connector when its route can provide guidance. If no connector route is usable, the HUD shows **Route unavailable** and hides the pin. The path through the connector is not verified.
- **Step-free.** When a `stepFree` navigation request reaches route generation, Unity returns `unavailable` with `STEP_FREE_UNVERIFIED` because the grid has no verified accessibility data.
- **HUD.** The chevron ribbon and screen arrow use the same clear waypoint ahead. The target advances while the current waypoint is within 1 m and the line to the next one is clear. The arrow takes over when the ribbon is too short or off screen. Navigation can pin the door on the destination floor or a usable connector on another floor. The world pin docks or clips to the screen when out of view. On the localization failure card, **Try again** returns `localizationFailed` to the host after a launch with valid floor evidence so the host can get a fresh floor confirmation and relaunch. With no valid launch evidence, **Try again** retries in place. Both paths remain subject to the process-wide VPS lock. See [HUD preview](Assets/IndoorAr/HUD-preview.md).
- **Arrival.** In ordinary localized guidance the user can tap **I'm here**, then **Done** returns `arrived`. Confirming the destination sign also opens the arrival card, including on a `stepFree` request when the sign is confirmed before route generation. **Done** then returns `arrived` using the `signArrival` distance convention described under **Interfaces**. Arrival is not detected by distance alone.

### Privacy

- Unity sends selected camera frames to MultiSet through its SDK for VPS. ML Kit processes OCR images and text on the device and reports usage metrics to Google.
- Unity stores the supplied token and expiry in the SDK's `PlayerPrefs` entry and keeps them out of logs. When built with local MultiSet client credentials, Android AARs and iOS frameworks can embed them. Share those artifacts privately and keep them out of source control. See [Android AAR build](tools/build-indoor-ar-aar.sh) and [iOS export](docs/ios-export.md).
- Unity diagnostics (`[IndoorAr]`, `[IndoorArBaseline]`, `[ArDiag]`) keep full camera poses, raw map codes, images, raw OCR text and credentials out of logs. Map identity uses a short hash. A VPS candidate on a supported floor can log its floor and height above that floor rounded to 0.01 m. Floor transitions can log the floors and a 0.5 m eye-height bucket. Sign diagnostics can include normalized room IDs, and Unity's logged final result can include `signRoom`. Host status JSON is a separate payload carrying the raw map code and, when localized, the camera pose. Keep it out of host logs.
- The Flutter launch request carries only a relative pressure summary. Absolute readings from Unity's own iOS and Android sensors stay on the device. The iOS host must supply a Motion & Fitness usage description. See the proposed wording in [the iOS export notes](docs/ios-export.md#floor-change-pressure).
- A frozen-camera guard hides world content and asks the user to reopen AR instead of showing misplaced guidance.

### Verification status

- **Automated.** Unity EditMode tests cover the contract, the session state machine, localizer gating, floor evidence, grid routing, sign tracking and HUD logic. CI runs them on pull requests when Unity license secrets are set ([CI notes](docs/CI.md)); it does not build the Android library.
- **Not verified on a device:** the exported Android AAR and iOS framework with the Flutter host; physical route and arrow alignment with real doorways; MultiSet pose accuracy against surveyed points; ML Kit OCR on phones; the native barometer on both platforms; world pin and ribbon behavior; memory and battery.
- **Not available yet:** sign-based poses (empty calibration), floors 4 and 5, verified step-free routing, a distance-based arrival check, and end-to-end height hinting. The Flutter app on its main branch does not yet send floor evidence, so hinting and the start-floor veto stay inactive until it does. The hint's server and SDK behavior is unconfirmed.
- Metrics and the device checks still required are listed in the [baseline](Assets/IndoorAr/BASELINE.md).

## Building Directory Index Guide:
- The abbreviation for building is the same format as classroom names. (Ex. VEC202)
- Entrances are `entr`
- Elevators are `elev`
- Stairs are `stairs`
- Bathrooms are `bath`
- Normal classrooms are in their ordinary format.
