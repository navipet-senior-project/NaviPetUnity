# iOS export

Produces the Xcode project and `UnityFramework` the Flutter app embeds. The request and result JSON match Android: the request is the `navipet.indoorAr.request` extra (schemaVersion 1 or 2, optional `arToken`, and optional `mode: "localize"`), and the result is the string `UnityHostActivity.deliverResult` returns, including `errorCode`. A localize-only request omits destination pose fields and scans without showing a pin or arrival controls. An outdoor request (`mode: "outdoor"`) runs the ARCore Geospatial scene when the export had the ARCore API key, and returns `unavailable` when it did not (see [ARCore API key](#arcore-api-key-for-outdoor-ar)). When `arToken` carries a `token` and `expiresOn`, Unity authenticates with it and needs no client id or secret; otherwise it falls back to the project's MultiSet configuration.

Unity **6000.3.26f1**. AR Foundation and the ARKit XR plugin are **6.3.5**. The iOS player settings committed in this project are minimum iOS **15.0** (ARKit's own floor is 11), **ARM64**, **IL2CPP**, **Metal** only, a camera usage description, a location usage description, the `UNITY_XR_ARKIT_LOADER_ENABLED` define, and a disabled Unity splash screen and logo. The iOS export reapplies and checks the splash settings immediately before building; if the license forces the splash back on, export stops with an error. XR Plug-in Management enables the ARKit loader for iOS and leaves the ARCore loader on Android. ARKit's requirement is optional, same as ARCore, so a host app can still install on a phone without AR.

This editor install has no iOS Build Support module (`PlaybackEngines` contains `AndroidPlayer` only). Install the module before the export below. Do not switch the editor version.

## Export the Xcode project

1. In Unity Hub, add **iOS Build Support** to editor `6000.3.26f1`.
2. From the Unity project root:

```bash
/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath "$PWD" \
  -buildTarget iOS \
  -executeMethod IndoorAr.Editor.IndoorArExport.ExportIos \
  -logFile -
```

The menu item is **Indoor AR → Export iOS Xcode Project**. Output is `Builds/IndoorArIos/` (gitignored), an Xcode project whose `UnityFramework` target is the library the Flutter app embeds. The export links `ARKit.framework`, `MultipeerConnectivity.framework`, and `Vision.framework` into that target.

3. Open `Builds/IndoorArIos/Unity-iPhone.xcworkspace` if the export wrote one, otherwise `Unity-iPhone.xcodeproj`. An export with the ARCore key writes the workspace. Select the **UnityFramework** scheme and a generic iOS device (`arm64`). Product → Build.
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

## Step-free routes

The provisional route grid comes from the VEC scan; it is not a navigation graph. It has no verified step-free attributes for same-floor segments, and the VEC directory does not prove that a floor connector is step-free. An indoor request with `"stepFree": true` therefore shows “A step-free route to this destination cannot be confirmed” and returns `unavailable` with `STEP_FREE_UNVERIFIED`; it never guides users onto an unverified route. Requests without the field keep ordinary routing. Flutter does not send the field yet; adding a saved mobility setting and serializing it in `IndoorArRequest.toJson` is a local Flutter follow-up.

Floor changes use a confirmed room sign on supported floors 1–2, or camera height only when it falls in the plausible eye-height band above exactly one floor elevation from the MapSet-frame scan grid. If neither establishes a floor, Unity shows no route. MultiSet poses that conflict with a confirmed sign or resolve above supported floors are rejected for retry. Successful MultiSet localization logs map codes only.

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

## Not verified here

The editor used for this change has no iOS Build Support, so the iOS player was not compiled and nothing was run on a device. EditMode tests cover the shared contract, the editor bridge stand-in, and the saved iOS player and XR settings.

The outdoor change was exported with iOS Build Support and the ARCore key, and the exported workspace was built with Xcode 26.5 without signing (`Release`, `iphoneos`). That shows the ARCore package resolves and `UnityFramework` links. Nothing ran on a device, so these are not verified: the key and its restriction, the location prompt, ARCore's Earth service, VPS coverage, and the framework inside the Flutter app. An export without the key file was not built in Xcode either.
