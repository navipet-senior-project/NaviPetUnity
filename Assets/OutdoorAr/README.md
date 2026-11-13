# Outdoor AR with ARCore Geospatial

`OutdoorAr/Scenes/OutdoorGeospatialDemo.unity` is the only scene with Geospatial mode on. `OutdoorGeospatialConfig.asset` enables it for that scene alone, and its AR session starts only after the user opts in. The indoor scene and its MultiSet session are unchanged. Do not reuse MultiSet poses outdoors or claim campus coverage.

Nothing here was run on a phone or against Google Cloud. EditMode tests cover the request parsing, the readiness gate, and the distance and turn math.

## Hosted session

The Flutter app starts outdoor AR through the same bridge as indoor AR, with this request:

```json
{"schemaVersion":2,"mode":"outdoor","destination":{"name":"VEC","latitude":33.7829,"longitude":-118.1108},"dataUseAccepted":true}
```

The request may also carry the walking route the host already shows, as ordered waypoints from the start to the destination:

```json
"route":{"waypoints":[{"latitude":33.7801,"longitude":-118.1120},{"latitude":33.7829,"longitude":-118.1108}]}
```

`route` is optional and additive (the schema stays 2). With 2 to `MaxRouteWaypoints` (1000) valid points the arrow follows the route, 12 m ahead of the nearest point. Beyond 30 m off the route the HUD says "You're off the route" and points back to the nearest point. Unity does not reroute and detects no obstacles: avoiding obstacles is whatever the host's route avoids. Without a route, or with an empty `waypoints`, the arrow points at the destination and the HUD says "Direction only, not a route". A route with one point, a missing or out-of-range coordinate, or more than 1000 points gets `BAD_REQUEST`.

The indoor scene is the first scene of the export and reads every request. For `mode: "outdoor"` it loads the outdoor scene, which reads the same request. A request without a destination, without either coordinate, or with a latitude or longitude out of range gets `BAD_REQUEST`; explicitly supplied `(0, 0)` is valid.

Unity always shows Google's ARCore notice and waits for the user to agree; `dataUseAccepted` from the host does not replace Unity's confirmation. Unity then asks for camera and precise location on Android, and for location on iOS (the host asks for the camera there), starts the session, and checks VPS availability at the phone's position. Once `OutdoorGeospatialGate` reports ready, the screen shows the indoor HUD (`IndoorHud`) in outdoor wording: "Finding your position", then "Location found", then the arrow with the turn, the distance (ft, then mi) and the destination, and "I'm here" to finish. The arrow comes from the heading and position, so it needs no anchor; a green terrain-resolved marker with the HUD pin label is added at the destination when the terrain anchor resolves, and its failure no longer ends the session.

The result uses the indoor envelope, always with `localizationQueries: 0`:

| Result | When |
| --- | --- |
| `arrived` with `checkDistanceM` | The user taps Done within `ArrivalRadiusM` (10 m) of the destination. |
| `cancelled` | The user taps Exit or presses back. |
| `unavailable` | The device does not support Geospatial, camera or location was denied, the Earth service is not authorized or is out of resources, or the export has no outdoor scene. |
| `localizationFailed` | The session ran for `ReadyTimeoutS` (60 s) without reaching ready. This includes a spot with no VPS coverage. |

After the result the outdoor AR session is off. The next request reloads the indoor scene, which routes it.

The gate requires device support, the Earth service, active Earth tracking, horizontal accuracy of at most 5 m and heading accuracy of at most 10°. The VPS result expires when the phone moves more than 5 m from the queried point. Any other state removes the marker. These thresholds, the arrival radius and the timeout need real-device validation. Before the user agrees only the ARCore notice (IMGUI) is shown; the standalone prototype keeps its IMGUI status box.

On a device the scene logs `[ArDiag]` (camera textures, session, entry count across scene reloads) and `[OutdoorAr]` (state, session, route flags) lines on change, with no location, image or key, to help diagnose AR re-entry.

## Authorization and export

`ProjectSettings/ARCoreExtensionsProjectSettings.json` turns Geospatial on and sets Android to Keyless authorization. It holds no key. Keyless works only when the Google Cloud project has the ARCore API enabled and an OAuth client for the host app's package name and signing SHA-1. That setup belongs to the Cloud project owner and was not changed or checked here.

The Android export (`Indoor AR → Export Android Library`) includes the outdoor scene. With Geospatial on, ARCore Extensions adds the fine and coarse location permissions to the library manifest and depends on `com.google.android.gms:play-services-location` and `com.google.android.gms:play-services-auth-base`. The export marks the location hardware features as not required, so the host app still installs on a phone without GPS.

iOS uses an API key. The committed settings leave ARCore Extensions iOS support off and hold no key. The iOS export (`Indoor AR → Export iOS Xcode Project`) reads the key from `~/.config/navipet/arcore-ios-key`, turns iOS support and API key authorization on for that build only, and includes the outdoor scene. Without that file it builds the indoor scene alone and an outdoor request on iOS returns `unavailable`. `docs/ios-export.md` has the details and the limits of a key shipped inside an app.

Both exports replace the Build Settings scene list with their own for the length of the build, then restore it. ARCore Extensions reads its session configs from that list and fails an iOS build that lists a Geospatial scene while iOS authorization is off.

## Standalone prototype

Without an outdoor request the scene behaves as before: it shows the notice, asks for permissions, and places a marker at the phone's own pose once the gate reports ready. To run it alone, make it the startup scene of a device build. No VPS request runs before consent, permissions and active Earth tracking.

The Unity project uses 6000.3.26f1, AR Foundation and the ARCore XR Plugin 6.3.5, and the ARCore Extensions 1.57.0 AR Foundation 6 tag.
