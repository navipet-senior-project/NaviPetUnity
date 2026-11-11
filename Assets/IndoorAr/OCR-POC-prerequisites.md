# OCR identity proof and device checks

`IndoorRoomIdentityMatcher` compares recognizer text with room IDs after checking the selected building and floor against the directory. A match identifies a directory entry; it does not prove that OCR was correct or that the phone is localized.

The Android POC uses bundled ML Kit Latin text recognition through `IndoorRoomOcrDemo`. It reads the AR Foundation camera's existing CPU image stream, downsizes frames to at most 640x480, submits at most one frame per second, and drops frames while conversion or recognition is in flight. No camera session restart or image upload occurs. Recognized text is passed to the existing VEC room matcher for `floor_1`; its `IndoorRoomMatch` result contains directory identity only.

Before accepting recognition as useful, validate on an Android device: build and install the exported Unity AAR in the host app; scan VEC room signs on `floor_1`; record the returned string and match status; repeat in portrait and landscape, low light, and soft focus; check cold startup offline to confirm the bundled model; and verify repeated scans do not grow memory or block rendering. This repository has no device result yet. The OCR adapter is a demo, not production localization.

Before any geometry or pose work, measure the sign width and height in metres and one sign corner's offset from a door represented in the backend's MapSet frame. Measure camera intrinsics (`fx`, `fy`, `cx`, `cy`) and distortion at the same device camera mode and resolution. A future PnP experiment must report both planar IPPE solutions and reprojection error in the OpenCV camera frame. Compare against `CameraInMap` captured at the same moment through a written, measured frame conversion. Until that paired comparison exists, OCR text and image corners cannot be treated as localization.

The VEC floor-plan JSON supplies the room IDs, building ID, and floor IDs for identity matching. Its positions and rotations are not interchangeable with the MapSet door frame; do not use them to create sign geometry, poses, or transforms.
