# Indoor AR OCR and device checks

`IndoorRoomIdentityMatcher` compares recognizer text with room IDs after checking the selected building and floor against the directory. A match identifies a directory entry; it does not prove that OCR was correct or that the phone is localized.

The Android adapter uses bundled ML Kit Latin text recognition (v2, `com.google.mlkit:text-recognition`) through `IndoorRoomOcrDemo`. It reads the AR Foundation camera's existing CPU image stream, downsizes frames to at most 1280x720, submits at most two frames per second, and drops frames while conversion or recognition is in flight. No camera session restart or image upload occurs. Recognized text goes to `IndoorRoomIdentityMatcher.MatchSign`, which accepts a VEC door on either floor from the whole text or any single line; a frame naming two rooms is `Ambiguous`. Its `IndoorRoomMatch` result contains directory identity only. The plugin also returns each word's four corner points, which feed the calibrated pose solver described below. The 1280x720 size and the corner coordinates have not run on an Android device.

Before accepting recognition as useful, validate on an Android device: build and install the exported Unity AAR in the host app; scan VEC room signs on `floor_1` and `floor_2`; record the returned string and match status; repeat in portrait and landscape, low light, and soft focus; check cold startup offline to confirm the bundled model; and verify repeated scans do not grow memory or block rendering. This repository has no device result yet. OCR identifies a room; it is not a pose.

The iOS adapter reads AR Foundation's existing CPU image stream, downsizes frames to at most 1920x1440, and submits a frame at most every 0.3 s while idle. Recognition runs on a background queue with Google ML Kit's bundled Latin text recognizer (`GoogleMLKit/TextRecognition` 6.0.0, linked into `UnityFramework` by the export; see `docs/ios-export.md`). The plugin turns the frame upright itself and returns each word's four corner points in that upright image. If ML Kit or its model bundle is unavailable, or the frame cannot be read, the adapter reports `RecognizerUnavailable`; a valid frame with no text reports `NoText`. It does not switch OCR engines. ML Kit has linked but has not run on a phone. It does not restart the camera session, upload images, log OCR text, call MultiSet, or produce a pose by itself. Device recognition has not been validated, and neither has the time or heat cost of the larger frame; the `ocrMs=` value in each `[IndoorAr] sign read` log line is the capture-to-result time to tune it from. Before accepting it as useful, scan VEC room signs on campus in portrait and landscape, low light, and soft focus; record returned text and match status; and check repeated scans for memory growth or rendering stalls.

## Sign-assisted localization

`IndoorArRunner` subscribes to both adapters when `signLocalization` is on (the default), including while pinned. Turning the flag off disables both adapters. The runner’s `localizationMode` defaults to `SignFirst`, which poses the camera from a sign validated on this device model and asks MultiSet as a fallback. `MultiSet` keeps the original VPS flow. Switch it on the `IndoorArRunner` component.

`IndoorSignTracker` confirms a room after two reads within four seconds. A frame naming two rooms resets the count; text with no room number is ignored. A confirmed sign does three things:

- The HUD stays in the scanning state and shows approximate copy: "Near VEC 120 · Floor 1". If the sign is on a different floor from the destination, it says so before any VPS answer. If the sign is the destination, it says the sign reads that room and asks the student to keep scanning. Slow down still takes precedence.
- If MultiSet already answered the open attempt without a pose and the confirmed sign is in view, the one retry is spent at once instead of at the 30 s timeout. The attempt budget does not change.
- The result JSON gains an optional `signRoom` field with the last confirmed room ID. Hosts that do not know the field ignore it.

One sign read alone never pins the door, shows the arrow, or sets `timeToFirstLocalizationMs`; only a solved pose from a validated sign (`SignFirst`) or a MultiSet answer does.

The Flutter `google_mlkit_text_recognition` plugin was evaluated and not adopted. Flutter does not own the camera while the Unity AR activity runs, so the plugin would need a second camera session or a frame copy between the engines. Both native adapters use ML Kit's bundled Latin recognizer on the AR camera's own frames instead.

## Signs first, MultiSet last (`SignFirst`, not device-verified)

OCR evidence has two levels, and only the second one moves the map.

- **Room recognized.** A confirmed sign names a room. The HUD shows it ("Room VEC 120 detected · Floor 1"), the result carries `signRoom`, and the status message carries `roomRecognized`. It is an identity, not a position: no XYZ is claimed.
- **Camera pose localized.** Only `IndoorSignPose` on a sign whose calibration entry is `validated` for this phone's `SystemInfo.deviceModel` (`validatedDevice`) can pose the camera, and the runner accepts the pose only while the session is localizing or pinned and the camera lands on the confirmed sign's floor.

`SignFirstLocalizer` wraps the MultiSet localizer. When a sign is validated on this device, a localization request starts a 15 s hold instead of a MultiSet query, authentication does not wait for MultiSet's (MultiSet keeps authenticating behind it, and its failure only removes the fallback), and a validated camera pose from a sign during the hold pins the session. If the hold ends without one, MultiSet is asked, and its pose then wins: later sign poses do not move the map. With no sign validated on this device (today: the calibration ships empty), there is no hold and MultiSet is asked at once, exactly as in `MultiSet` mode.

### One sign, from its corners (not device-verified)

Reading a room number says which door is near, not where the camera is. The calibrated solver uses the observed text-box corners, camera intrinsics and the sign geometry recorded in `IndoorSignCalibration.json` to estimate camera pose. The calibration ships empty, and a sign is trusted only on the device model on which it was validated. Room identity alone never poses the map.

The solver uses the captured image corners as the text-box geometry. Those corners are approximate and have not been checked against physical sign measurements.

Remaining limits:

- The camera pose is read when the image is acquired; the two clocks are not synchronised beyond that.
- None of the thresholds were tuned on a device.

The device logs sign-pose status and solve time without logging the camera pose or recognized text. Physical sign calibration and device validation are still required before claiming aligned AR.

## Hallway route

`IndoorWalkGridExport` (menu Indoor AR > Rebuild Walk Grid From Scan) rasterizes `Assets/building scans/floor 1 + 2.glb` into `Resources/IndoorWalkGrid.json`: 0.25 m cells in the MapSet frame, walkable where the scan has a floor surface and nothing between 0.4 m and 1.4 m above it. `IndoorWalkGrid` runs A* over it from the camera to the door, and the arrow aims at the next route point instead of straight at the door. The HUD shows "Hallway route to" with the route length. When the camera or door is more than 1.6 m from a walkable cell, or the camera is on another floor, there is no route and the arrow falls back to the straight line ("Arrow points to the room").

Coverage today: all 9 mapped floor-1 doors and all 8 floor-2 doors snap onto the grid and connect to each other (`IndoorWalkGridTests`). The scan has hallways only, not room interiors, and no floor surface under the VEC119, 120, 121 and 124 markers, so their routes end at the nearest hallway cell, up to 1.6 m from the marker. There is no route between floors (stairs and elevators are point markers only), no drawn path line, and the route has not been walked on a device. Re-run the export when the scan or the MapSet alignment changes.

## Precise pose from a sign (inert until surveyed and validated)

`IndoorSignPose` solves the camera pose in the MapSet frame from the four image corners of one calibrated sign, with a 3 px reprojection gate and two checks. The camera must be in front of the sign and 0.3 to 5 m from it (`Implausible` otherwise), and no other candidate pose may also reproject within 3 px while moving the camera more than 5 degrees in yaw or 0.25 m in plan (`Ambiguous` otherwise). `SignFirst` uses it; on success the MapSet origin is moved (yaw only) so the AR camera sits at the solved pose, with no MultiSet call.

On iOS and Android the solve is OpenCV 4.14.0 `solvePnPGeneric` with `SOLVEPNP_IPPE`, each of its two solutions refined by `solvePnPRefineLM` (`tools/sign-pnp/IndoorSignPnp.cpp`). The pose is the better-fitting refinement; the two raw solutions and the other refinement are the candidates for the ambiguity check. Both are needed: near a frontal view raw IPPE can return a pose centimetres off that still passes the gate, which refinement corrects, and far away refinement can pull both flips into one pose, which only the raw pair shows. Raw IPPE also fails outright on some exactly symmetric views; those are rejected by the gate, not solved. The Editor and EditMode tests have no OpenCV build and use a managed planar homography plus its mirrored candidate instead, so survey validation (`IndoorSignPose.Validate`, run by the importer in the Editor) checks the managed solve, not the phone's. shortcut: build OpenCV for the Editor too if real captures show the two disagree. There is no lens distortion model, the thresholds are untuned, and OCR gives text-box rectangles rather than perspective corners, which limits accuracy at oblique views.

The native solver is not in the repository. Before an iOS or Android build, run `tools/build-sign-pnp.sh ios` (needs Xcode) or `tools/build-sign-pnp.sh android` (needs the editor's Android Build Support NDK); `tools/build-indoor-ar-aar.sh` runs the Android one itself. The first run downloads the official OpenCV release zips from GitHub and checks them against the sha256 values pinned in the script, then caches them in `.cache/opencv` (`OPENCV_CACHE` overrides), so later runs work offline. Offline with no cached zip, or with a zip that fails the check, it stops with an error and uses nothing. Outputs (`Assets/Plugins/iOS/libindoorsignpnp.a`, `Assets/Plugins/Android/libs/arm64-v8a/libindoorsignpnp.so`), staged notice resources, and the cache are gitignored. Both exports require `Assets/StreamingAssets/ThirdPartyNotices/OpenCV/`, containing the OpenCV license, project notice, and bundled SDK third-party license files; Unity packages this folder into iOS and Android runtime assets. The Android export path is `unityLibrary/src/main/assets/ThirdPartyNotices/OpenCV/`.

The math is covered by synthetic round-trip tests, and the phone path by fixtures of the native solver's output (`tools/sign-pnp/ippe-fixtures.py` prints them, with `opencv-python-headless==4.14.0.94`). The native library was linked and run on a Mac only, not on a phone. Nothing here has run on a device, and no pose accuracy is claimed.

A sign poses the camera only when its entry in `Assets/IndoorAr/Resources/IndoorSignCalibration.json` (shipped as `{"signs": []}`) is `validated` and its `validatedDevice` equals the phone's `SystemInfo.deviceModel`; otherwise the status is `NoCalibration`, `Unvalidated`, or `OtherDevice`. Entries are produced only by the survey importer, never by hand or from a MultiSet estimate:

1. On site, per sign: measure `widthM` and `heightM` of the digits' box the OCR corners outline (not the sign plate), `centerInMap` of that box in the MapSet frame, and `intoWallHeadingDeg` (the door-marker convention below).
2. From a development build on the phone to validate, stand at surveyed camera positions in front of the sign. Each frame whose status is `NoCalibration`, `Unvalidated`, or `OtherDevice` logs `[IndoorAr] sign capture room=... fx= fy= cx= cy= corners=... device=...`: image intrinsics and corner pixels only, no position and no text. Release builds never print it.
3. Write the survey JSON: `signs` holds the measured entries (`doorKey`, `widthM`, `heightM`, `centerInMap`, `intoWallHeadingDeg`), and `samples` holds one object per capture with the surveyed `cameraInMap`, the surveyed `headingDeg`, and the `capture` log line verbatim.
4. Run **Indoor AR > Import Sign Survey** in the editor. It writes the calibration and logs `<doorKey>: samples=N validated=True|False` per sign. A sign is validated, for the captures' device model, only if every capture in the survey parses, the sign has at least 3 captures, all from one device model, and every one of them solves to within 0.3 m and 5 degrees of its surveyed pose. Anything the survey file claims about validation is ignored.

The device log prints `[IndoorAr] sign pose room= status= solveUs=` when the status changes, never the pose. `poseEstimateUs` and `ocrLatencyMs` in the baseline line are the first solve time and the first OCR capture-to-result time of the session.

## Room identity and door poses (reuse, no second database)

`Assets/IndoorNavigation/Data/vec_floors1and2.json` is the single directory. It holds 32 hand-placed markers (17 doors, 3 entrances, 4 bathrooms, 4 stairs, 4 elevators) exported by `IndoorMapExporter` relative to `IndoorMapOrigin`. `IndoorRoomIdentityMatcher` already turns sign text into a room ID from it; each `IndoorRoomLandmark` now also carries `DoorPositionInMap` and `DoorHeadingInMapDeg`.

The JSON frame is not the MapSet frame, but it differs by one rigid transform: yaw 5.356 degrees, offset (0.279, -2.212, 0.838) m (`IndoorMapFrame`). That was fitted on the 17 door rows copied from `vec_destination_points` (`IndoorArDoorMeshTests`) and holds to 0.0001 m, with the heading in the real VEC119 request (185.36) matching. `IndoorMapFrameTests` fails if the JSON or the transform drifts; re-derive the transform when the MapSet is re-aligned. The backend table stays authoritative for doors; the transform only reaches rooms or markers the request does not carry.

What a point is: a door marker, a 0.3 m cube placed by hand on the floor at the door threshold (y is within 0.5 m of the scanned floor), not a room centre, a sign, or a navigation waypoint. Its heading points from the hallway into the room (doors on opposite sides of a hallway differ by 180 degrees), which is the `intoWallHeadingDeg` convention `IndoorSignPose` uses. These two facts are read from the data, not measured.

Still unmeasured for a pose, so `IndoorSignCalibration.json` stays empty: sign width and height, the sign centre's height above the floor and its sideways offset from the door marker (signs often sit beside the door), the OCR corners, the camera intrinsics and screen-to-camera transform, and a paired comparison against `CameraInMap`. Nothing named "BlueTight" exists in this repo, the backend, or the Flutter app.
