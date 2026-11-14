using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace IndoorAr
{
    /// <summary>
    /// The first scene only routes an outdoor request to the outdoor scene. On a re-entry its ARSession would
    /// start ARKit and be torn down again within a second; iPhone logs show the outdoor scene's ARSession then
    /// never leaves Ready and no camera texture arrives (first entry of a process never hits this, it is still
    /// checking availability). So an outdoor request keeps this scene's session and camera idle.
    /// </summary>
    [DefaultExecutionOrder(-10000)] // before ARSession.OnEnable starts the session subsystem
    public sealed class IndoorArRouterHold : MonoBehaviour
    {
        public static bool Holds(string requestJson) =>
            IndoorArContract.TryParseRequest(requestJson, out var request) && request.OutdoorOnly;

        void Awake()
        {
            if (!Holds(IndoorArBridge.ReadRequestJson())) return;
            foreach (var session in FindObjectsByType<ARSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                session.enabled = false;
            foreach (var camera in FindObjectsByType<ARCameraManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                camera.enabled = false;
        }
    }
}
