# Indoor AR HUD preview

This is a mock preview, not live navigation. The corridor, positions, route, distances and destinations are illustrative. No localization, routing, map alignment, floor transitions, rerouting or arrival detection is connected. The existing Flutter bridge and door verifier are unchanged; in particular, Unity's v2 token support is still needed before the current Flutter launch can localize.

In Unity, choose **Indoor AR → Open Mock HUD Preview**, press Play, then select **Mock HUD preview** in the Hierarchy. Change **State** and **Floor** in the Inspector to review the seven states and both mock destinations. The preview scene is temporary and does not change the shipping scene.

To connect real navigation later, initialize `IndoorHud` with the AR camera, parent it in the route's map frame, and call `Show(IndoorHudView)` as the provider changes state. Supply the current remaining waypoints in that frame, destination, floor and distance; subscribe to `ExitRequested` and connect an EventSystem for the button. The first waypoint is the displayed position on the mini-map. The provider owns localization, timeout, turn direction, recalculation and arrival decisions. The current turn card illustrates a right turn only.

To reproduce screenshots, set `NAVIPET_HUD_SHOTS` to an output directory and run Unity in batch mode with `-executeMethod IndoorAr.Editor.IndoorHudCapture.Capture`. The images are rendered by Unity over a generated corridor, not a live camera feed.
