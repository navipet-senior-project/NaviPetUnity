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
        GameObject _trail;
        Material _trailMaterial;
        Sprite _round;
        Font _font;
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
            while (_root.childCount > 0)
            {
                var child = _root.GetChild(0);
                child.SetParent(null);
                RemoveObject(child.gameObject);
            }
            ClearTrail();
            var state = view.State;
            bool navigating = state == IndoorHudState.Localized || state == IndoorHudState.Straight || state == IndoorHudState.Turn || state == IndoorHudState.WrongWay || state == IndoorHudState.Arrived;
            bool warning = state == IndoorHudState.WrongWay;
            Color accent = warning ? Orange : Blue;

            Panel(_root, "Destination", new Vector2(0, -110), new Vector2(980, 112), White, new Vector2(.5f, 1));
            Label(_root, "DestinationLabel", "TO  " + view.Destination + "  ·  FLOOR " + view.Floor, new Vector2(-80, -110), new Vector2(690, 90), 32, Ink, TextAnchor.MiddleLeft, new Vector2(.5f, 1), FontStyle.Bold);
            var exit = Panel(_root, "Exit", new Vector2(405, -110), new Vector2(82, 82), new Color32(237, 242, 249, 255), new Vector2(.5f, 1));
            Label(exit, "ExitLabel", "×", Vector2.zero, new Vector2(80, 80), 46, Ink, TextAnchor.MiddleCenter, new Vector2(.5f, .5f));
            var button = exit.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => ExitRequested?.Invoke());

            string title, detail, icon;
            switch (state)
            {
                case IndoorHudState.Scanning: title = "Scanning area..."; detail = "Slowly move your phone across the hallway."; icon = "◌"; break;
                case IndoorHudState.NotFound: title = "Could not find your location"; detail = "Step into a hallway and scan the walls and signs."; icon = "!"; break;
                case IndoorHudState.Localized: title = "Location found"; detail = "You're ready to navigate."; icon = "✓"; break;
                case IndoorHudState.Straight: title = "Continue straight"; detail = "Follow the arrows ahead · " + view.Distance.ToString("0") + " m"; icon = "↑"; break;
                case IndoorHudState.Turn: title = "Turn right"; detail = "In " + view.Distance.ToString("0") + " m"; icon = "↱"; break;
                case IndoorHudState.WrongWay: title = "Recalculating route"; detail = "Finding a new way to your destination."; icon = "↻"; break;
                default: title = "You have arrived"; detail = view.Destination + " is here."; icon = "✓"; break;
            }
            var card = Panel(_root, "Instruction", new Vector2(0, -280), new Vector2(980, 190), White, new Vector2(.5f, 1));
            Panel(card, "IconBackground", new Vector2(-390, 0), new Vector2(120, 120), accent, new Vector2(.5f, .5f));
            Label(card, "Icon", icon, new Vector2(-390, 0), new Vector2(120, 120), 67, White, TextAnchor.MiddleCenter, new Vector2(.5f, .5f), FontStyle.Bold);
            Label(card, "Title", title, new Vector2(65, 32), new Vector2(680, 65), 43, Ink, TextAnchor.MiddleLeft, new Vector2(.5f, .5f), FontStyle.Bold);
            Label(card, "Detail", detail, new Vector2(65, -37), new Vector2(680, 64), 28, Muted, TextAnchor.MiddleLeft, new Vector2(.5f, .5f));
            if (navigating) DrawMap(view, accent);
            if (state == IndoorHudState.Straight || state == IndoorHudState.Turn) DrawTrail(view.Waypoints, accent);
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

        void DrawTrail(Vector3[] waypoints, Color color)
        {
            if (waypoints == null || waypoints.Length < 2) return;
            _trail = new GameObject("Route chevrons (mock)");
            _trail.transform.SetParent(transform, false);
            _trailMaterial = new Material(Shader.Find("Sprites/Default")) { color = color };
            for (int segment = 0; segment < waypoints.Length - 1; segment++)
            {
                var a = waypoints[segment]; var b = waypoints[segment + 1];
                var delta = b - a;
                float start = segment == 0 ? 2f : .5f;
                for (float distance = start; distance < delta.magnitude; distance += 1.3f)
                {
                    var marker = new GameObject("Chevron", typeof(MeshFilter), typeof(MeshRenderer));
                    marker.transform.SetParent(_trail.transform, false);
                    marker.transform.localPosition = a + delta.normalized * distance + Vector3.up * .025f;
                    marker.transform.localRotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
                    var mesh = new Mesh { vertices = new[] { new Vector3(-.45f,0,-.25f), new Vector3(0,0,.15f), new Vector3(.45f,0,-.25f), new Vector3(-.45f,0,-.05f), new Vector3(0,0,.35f), new Vector3(.45f,0,-.05f) }, triangles = new[] { 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5 } };
                    mesh.RecalculateNormals(); marker.GetComponent<MeshFilter>().mesh = mesh;
                    marker.GetComponent<MeshRenderer>().sharedMaterial = _trailMaterial;
                }
            }
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
        void ClearTrail()
        {
            if (_trail == null) return;
            if (_trailMaterial != null) RemoveObject(_trailMaterial);
            _trailMaterial = null;
            foreach (var filter in _trail.GetComponentsInChildren<MeshFilter>()) RemoveObject(filter.sharedMesh);
            RemoveObject(_trail);
            _trail = null;
        }
        void OnDestroy()
        {
            ClearTrail();
            if (_round != null) { RemoveObject(_round.texture); RemoveObject(_round); }
        }
        static void RemoveObject(UnityEngine.Object obj) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
