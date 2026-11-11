# Outdoor ARCore Geospatial prototype

This prototype is isolated from `IndoorAr` and the existing AR session setup. `OutdoorGeospatialConfig.asset` enables Geospatial mode only for the dedicated outdoor scene; its AR session starts only after the user opts in. Google Cloud authorization remains an external prerequisite. No cloud project, key, billing, or campus coverage check was created or performed for this change.

`OutdoorGeospatialDemo` requests user consent and Android camera/fine-location permissions, then starts the dedicated session. It checks point-level VPS availability and creates a green marker on an anchor at the tracked device's current pose only when `OutdoorGeospatialGate` reports ready. The VPS result expires when the tracked position moves more than 5 m from the queried point. Device support, Earth service, active Earth tracking, horizontal accuracy at most 5 m, and heading accuracy at most 10° are required. All other states remove the marker and deny anchor placement. Thresholds need real-device validation.

Open `OutdoorAr/Scenes/OutdoorGeospatialDemo.unity` to build and run the prototype. Add the user-provided ARCore authorization in the project settings before device validation. The scene is listed after the existing indoor scene in Build Settings; set it as the startup scene for an outdoor device build. Do not reuse MultiSet poses or claim campus coverage. No VPS request runs before consent, permissions, and active Earth tracking.

The Unity project uses 6000.3.26f1, AR Foundation/ARCore XR Plugin 6.3.5, and ARCore Extensions 1.57.0 AR Foundation 6 tag. Google's 1.53.0 release notes report the Unity 6.3 compile fix; device and package-resolution validation is still required.
