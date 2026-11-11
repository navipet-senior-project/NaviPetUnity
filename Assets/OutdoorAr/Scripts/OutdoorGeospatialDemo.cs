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
        PermissionCallbacks _permissionCallbacks;
#endif
        ARGeospatialAnchor _anchor;
        GameObject _cue;

        void Update()
        {
            if (!_consented) return;

#if UNITY_ANDROID
            var camera = Permission.HasUserAuthorizedPermission(Permission.Camera)
                ? OutdoorArPermission.Granted
                : _cameraDenied ? OutdoorArPermission.Denied : OutdoorArPermission.Unknown;
            const string fineLocation = "android.permission.ACCESS_FINE_LOCATION";
            var location = Permission.HasUserAuthorizedPermission(fineLocation)
                ? OutdoorArPermission.Granted
                : _locationDenied ? OutdoorArPermission.Denied : OutdoorArPermission.Unknown;
            readiness.SetPermissions(camera, location);
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
                GUI.Label(new Rect(20, 20, Screen.width - 40, 90),
                    "Outdoor AR uses camera and precise location data with Google ARCore.");
                if (GUI.Button(new Rect(20, 115, 260, 60), "Agree and enable outdoor AR"))
                {
                    _consented = true;
                    readiness.SetOptIn(true, true);
                    session.enabled = true;
#if UNITY_ANDROID
                    _permissionCallbacks = new PermissionCallbacks();
                    _permissionCallbacks.PermissionDenied += OnPermissionDenied;
                    _permissionCallbacks.PermissionDeniedAndDontAskAgain += OnPermissionDenied;
                    if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                        Permission.RequestUserPermission(Permission.Camera, _permissionCallbacks);
                    if (!Permission.HasUserAuthorizedPermission("android.permission.ACCESS_FINE_LOCATION"))
                        Permission.RequestUserPermission("android.permission.ACCESS_FINE_LOCATION", _permissionCallbacks);
#endif
                }
                return;
            }

            GUI.Label(new Rect(20, 20, Screen.width - 40, 70), "Outdoor Geospatial: " + readiness.State);
        }

#if UNITY_ANDROID
        void OnPermissionDenied(string permission)
        {
            if (permission == Permission.Camera) _cameraDenied = true;
            if (permission == "android.permission.ACCESS_FINE_LOCATION") _locationDenied = true;
        }
#endif
    }
}
