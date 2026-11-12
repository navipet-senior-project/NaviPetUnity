# Outdoor AR with ARCore Geospatial

`OutdoorAr/Scenes/OutdoorGeospatialDemo.unity` is the only scene with Geospatial mode on. `OutdoorGeospatialConfig.asset` enables it for that scene alone, and its AR session starts only after the user opts in. The indoor scene and its MultiSet session are unchanged. Do not reuse MultiSet poses outdoors or claim campus coverage.

Nothing here was run on a phone or against Google Cloud. EditMode tests cover the request parsing, the readiness gate, and the distance and turn math.

## Hosted session

The Flutter app starts outdoor AR through the same bridge as indoor AR, with this request:

```json
{"schemaVersion":2,"mode":"outdoor","destination":{"name":"VEC","latitude":33.7829,"longitude":-118.1108},"dataUseAccepted":true}
```

The indoor scene is the first scene of the export and reads every request. For `mode: "outdoor"` it loads the outdoor scene, which reads the same request. A request without a destination, with a destination of (0, 0), or with a latitude or longitude out of range gets `BAD_REQUEST`.

`dataUseAccepted: true` means the host already showed Google's ARCore notice, so Unity skips its own copy of it. Without the flag Unity shows the notice and waits for the user to agree. Unity then asks for camera and precise location on Android, starts the session, and checks VPS availability at the phone's position. Once `OutdoorGeospatialGate` reports ready, a green marker is anchored at the destination's latitude and longitude, at the phone's altitude, and the screen shows the distance and the direction to turn.

The result uses the indoor envelope, always with `localizationQueries: 0`:

| Result | When |
| --- | --- |
| `arrived` with `checkDistanceM` | The user taps Done within `ArrivalRadiusM` (10 m) of the destination. |
| `cancelled` | The user taps Exit or presses back. |
| `unavailable` | The device does not support Geospatial, camera or location was denied, the Earth service is not authorized or is out of resources, or the export has no outdoor scene. |
| `localizationFailed` | The session ran for `ReadyTimeoutS` (60 s) without reaching ready. This includes a spot with no VPS coverage. |

After the result the outdoor AR session is off. The next request reloads the indoor scene, which routes it.

The gate requires device support, the Earth service, active Earth tracking, horizontal accuracy of at most 5 m and heading accuracy of at most 10°. The VPS result expires when the phone moves more than 5 m from the queried point. Any other state removes the marker. These thresholds, the arrival radius and the timeout need real-device validation. The on-screen text is IMGUI and is a stand-in for a designed HUD.

## Authorization and export

`ProjectSettings/ARCoreExtensionsProjectSettings.json` turns Geospatial on and sets Android to Keyless authorization. It holds no key. Keyless works only when the Google Cloud project has the ARCore API enabled and an OAuth client for the host app's package name and signing SHA-1. That setup belongs to the Cloud project owner and was not changed or checked here.

The Android export (`Indoor AR → Export Android Library`) includes the outdoor scene. With Geospatial on, ARCore Extensions adds the fine and coarse location permissions to the library manifest and depends on `com.google.android.gms:play-services-location` and `com.google.android.gms:play-services-auth-base`. The export marks the location hardware features as not required, so the host app still installs on a phone without GPS.

The iOS export leaves the outdoor scene out, so an outdoor request on iOS returns `unavailable`. iOS has no ARCore authorization yet: iOS support in ARCore Extensions is off and its authentication strategy is unset. Once that is decided, add the scene to `IosScenes` in `IndoorArExport`.

Both exports replace the Build Settings scene list with their own for the length of the build, then restore it. ARCore Extensions reads its session configs from that list and fails an iOS build that lists a Geospatial scene.

## Standalone prototype

Without an outdoor request the scene behaves as before: it shows the notice, asks for permissions, and places a marker at the phone's own pose once the gate reports ready. To run it alone, make it the startup scene of a device build. No VPS request runs before consent, permissions and active Earth tracking.

The Unity project uses 6000.3.26f1, AR Foundation and the ARCore XR Plugin 6.3.5, and the ARCore Extensions 1.57.0 AR Foundation 6 tag.
