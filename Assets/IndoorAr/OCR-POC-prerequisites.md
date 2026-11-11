# OCR identity proof prerequisites

`IndoorRoomIdentityMatcher` only compares recognizer text with room IDs and explicit building/floor context. A match identifies a directory entry; it does not prove that OCR was correct or that the phone is localized.

Before adding on-device OCR, collect one VEC sign image and record the recognizer, capture resolution, returned room string, and whether the string matches. Keep the image and result as a text-recognition observation only. No camera integration, MultiSet call, or pose output is part of this proof.

Before any geometry or pose work, measure the sign width and height in metres and one sign corner's offset from a door represented in the backend's MapSet frame. Measure camera intrinsics (`fx`, `fy`, `cx`, `cy`) and distortion at the same device camera mode and resolution. A future PnP experiment must report both planar IPPE solutions and reprojection error in the OpenCV camera frame. Compare against `CameraInMap` captured at the same moment through a written, measured frame conversion. Until that paired comparison exists, OCR text and image corners cannot be treated as localization.

The VEC floor-plan JSON is not interchangeable with the MapSet door frame. Use its room identifiers for identity fixtures only; do not use its positions or rotations to create sign geometry, poses, or transforms.
