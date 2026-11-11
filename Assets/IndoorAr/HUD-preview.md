# Indoor AR HUD

On the phone, `IndoorArRunner` parents `IndoorHud` to the MultiSet map and feeds it `LiveDoorHud`. The background is the live AR camera (`ARCameraBackground` on the XR camera in `IndoorArSpike`). That scene has no generated hallway.

The overlay is a small room pill at the top left ("VEC 119 · Floor 1"), one white round close button at the top right, and two short guidance lines at the bottom. There is no mini-map, no instruction card, and no distance. While MultiSet is localizing, the lines say the phone is scanning; if the phone moves faster than a slow scan, they say Slow down. After a fix, the HUD shows a white direction-to-room arrow with a navy outline aimed at the saved destination; it does not provide turn-by-turn guidance or a walkable route. When the room is off to the side, a **Look this way** pill appears on that edge of the screen. On a different floor, the lines name the room's floor. The amber destination pin has a white core and sits at the saved room position with the room label floating above it. The label reads the searched room, so VEC119 shows as VEC 119. Tap the small **I'm here** pill beside the guidance lines to open the arrival card, then **Done** to return the arrival result. A location-only request has no destination, pin, or arrival action. If localization fails, a card inside AR offers **Try again** or **Back to map**. Authentication failures remain visible for about four seconds on device before the activity returns an error; Editor play returns immediately.

**Indoor AR → Open Mock HUD Preview** is still the editor preview. It uses `MockIndoorRoute` and a generated corridor, and it does not change the shipping scene. Pick the state on the `Mock HUD preview` object: scanning, location found, direction to room, off-screen direction, wrong floor, localization failure, or arrival. The seven states rendered over that corridor are in `docs/hud-states/`. To regenerate them, set `NAVIPET_HUD_SHOTS` to an output directory and run Unity in batch mode (with graphics, without `-quit`) with `-executeMethod IndoorAr.Editor.IndoorHudCapture.Capture`.

Still not real: the Gradle AAR was not assembled and the phone install was not run, floors 3-5 are out of scope, and there is no NavMesh route or Supabase room data. The saved door pose is whatever Flutter sends; rooms without a pose can only be used for localization.

The captain builds the phone library on a Mac that already has the gitignored `Assets/Samples/MultiSet-SDK/**/Resources/MultiSetConfig.asset`. Never commit, print, or request that file.

```
tools/build-indoor-ar-aar.sh /path/to/NaviPetFlutter/android/unity-ar
```

Requires Unity `6000.3.26f1` with Android Build Support. The script exports `Builds/IndoorArAndroid` and copies the release AARs into that `unity-ar` folder.
