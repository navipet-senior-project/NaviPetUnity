using System;
using UnityEngine;
using UnityEngine.UI;

namespace IndoorAr
{
    public enum IndoorHudState { Scanning, NotFound, Localized, Straight, Turn, WrongWay, Arrived }

    // A route provider supplies this view; coordinates are in its own local map frame.
    public sealed class IndoorHudView
    {
        public IndoorHudState State;
        public string Destination;
        public int Floor;
        public float Distance;
        public Vector3[] Waypoints;
        public float ArrowYaw;
        public bool ScanTooFast;
        public string TitleOverride;
        public string DetailOverride;
    }

    public static class MockIndoorRoute
    {
        public static IndoorHudView Snapshot(IndoorHudState state, int floor) => new IndoorHudView
        {
            State = state, Floor = floor, Destination = floor == 2 ? "VEC 220" : "VEC 119",
            Distance = state == IndoorHudState.Turn ? 30 : 12,
            // Illustrative only; these coordinates are not surveyed VEC geometry.
            Waypoints = new[] { new Vector3(0, 0, 1), new Vector3(0, 0, 5), new Vector3(3, 0, 5) }
        };
    }

    /// <summary>Renders one supplied route snapshot. Does not perform localization or routing.</summary>
    public sealed class IndoorHud : MonoBehaviour
    {
        static readonly Color Blue = new Color32(28, 119, 237, 255);
        static readonly Color Orange = new Color32(232, 148, 43, 255);
        static readonly Color Ink = new Color32(19, 31, 51, 255);
        static readonly Color Muted = new Color32(94, 108, 128, 255);
        static readonly Color White = Color.white;
        Transform _root;
        Sprite _round;
        Sprite _arrowSprite;
        Font _font;
        Text _detail;
        string _chrome;
        Vector3[] _drawn;
        RectTransform _arrow;
        RectTransform _look;
        RectTransform _slow;
        const float RedrawMeters = .25f;
        public event Action ExitRequested;

        public void Initialize(Camera camera)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _round = MakeRoundedSprite();
            var canvasObject = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.5f;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasObject.transform;
        }

        public void Show(IndoorHudView view)
        {
            if (_root == null) throw new InvalidOperationException("Initialize HUD first");
            var accent = view.State == IndoorHudState.WrongWay ? Orange : Blue;
            var chrome = view.State + "|" + view.Destination + "|" + view.Floor + "|" + view.TitleOverride;
            if (_chrome == chrome)
            {
                if (_detail != null && (view.State == IndoorHudState.Straight || view.State == IndoorHudState.Turn))
                    _detail.text = DetailText(view);
                RefreshRoute(view, accent);
                return;
            }
            _arrow = null;
            _look = null;
            _slow = null;
            while (_root.childCount > 0)
            {
                var child = _root.GetChild(0);
                child.SetParent(null);
                RemoveObject(child.gameObject);
            }
            _drawn = null;
            var state = view.State;

            Panel(_root, "Destination", new Vector2(0, -110), new Vector2(980, 112), White, new Vector2(.5f, 1));
            Label(_root, "DestinationLabel", "TO  " + view.Destination + "  ·  FLOOR " + view.Floor, new Vector2(-80, -110), new Vector2(690, 90), 32, Ink, TextAnchor.MiddleLeft, new Vector2(.5f, 1), FontStyle.Bold);
            var exit = Panel(_root, "Exit", new Vector2(405, -110), new Vector2(82, 82), new Color32(237, 242, 249, 255), new Vector2(.5f, 1));
            Label(exit, "ExitLabel", "×", Vector2.zero, new Vector2(80, 80), 46, Ink, TextAnchor.MiddleCenter, new Vector2(.5f, .5f));
            var button = exit.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => ExitRequested?.Invoke());

            string title, icon;
            switch (state)
            {
                case IndoorHudState.Scanning: title = "Scanning area..."; icon = "◌"; break;
                case IndoorHudState.NotFound: title = "Could not find your location"; icon = "!"; break;
                case IndoorHudState.Localized: title = "Location found"; icon = "✓"; break;
                case IndoorHudState.Straight: title = "Continue ahead"; icon = "●"; break;
                case IndoorHudState.Turn: title = "Turn right"; icon = "↱"; break;
                case IndoorHudState.WrongWay: title = "Recalculating route"; icon = "↻"; break;
                default: title = "You have arrived"; icon = "✓"; break;
            }
            if (!string.IsNullOrEmpty(view.TitleOverride)) title = view.TitleOverride;
            var card = Panel(_root, "Instruction", new Vector2(0, -280), new Vector2(980, 190), White, new Vector2(.5f, 1));
            Panel(card, "IconBackground", new Vector2(-390, 0), new Vector2(120, 120), accent, new Vector2(.5f, .5f));
            Label(card, "Icon", icon, new Vector2(-390, 0), new Vector2(120, 120), 67, White, TextAnchor.MiddleCenter, new Vector2(.5f, .5f), FontStyle.Bold);
            Label(card, "Title", title, new Vector2(65, 32), new Vector2(680, 65), 43, Ink, TextAnchor.MiddleLeft, new Vector2(.5f, .5f), FontStyle.Bold);
            Label(card, "Detail", DetailText(view), new Vector2(65, -37), new Vector2(680, 64), 28, Muted, TextAnchor.MiddleLeft, new Vector2(.5f, .5f));
            _detail = card.Find("Detail").GetComponent<Text>();
            if (state == IndoorHudState.Scanning) _slow = SlowPill();
            _chrome = chrome;
            RefreshRoute(view, accent);
        }

        static string DetailText(IndoorHudView view)
        {
            if (!string.IsNullOrEmpty(view.DetailOverride)) return view.DetailOverride;
            switch (view.State)
            {
                case IndoorHudState.Scanning: return "Slowly move your phone across the hallway.";
                case IndoorHudState.NotFound: return "Step into a hallway and scan the walls and signs.";
                case IndoorHudState.Localized: return "You're ready to navigate.";
                case IndoorHudState.Straight: return "In " + view.Distance.ToString("0") + " m";
                case IndoorHudState.Turn: return "In " + view.Distance.ToString("0") + " m";
                case IndoorHudState.WrongWay: return "Finding a new way to your destination.";
                default: return view.Destination + " is here.";
            }
        }

        bool RouteUnchanged(Vector3[] points)
        {
            if (_drawn == null || points == null || _drawn.Length != points.Length) return false;
            for (int i = 0; i < points.Length; i++)
                if ((points[i] - _drawn[i]).sqrMagnitude >= RedrawMeters * RedrawMeters) return false;
            return true;
        }

        void RefreshRoute(IndoorHudView view, Color accent)
        {
            UpdateCue(view);
            if (RouteUnchanged(view.Waypoints)) return;
            _drawn = view.Waypoints == null ? null : (Vector3[])view.Waypoints.Clone();
            var existing = _root.Find("Mini map");
            if (existing != null)
            {
                existing.SetParent(null);
                RemoveObject(existing.gameObject);
            }
            var state = view.State;
            bool navigating = state == IndoorHudState.Localized || state == IndoorHudState.Straight || state == IndoorHudState.Turn || state == IndoorHudState.WrongWay || state == IndoorHudState.Arrived;
            if (navigating) DrawMap(view, accent);
        }

        void UpdateCue(IndoorHudView view)
        {
            bool guide = view.State == IndoorHudState.Straight || view.State == IndoorHudState.Turn;
            if (guide) EnsureGuide();
            if (_arrow != null)
            {
                _arrow.gameObject.SetActive(guide);
                if (guide)
                    _arrow.localEulerAngles = new Vector3(0f, 0f, GuidanceCue.ArrowScreenRotation(view.ArrowYaw));
            }
            if (_look != null)
                _look.gameObject.SetActive(guide && !GuidanceCue.FacingWaypoint(view.ArrowYaw));
            if (_slow != null)
                _slow.gameObject.SetActive(view.State == IndoorHudState.Scanning && view.ScanTooFast);
        }

        void EnsureGuide()
        {
            if (_arrow != null) return;
            var obj = new GameObject("Guide arrow", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(_root, false);
            _arrow = obj.GetComponent<RectTransform>();
            _arrow.anchorMin = _arrow.anchorMax = new Vector2(.5f, .5f);
            _arrow.anchoredPosition = new Vector2(0f, 160f);
            _arrow.sizeDelta = new Vector2(280f, 280f);
            var image = obj.GetComponent<Image>();
            image.sprite = ArrowSprite();
            image.color = Blue;
            image.raycastTarget = false;
            _look = Panel(_root, "Look this way", new Vector2(0f, -90f), new Vector2(560f, 100f), White, new Vector2(.5f, .5f));
            Label(_look, "Look label", "Look this way", Vector2.zero, new Vector2(500f, 80f), 40, Ink, TextAnchor.MiddleCenter, new Vector2(.5f, .5f), FontStyle.Bold);
            _look.gameObject.SetActive(false);
        }

        RectTransform SlowPill()
        {
            var pill = Panel(_root, "Scan slowly", new Vector2(0f, -40f), new Vector2(460f, 96f), Ink, new Vector2(.5f, .5f));
            Label(pill, "Scan label", "Scan slowly", Vector2.zero, new Vector2(420f, 80f), 40, White, TextAnchor.MiddleCenter, new Vector2(.5f, .5f), FontStyle.Bold);
            pill.gameObject.SetActive(false);
            return pill;
        }

        Sprite ArrowSprite()
        {
            if (_arrowSprite != null) return _arrowSprite;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float cx = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float ny = y / (float)(size - 1);
                float dx = Mathf.Abs(x - cx);
                bool head = ny >= 0.38f && dx <= (1f - (ny - 0.38f) / 0.62f) * (cx * 0.95f);
                bool shaft = ny < 0.46f && dx <= cx * 0.26f;
                texture.SetPixel(x, y, head || shaft ? Color.white : Color.clear);
            }
            texture.Apply();
            _arrowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
            return _arrowSprite;
        }

        void DrawMap(IndoorHudView view, Color accent)
        {
            var map = Panel(_root, "Mini map", new Vector2(-330, 225), new Vector2(300, 300), new Color32(19, 31, 51, 242), new Vector2(.5f, 0));
            Label(map, "Floor", "FLOOR " + view.Floor, new Vector2(0, 110), new Vector2(240, 40), 22, White, TextAnchor.MiddleLeft, new Vector2(.5f, .5f), FontStyle.Bold);
            var points = view.Waypoints;
            if (points == null || points.Length < 2) return;
            float minX = points[0].x, maxX = minX, minZ = points[0].z, maxZ = minZ;
            foreach (var point in points)
            {
                minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.z); maxZ = Mathf.Max(maxZ, point.z);
            }
            float scale = 175f / Mathf.Max(1f, maxX - minX, maxZ - minZ);
            Vector2 Project(Vector3 p) => new Vector2((p.x - (minX + maxX) / 2) * scale, (p.z - (minZ + maxZ) / 2) * scale - 15);
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = Project(points[i]), b = Project(points[i + 1]), delta = b - a;
                var road = Panel(map, "Hallway", (a + b) / 2, new Vector2(delta.magnitude + 30, 35), new Color32(63, 77, 95, 255), new Vector2(.5f, .5f));
                road.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                var route = Panel(map, "Route", (a + b) / 2, new Vector2(delta.magnitude, 13), accent, new Vector2(.5f, .5f));
                route.localRotation = road.localRotation;
            }
            var position = Project(points[0]);
            Panel(map, "Position ring", position, new Vector2(40, 40), White, new Vector2(.5f, .5f));
            Panel(map, "Position", position, new Vector2(25, 25), accent, new Vector2(.5f, .5f));
        }

        RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color, Vector2 anchor)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false); var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = obj.GetComponent<Image>(); image.sprite = _round; image.type = Image.Type.Sliced; image.color = color;
            return rect;
        }
        void Label(Transform parent, string name, string text, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment, Vector2 anchor, FontStyle style = FontStyle.Normal)
        {
            _font.RequestCharactersInTexture(text, fontSize, style);
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            var label = obj.GetComponent<Text>(); label.font = _font; label.text = text; label.fontSize = fontSize; label.color = color; label.alignment = alignment; label.fontStyle = style; label.resizeTextForBestFit = false; label.raycastTarget = false;
        }
        static Sprite MakeRoundedSprite()
        {
            const int size = 64, radius = 20; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, 0, x - (size - 1 - radius));
                float dy = Mathf.Max(radius - y, 0, y - (size - 1 - radius));
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius + .5f - Mathf.Sqrt(dx * dx + dy * dy))));
            }
            texture.Apply(); return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
        void OnDestroy()
        {
            if (_round != null) { RemoveObject(_round.texture); RemoveObject(_round); }
            if (_arrowSprite != null) { RemoveObject(_arrowSprite.texture); RemoveObject(_arrowSprite); }
        }
        static void RemoveObject(UnityEngine.Object obj) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
