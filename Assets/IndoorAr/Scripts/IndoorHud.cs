using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IndoorAr
{
    public enum IndoorHudState { Scanning, NotFound, Localized, Straight, Arrived }

    // The agreed visible states, for the editor preview and its screenshots.
    public enum IndoorHudPreview { Scanning, LocationFound, DirectionToRoom, OffScreenDirection, WrongFloor, LocalizationFailure, Arrival }

    // LiveDoorHud or the mock supplies this view.
    public sealed class IndoorHudView
    {
        public IndoorHudState State;
        public string Destination;
        public int Floor;
        public float ArrowYaw;
        public bool ScanTooFast;
        public bool LocalizeOnly;
        public bool WrongFloor;
        public string TitleOverride;
        public string DetailOverride;
        // World position the room label floats over; null hides the label.
        public Vector3? LabelAnchor;
    }

    public static class MockIndoorRoute
    {
        public static IndoorHudView Snapshot(IndoorHudPreview preview, int floor, Vector3? labelAnchor = null)
        {
            var view = new IndoorHudView
            {
                Floor = floor,
                Destination = floor == 2 ? "VEC 220" : "VEC 119",
                LabelAnchor = labelAnchor
            };
            switch (preview)
            {
                case IndoorHudPreview.Scanning: view.State = IndoorHudState.Scanning; view.LabelAnchor = null; break;
                case IndoorHudPreview.LocationFound: view.State = IndoorHudState.Localized; break;
                case IndoorHudPreview.DirectionToRoom: view.State = IndoorHudState.Straight; break;
                case IndoorHudPreview.OffScreenDirection: view.State = IndoorHudState.Straight; view.ArrowYaw = 70f; view.LabelAnchor = null; break;
                case IndoorHudPreview.WrongFloor: view.State = IndoorHudState.Straight; view.WrongFloor = true; break;
                case IndoorHudPreview.LocalizationFailure: view.State = IndoorHudState.NotFound; view.LabelAnchor = null; break;
                default: view.State = IndoorHudState.Arrived; break;
            }
            return view;
        }
    }

    /// <summary>Renders one supplied view. Does not perform localization or routing.</summary>
    public sealed class IndoorHud : MonoBehaviour
    {
        static readonly Color Navy = new Color32(0, 43, 91, 255);
        static readonly Color Amber = new Color32(245, 166, 35, 255);
        static readonly Color Muted = new Color32(94, 108, 128, 255);
        static readonly Color Soft = new Color32(238, 242, 247, 255);
        static readonly Color White = Color.white;
        static readonly Vector2 Center = new Vector2(.5f, .5f);
        readonly List<Sprite> _sprites = new List<Sprite>();
        Camera _camera;
        Canvas _canvas;
        RectTransform _root;
        Rect _safe;
        Sprite _pill;
        Sprite _card;
        Sprite _arrowSprite;
        Sprite _scrimSprite;
        Font _font;
        Text _title;
        Text _detail;
        string _chrome;
        RectTransform _arrow;
        RectTransform _lookLeft;
        RectTransform _lookRight;
        RectTransform _pinLabel;
        public event Action ExitRequested;
        public event Action HereRequested;
        public event Action RetryRequested;
        public event Action DoneRequested;

        public void Initialize(Camera camera)
        {
            _camera = camera;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _pill = RoundedSprite(63);
            _card = RoundedSprite(48);
            var canvasObject = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            // Overlay hit-tests in screen space. A camera-space canvas misses the
            // exit tap once the AR camera stops tracking.
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = camera;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            var safeObject = new GameObject("Safe area", typeof(RectTransform));
            safeObject.transform.SetParent(canvasObject.transform, false);
            _root = Span(safeObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            FitSafeArea();
        }

        public void Show(IndoorHudView view)
        {
            if (_root == null) throw new InvalidOperationException("Initialize HUD first");
            FitSafeArea();
            var chrome = view.State + "|" + view.Destination + "|" + view.Floor + "|" + view.TitleOverride + "|" + view.DetailOverride + "|" + view.LocalizeOnly;
            if (_chrome != chrome)
            {
                Build(view);
                _chrome = chrome;
            }
            Refresh(view);
        }

        // Keeps the room pill, close button and guidance clear of the notch and home indicator.
        void FitSafeArea()
        {
            var safe = Screen.safeArea;
            if (safe == _safe || safe.width <= 0f || safe.height <= 0f || Screen.width <= 0 || Screen.height <= 0) return;
            _safe = safe;
            _root.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            _root.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        }

        void Build(IndoorHudView view)
        {
            _title = _detail = null;
            _arrow = _lookLeft = _lookRight = _pinLabel = null;
            while (_root.childCount > 0)
            {
                var child = _root.GetChild(0);
                child.SetParent(null);
                RemoveObject(child.gameObject);
            }
            var state = view.State;
            bool card = state == IndoorHudState.NotFound || state == IndoorHudState.Arrived;
            if (!card)
            {
                // Runs past the safe area so the text stays readable down to the screen edge.
                var scrim = Span(Box(_root, "Scrim", ScrimSprite(), Navy), Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, -200f), new Vector2(0f, 560f));
                scrim.GetComponent<Image>().type = Image.Type.Simple;
            }

            // Built before any other button, so it is the first Button under the canvas.
            var exit = At(Box(_root, "Exit", _pill, White), Vector2.one, new Vector2(-40f, -23f), new Vector2(110f, 110f));
            Label(exit, "Exit label", "×", 60, Navy, TextAnchor.MiddleCenter);
            Lift(exit);
            OnTap(exit, () => ExitRequested?.Invoke());
            Pill("Room pill", view.LocalizeOnly ? "Location check" : RoomLine(view), 34, new Vector2(0f, 1f), new Vector2(40f, -36f), 84f);

            if (!view.LocalizeOnly)
            {
                _pinLabel = Pill("Pin label", view.Destination, 30, Center, Vector2.zero, 64f);
                _pinLabel.pivot = new Vector2(.5f, 0f);
                _pinLabel.gameObject.SetActive(false);
            }
            if (state == IndoorHudState.NotFound) FailureCard(view);
            else if (state == IndoorHudState.Arrived) ArrivalCard(view);
            else Guidance(view);
        }

        void Guidance(IndoorHudView view)
        {
            float right = 56f;
            if (view.State == IndoorHudState.Straight)
            {
                _arrow = At(Box(_root, "Guide arrow", ArrowSprite(), White), Center, new Vector2(0f, -60f), new Vector2(240f, 240f));
                _arrow.GetComponent<Image>().type = Image.Type.Simple;
                _lookLeft = Pill("Look left", "‹  Look this way", 34, new Vector2(0f, .5f), new Vector2(24f, 190f), 84f);
                _lookRight = Pill("Look right", "Look this way  ›", 34, new Vector2(1f, .5f), new Vector2(-24f, 190f), 84f);
                var here = Pill("Here", "I'm here", 36, new Vector2(1f, 0f), new Vector2(-48f, 72f), 100f);
                OnTap(here, () => HereRequested?.Invoke());
                right = 48f + here.sizeDelta.x + 28f;
            }
            _title = Line("Title", 118f, 64f, 46, White, FontStyle.Bold, right);
            _detail = Line("Detail", 64f, 46f, 30, new Color(1f, 1f, 1f, .9f), FontStyle.Normal, right);
        }

        void FailureCard(IndoorHudView view)
        {
            bool retry = string.IsNullOrEmpty(view.TitleOverride);
            var card = Card("Failure card", retry ? 380f : 246f);
            CardLine(card, "Title", TitleText(view), 44f, 60f, 44, Navy, TextAnchor.MiddleLeft, FontStyle.Bold);
            CardLine(card, "Detail", DetailText(view), 112f, 90f, 30, Muted, TextAnchor.UpperLeft, FontStyle.Normal);
            if (!retry) return;
            CardButton(card, "Back", "Back to map", 0f, .5f, Soft, Navy, () => ExitRequested?.Invoke());
            CardButton(card, "Retry", "Try again", .5f, 1f, Navy, White, () => RetryRequested?.Invoke());
        }

        void ArrivalCard(IndoorHudView view)
        {
            var card = Card("Arrival card", 460f);
            var pin = At(Box(card, "Arrival pin", _pill, Amber), new Vector2(.5f, 1f), new Vector2(0f, -44f), new Vector2(84f, 84f));
            At(Box(pin, "Arrival pin core", _pill, White), Center, Vector2.zero, new Vector2(34f, 34f));
            CardLine(card, "Title", TitleText(view), 148f, 64f, 48, Navy, TextAnchor.MiddleCenter, FontStyle.Bold);
            CardLine(card, "Detail", DetailText(view), 218f, 46f, 32, Muted, TextAnchor.MiddleCenter, FontStyle.Normal);
            CardButton(card, "Done", "Done", 0f, 1f, Navy, White, () => DoneRequested?.Invoke());
        }

        static string RoomLine(IndoorHudView view) => view.Destination + " · Floor " + view.Floor;

        static string TitleText(IndoorHudView view)
        {
            if (!string.IsNullOrEmpty(view.TitleOverride)) return view.TitleOverride;
            switch (view.State)
            {
                case IndoorHudState.Scanning: return view.ScanTooFast ? "Slow down" : "Scanning the hallway";
                case IndoorHudState.NotFound: return "Couldn't find you";
                case IndoorHudState.Localized: return "Location found";
                case IndoorHudState.Straight:
                    if (view.WrongFloor) return view.Destination + " is on Floor " + view.Floor;
                    if (GuidanceCue.FacingWaypoint(view.ArrowYaw)) return view.Destination + " is ahead";
                    float yaw = Mathf.DeltaAngle(0f, view.ArrowYaw);
                    if (Mathf.Abs(yaw) > 135f) return view.Destination + " is behind you";
                    return view.Destination + (yaw > 0f ? " is to your right" : " is to your left");
                default: return "You've arrived";
            }
        }

        static string DetailText(IndoorHudView view)
        {
            if (!string.IsNullOrEmpty(view.DetailOverride)) return view.DetailOverride;
            switch (view.State)
            {
                case IndoorHudState.Scanning: return view.ScanTooFast ? "Move your phone more slowly" : "Move your phone slowly across the hallway";
                case IndoorHudState.NotFound: return "Step into a hallway and scan the walls and signs.";
                case IndoorHudState.Localized: return view.LocalizeOnly ? "Your location was found" : "Direction to " + view.Destination + " is ready";
                case IndoorHudState.Straight: return view.WrongFloor ? "You are on a different floor" : "Arrow points to the room";
                default: return RoomLine(view);
            }
        }

        void Refresh(IndoorHudView view)
        {
            if (_title != null)
            {
                _title.text = TitleText(view);
                _detail.text = DetailText(view);
            }
            if (_arrow != null)
            {
                _arrow.localEulerAngles = new Vector3(0f, 0f, GuidanceCue.ArrowScreenRotation(view.ArrowYaw));
                bool facing = GuidanceCue.FacingWaypoint(view.ArrowYaw);
                bool right = Mathf.DeltaAngle(0f, view.ArrowYaw) > 0f;
                _lookLeft.gameObject.SetActive(!facing && !right);
                _lookRight.gameObject.SetActive(!facing && right);
            }
            PlacePinLabel(view.LabelAnchor);
        }

        void PlacePinLabel(Vector3? anchor)
        {
            if (_pinLabel == null) return;
            var local = Vector2.zero;
            bool visible = anchor.HasValue && _camera != null;
            if (visible)
            {
                var screen = _camera.WorldToScreenPoint(anchor.Value);
                var uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
                visible = screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, uiCamera, out local);
            }
            _pinLabel.gameObject.SetActive(visible);
            if (visible) _pinLabel.anchoredPosition = local + new Vector2(0f, 12f);
        }

        RectTransform Pill(string name, string text, int fontSize, Vector2 anchor, Vector2 position, float height)
        {
            var pill = Box(_root, name, _pill, White);
            var label = Label(pill, name + " label", text, fontSize, Navy, TextAnchor.MiddleCenter, FontStyle.Bold);
            Lift(pill);
            return At(pill, anchor, position, new Vector2(label.preferredWidth + height * .8f, height));
        }

        Text Line(string name, float bottom, float height, int fontSize, Color color, FontStyle style, float right)
        {
            var line = Label(_root, name, "", fontSize, color, TextAnchor.MiddleLeft, style);
            Span(line.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(56f, bottom), new Vector2(-right, bottom + height));
            line.resizeTextForBestFit = true;
            line.resizeTextMinSize = fontSize * 2 / 3;
            line.resizeTextMaxSize = fontSize;
            var shadow = line.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, .1f, .22f, .7f);
            shadow.effectDistance = new Vector2(0f, -2f);
            return line;
        }

        RectTransform Card(string name, float height)
        {
            var card = Span(Box(_root, name, _card, White), Vector2.zero, new Vector2(1f, 0f), new Vector2(40f, 48f), new Vector2(-40f, 48f + height));
            Lift(card);
            return card;
        }

        void CardLine(RectTransform card, string name, string text, float top, float height, int fontSize, Color color, TextAnchor alignment, FontStyle style)
        {
            var line = Label(card, name, text, fontSize, color, alignment, style);
            Span(line.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(48f, -top - height), new Vector2(-48f, -top));
        }

        void CardButton(RectTransform card, string name, string text, float from, float to, Color color, Color ink, Action action)
        {
            var button = Span(Box(card, name, _pill, color), new Vector2(from, 0f), new Vector2(to, 0f),
                new Vector2(from > 0f ? 10f : 44f, 40f), new Vector2(to < 1f ? -10f : -44f, 144f));
            Label(button, name + " label", text, 36, ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            OnTap(button, action);
        }

        static void OnTap(RectTransform target, Action action)
        {
            target.GetComponent<Image>().raycastTarget = true;
            target.gameObject.AddComponent<Button>().onClick.AddListener(() => action());
        }

        // White controls sit on a live camera image, often a white wall.
        static void Lift(RectTransform target)
        {
            var shadow = target.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, .17f, .36f, .3f);
            shadow.effectDistance = new Vector2(0f, -5f);
        }

        static RectTransform At(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static RectTransform Span(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        RectTransform Box(Transform parent, string name, Sprite sprite, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            var image = obj.GetComponent<Image>(); image.sprite = sprite; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = false;
            return obj.GetComponent<RectTransform>();
        }

        Text Label(Transform parent, string name, string text, int fontSize, Color color, TextAnchor alignment, FontStyle style = FontStyle.Normal)
        {
            _font.RequestCharactersInTexture(text, fontSize, style);
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            Span(obj.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var label = obj.GetComponent<Text>(); label.font = _font; label.text = text; label.fontSize = fontSize; label.color = color; label.alignment = alignment; label.fontStyle = style; label.resizeTextForBestFit = false; label.raycastTarget = false;
            return label;
        }

        Sprite Keep(Sprite sprite)
        {
            _sprites.Add(sprite);
            return sprite;
        }

        // A sliced circle: corners shrink to fit, so one sprite serves pills and round buttons.
        Sprite RoundedSprite(int radius)
        {
            const int size = 128; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x, 0, x - (size - 1 - radius));
                    float dy = Mathf.Max(radius - y, 0, y - (size - 1 - radius));
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius + .5f - Mathf.Sqrt(dx * dx + dy * dy))));
                }
            texture.Apply(); return Keep(Sprite.Create(texture, new Rect(0, 0, size, size), Center, 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius)));
        }

        Sprite ScrimSprite()
        {
            if (_scrimSprite != null) return _scrimSprite;
            const int size = 64; var texture = new Texture2D(1, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                texture.SetPixel(0, y, new Color(1, 1, 1, .78f * Mathf.Pow(1f - y / (size - 1f), 1.5f)));
            texture.Apply(); return _scrimSprite = Keep(Sprite.Create(texture, new Rect(0, 0, 1, size), Center, 100));
        }

        // White arrow, navy outline, pointing up at yaw 0.
        Sprite ArrowSprite()
        {
            if (_arrowSprite != null) return _arrowSprite;
            const int size = 192;
            const float round = .05f, outline = .045f, soft = 1f / size;
            var shape = new[]
            {
                new Vector2(.5f, .9f), new Vector2(.84f, .52f), new Vector2(.62f, .52f), new Vector2(.62f, .12f),
                new Vector2(.38f, .12f), new Vector2(.38f, .52f), new Vector2(.16f, .52f)
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = SignedDistance(shape, new Vector2((x + .5f) / size, (y + .5f) / size)) - round;
                    var color = Color.Lerp(Navy, White, Mathf.Clamp01(.5f - (distance + outline) / soft));
                    color.a = Mathf.Clamp01(.5f - distance / soft);
                    pixels[y * size + x] = color;
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            return _arrowSprite = Keep(Sprite.Create(texture, new Rect(0, 0, size, size), Center, 100));
        }

        // Negative inside the polygon.
        static float SignedDistance(Vector2[] shape, Vector2 point)
        {
            float nearest = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = shape.Length - 1; i < shape.Length; j = i++)
            {
                Vector2 a = shape[i], edge = shape[j] - a, offset = point - a;
                nearest = Mathf.Min(nearest, (offset - edge * Mathf.Clamp01(Vector2.Dot(offset, edge) / edge.sqrMagnitude)).sqrMagnitude);
                if ((a.y > point.y) != (shape[j].y > point.y) && point.x < a.x + edge.x * offset.y / edge.y) inside = !inside;
            }
            return inside ? -Mathf.Sqrt(nearest) : Mathf.Sqrt(nearest);
        }

        void OnDestroy()
        {
            foreach (var sprite in _sprites) { RemoveObject(sprite.texture); RemoveObject(sprite); }
        }
        static void RemoveObject(UnityEngine.Object obj) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
