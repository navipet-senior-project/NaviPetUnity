using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace IndoorAr
{
    // Preview only: no position or destination here represents a live user.
    public sealed class IndoorHudMockPreview : MonoBehaviour
    {
        public Camera previewCamera;
        public Transform pin;
        public IndoorHudPreview state = IndoorHudPreview.Scanning;
        [Range(1, 2)] public int floor = 1;
        IndoorHud _hud;
        IndoorHudPreview _previousState;
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
            var view = MockIndoorRoute.Snapshot(state, floor, pin == null ? (Vector3?)null : IndoorArRunner.PinLabelAnchor(pin));
            if (pin != null) pin.gameObject.SetActive(view.LabelAnchor.HasValue);
            _hud.Show(view);
            _previousState = state; _previousFloor = floor;
        }
    }
}
