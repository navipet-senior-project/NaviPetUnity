using System.Collections;
using System.Globalization;
using UnityEngine;
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
    public class IndoorArRunner : MonoBehaviour
    {
        [SerializeField] MonoBehaviour localizerBehaviour; // must implement IIndoorArLocalizer
        [SerializeField] GameObject doorPinPrefab;
        [SerializeField] Camera arCamera;

        IIndoorArLocalizer _localizer;
        IndoorArSession _session;
        Transform _pin;
        bool _delivered;
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
            if (!IndoorArContract.TryParseRequest(IndoorArBridge.ReadRequestJson(), out var request))
            {
                Deliver(IndoorArContract.Error(IndoorArContract.BadRequest, 0, 0));
                yield break;
            }
            _session = new IndoorArSession(request, _localizer.MapCode);
            if (_session.State == IndoorArState.Done) yield break; // MAP_MISMATCH, delivered in Update
            _session.LocalizeRequested += _localizer.Localize;
            _localizer.Authenticated += _session.OnAuthenticated;
            _localizer.AuthFailed += _session.OnAuthFailed;
            _localizer.QuerySent += _session.OnQuerySent;
            _localizer.Localized += _session.OnLocalized;

            yield return ARSession.CheckAvailability();
            _session.OnArSupport(ARSession.state != ARSessionState.Unsupported);
            if (_session.State == IndoorArState.Auth) _localizer.Authenticate();
        }

        void Update()
        {
            if (_session == null || _delivered) return;
            if (BackPressed()) _session.Cancel();
            if (_session.State == IndoorArState.Pinned && ARSession.state != ARSessionState.SessionTracking)
                _session.OnTrackingLost();
            _session.Tick(Time.unscaledDeltaTime);
            UpdatePin();
            if (_session.State == IndoorArState.Done) Deliver(_session.ResultJson);
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

        Vector3 CameraInMap() => _localizer.MapSpace.InverseTransformPoint(arCamera.transform.position);

        void OnGUI()
        {
            if (_session == null || _delivered) return;
            var fontSize = Screen.height / 32;
            _label ??= new GUIStyle(GUI.skin.label)
                { fontSize = fontSize, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _button ??= new GUIStyle(GUI.skin.button) { fontSize = fontSize };
            var width = Screen.width * 0.8f;
            var height = Screen.height * 0.08f;
            var x = (Screen.width - width) / 2f;

            GUI.Label(new Rect(x, Screen.height * 0.06f, width, height), StatusText(), _label);
            if (_session.State == IndoorArState.Pinned)
            {
                var camera = CameraInMap();
                var door = _session.Request;
                GUI.Label(new Rect(x, Screen.height * 0.14f, width, height),
                    $"{door.doorKey} · floor {door.floor} · {_session.HorizontalDistance(camera):0.0} m", _label);
                if (_session.IsWrongFloor(camera))
                    GUI.Label(new Rect(x, Screen.height * 0.22f, width, height),
                        $"{door.doorKey} is on floor {door.floor}", _label);
                if (GUI.Button(new Rect(x, Screen.height * 0.74f, width, height), "I'm here", _button))
                {
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[IndoorAr] arrived door={0} camera=({1:F3}, {2:F3}, {3:F3})",
                        door.doorKey, camera.x, camera.y, camera.z));
                    _session.ImHere(camera);
                }
            }
            if (GUI.Button(new Rect(x, Screen.height * 0.86f, width, height), "Exit", _button))
                _session.Cancel();
        }

        string StatusText() => _session.State switch
        {
            IndoorArState.Init => "Starting AR…",
            IndoorArState.Auth => "Connecting to MultiSet…",
            IndoorArState.Localizing => _session.WasPinned
                ? "Re-finding your position"
                : "Point at walls, doors and signs",
            IndoorArState.Pinned => "Walk to the pin, then tap I'm here",
            _ => "",
        };

        void OnDestroy()
        {
            if (_session == null || _localizer == null) return;
            _session.LocalizeRequested -= _localizer.Localize;
            _localizer.Authenticated -= _session.OnAuthenticated;
            _localizer.AuthFailed -= _session.OnAuthFailed;
            _localizer.QuerySent -= _session.OnQuerySent;
            _localizer.Localized -= _session.OnLocalized;
        }

        void Deliver(string resultJson)
        {
            if (_delivered) return;
            _delivered = true;
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
