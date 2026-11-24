# NaviPetUnity
The Unity Component of the NaviPet App.

Indoor AR starts a fresh navigation session for each launch, including after arrival, cancellation, or closing AR. Build and host setup: [Android library](Assets/IndoorAr/HUD-preview.md), [iOS export](docs/ios-export.md). See [OCR localization and route prerequisites](Assets/IndoorAr/OCR-POC-prerequisites.md) before using sign-based alignment; physical calibration and device validation remain pending.

## Indoor navigation architecture (MultiSet)

This repository is only the Unity AR runtime that the NaviPet Flutter app embeds. It is not the app and not the mapping service. The pieces are separate:

| Part | Repository | Owns |
| --- | --- | --- |
| Flutter app | NaviPetFlutter | Search and room selection, camera permission, fetching the MultiSet token, building the launch request, launching Unity, showing the result. |
| Backend | NavipetBackend | Campus and room data, the saved door pose for each indoor destination, and the authenticated token endpoint. Holds the MultiSet client credentials. |
| MultiSet | External service | Visual positioning (VPS) against the hosted VEC scan map. |
| Unity runtime (this repo) | NaviPetUnity | The AR camera session, localization, floor decision, hallway routing and the AR display. |

Unity does not call the backend or Supabase. It also has no door database of its own for navigation targets: the destination door pose arrives in the request. Outdoor AR (`Assets/OutdoorAr`) shares the host bridge but is a separate flow and is not described here.

```
Flutter ──GET door pose──▶ Backend            Flutter ──POST /indoor/ar-token──▶ Backend ──▶ MultiSet (token only)
   │  request JSON (door pose, floor, short-lived token)
   ▼
Unity: AR camera → MultiSet VPS request (Unity ↔ MultiSet, camera frame) → pose in MapSet frame
       → floor check → walk-grid route → AR HUD
   │  progress status + one final result JSON
   ▼
Flutter
```

### Interfaces

- **Flutter → Unity request.** Flutter JSON-encodes the request and passes it untouched through the native channel (`navipet/indoor_ar`). On Android it is the `navipet.indoorAr.request` intent extra; on iOS it goes through `IndoorArHost`. `IndoorArContract.TryParseRequest` accepts `schemaVersion` 1 or 2. A request carries the room, door key, floor, door position and heading, build map code, the frame name, and the token. A `mode: localize` request has no door and only locates the phone. Pose fields are in the MultiSet MapSet frame and nothing converts them. A request whose frame or map code does not match the build is refused as `MAP_MISMATCH`.
- **Unity → Flutter result.** One result per session through `IndoorArBridge.Deliver`: `arrived`, `cancelled`, `localizationFailed`, `unavailable` or `error`, with counts and duration, and an optional distance, confidence and `signRoom`. Error codes include `BAD_REQUEST`, `MAP_MISMATCH`, `ARCORE_UNSUPPORTED`, `MULTISET_AUTH`, `NETWORK` and `STEP_FREE_UNVERIFIED`.
- **Unity → Flutter status.** Optional progress messages (state, tracking, recognized room, whether localized, pose source) through the host's status handler. A host without one drops them.
- **Backend token.** `POST /indoor/ar-token` needs the app's normal signed-in session and returns a short-lived MultiSet token and its expiry. The MultiSet client credentials stay on the backend, which caches the token. The app fetches it per launch and sends it in the request; Unity loads it into the MultiSet SDK instead of using client credentials. A project-local, gitignored `MultiSetConfig.asset` exists only for builds and editor runs; never commit or print it.
- **Door poses.** The backend stores each VEC door as a point in the MapSet frame and the Flutter app forwards it. The app has no door pose for a room without one, so it can only run a localize request. Unity's own copy of the directory (`vec_floors123.json`) is used for sign matching, floor connectors and rooms the request does not carry; the backend stays authoritative for the destination door.

Detailed field tables: [floor evidence contract](docs/floor-evidence-contract.md), [host bridge](docs/ios-export.md#host-bridge).

### Localization

1. The session opens and waits for AR support and camera tracking.
2. Flutter's token authenticates the MultiSet SDK. Unity then sends one camera frame at a time for VPS localization; at most one SDK request exists process-wide. Each request times out after 30 s, and one automatic retry follows (after 3 s if MultiSet answered without a pose, or immediately if a confirmed sign is in view).
3. A returned pose is only a candidate. Unity checks it before moving the map: the camera must be 1 to 2.2 m above exactly one supported floor, and the floor must agree with any active sign or start-floor evidence. A rejected pose is discarded and follows the retry path.
4. An accepted pose moves the navigation root so the AR camera sits at the pose; the session then shows a short "Location found" hold and starts guidance.

Room-sign OCR runs alongside, on the AR camera's own frames, using the bundled ML Kit Latin recognizer on both platforms. A room is confirmed after two matching reads within four seconds. A sign identifies a room and a floor; on its own it never sets a pose. A sign can pose the camera only through `SignFirst`, which needs a surveyed, device-validated calibration entry. The calibration ships empty, so sign poses are inert today. Without a pose after the MultiSet budget, the HUD keeps scanning and gives room-only directions. See [OCR and sign prerequisites](Assets/IndoorAr/OCR-POC-prerequisites.md).

### Pressure floor hints

The barometer is advisory evidence for which floor the user is on. It never produces a position.

- **Host evidence (schema 2).** After the user confirms their starting floor, the host may send `startFloor`, its age, and a pressure summary (`settled`, `moved`, optionally a signed relative change in hPa). Raw or absolute pressure never crosses the bridge.
- **Unity's own barometer.** Only for a request carrying such evidence, Unity reads the device barometer (Core Motion on iOS, the pressure sensor on Android). It uses only differences from a settled baseline. A change of at least 0.30 hPa releases a stale confirmed sign or the start-floor veto. Unity stops sampling and clears its window when the session ends.
- **Start-floor veto.** While the confirmed start floor is trusted and pressure is unchanged, a MultiSet pose on another supported floor is rejected and localization retries. The veto drops after a confirmed sign, tracked motion onto another floor, a settled pressure change, or two separate attempts that return a pose on another floor.
- **First-request height hint.** When the evidence is complete and fresh, Unity may narrow the first MultiSet request to a height band for floors 1 to 3 (the candidate floor's surface Y minus 0.3 m to plus 3.3 m). The band is advisory, and no horizontal position hint is sent. If an unhinted retry is not available the hint is not sent.

Without valid floor evidence none of this runs and localization behaves as before. Evidence ages require the device's boot clock, and a stale or malformed field leaves the request unhinted.

### Routing and AR display

- **Walk grid.** `Resources/IndoorWalkGrid.json` is generated in the editor from the scanned floor mesh: 0.25 m cells in the MapSet frame, walkable where a floor surface exists and nothing sits 0.4 to 1.4 m above it. Floors 1 to 3 are covered; the scan has hallways only, not room interiors.
- **Route.** A* runs over the grid from the localized camera to the door, with endpoint snapping within 1.6 m and smoothing that respects blocked cells. On another floor the route goes to the nearest stairs or elevator marker on the user's floor, and the HUD names the connector. The path through the connector is not verified.
- **Step-free.** A `stepFree` request returns `unavailable` with `STEP_FREE_UNVERIFIED`, because the grid has no verified accessibility data.
- **HUD.** `IndoorHud` and `IndoorRoutePath` draw a chevron ribbon on the floor to the first clear waypoint, falling back to a screen arrow when the stretch is too short or off screen, with **Look this way**, **Route unavailable**, wrong-floor, arrival and **Try again** states. The destination appears as a pin in the world, which docks or clips to the screen when out of view. See [HUD preview](Assets/IndoorAr/HUD-preview.md).
- **Arrival.** The user taps **I'm here**, or reads the destination sign, then **Done** returns `arrived`. Arrival is not detected by distance alone.

### Privacy

- Camera frames stay on the device. MultiSet VPS localization sends frames to MultiSet through its SDK; OCR does not upload anything.
- The MultiSet token is handled in memory in the request and stored by the SDK in `PlayerPrefs` on the device. It is not logged. Client credentials are never in the app or this repository.
- Logs (`[IndoorAr]`, `[IndoorArBaseline]`, `[ArDiag]`) carry no positions, raw map codes, images, recognized text or credentials. Map identity is reduced to a short hash.
- Pressure readings stay on the device, and only a relative summary reaches Unity. The iOS host must supply a Motion & Fitness usage description; proposed wording is in [the iOS export notes](docs/ios-export.md#floor-change-pressure).
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
