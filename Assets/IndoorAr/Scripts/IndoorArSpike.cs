using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IndoorAr
{
    /// <summary>
    /// #41 transport spike: shows the request and returns a fixed result. No
    /// localization; the real session replaces this in build step 4.
    /// </summary>
    public class IndoorArSpike : MonoBehaviour
    {
        // Test value, not a measurement: the spike has no localization.
        const float TestDistanceM = 0.5f;

        IndoorArRequest _request;
        float _startedAt;
        bool _done;
        GUIStyle _label;
        GUIStyle _button;

        void Start()
        {
            _startedAt = Time.realtimeSinceStartup;
            if (!IndoorArContract.TryParseRequest(IndoorArBridge.ReadRequestJson(), out _request))
                Finish(IndoorArContract.Error(IndoorArContract.BadRequest, 0, ElapsedMs()));
        }

        void Update()
        {
            if (!_done && BackPressed())
                Finish(IndoorArContract.Cancelled(0, ElapsedMs()));
        }

        void OnGUI()
        {
            if (_done || _request == null) return;
            var fontSize = Screen.height / 32;
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleCenter };
            _button ??= new GUIStyle(GUI.skin.button) { fontSize = fontSize };

            var width = Screen.width * 0.8f;
            var height = Screen.height * 0.08f;
            var x = (Screen.width - width) / 2f;
            GUI.Label(new Rect(x, Screen.height * 0.1f, width, height),
                $"{_request.doorKey} · floor {_request.floor}", _label);
            if (GUI.Button(new Rect(x, Screen.height * 0.62f, width, height), "Send arrived (0.5 m test)", _button))
                Finish(IndoorArContract.Arrived(TestDistanceM, 0, ElapsedMs()));
            if (GUI.Button(new Rect(x, Screen.height * 0.74f, width, height), "Cancel", _button))
                Finish(IndoorArContract.Cancelled(0, ElapsedMs()));
        }

        long ElapsedMs() => (long)((Time.realtimeSinceStartup - _startedAt) * 1000f);

        void Finish(string resultJson)
        {
            if (_done) return;
            _done = true;
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
