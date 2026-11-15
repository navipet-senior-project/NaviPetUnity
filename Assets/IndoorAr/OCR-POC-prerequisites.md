# Indoor AR OCR and device checks

`IndoorRoomIdentityMatcher` compares recognizer text with room IDs after checking the selected building and floor against the directory. A match identifies a directory entry; it does not prove that OCR was correct or that the phone is localized.

The Android adapter uses bundled ML Kit Latin text recognition (v2, `com.google.mlkit:text-recognition`) through `IndoorRoomOcrDemo`. It reads the AR Foundation camera's existing CPU image stream, downsizes frames to at most 1280x720, submits at most two frames per second, and drops frames while conversion or recognition is in flight. No camera session restart or image upload occurs. Recognized text goes to `IndoorRoomIdentityMatcher.MatchSign`, which accepts a VEC door on either floor from the whole text or any single line; a frame naming two rooms is `Ambiguous`. Its `IndoorRoomMatch` result contains directory identity only. The plugin also returns each word's four corner points, which feed the sign fix described below. The 1280x720 size and the corner coordinates have not run on an Android device.

Before accepting recognition as useful, validate on an Android device: build and install the exported Unity AAR in the host app; scan VEC room signs on `floor_1` and `floor_2`; record the returned string and match status; repeat in portrait and landscape, low light, and soft focus; check cold startup offline to confirm the bundled model; and verify repeated scans do not grow memory or block rendering. This repository has no device result yet. OCR identifies a room; it is not a pose.

The iOS adapter reads AR Foundation's existing CPU image stream, downsizes frames to at most 1920x1440, and submits a frame at most every 0.3 s while idle. Recognition runs on a background queue with Google ML Kit's bundled Latin text recognizer (`GoogleMLKit/TextRecognition` 6.0.0, linked into `UnityFramework` by the export; see `docs/ios-export.md`). The plugin turns the frame upright itself and returns each word's four corner points in that upright image. If ML Kit is not in the build, its model bundle is missing, or it returns no result, the same frame goes to Apple Vision instead, with the minimum text height lowered from the default 1/32 of the image to 1/100 and the four corners of each run of digits. Every result names its recognizer, and the adapter logs `[IndoorAr] ocr engine=mlkit` or `engine=vision` when that changes, so a device log shows which one read the signs. ML Kit has linked but has not run on a phone. It does not restart the camera session, upload images, log OCR text, call MultiSet, or produce a pose by itself. Device recognition has not been validated, and neither has the time or heat cost of the larger frame; the `ocrMs=` value in each `[IndoorAr] sign read` log line is the capture-to-result time to tune it from. Before accepting it as useful, scan VEC room signs on campus in portrait and landscape, low light, and soft focus; record returned text and match status; and check repeated scans for memory growth or rendering stalls.

## Sign-assisted localization

`IndoorArRunner` subscribes to both adapters when `signLocalization` is on (the default). In `MultiSet` and `SignPose` modes OCR runs only while the session is starting or localizing and stops at the first pose; in `SignFirst` it keeps running while pinned. Turning the flag off disables both adapters. The runner's `localizationMode` (default `SignFirst`) decides who answers the pose: `SignFirst` aligns the map coarsely from a trail of room signs and asks MultiSet only as a fallback (next section); `MultiSet` is the original VPS flow; `SignPose` never authenticates or calls MultiSet (see below) and cannot localize until the measurements there exist. Switch it on the `IndoorArRunner` component.

`IndoorSignTracker` confirms a room after two reads within four seconds. A frame naming two rooms resets the count; text with no room number is ignored. A confirmed sign does three things:

- The HUD stays in the scanning state and shows approximate copy: "Near VEC 120 · Floor 1". If the sign is on a different floor from the destination, it says so before any VPS answer. If the sign is the destination, it says the sign reads that room and asks the student to keep scanning. Slow down still takes precedence.
- If MultiSet already answered the open attempt without a pose and the confirmed sign is in view, the one retry is spent at once instead of at the 30 s timeout. The attempt budget does not change.
- The result JSON gains an optional `signRoom` field with the last confirmed room ID. Hosts that do not know the field ignore it.

One sign read alone never pins the door, shows the arrow, or sets `timeToFirstLocalizationMs`; only a solved pose (`SignPose`), a MultiSet answer, or a usable sign fix (`SignFirst`) does. `SignPose` never falls back to MultiSet.

The Flutter `google_mlkit_text_recognition` plugin was evaluated and not adopted. Flutter does not own the camera while the Unity AR activity runs, so the plugin would need a second camera session or a frame copy between the engines. Both native adapters use ML Kit's bundled Latin recognizer on the AR camera's own frames instead; on iOS Apple Vision remains as the fallback.

## Signs first, MultiSet last (`SignFirst`, coarse, not device-verified)

`SignFirstLocalizer` wraps the MultiSet localizer. A localization request starts a 15 s hold instead of a MultiSet query. Each confirmed sign is stored in `IndoorSignTrail` with the AR camera position at that moment. One sign names a room, which is an area, so it never produces a fix. Two or more signs give a 2D least-squares yaw and offset for the MapSet origin from the path AR tracked between them. The fix is used only when the doors are at least 8 m apart and the fit residual is at most 1.5 m. If the hold ends without such a fix, MultiSet is asked, and its pose then wins: later sign fixes are logged against it but do not move the map.

What the fix is not: an exact pose. It assumes the reader stands 1.5 m out from the door marker, within 1.5 m of that point, holding the phone 1.4 m above the floor. None of the three was measured. The heading is never claimed tighter than `atan(2 * 1.5 m / baseline)` (21 degrees at 8 m, 17 at 10 m) and the position is no better than about 1.5 m. While the map is posed from signs the HUD says "Approximate, from room signs", the door pin is hidden because it could land on the wrong door, and the `checkDistanceM` in an arrival result is equally coarse. The device log prints `[IndoorAr] sign trail signs= baselineM= residualM= yawBoundDeg= usable= applied=`, plus `vsPoseM=` and `vsPoseDeg=` when a MultiSet pose is in use, which is the paired comparison needed to tune or reject these assumptions. Covered by synthetic tests only.

### One sign, from its corners (not device-verified)

Reading a room number says which door is near, not where the camera is. The camera pose comes from the corners of the number's text box, seen in several frames while AR tracks the phone between them. `IndoorOcrFrame` pairs each frame's corners with the camera intrinsics (`ARCameraManager.TryGetIntrinsics`, scaled to the OCR image and turned with it) and the AR camera pose taken when the image was acquired. `IndoorSignTriangulator` intersects the rays to each corner across up to 12 frames. That gives the text box's position, size and wall direction in the AR world without a measured sign size. It needs sideways motion: at least 3 frames, 6 degrees of parallax, and a ray residual of at most 3 cm. Standing still gives no fix, and the HUD then says "Keep the sign in view and walk past it".

A located sign enters the trail as a point instead of a 1.5 m guess about the reader. With one located sign, `SignFirstLocalizer` poses the map by turning the measured wall direction onto the door's heading from the directory and placing the sign 1.5 m above the door marker, if the estimated heading error is at most 20 degrees. Two located signs replace that with the trail fit. MultiSet is still asked after the 15 s hold when no sign fix arrived, and its pose still wins.

What is assumed, not measured:

- The sign is on the same wall as its door, so the wall direction equals the door heading. A sign mounted on a flag bracket or a side wall breaks this by up to 90 degrees.
- The text is within 1 m of the door marker along the wall (`SignOffsetM`) and 1.5 m above it (`SignHeightM`). The one-sign position is therefore off by the sign's real offset from its door, about a metre sideways.
- The heading error estimate assumes at least one pixel of corner noise. It is a formula, not a measured spread.
- The camera pose is read when the image is acquired; the two clocks are not synchronised beyond that.
- None of the thresholds were tuned on a device.

The device log prints `[IndoorAr] sign fix room= status= frames= parallaxDeg= residualMm=` when the status changes, and on each solved frame adds `widthM= heightM= yawSigmaDeg= oneSign= usable= applied=`. When a MultiSet pose is in use it also adds `alongM= outM= upM= wallVsDoorDeg=`: where the text sits from its door marker and how the wall faces against the door heading. Those four numbers, collected per sign, are the calibration the precise solver below is missing. No line contains the camera position or the recognized text.

## Hallway route

`IndoorWalkGridExport` (menu Indoor AR > Rebuild Walk Grid From Scan) rasterizes `Assets/building scans/floor 1 + 2.glb` into `Resources/IndoorWalkGrid.json`: 0.25 m cells in the MapSet frame, walkable where the scan has a floor surface and nothing between 0.4 m and 1.4 m above it. `IndoorWalkGrid` runs A* over it from the camera to the door, and the arrow aims at the next route point instead of straight at the door. The HUD shows "Hallway route to" with the route length. When the camera or door is more than 1.6 m from a walkable cell, or the camera is on another floor, there is no route and the arrow falls back to the straight line ("Arrow points to the room").

Coverage today: all 9 mapped floor-1 doors and all 8 floor-2 doors snap onto the grid and connect to each other (`IndoorWalkGridTests`). The scan has hallways only, not room interiors, and no floor surface under the VEC119, 120, 121 and 124 markers, so their routes end at the nearest hallway cell, up to 1.6 m from the marker. There is no route between floors (stairs and elevators are point markers only), no drawn path line, and the route has not been walked on a device. Re-run the export when the scan or the MapSet alignment changes.

## Precise pose from a sign (prototype, inert until measured)

`IndoorSignPose` solves the camera pose in the MapSet frame from the four image corners of one measured sign (planar homography decomposition, reprojection error gate of 3 px). `SignPoseLocalizer` feeds it and, on success, moves the MapSet origin (yaw only) so the AR camera sits at the solved pose; it raises no MultiSet call and no attempt, so `localizationQueries` stays 0. The math is covered by synthetic round-trip tests only. Nothing here has run on a device.

It produces no pose today because these inputs do not exist:

1. Per sign, in `Assets/IndoorAr/Resources/IndoorSignCalibration.json` (shipped empty): `widthM` and `heightM` of the rectangle the OCR corners outline, `centerInMap` of that rectangle in the MapSet frame, and `intoWallHeadingDeg`. An entry only counts once `validated` is true.
2. Sign image corners from the OCR adapters. Both adapters now return them, but they outline the digits, not the sign plate, so the calibrated rectangle has to be the digits' box.
3. Lens distortion at the device camera mode of the OCR frame. Intrinsics and the turn to the upright image now come with each observation (`IndoorOcrFrame.Sighting`); distortion is ignored.
4. A paired validation of the solved pose against `CameraInMap` captured at the same moment, through a written, measured frame conversion. The solver uses one homography decomposition; check both planar IPPE solutions before trusting oblique views.

Until all four exist, the device log prints `[IndoorAr] sign pose room=... status=NoCalibration` (or the next missing input), the HUD keeps the approximate "Near VEC 120" copy, and the session ends with `localizationFailed` at its normal timeout. That is the intended honest failure in `SignPose` mode; `SignFirst` is the default.

## Room identity and door poses (reuse, no second database)

`Assets/IndoorNavigation/Data/vec_floors1and2.json` is the single directory. It holds 32 hand-placed markers (17 doors, 3 entrances, 4 bathrooms, 4 stairs, 4 elevators) exported by `IndoorMapExporter` relative to `IndoorMapOrigin`. `IndoorRoomIdentityMatcher` already turns sign text into a room ID from it; each `IndoorRoomLandmark` now also carries `DoorPositionInMap` and `DoorHeadingInMapDeg`.

The JSON frame is not the MapSet frame, but it differs by one rigid transform: yaw 5.356 degrees, offset (0.279, -2.212, 0.838) m (`IndoorMapFrame`). That was fitted on the 17 door rows copied from `vec_destination_points` (`IndoorArDoorMeshTests`) and holds to 0.0001 m, with the heading in the real VEC119 request (185.36) matching. `IndoorMapFrameTests` fails if the JSON or the transform drifts; re-derive the transform when the MapSet is re-aligned. The backend table stays authoritative for doors; the transform only reaches rooms or markers the request does not carry.

What a point is: a door marker, a 0.3 m cube placed by hand on the floor at the door threshold (y is within 0.5 m of the scanned floor), not a room centre, a sign, or a navigation waypoint. Its heading points from the hallway into the room (doors on opposite sides of a hallway differ by 180 degrees), which is the `intoWallHeadingDeg` convention `IndoorSignPose` uses. These two facts are read from the data, not measured.

Still unmeasured for a pose, so `IndoorSignCalibration.json` stays empty: sign width and height, the sign centre's height above the floor and its sideways offset from the door marker (signs often sit beside the door), the OCR corners, the camera intrinsics and screen-to-camera transform, and a paired comparison against `CameraInMap`. Nothing named "BlueTight" exists in this repo, the backend, or the Flutter app.
