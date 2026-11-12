# iOS export

Produces the Xcode project and `UnityFramework` the Flutter app embeds. The request and result JSON match Android: the request is the `navipet.indoorAr.request` extra (schemaVersion 1 or 2, optional `arToken`, and optional `mode: "localize"`), and the result is the string `UnityHostActivity.deliverResult` returns, including `errorCode`. A localize-only request omits destination pose fields and scans without showing a pin or arrival controls. When `arToken` carries a `token` and `expiresOn`, Unity authenticates with it and needs no client id or secret; otherwise it falls back to the project's MultiSet configuration.

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

3. Open `Builds/IndoorArIos/Unity-iPhone.xcodeproj`. Select the **UnityFramework** scheme and a generic iOS device (`arm64`). Product → Build.
4. Copy the built `UnityFramework.framework` into the Flutter app. The Flutter project gitignores that framework. Build it on a machine that has this Unity editor: the export symlinks editor libraries, so the framework binary is what moves, not the Xcode project alone.

The export can include MultiSet credentials from the Unity project. Share the framework privately. Do not commit it.

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
