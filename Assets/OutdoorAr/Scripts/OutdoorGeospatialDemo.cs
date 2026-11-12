using Google.XR.ARCoreExtensions;
using IndoorAr;
using UnityEngine;
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
    /// request's destination and answers through IndoorArBridge. Without one it is the
    /// standalone prototype: consent, then a marker at the phone's own pose.
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
        GameObject _cue;
        string _requestJson;
        IndoorArRequest _request;
        bool _finished;
        float _startedAt;
        float _waitingFor;
        double _distanceM = double.NaN;
        double _turnDeg;

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
            if (request.dataUseAccepted) Consent(); // the host already showed Google's notice
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
            }

            if (!readiness.CanPlaceGuidanceAnchor)
            {
                ClearAnchor();
                _distanceM = double.NaN;
                return;
            }

            var pose = readiness.CurrentPose;
            if (_request != null)
            {
                var to = _request.destination;
                _distanceM = OutdoorGeospatialGate.DistanceM(pose.Latitude, pose.Longitude, to.latitude, to.longitude);
                // EUN: x is east and z is north.
                var forward = pose.EunRotation * Vector3.forward;
                _turnDeg = OutdoorGeospatialGate.TurnDeg(
                    OutdoorGeospatialGate.BearingDeg(pose.Latitude, pose.Longitude, to.latitude, to.longitude),
                    Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
            }

            if (_anchor == null)
            {
                _anchor = _request == null
                    ? anchors.AddAnchor(pose.Latitude, pose.Longitude, pose.Altitude, pose.EunRotation)
                    // shortcut: the marker floats at the phone's altitude, use a terrain anchor if slopes hide it.
                    : anchors.AddAnchor(_request.destination.latitude, _request.destination.longitude,
                        pose.Altitude, Quaternion.identity);
                if (_anchor == null) return;
                _cue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _cue.name = "Geospatial tracking cue";
                _cue.transform.SetParent(_anchor.transform, false);
                _cue.transform.localPosition = Vector3.up * 1.5f;
                _cue.transform.localScale = Vector3.one * (_request == null ? 0.3f : 1f);
                _cue.GetComponent<Renderer>().material.color = Color.green;
            }
        }

        void Consent()
        {
            _consented = true;
            readiness.SetOptIn(true, true);
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
#endif
        }

        // The host may only pause the player, so the camera and the Geospatial session stop here.
        void Finish(string resultJson)
        {
            _finished = true;
            _requestJson = null;
            ClearAnchor();
            session.enabled = false;
            IndoorArBridge.Deliver(resultJson);
        }

        void ClearAnchor()
        {
            if (_anchor != null) Destroy(_anchor.gameObject);
            _anchor = null;
            if (_cue != null) Destroy(_cue);
            _cue = null;
        }

        long DurationMs() => (long)((Time.realtimeSinceStartup - _startedAt) * 1000f);

        void OnGUI()
        {
            if (_finished) return;
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
                GUILayout.Box(_request == null ? "Outdoor Geospatial: " + readiness.State : GuidanceText());
                if (readiness.State == OutdoorArState.VpsCheckFailed
                    && GUILayout.Button("Retry VPS check", GUILayout.Height(52)))
                    readiness.RetryVpsCheck();
            }
            if (_request != null)
            {
                GUILayout.FlexibleSpace();
                var arrived = _distanceM <= OutdoorGeospatialGate.ArrivalRadiusM;
                if (GUILayout.Button(arrived ? "Done" : "Exit outdoor AR", GUILayout.Height(52)))
                    Finish(arrived
                        ? IndoorArContract.Arrived((float)_distanceM, 0, DurationMs())
                        : IndoorArContract.Cancelled(0, DurationMs()));
            }
            GUILayout.EndArea();
        }

        string GuidanceText()
        {
            var name = string.IsNullOrEmpty(_request.destination.name) ? "Your destination" : _request.destination.name;
            if (!double.IsNaN(_distanceM))
                return _distanceM <= OutdoorGeospatialGate.ArrivalRadiusM
                    ? name + " is here."
                    : $"{name} is {_distanceM:0} m {OutdoorGeospatialGate.TurnHint(_turnDeg)}.";
            switch (readiness.State)
            {
                case OutdoorArState.CameraPermissionRequired:
                case OutdoorArState.LocationPermissionRequired:
                    return "Allow camera and precise location to use outdoor AR.";
                case OutdoorArState.VpsCheckFailed:
                    return "Could not check outdoor AR coverage here.";
                case OutdoorArState.VpsUnavailable:
                    return "Outdoor AR has no coverage at this spot. Keep following your route.";
                default:
                    return "Finding your position. Point the camera at the buildings around you.";
            }
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
