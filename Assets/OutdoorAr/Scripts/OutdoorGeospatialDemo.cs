using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace OutdoorAr
{
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

#if UNITY_ANDROID
        void Start()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                _requiresCoarseLocation = version.GetStatic<int>("SDK_INT") >= 31;
        }
#endif

        void Update()
        {
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

            if (!readiness.CanPlaceGuidanceAnchor)
            {
                if (_anchor != null) Destroy(_anchor.gameObject);
                _anchor = null;
                if (_cue != null) Destroy(_cue);
                _cue = null;
                return;
            }

            if (_anchor == null)
            {
                var pose = readiness.CurrentPose;
                _anchor = anchors.AddAnchor(pose.Latitude, pose.Longitude, pose.Altitude, pose.EunRotation);
                if (_anchor == null) return;
                _cue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _cue.name = "Geospatial tracking cue";
                _cue.transform.SetParent(_anchor.transform, false);
                _cue.transform.localPosition = Vector3.up * 1.5f;
                _cue.transform.localScale = Vector3.one * 0.3f;
                _cue.GetComponent<Renderer>().material.color = Color.green;
            }
        }

        void OnGUI()
        {
            if (!_consented)
            {
                GUI.Label(new Rect(20, 20, Screen.width - 40, 170),
                    "This app runs on Google Play Services for AR (ARCore), provided by Google LLC and governed by the Google Privacy Policy. To power this Geospatial session, Google will process sensor data (e.g., camera and location).");
                if (GUI.Button(new Rect(20, 200, Screen.width - 40, 50), "Learn how ARCore handles data"))
                    Application.OpenURL("https://support.google.com/ar?p=how-google-play-services-for-ar-handles-your-data");
                if (GUI.Button(new Rect(20, 255, Screen.width - 40, 50), "Google Privacy Policy"))
                    Application.OpenURL("https://policies.google.com/privacy");
                if (GUI.Button(new Rect(20, 310, Screen.width - 40, 60), "Agree and enable outdoor AR"))
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
                return;
            }

            GUI.Label(new Rect(20, 20, Screen.width - 40, 70), "Outdoor Geospatial: " + readiness.State);
            if (readiness.State == OutdoorArState.VpsCheckFailed
                && GUI.Button(new Rect(20, 95, Screen.width - 40, 60), "Retry VPS check"))
                readiness.RetryVpsCheck();
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
