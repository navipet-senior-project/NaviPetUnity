# iOS export

Produces the Xcode project and `UnityFramework` the Flutter app embeds. The request and result JSON match Android: the request is the `navipet.indoorAr.request` extra (schemaVersion 1 or 2, optional `arToken`, and optional `mode: "localize"`), and the result is the string `UnityHostActivity.deliverResult` returns, including `errorCode`. A localize-only request omits destination pose fields and scans without showing a pin or arrival controls. An outdoor request (`mode: "outdoor"`) runs the ARCore Geospatial scene when the export had the ARCore API key, and returns `unavailable` when it did not (see [ARCore API key](#arcore-api-key-for-outdoor-ar)). When `arToken` carries a `token` and `expiresOn`, Unity authenticates with it and needs no client id or secret; otherwise it falls back to the project's MultiSet configuration.

Unity **6000.3.26f1**. AR Foundation and the ARKit XR plugin are **6.3.5**. The iOS player settings committed in this project are minimum iOS **15.0** (ARKit's own floor is 11), **ARM64**, **IL2CPP**, **Metal** only, a camera usage description, a location usage description, the `UNITY_XR_ARKIT_LOADER_ENABLED` define, and a disabled Unity splash screen and logo. The iOS export reapplies and checks the splash settings immediately before building; if the license forces the splash back on, export stops with an error. XR Plug-in Management enables the ARKit loader for iOS and leaves the ARCore loader on Android. ARKit's requirement is optional, same as ARCore, so a host app can still install on a phone without AR.

The export requires the iOS Build Support module for this editor version. Install it before the export below if it is missing. Do not switch the editor version.

## Export the Xcode project

1. In Unity Hub, add **iOS Build Support** to editor `6000.3.26f1`.
2. From the Unity project root, build the native sign-pose solver, then export. The script checksum-verifies and caches the OpenCV releases in `.cache/opencv`, then stages its license and bundled third-party notices under `Assets/StreamingAssets/ThirdPartyNotices/OpenCV/`; the export fails if the native solver or notices are missing (see [Precise pose from a sign](../Assets/IndoorAr/OCR-POC-prerequisites.md#precise-pose-from-a-sign-inert-until-surveyed-and-validated)). Unity includes the folder in `UnityFramework` runtime assets.

```bash
tools/build-sign-pnp.sh ios
/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath "$PWD" \
  -buildTarget iOS \
  -executeMethod IndoorAr.Editor.IndoorArExport.ExportIos \
  -logFile -
```

The menu item is **Indoor AR → Export iOS Xcode Project**. Output is `Builds/IndoorArIos/` (gitignored), an Xcode project whose `UnityFramework` target is the library the Flutter app embeds. The export links `ARKit.framework` and `MultipeerConnectivity.framework` into that target.

3. Open `Builds/IndoorArIos/Unity-iPhone.xcworkspace` if the export wrote one, otherwise `Unity-iPhone.xcodeproj`. CocoaPods creates the workspace for ML Kit, including indoor-only exports. Select the **UnityFramework** scheme and a generic iOS device (`arm64`). Product → Build.
4. Copy the built `UnityFramework.framework` into the Flutter app. The Flutter project gitignores that framework. Build it on a machine that has this Unity editor: the export symlinks editor libraries, so the framework binary is what moves, not the Xcode project alone.

The export can include MultiSet credentials from the Unity project, and the ARCore API key when it had one. Share the framework privately. Do not commit it.

## ARCore API key for outdoor AR

On iOS, ARCore Geospatial is authorized with an API key. The key is not in this repository, which is public. Keep it in one file on the machine that exports:

```bash
mkdir -p ~/.config/navipet && chmod 700 ~/.config/navipet
# paste the key as the only line of this file
chmod 600 ~/.config/navipet/arcore-ios-key
```

Do not type the key into **Project Settings → XR Plug-in Management → ARCore Extensions**. That saves it to `ProjectSettings/ARCoreExtensionsProjectSettings.json`, which is tracked. An EditMode test fails when that file holds a key.

With the key file, `ExportIos` turns on ARCore Extensions iOS support and API key authorization in memory for that one build, and adds the outdoor scene. After the build it reloads the committed settings, restores the iOS scripting defines and deletes `Assets/ExtensionsAssets/Runtime/RuntimeConfig.asset`, where ARCore Extensions puts the key for the build. The export does not log the key. The key ends up in `Builds/IndoorArIos/Data/` and in `Library/PlayerDataCache/`. Both are gitignored.

Without the key file the export logs a warning and builds the indoor scene only, as before. An outdoor request then returns `unavailable`.

A key inside an app can be read out of the app by anyone who has it. ARCore Extensions also writes the key to the device log when it creates its session. So the key must stay restricted in Google Cloud to the host app's iOS bundle ID and to the ARCore API, and it should be replaced if it leaks. The bundle ID that ARCore sees is the Flutter app's, not this Unity project's.

The keyed export adds the `ARCoreGARSession` and `ARCoreGeospatial` products of the Swift package `https://github.com/google-ar/arcore-ios-sdk.git` (1.57.1) to the `UnityFramework` target. ARCore Extensions asks for 1.57.0, which Xcode cannot resolve: Google's `CloudAnchors.zip` no longer matches the checksum in that version. 1.57.1 is the same SDK with the checksum corrected, so the export pins it. Xcode downloads the package the first time it opens the project. The export also runs `pod install`, which creates the workspace, so the export machine needs CocoaPods.

The Podfile holds one pod, `GoogleMLKit/TextRecognition` 6.0.0, from `Assets/IndoorAr/Editor/IndoorArOcrDependencies.xml`. It is the room-sign recognizer and it is linked statically into `UnityFramework`; its model, `LatinOCRResources.bundle`, is copied into `UnityFramework.framework`, where ML Kit looks for it, so the host app needs no pod and no extra resource. 6.0.0 is the last ML Kit that supports iOS 15.0; later ones need 15.5.

In a keyed export ARCore's Swift package and the ML Kit pod both bring Promises, GoogleDataTransport, GoogleUtilities and nanopb, and linking both copies fails with duplicate symbols. The export therefore removes the pod's copies from `Pods-UnityFramework.*.xcconfig`, and ML Kit links against ARCore's. Those are one major version newer than ML Kit 6.0.0 declares (GoogleUtilities 8 for 7, GoogleDataTransport 10 for 9). The framework links, and every class and selector ML Kit references is present in the newer copies, but no device run has confirmed ML Kit's usage logging with them. Running `pod install` by hand in the export restores the duplicates; export again instead. ML Kit recognizes on the device and uploads no image or text; like ARCore it reports usage metrics to Google.

For outdoor AR the host app must declare `NSLocationWhenInUseUsageDescription` and ask for the camera before it starts Unity. Unity starts its location service, which shows the location prompt. A refusal ends the session with `unavailable`.

## Indoor localization and routes

The shared runtime contracts live in [Signs first, MultiSet last](../Assets/IndoorAr/OCR-POC-prerequisites.md#signs-first-multiset-last-signfirst-not-device-verified) (authentication, request ownership and transform isolation) and [Hallway route](../Assets/IndoorAr/OCR-POC-prerequisites.md#hallway-route) (floor evidence, clearance and strict step-free policy).

## Host bridge

Before Unity's first scene runs, from the app that embeds `UnityFramework`:

```objc
#import "IndoorArHost.h"

[IndoorArHost setRequest:requestJson];
[IndoorArHost setResultHandler:^(NSString *json) {
    // Complete the Flutter method channel with json. Called once, on the main queue.
}];
```

`IndoorArHost.h` is copied into the export at `Libraries/Plugins/iOS/IndoorArHost.h`. Delivering a result clears the request. For a later launch, set the new request and result handler again; if the player was unloaded and left paused, setting both reloads it (see `IndoorArHost.h`). That reload path calls Unity trampoline symbols and is not verified on a device. A missing request makes Unity return `BAD_REQUEST`. A missing handler drops the result. The same error codes as Android apply, including `ARCORE_UNSUPPORTED` when the device reports no AR support.

On iOS, indoor exit disables the AR components but keeps the XR loader for reuse on re-entry. This is intended to avoid the missing XR input device observed after loader reinitialization; the change has not been verified on a device, and the earlier black-camera regression may return. The fallback is to pass `keepLoader: false` on indoor exit. Outdoor exit still tears down the loader; see [outdoor AR](../Assets/OutdoorAr/README.md#hosted-session).

Optionally, `[IndoorArHost setStatusHandler:^(NSString *json) { ... }]` receives progress while a session runs, on the main queue; it stays set across sessions, and without it status is dropped. On Android the same string goes to the host activity's `postStatus(String)` if it has one; a host without that method is detected once and skipped. Unity posts only when the state, tracking, recognized room, localized flag or fallback availability changes:

```json
{"type":"status","schemaVersion":2,"sessionId":"<32 hex>","seq":2,"mapCode":"MSET_XTBY7AM7ZK77",
 "frame":"multiset-mapset","state":"pinned","tracking":true,"roomRecognized":"VEC120","roomFloor":1,
 "localized":true,"fallbackAvailable":true,
 "pose":{"source":"sign","position":{"x":0,"y":0,"z":0},"rotation":{"x":0,"y":0,"z":0,"w":1}}}
```

`sessionId` is new for each request and `seq` counts up within it. `roomRecognized` and `roomFloor` appear once a room sign is confirmed; that is an identity, not a position. The final result also gains optional `signRoom` with the last confirmed room ID; older hosts can ignore it. `pose` (camera in the MapSet frame, `source` `sign` or `multiset`) appears only when `localized` is true, and is the pose at the moment of that change, not a stream. Do not log status JSON: it can carry the camera pose. The Flutter side does not decode it yet; adding the handler there and `postStatus` to `UnityHostActivity` is a local Flutter follow-up.

## Floor-change pressure

Unity reads the phone barometer through the Core Motion altimeter in `IndoorArPressure.mm` only if the user confirms a starting floor and the schema 2 launch carries complete version 1 floor evidence. The confirmation age must be within 0–1200 seconds and the newest host sample age within 0–10 seconds. Skipped confirmation or stale evidence leaves the barometer off and preserves the existing pose and sign policy. Raw pressure stays on the device. If Motion & Fitness access is refused or the phone lacks a barometer, the remaining floor evidence rules still apply. A settled change of at least 0.30 hPa can release a stale floor sign or the trusted start-floor veto. Pressure never identifies a floor or directly rejects a pose.

Proposed `NSMotionUsageDescription` for the host app’s Info.plist

> NaviPet uses Motion & Fitness data to count your steps and, only if you confirm which floor you are starting on, to detect when you change floors indoors using the barometer. Barometer readings stay on your device and are used only during indoor navigation.

Unity uses its first settled window after launch for the trusted start-floor baseline. A move before that window settles can be missed. A sign confirmed before pressure settles must be confirmed again during a settled window before pressure can release it. The thresholds come from one iPhone in one building. Exported `UnityFramework` linking, permission behavior, and device readings remain unverified. See the [floor evidence contract](floor-evidence-contract.md) and [calibrated constants](../Assets/IndoorAr/Scripts/IndoorFloorBarometer.cs).

Even with a valid pressure anchor, a stale sign can veto poses on the new floor while pressure is still settling after an elevator ride. MultiSet can exhaust its automatic retry in that time, and the user may need to retry localization manually.

## Not verified here

The final indoor OCR change still requires a native player build and device validation. Prior framework linking does not verify the final change. EditMode tests cover the shared contract, the editor bridge stand-in, and the saved iOS player and XR settings.

The outdoor change was exported with iOS Build Support and the ARCore key, and the exported workspace was built with Xcode 26.5 without signing (`Release`, `iphoneos`). That shows the ARCore package resolves and `UnityFramework` links. Nothing ran on a device, so these are not verified: the key and its restriction, the location prompt, ARCore's Earth service, VPS coverage, and the framework inside the Flutter app. An export without the key file was not built in Xcode either.
