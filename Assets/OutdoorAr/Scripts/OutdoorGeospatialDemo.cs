using Google.XR.ARCoreExtensions;
using IndoorAr;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace OutdoorAr
{
    /// <summary>
    /// Runs the outdoor scene. With a mode "outdoor" request from the host it guides to the
    /// request's destination and answers through IndoorArBridge, drawn by the indoor HUD. With
    /// a route in the request the arrow follows the route, without one it points at the destination.
    /// Without a request it is the standalone prototype: consent, then a marker at the phone's own pose.
    /// </summary>
    public sealed class OutdoorGeospatialDemo : MonoBehaviour
    {
        [SerializeField] OutdoorGeospatialReadiness readiness;
        [SerializeField] ARSession session;
        [SerializeField] ARAnchorManager anchors;
        bool _consented;
#if UNITY_ANDROID
        bool _cameraDenied;
        bool _locationDenied;
        bool _requiresCoarseLocation;
        PermissionCallbacks _permissionCallbacks;
#endif
        ARGeospatialAnchor _anchor;
        ResolveAnchorOnTerrainPromise _terrainPromise;
        GameObject _cue;
        string _requestJson;
        IndoorArRequest _request;
        bool _finished;
        float _startedAt;
        float _waitingFor;
        double _distanceM = double.NaN;
        double _turnDeg;
        IndoorHud _hud;
        bool _arrived;
        bool _noPin;
        bool _offRoute;
        int _segment;
        double _routeRemainingM = double.NaN;
        float _readyFor;
        string _loggedLine;

        void Start()
        {
#if UNITY_ANDROID
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                _requiresCoarseLocation = version.GetStatic<int>("SDK_INT") >= 31;
#endif
            _requestJson = IndoorArBridge.ReadRequestJson();
            if (!IndoorArContract.TryParseRequest(_requestJson, out var request) || !request.OutdoorOnly) return;
            _request = request;
            _startedAt = Time.realtimeSinceStartup;
            ArCameraDiagnostics.Attach();
        }

        void Update()
        {
            // A host that only unpauses the player sends its next request here.
            // The first scene routes every request. The editor stand-in is an indoor request.
            var json = Application.isEditor ? null : IndoorArBridge.ReadRequestJson();
            if (json != null && json != _requestJson)
            {
                _requestJson = json;
                SceneManager.LoadScene(0);
                return;
            }
            if (_finished) return;
            if (_request != null && IndoorArRunner.BackPressed())
            {
                Finish(IndoorArContract.Cancelled(0, DurationMs()));
                return;
            }
            if (!_consented) return;

#if UNITY_ANDROID
            var camera = Permission.HasUserAuthorizedPermission(Permission.Camera)
                ? OutdoorArPermission.Granted
                : _cameraDenied ? OutdoorArPermission.Denied : OutdoorArPermission.Unknown;
            const string fineLocation = "android.permission.ACCESS_FINE_LOCATION";
            const string coarseLocation = "android.permission.ACCESS_COARSE_LOCATION";
            var fineGranted = Permission.HasUserAuthorizedPermission(fineLocation);
            var coarseGranted = Permission.HasUserAuthorizedPermission(coarseLocation);
            var location = OutdoorGeospatialGate.LocationPermissionsGranted(
                    fineGranted, coarseGranted, _requiresCoarseLocation)
                ? OutdoorArPermission.Granted
                : _locationDenied ? OutdoorArPermission.Denied : OutdoorArPermission.Unknown;
            readiness.SetPermissions(camera, location);
            if (camera == OutdoorArPermission.Granted && location == OutdoorArPermission.Granted)
                session.enabled = true;
#elif UNITY_IOS
            // The host asks for the camera before it starts Unity, and ARKit asks again if it has to.
            var location = OutdoorGeospatialGate.IosLocationPermission(Input.location.status);
            readiness.SetPermissions(OutdoorArPermission.Granted, location);
            if (location == OutdoorArPermission.Granted) session.enabled = true;
#else
            readiness.SetPermissions(OutdoorArPermission.Unknown, OutdoorArPermission.Unknown);
#endif

            if (readiness.State == OutdoorArState.VpsCheckRequired)
                readiness.CheckVpsAtCurrentPosition();

            if (_request != null)
            {
                if (OutdoorGeospatialGate.IsUnavailable(readiness.State))
                {
                    Finish(IndoorArContract.Unavailable(0, DurationMs()));
                    return;
                }
                // Counted only while the session runs, so a permission dialog does not use it up.
                _waitingFor = session.enabled && !readiness.CanPlaceGuidanceAnchor ? _waitingFor + Time.deltaTime : 0f;
                if (_waitingFor > OutdoorGeospatialGate.ReadyTimeoutS)
                {
                    Finish(IndoorArContract.LocalizationFailed(0, DurationMs()));
                    return;
                }
                if (_terrainPromise != null && _terrainPromise.State != PromiseState.Pending)
                {
                    // The HUD arrow needs no anchor, so a failed terrain anchor only drops the pin label.
                    if (_terrainPromise.State == PromiseState.Done
                        && _terrainPromise.Result.TerrainAnchorState == TerrainAnchorState.Success
                        && _terrainPromise.Result.Anchor != null)
                    {
                        _anchor = _terrainPromise.Result.Anchor;
                        CreateCue();
                    }
                    else _noPin = true;
                    _terrainPromise = null;
                }
            }

            if (!readiness.CanPlaceGuidanceAnchor)
            {
                ClearAnchor();
                _distanceM = double.NaN;
                _readyFor = 0f;
                UpdateHud(false);
                return;
            }
            _readyFor += Time.unscaledDeltaTime;

            var pose = readiness.CurrentPose;
            if (_request != null)
            {
                var to = _request.destination;
                _distanceM = OutdoorGeospatialGate.DistanceM(pose.Latitude, pose.Longitude, to.latitude, to.longitude);
                _arrived |= _distanceM <= OutdoorGeospatialGate.ArrivalRadiusM;
                var bearing = OutdoorGeospatialGate.BearingDeg(pose.Latitude, pose.Longitude, to.latitude, to.longitude);
                if (_request.HasRoute)
                {
                    var route = OutdoorRouteGuide.Follow(_request.route.waypoints, pose.Latitude, pose.Longitude, _segment);
                    _segment = route.Segment;
                    _routeRemainingM = route.RemainingM;
                    _offRoute = route.IsOffRoute;
                    bearing = route.TargetBearingDeg;
                }
                // EUN: x is east and z is north.
                var forward = pose.EunRotation * Vector3.forward;
                _turnDeg = OutdoorGeospatialGate.TurnDeg(bearing, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
                UpdateHud(true);
            }

            if (_anchor == null)
            {
                if (_request == null)
                {
                    _anchor = anchors.AddAnchor(pose.Latitude, pose.Longitude, pose.Altitude, pose.EunRotation);
                    if (_anchor != null) CreateCue();
                }
                else if (_terrainPromise == null && !_noPin)
                    _terrainPromise = anchors.ResolveAnchorOnTerrainAsync(_request.destination.latitude,
                        _request.destination.longitude, 1.5, Quaternion.identity);
            }
        }

        void CreateCue()
        {
            _cue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cue.name = "Geospatial tracking cue";
            _cue.transform.SetParent(_anchor.transform, false);
            _cue.transform.localPosition = Vector3.up * 1.5f;
            _cue.transform.localScale = Vector3.one * (_request == null ? 0.3f : 1f);
            _cue.GetComponent<Renderer>().material.color = Color.green;
        }

        void Consent()
        {
            _consented = true;
            readiness.SetOptIn(true, true);
            if (_request != null) AttachHud();
#if UNITY_ANDROID
            _permissionCallbacks = new PermissionCallbacks();
            _permissionCallbacks.PermissionDenied += OnPermissionDenied;
            _permissionCallbacks.PermissionDeniedAndDontAskAgain += OnPermissionDenied;
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                Permission.RequestUserPermission(Permission.Camera, _permissionCallbacks);
            const string fineLocation = "android.permission.ACCESS_FINE_LOCATION";
            const string coarseLocation = "android.permission.ACCESS_COARSE_LOCATION";
            if (_requiresCoarseLocation)
            {
                if (!Permission.HasUserAuthorizedPermission(fineLocation)
                    || !Permission.HasUserAuthorizedPermission(coarseLocation))
                    Permission.RequestUserPermissions(
                        new[] { coarseLocation, fineLocation }, _permissionCallbacks);
            }
            else if (!Permission.HasUserAuthorizedPermission(fineLocation))
            {
                Permission.RequestUserPermission(fineLocation, _permissionCallbacks);
            }
#elif UNITY_IOS
            Input.location.Start(); // shows the location prompt
#endif
        }

        // The host may only pause the player, so the camera and the Geospatial session stop here.
        void Finish(string resultJson)
        {
            _finished = true;
            _requestJson = null;
            if (_hud != null) Destroy(_hud.gameObject);
            ClearAnchor();
            session.enabled = false;
#if UNITY_IOS
            Input.location.Stop();
#endif
            IndoorArBridge.Deliver(resultJson);
        }

        void ClearAnchor()
        {
            if (_anchor != null) Destroy(_anchor.gameObject);
            _anchor = null;
            if (_cue != null) Destroy(_cue);
            _cue = null;
        }

        void AttachHud()
        {
            _hud = new GameObject("Outdoor HUD").AddComponent<IndoorHud>();
            _hud.transform.SetParent(transform, false);
            _hud.Initialize(Camera.main);
            _hud.ExitRequested += () => Finish(IndoorArContract.Cancelled(0, DurationMs()));
            _hud.HereRequested += OnArrivedTapped;
            _hud.DoneRequested += OnArrivedTapped;
            _hud.RetryRequested += readiness.RetryVpsCheck;
            if (EventSystem.current == null)
                new GameObject("HUD EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule))
                    .transform.SetParent(transform, false);
        }

        void OnArrivedTapped()
        {
            if (!double.IsNaN(_distanceM))
                Finish(IndoorArContract.Arrived((float)_distanceM, 0, DurationMs()));
        }

        void UpdateHud(bool hasPose)
        {
            if (_hud == null || _finished) return;
            var anchor = _cue == null ? (Vector3?)null : _cue.transform.position + Vector3.up;
            _hud.Show(OutdoorGuidanceHud.Compose(_request, readiness.State, hasPose, _arrived, _distanceM,
                _routeRemainingM, _turnDeg, _offRoute, _readyFor, anchor));
            LogState();
        }

        // One fixed-label line per change, for the re-entry diagnosis. No location, no key.
        void LogState()
        {
            if (Application.isEditor) return;
            var line = $"[OutdoorAr] state={readiness.State} sessionEnabled={session.enabled} route={_request.HasRoute} offRoute={_offRoute} arrived={_arrived}";
            if (line == _loggedLine) return;
            _loggedLine = line;
            Debug.Log(line);
        }

        long DurationMs() => (long)((Time.realtimeSinceStartup - _startedAt) * 1000f);

        void OnGUI()
        {
            // A hosted session shows the indoor HUD once the user has agreed.
            if (_finished || (_request != null && _consented)) return;
            // shortcut: IMGUI scaled to a 400-unit-wide canvas, replace with a designed HUD before release.
            var scale = Screen.width / 400f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.skin.box.wordWrap = true;
            var safe = Screen.safeArea;
            GUILayout.BeginArea(new Rect(safe.x / scale + 12, (Screen.height - safe.yMax) / scale + 12,
                safe.width / scale - 24, safe.height / scale - 24));
            if (!_consented)
            {
                GUILayout.Label("This app runs on Google Play Services for AR (ARCore), provided by Google LLC and governed by the Google Privacy Policy. To power this Geospatial session, Google will process sensor data (e.g., camera and location).");
                if (GUILayout.Button("Learn how ARCore handles data", GUILayout.Height(44)))
                    Application.OpenURL("https://support.google.com/ar?p=how-google-play-services-for-ar-handles-your-data");
                if (GUILayout.Button("Google Privacy Policy", GUILayout.Height(44)))
                    Application.OpenURL("https://policies.google.com/privacy");
                if (GUILayout.Button("Agree and enable outdoor AR", GUILayout.Height(52)))
                    Consent();
            }
            else
            {
                GUILayout.Box("Outdoor Geospatial: " + readiness.State);
                if (readiness.State == OutdoorArState.VpsCheckFailed
                    && GUILayout.Button("Retry VPS check", GUILayout.Height(52)))
                    readiness.RetryVpsCheck();
            }
            // Before consent the HUD is not up yet, so hosted sessions keep a way out here.
            if (_request != null)
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Exit outdoor AR", GUILayout.Height(52)))
                    Finish(IndoorArContract.Cancelled(0, DurationMs()));
            }
            GUILayout.EndArea();
        }

#if UNITY_ANDROID
        void OnPermissionDenied(string permission)
        {
            if (permission == Permission.Camera) _cameraDenied = true;
            if (permission == "android.permission.ACCESS_FINE_LOCATION"
                || permission == "android.permission.ACCESS_COARSE_LOCATION") _locationDenied = true;
        }
#endif
    }
}
