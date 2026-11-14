# OCR identity proof and device checks

`IndoorRoomIdentityMatcher` compares recognizer text with room IDs after checking the selected building and floor against the directory. A match identifies a directory entry; it does not prove that OCR was correct or that the phone is localized.

The Android adapter uses bundled ML Kit Latin text recognition (v2, `com.google.mlkit:text-recognition`) through `IndoorRoomOcrDemo`. It reads the AR Foundation camera's existing CPU image stream, downsizes frames to at most 640x480, submits at most two frames per second, and drops frames while conversion or recognition is in flight. No camera session restart or image upload occurs. Recognized text goes to `IndoorRoomIdentityMatcher.MatchSign`, which accepts a VEC door on either floor from the whole text or any single line; a frame naming two rooms is `Ambiguous`. Its `IndoorRoomMatch` result contains directory identity only.

Before accepting recognition as useful, validate on an Android device: build and install the exported Unity AAR in the host app; scan VEC room signs on `floor_1` and `floor_2`; record the returned string and match status; repeat in portrait and landscape, low light, and soft focus; check cold startup offline to confirm the bundled model; and verify repeated scans do not grow memory or block rendering. This repository has no device result yet. OCR identifies a room; it is not a pose.

The iOS adapter reads AR Foundation's existing CPU image stream, downsizes frames to at most 640x480, and submits at most two frames per second while idle. It passes the display orientation to Vision and runs recognition on a background queue. It does not restart the camera session, upload images, log OCR text, call MultiSet, or produce a pose. Device recognition has not been validated. Before accepting it as useful, scan VEC room signs on campus in portrait and landscape, low light, and soft focus; record returned text and match status; and check repeated scans for memory growth or rendering stalls.

## Sign-assisted localization

`IndoorArRunner` subscribes to both adapters when `signLocalization` is on (the default). OCR runs only while the session is starting or localizing and stops once MultiSet returns a pose, so it never competes with the pinned view. Turning the flag off disables both adapters and leaves the MultiSet-only flow unchanged.

`IndoorSignTracker` confirms a room after two reads within four seconds. A frame naming two rooms resets the count; text with no room number is ignored. A confirmed sign does three things:

- The HUD stays in the scanning state and shows approximate copy: "Near VEC 120 · Floor 1". If the sign is on a different floor from the destination, it says so before any VPS answer. If the sign is the destination, it says the sign reads that room and asks the student to keep scanning. Slow down still takes precedence.
- If MultiSet already answered the open attempt without a pose and the confirmed sign is in view, the one retry is spent at once instead of at the 30 s timeout. The attempt budget does not change.
- The result JSON gains an optional `signRoom` field with the last confirmed room ID. Hosts that do not know the field ignore it.

A sign never pins the door, shows the arrow, or sets `timeToFirstLocalizationMs`. The precise pose still comes only from MultiSet, which remains the fallback for every session.

The Flutter `google_mlkit_text_recognition` plugin was evaluated and not adopted. Flutter does not own the camera while the Unity AR activity runs, so the plugin would need a second camera session or a frame copy between the engines. The Android adapter already uses the same ML Kit v2 bundled Latin recognizer, and iOS uses Apple Vision, which needs no extra pods.

## Precise pose from a sign (not implemented)

Before any geometry or pose work, measure the sign width and height in metres and one sign corner's offset from a door represented in the backend's MapSet frame. Measure camera intrinsics (`fx`, `fy`, `cx`, `cy`) and distortion at the same device camera mode and resolution. A future PnP experiment must report both planar IPPE solutions and reprojection error in the OpenCV camera frame. Compare against `CameraInMap` captured at the same moment through a written, measured frame conversion. Until that paired comparison exists, OCR text and image corners cannot be treated as localization. No calibration file is shipped because nothing could consume it yet; when the measurements above exist, add one entry per sign (door key, sign size, corner offset in the MapSet frame) and keep any pose behind the paired validation.

The VEC floor-plan JSON supplies the room IDs, building ID, and floor IDs for identity matching. Its positions and rotations are not interchangeable with the MapSet door frame; do not use them to create sign geometry, poses, or transforms.
