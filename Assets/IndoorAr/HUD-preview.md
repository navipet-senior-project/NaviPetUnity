# Indoor AR HUD

On the phone, `IndoorArRunner` parents `IndoorHud` to the MultiSet map and feeds it `LiveDoorHud`. The background is the live AR camera (`ARCameraBackground` on the XR camera in `IndoorArSpike`). That scene has no generated hallway.

While MultiSet is localizing, the card says the phone is scanning. If the phone moves faster than a slow scan, a centered line says Scan slowly. After a fix, the HUD shows a direction-to-room arrow aimed at the saved destination; it does not provide turn-by-turn guidance or a walkable route. The amber destination pin has a white core. The destination reads the searched room, so VEC119 shows as VEC 119. Tap **I'm here** to open the arrival card, then **Done** to return the arrival result. A location-only request has no destination, pin, or arrival action. If localization fails, the card offers **Try again** or **Back to map**. Authentication failures remain visible for about four seconds on device before the activity returns an error; Editor play returns immediately.

**Indoor AR → Open Mock HUD Preview** is still the editor preview. It uses `MockIndoorRoute` and a generated corridor, and it does not change the shipping scene. The seven cards rendered over that corridor are in `docs/hud-states/`. To regenerate them, set `NAVIPET_HUD_SHOTS` to an output directory and run Unity in batch mode with `-executeMethod IndoorAr.Editor.IndoorHudCapture.Capture`.

Still not real: the Gradle AAR was not assembled and the phone install was not run, floors 3-5 are out of scope, and there is no NavMesh route or Supabase room data. The saved door pose is whatever Flutter sends; rooms without a pose can only be used for localization.

The captain builds the phone library on a Mac that already has the gitignored `Assets/Samples/MultiSet-SDK/**/Resources/MultiSetConfig.asset`. Never commit, print, or request that file.

```
tools/build-indoor-ar-aar.sh /path/to/NaviPetFlutter/android/unity-ar
```

Requires Unity `6000.3.26f1` with Android Build Support. The script exports `Builds/IndoorArAndroid` and copies the release AARs into that `unity-ar` folder.
