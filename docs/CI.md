# Unity CI

GitHub Actions workflow: `.github/workflows/unity-editmode.yml`.

It runs on pull requests and on pushes to `main`. The only check is Unity **EditMode** tests (`Assets/IndoorAr` tests, including `IndoorHudTests`) through GameCI `unity-test-runner` on Unity `6000.3.26f1`.

The Android library export (`tools/build-indoor-ar-aar.sh`) is not a CI job. It needs a Flutter `unity-ar` Gradle project and a local Android player, which is more than the license secrets below.

## Secrets

Create these in the GitHub repository under Settings → Secrets and variables → Actions. Do not commit them or paste them into logs.

Use one license form:

- Personal license: `UNITY_LICENSE` (full contents of the Unity `.ulf` file), `UNITY_EMAIL`, `UNITY_PASSWORD`
- Unity Plus or Pro: `UNITY_SERIAL`, `UNITY_EMAIL`, `UNITY_PASSWORD`

Leave the other license secret unset. `UNITY_LICENSE` wins if both are set.

How to get the license file: [GameCI activation](https://game.ci/docs/github/activation). For a personal license, activate it in Unity Hub (Preferences → Licenses → Add → Get a free personal license), then copy the `.ulf` from `/Library/Application Support/Unity/Unity_lic.ulf` on macOS into `UNITY_LICENSE`. For Plus or Pro, copy the serial from the Unity subscriptions page into `UNITY_SERIAL`.

If `UNITY_LICENSE` and `UNITY_SERIAL` are both unset, the job prints a notice and stays green. A rejected license that never produces test results does the same. EditMode failures still fail the check.

## Not verified

GameCI's current activation and test-runner docs describe the personal (`.ulf` as `UNITY_LICENSE`) and professional (`UNITY_SERIAL`) environment variables. They do not document a Unity 6000.3-specific license path. This workflow uses those documented names and does not claim a separate 6000.3 activation procedure.
