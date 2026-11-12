using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
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
        IndoorArState _loggedState;
        bool _stateLogged;
        float _sessionStarted;
        IndoorArBaseline _baseline;
        string _baselineLog;
        Material _pinAmberMaterial;
        Material _pinCoreMaterial;

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
            NoteArReady();
            if (_session.State == IndoorArState.Auth) _localizer.Authenticate();
        }

        // A host that only unpauses the player keeps this object. The launch
        // JSON is the new session; the previous door and the delivered flag are not.
        void BeginSession(string json)
        {
            var restart = _started;
            _started = true;
            _sessionStarted = Time.realtimeSinceStartup;
            _stateLogged = false;
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
            if (request.OutdoorOnly)
            {
                // The outdoor scene reads the same request and answers it. Exports without that scene say so.
                if (Application.CanStreamedLevelBeLoaded(IndoorArContract.OutdoorScene))
                    SceneManager.LoadScene(IndoorArContract.OutdoorScene);
                else
                    Deliver(IndoorArContract.Unavailable(0, 0));
                return;
            }
            _session = new IndoorArSession(request, _localizer.MapCode);
            if (_session.State == IndoorArState.Done) return; // MAP_MISMATCH, delivered in Update
            _baseline = new IndoorArBaseline();
            _baselineLog = null;
            _baseline.NoteSessionOpened(NowMs());
            AttachHud();
            _session.LocalizeRequested += RequestLocalization;
            _localizer.Authenticated += _session.OnAuthenticated;
            _localizer.AuthFailed += _session.OnAuthFailed;
            _localizer.AttemptStarted += _session.OnAttemptStarted;
            _localizer.Localized += OnLocalizedObserved;
            if (!restart || Application.isEditor) return;
            var ar = UnityEngine.Object.FindAnyObjectByType<ARSession>();
            if (ar != null) ar.Reset();
            _session.OnArSupport(ARSession.state != ARSessionState.Unsupported);
            NoteArReady();
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
            if (_session.State != IndoorArState.Init) NoteArReady();
            if (_session.State == IndoorArState.Pinned && ARSession.state != ARSessionState.SessionTracking)
            {
                _session.OnTrackingLost();
                _baseline?.NoteTrackingLost();
                LogBaseline();
            }
            if (_session.State == IndoorArState.Pinned) _pinnedFor += Time.unscaledDeltaTime;
            else _pinnedFor = 0f;
            _session.Tick(Time.unscaledDeltaTime);
            if (!_stateLogged || _loggedState != _session.State)
            {
                _loggedState = _session.State;
                _stateLogged = true;
                if (!Application.isEditor)
                    Debug.Log($"[IndoorAr] state={_loggedState} elapsedMs={(long)((Time.realtimeSinceStartup - _sessionStarted) * 1000)} attempts={_session.Queries}");
            }
            UpdatePin();
            // shortcut: hold the failure card 4s on device so it can be read before the activity closes.
            if (LiveDoorHud.AuthFailed(_session) && _failureLeft < 0f)
                _failureLeft = Application.isEditor ? 0f : 4f;
            var holdingFailure = LiveDoorHud.LocalizationFailed(_session) || (_failureLeft > 0f && !_failureRelease);
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
            _hud.HereRequested += OnHudHere;
            _hud.DoneRequested += OnHudDone;
            _hud.RetryRequested += OnHudRetry;
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

        void RequestLocalization()
        {
            _baseline?.NoteAttemptStarted(NowMs());
            if (!Application.isEditor)
                Debug.Log($"[IndoorAr] localization attempt={_session.Queries + 1} elapsedMs={(long)((Time.realtimeSinceStartup - _sessionStarted) * 1000)}");
            _localizer.Localize();
            LogBaseline();
        }

        void OnLocalizedObserved(float? confidence)
        {
            var wasPinned = _session.WasPinned;
            _session.OnLocalized(confidence);
            if (!wasPinned && _session.WasPinned) _baseline?.NotePoseAccepted(NowMs());
            LogBaseline();
        }

        void NoteArReady()
        {
            _baseline?.NoteArReady(NowMs());
            LogBaseline();
        }

        void LogBaseline()
        {
            if (Application.isEditor || _baseline == null) return;
            var line = _baseline.LogLine();
            if (line == _baselineLog) return;
            _baselineLog = line;
            Debug.Log(line);
        }

        long NowMs() => (long)(Time.realtimeSinceStartup * 1000f);

        void OnHudHere() => _session?.ImHere(CameraInMap());
        void OnHudDone() => _session?.ConfirmArrival();
        void OnHudRetry() => _session?.Retry();

        void UpdateHud()
        {
            if (_hud == null || _delivered) return;
            if (_session.State == IndoorArState.Done
                && !LiveDoorHud.LocalizationFailed(_session)
                && !LiveDoorHud.AuthFailed(_session)) return;
            var view = LiveDoorHud.Compose(_session, CameraInMap(), CameraForwardInMap(), _pinnedFor);
            if (_pin != null && _pin.gameObject.activeSelf) view.LabelAnchor = PinLabelAnchor(_pin);
            if (view.State == IndoorHudState.Scanning)
                view.ScanTooFast = SampleScanMotion();
            else
            {
                _scanPoseReady = false;
                _slowHoldLeft = 0f;
            }
            if (view.State == IndoorHudState.Straight)
            {
                _baseline?.NoteArrowShown(NowMs());
                LogBaseline();
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
            // The pin stays behind the arrival card.
            var pinned = (_session.State == IndoorArState.Pinned || _session.State == IndoorArState.Arrived) && !_session.Request.LocalizeOnly;
            if (pinned && _pin == null)
            {
                var door = _session.Request;
                _pin = CreatePin(_localizer.MapSpace, out _pinAmberMaterial, out _pinCoreMaterial);
                _pin.localPosition = new Vector3(door.position.x, door.position.y, door.position.z);
                _pin.localRotation = Quaternion.Euler(0f, door.headingDeg, 0f);
            }
            if (_pin != null)
            {
                _pin.gameObject.SetActive(pinned);
                if (pinned)
                {
                    var direction = _pin.position - arCamera.transform.position;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 0.0001f) _pin.rotation = Quaternion.LookRotation(direction);
                }
            }
        }

        Vector3 CameraInMap() => _localizer.MapSpace.InverseTransformPoint(arCamera.transform.position);

        Vector3 CameraForwardInMap() => _localizer.MapSpace.InverseTransformDirection(arCamera.transform.forward);

        /// <summary>World point just above the pin head, where the HUD floats the room label.</summary>
        public static Vector3 PinLabelAnchor(Transform pin) => pin.TransformPoint(0f, .45f, 0f);

        // Static so the editor HUD preview shows the same pin. The caller owns both materials.
        public static Transform CreatePin(Transform parent, out Material amber, out Material coreMaterial)
        {
            var root = new GameObject("Destination pin").transform;
            root.SetParent(parent, false);
            amber = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            amber.color = new Color32(245, 166, 35, 255);
            coreMaterial = new Material(amber) { color = Color.white };
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Amber pin";
            head.transform.SetParent(root, false);
            head.transform.localPosition = new Vector3(0, .23f, 0);
            head.transform.localScale = Vector3.one * .3f;
            head.GetComponent<Renderer>().sharedMaterial = amber;
            DestroyOwned(head.GetComponent<Collider>());
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "White core";
            core.transform.SetParent(root, false);
            core.transform.localPosition = new Vector3(0, .23f, -.14f);
            core.transform.localScale = Vector3.one * .12f;
            core.GetComponent<Renderer>().sharedMaterial = coreMaterial;
            DestroyOwned(core.GetComponent<Collider>());
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tip.name = "Pin stem";
            tip.transform.SetParent(root, false);
            tip.transform.localPosition = new Vector3(0, .08f, 0);
            tip.transform.localScale = new Vector3(.07f, .08f, .07f);
            tip.GetComponent<Renderer>().sharedMaterial = amber;
            DestroyOwned(tip.GetComponent<Collider>());
            return root;
        }

        void OnDestroy() => ReleaseSession();

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
            DestroyOwned(_pinAmberMaterial);
            DestroyOwned(_pinCoreMaterial);
            _pinAmberMaterial = null;
            _pinCoreMaterial = null;
            _session = null;
            _baseline = null;
            _baselineLog = null;
        }

        void Unhook()
        {
            if (_hud != null)
            {
                _hud.ExitRequested -= OnHudExit;
                _hud.HereRequested -= OnHudHere;
                _hud.DoneRequested -= OnHudDone;
                _hud.RetryRequested -= OnHudRetry;
            }
            if (_session == null || _localizer == null) return;
            _session.LocalizeRequested -= RequestLocalization;
            _localizer.Authenticated -= _session.OnAuthenticated;
            _localizer.AuthFailed -= _session.OnAuthFailed;
            _localizer.AttemptStarted -= _session.OnAttemptStarted;
            _localizer.Localized -= OnLocalizedObserved;
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
        public static bool BackPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
