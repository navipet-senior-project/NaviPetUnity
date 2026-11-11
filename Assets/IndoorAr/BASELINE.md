# MultiSet localization baseline

`IndoorArBaseline` records durations and counts from the existing session hooks. It does not change localization, the camera session, or navigation. The device log line is `[IndoorArBaseline]`. It contains no camera frame, pose, token, map payload, or user location.

| Field | Origin | Meaning |
| --- | --- | --- |
| `arStartupMs` | session opened | `BeginSession` until `OnArSupport`. Includes `ARSession.CheckAvailability`. Excludes the Unity splash and the Flutter tap. |
| `requestLatencyMs` | attempt started | `LocalizeFrame` start until the first accepted pose. A dropped or missing pose leaves this `na`. It is not the 30 s timeout. |
| `timeToFirstLocalizationMs` | session opened | First accepted pose (`poseFound`). Later poses do not replace it. |
| `arrowShownMs` | session opened | HUD first enters the guide-arrow state, after the 1.5 s “Location found” hold. It records display timing, not a rendered-frame timestamp. |
| `arrowAlignment` | — | Always `unmeasured`. Arrow display is not evidence the arrow matches the real doorway. |
| `attempts` | — | `LocalizeFrame` starts. |
| `retries` | — | Starts after the first, including the timeout retry, tracking-loss restart, and Try again. |
| `trackingLosses` | — | Pinned sessions that left `SessionTracking`. |
| `unansweredAttempts` | — | Starts that have not returned a pose. The open attempt is included until it does. |

Editor play mode does not emit this line. On a phone, filter logcat or the Xcode console for `IndoorArBaseline`. One line is printed each time a field changes.

## What a device run still has to measure

These numbers are not produced by this instrumentation. Do not fill them in from the log.

- Real-world arrow alignment. Compare the arrow with the doorway only while standing in the mapped hallway. Record the door key, floor, and error you observed. `arrowShownMs` stays a display time.
- Pose accuracy against a surveyed point.
- Memory and battery during a scan.
- Splash time, Flutter tap-to-activity time, and camera-permission time. `arStartupMs` starts after the player is already running.

Use one phone for a series. Same door, same network, commit recorded. Stand in a hallway the map contains, start from the chest, one slow sweep. Repeat. Log the attempt index. Do not log the token, the raw pose, or `MultiSetConfig`.

Offline control: airplane mode should return `NETWORK` before a VPS call, with `attempts=0`.
