using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.ARFoundation;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IndoorAr
{
    /// <summary>
    /// Runs one door verification: reads the launch request, drives
    /// IndoorArSession from AR Foundation and the localizer, shows the pin and
    /// HUD, and hands the result to the Android host.
    /// </summary>
    [DefaultExecutionOrder(100)] // after EventSystem, so an exit tap cancels before tracking loss rebuilds the HUD
    public class IndoorArRunner : MonoBehaviour
    {
        [SerializeField] MonoBehaviour localizerBehaviour; // must implement IIndoorArLocalizer
        [SerializeField] GameObject doorPinPrefab;
        [SerializeField] Camera arCamera;
        [SerializeField] IndoorArFloorBand[] floorBands = System.Array.Empty<IndoorArFloorBand>();

        IIndoorArLocalizer _localizer;
        IndoorArSession _session;
        string _requestJson;
        bool _started;
        Transform _pin;
        IndoorHud _hud;
        float _pinnedFor;
        float _failureLeft = -1f;
        bool _failureRelease;
        bool _delivered;
        Vector3 _scanPos;
        Quaternion _scanRot;
        bool _scanPoseReady;
        float _slowHoldLeft;
        GUIStyle _label;
        GUIStyle _button;

        public void Configure(MonoBehaviour localizer, GameObject pinPrefab, Camera camera)
        {
            localizerBehaviour = localizer;
            doorPinPrefab = pinPrefab;
            arCamera = camera;
        }

        IEnumerator Start()
        {
            _localizer = (IIndoorArLocalizer)localizerBehaviour;
            BeginSession(IndoorArBridge.ReadRequestJson());
            if (_session == null || _session.State == IndoorArState.Done) yield break;
            var started = _session;
            yield return ARSession.CheckAvailability();
            if (_session != started) yield break; // a restart already authenticated the newer session
            // Editor smoke must never authenticate or consume VPS quota.
            _session.OnArSupport(!Application.isEditor && ARSession.state != ARSessionState.Unsupported);
            if (_session.State == IndoorArState.Auth) _localizer.Authenticate();
        }

        // A host that only unpauses the player keeps this object. The launch
        // JSON is the new session; the previous door and the delivered flag are not.
        void BeginSession(string json)
        {
            var restart = _started;
            _started = true;
            ReleaseSession();
            _requestJson = json;
            _delivered = false;
            _pinnedFor = 0f;
            _failureLeft = -1f;
            _failureRelease = false;
            _scanPoseReady = false;
            _slowHoldLeft = 0f;
            if (!IndoorArContract.TryParseRequest(json, out var request))
            {
                Deliver(IndoorArContract.Error(IndoorArContract.BadRequest, 0, 0));
                return;
            }
            _session = new IndoorArSession(request, _localizer.MapCode);
            if (_session.State == IndoorArState.Done) return; // MAP_MISMATCH, delivered in Update
            AttachHud();
            _session.LocalizeRequested += _localizer.Localize;
            _localizer.Authenticated += _session.OnAuthenticated;
            _localizer.AuthFailed += _session.OnAuthFailed;
            _localizer.AttemptStarted += _session.OnAttemptStarted;
            _localizer.Localized += OnLocalized;
            if (!restart || Application.isEditor) return;
            var ar = UnityEngine.Object.FindAnyObjectByType<ARSession>();
            if (ar != null) ar.Reset();
            _session.OnArSupport(ARSession.state != ARSessionState.Unsupported);
            if (_session.State == IndoorArState.Auth) _localizer.Authenticate();
        }

        void Update()
        {
            if (_localizer != null)
            {
                var json = IndoorArBridge.ReadRequestJson();
                if (json != null && json != _requestJson) BeginSession(json);
            }
            if (_session == null || _delivered) return;
            if (BackPressed()) OnHudExit();
            if (_session.State == IndoorArState.Pinned && ARSession.state != ARSessionState.SessionTracking)
                _session.OnTrackingLost();
            if (_session.State == IndoorArState.Pinned) _pinnedFor += Time.unscaledDeltaTime;
            else _pinnedFor = 0f;
            _session.Tick(Time.unscaledDeltaTime);
            UpdatePin();
            // shortcut: hold the failure card 4s on device so it can be read before the activity closes.
            if ((LiveDoorHud.LocalizationFailed(_session) || LiveDoorHud.AuthFailed(_session)) && _failureLeft < 0f)
                _failureLeft = Application.isEditor ? 0f : 4f;
            var holdingFailure = _failureLeft > 0f && !_failureRelease;
            if (holdingFailure) _failureLeft -= Time.unscaledDeltaTime;
            UpdateHud();
            if (_session.State == IndoorArState.Done && !holdingFailure) Deliver(_session.ResultJson);
        }

        void AttachHud()
        {
            var hudObject = new GameObject("Indoor HUD");
            hudObject.transform.SetParent(_localizer.MapSpace, false);
            _hud = hudObject.AddComponent<IndoorHud>();
            _hud.Initialize(arCamera);
            _hud.ExitRequested += OnHudExit;
            if (EventSystem.current == null)
            {
                var events = new GameObject("HUD EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
        }

        void OnHudExit()
        {
            _failureRelease = true;
            _session?.Cancel();
        }

        void UpdateHud()
        {
            if (_hud == null || _delivered) return;
            if (_session.State == IndoorArState.Done
                && !LiveDoorHud.LocalizationFailed(_session)
                && !LiveDoorHud.AuthFailed(_session)) return;
            var view = LiveDoorHud.Compose(_session, CameraInMap(), CameraForwardInMap(), _pinnedFor);
            if (view.State == IndoorHudState.Scanning)
                view.ScanTooFast = SampleScanMotion();
            else
            {
                _scanPoseReady = false;
                _slowHoldLeft = 0f;
            }
            _hud.Show(view);
        }

        bool SampleScanMotion()
        {
            var pose = arCamera.transform;
            var tooFast = _scanPoseReady && GuidanceCue.ScanTooFast(
                Vector3.Distance(_scanPos, pose.position),
                Quaternion.Angle(_scanRot, pose.rotation),
                Time.unscaledDeltaTime);
            _scanPos = pose.position;
            _scanRot = pose.rotation;
            _scanPoseReady = true;
            _slowHoldLeft = GuidanceCue.HoldSlowScan(_slowHoldLeft, tooFast, Time.unscaledDeltaTime);
            return _slowHoldLeft > 0f;
        }

        void UpdatePin()
        {
            var pinned = _session.State == IndoorArState.Pinned;
            if (pinned && _pin == null)
            {
                var door = _session.Request;
                _pin = Instantiate(doorPinPrefab, _localizer.MapSpace).transform;
                _pin.localPosition = new Vector3(door.position.x, door.position.y, door.position.z);
                _pin.localRotation = Quaternion.Euler(0f, door.headingDeg, 0f);
            }
            if (_pin != null) _pin.gameObject.SetActive(pinned);
        }

        void OnLocalized(float? confidence) =>
            _session.OnLocalizedWithFloorCheck(confidence, CameraInMap().y, floorBands);

        Vector3 CameraInMap() => _localizer.MapSpace.InverseTransformPoint(arCamera.transform.position);

        Vector3 CameraForwardInMap() => _localizer.MapSpace.InverseTransformDirection(arCamera.transform.forward);

        void OnGUI()
        {
            if (_session == null || _delivered || _session.State != IndoorArState.Pinned) return;
            var fontSize = Screen.height / 32;
            _label ??= new GUIStyle(GUI.skin.label)
                { fontSize = fontSize, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _button ??= new GUIStyle(GUI.skin.button) { fontSize = fontSize };
            var width = Screen.width * 0.8f;
            var height = Screen.height * 0.08f;
            var x = (Screen.width - width) / 2f;
            var camera = CameraInMap();
            var door = _session.Request;
            if (_session.IsWrongFloor(camera))
                GUI.Label(new Rect(x, Screen.height * 0.42f, width, height),
                    $"{LiveDoorHud.Destination(door)} is on floor {door.floor}", _label);
            if (GUI.Button(new Rect(x, Screen.height * 0.78f, width, height), "I'm here", _button))
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[IndoorAr] arrived door={0} camera=({1:F3}, {2:F3}, {3:F3})",
                    door.doorKey, camera.x, camera.y, camera.z));
                _session.ImHere(camera);
            }
        }

        void OnDestroy() => Unhook();

        void ReleaseSession()
        {
            Unhook();
            if (_hud != null)
            {
                DestroyOwned(_hud.gameObject);
                _hud = null;
            }
            if (_pin != null)
            {
                DestroyOwned(_pin.gameObject);
                _pin = null;
            }
            _session = null;
        }

        void Unhook()
        {
            if (_hud != null) _hud.ExitRequested -= OnHudExit;
            if (_session == null || _localizer == null) return;
            _session.LocalizeRequested -= _localizer.Localize;
            _localizer.Authenticated -= _session.OnAuthenticated;
            _localizer.AuthFailed -= _session.OnAuthFailed;
            _localizer.AttemptStarted -= _session.OnAttemptStarted;
            _localizer.Localized -= OnLocalized;
        }

        static void DestroyOwned(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        void Deliver(string resultJson)
        {
            if (_delivered) return;
            _delivered = true;
            _requestJson = null;
            IndoorArBridge.Deliver(resultJson);
        }

        // Android's back button arrives as Escape.
        static bool BackPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
