using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace IndoorAr
{
    // Preview only: no position, route or destination here represents a live user.
    public sealed class IndoorHudMockPreview : MonoBehaviour
    {
        public Camera previewCamera;
        public IndoorHudState state = IndoorHudState.Scanning;
        [Range(1, 2)] public int floor = 1;
        IndoorHud _hud;
        IndoorHudState _previousState;
        int _previousFloor;

        void Start()
        {
            _hud = gameObject.AddComponent<IndoorHud>();
            _hud.Initialize(previewCamera);
            _hud.ExitRequested += () => Destroy(gameObject);
            if (EventSystem.current == null)
                new GameObject("Preview EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Refresh();
        }
        void Update()
        {
            if (state != _previousState || floor != _previousFloor) Refresh();
        }
        void Refresh()
        {
            _hud.Show(MockIndoorRoute.Snapshot(state, floor));
            _previousState = state; _previousFloor = floor;
        }
    }
}
