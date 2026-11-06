# Indoor AR HUD

On the phone, `IndoorArRunner` parents `IndoorHud` to the MultiSet map and feeds it `LiveDoorHud`. The background is the live AR camera (`ARCameraBackground` on the XR camera in `IndoorArSpike`). That scene has no generated hallway.

While MultiSet is localizing, the card says the phone is scanning. If the phone moves faster than a slow scan, a centered line says Scan slowly. After a fix, it says the location was found, then one arrow in the center of the camera turns toward the door as the phone turns. It does not route around walls. The destination reads the searched room, so VEC119 shows as VEC 119. If localization fails, the card says it could not find the location. On a device that card stays up for about four seconds, then the activity returns `localizationFailed`. Editor play returns immediately.

**Indoor AR → Open Mock HUD Preview** is still the editor preview. It uses `MockIndoorRoute` and a generated corridor, and it does not change the shipping scene. The seven cards rendered over that corridor are in `docs/hud-states/`. To regenerate them, set `NAVIPET_HUD_SHOTS` to an output directory and run Unity in batch mode with `-executeMethod IndoorAr.Editor.IndoorHudCapture.Capture`.

Still not real: the Gradle AAR was not assembled and the phone install was not run, floors 3-5 are out of scope, and there is no NavMesh route. There is no Supabase room data. The door pose is whatever Flutter sends.

The captain builds the phone library on a Mac that already has the gitignored `Assets/Samples/MultiSet-SDK/**/Resources/MultiSetConfig.asset`. Never commit, print, or request that file.

```
tools/build-indoor-ar-aar.sh /path/to/NaviPetFlutter/android/unity-ar
```

Requires Unity `6000.3.26f1` with Android Build Support. The script exports `Builds/IndoorArAndroid` and copies the release AARs into that `unity-ar` folder.
