using System;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// A purple ribbon laid on the floor along a clear hallway segment.
    /// The route is already in the MapSpace frame, so the ribbon is parented to MapSpace and follows its alignment.
    /// It draws only the route it is given: no route, no ribbon.
    /// </summary>
    public sealed class IndoorRoutePath : IDisposable
    {
        public const float WidthM = .7f;
        public const float LiftM = .02f;
        public const float TileM = 1f;
        const float MinWidthM = .04f;
        const float MinCoveragePixels = 4f;

        readonly GameObject _object;
        readonly Mesh _mesh;
        readonly MeshRenderer _renderer;
        readonly Material _material;
        readonly Texture2D _texture;
        readonly Vector3[] _corners =
        {
            new Vector3(-.5f, 0f, 0f), new Vector3(-.5f, 0f, 1f),
            new Vector3(.5f, 0f, 1f), new Vector3(.5f, 0f, 0f)
        };
        readonly Vector2[] _uv = { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
        readonly Plane[] _frustum = new Plane[6];
        readonly Vector3[] _clipA = new Vector3[10];
        readonly Vector3[] _clipB = new Vector3[10];
        int _tiles = 1;

        public GameObject Root => _object;

        public IndoorRoutePath(Transform mapSpace)
        {
            _object = new GameObject("Route path");
            _object.transform.SetParent(mapSpace, false);
            _mesh = new Mesh { name = "Route path" };
            _mesh.vertices = _corners;
            _mesh.uv = _uv;
            _mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _mesh.triangles = new[] { 0, 1, 3, 3, 1, 2 };
            var shader = Shader.Find("Sprites/Default"); // always included, unlit, alpha blended, ignores lighting
            if (shader != null)
            {
                _texture = ChevronTexture();
                _material = new Material(shader) { mainTexture = _texture, name = "Route path" };
            }
            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
            _object.SetActive(false);
        }

        public void Place(Vector3 start, Vector3 end, IndoorWalkGrid grid = null)
        {
            var placed = TryPlace(start, end, grid);
            if (_object.activeSelf != placed) _object.SetActive(placed);
        }

        bool TryPlace(Vector3 start, Vector3 end, IndoorWalkGrid grid)
        {
            if (_material == null || !Finite(start) || !Finite(end)) return false;

            start.y = end.y;
            var direction = end - start;
            var length = direction.magnitude;
            if (length < .001f || !Finite(length)) return false;
            var width = WidthM;
            while (grid != null && !grid.IsRibbonClear(start, end, width))
            {
                width *= .5f;
                if (width < MinWidthM) return false;
            }

            var transform = _object.transform;
            transform.SetLocalPositionAndRotation(start + Vector3.up * LiftM, Quaternion.LookRotation(direction));
            transform.localScale = new Vector3(width, 1f, length);

            var tiles = Mathf.Max(1, Mathf.RoundToInt(length / TileM));
            if (tiles != _tiles)
            {
                _uv[0].y = _uv[3].y = 1f - tiles;
                _mesh.uv = _uv;
                _tiles = tiles;
            }
            return true;
        }

        internal bool RefreshVisibility(Camera camera)
        {
            if (_object == null || !_object.activeInHierarchy) return false;
            var visible = camera != null && (camera.cullingMask & (1 << _object.layer)) != 0 && InView(camera);
            if (_renderer.enabled != visible) _renderer.enabled = visible;
            return visible;
        }

        bool InView(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
            var input = _clipA;
            var output = _clipB;
            var count = _corners.Length;
            for (var i = 0; i < count; i++) input[i] = _object.transform.TransformPoint(_corners[i]);
            foreach (var plane in _frustum)
            {
                var clipped = 0;
                var previous = input[count - 1];
                var previousDistance = plane.GetDistanceToPoint(previous);
                for (var i = 0; i < count; i++)
                {
                    var current = input[i];
                    var distance = plane.GetDistanceToPoint(current);
                    if ((distance < 0f && previousDistance > 0f) || (distance > 0f && previousDistance < 0f))
                        output[clipped++] = Vector3.LerpUnclamped(previous, current, previousDistance / (previousDistance - distance));
                    if (distance >= 0f) output[clipped++] = current;
                    previous = current;
                    previousDistance = distance;
                }
                count = clipped;
                if (count < 3) return false;
                (input, output) = (output, input);
            }
            var pixelSize = new Vector2(camera.scaledPixelWidth, camera.scaledPixelHeight);
            var first = (Vector2)camera.WorldToViewportPoint(input[0]);
            var previousPixel = Vector2.zero;
            var twiceArea = 0f;
            var longestEdge = 0f;
            for (var i = 1; i < count; i++)
            {
                var pixel = Vector2.Scale((Vector2)camera.WorldToViewportPoint(input[i]) - first, pixelSize);
                twiceArea += previousPixel.x * pixel.y - previousPixel.y * pixel.x;
                longestEdge = Mathf.Max(longestEdge, (pixel - previousPixel).magnitude);
                previousPixel = pixel;
            }
            longestEdge = Mathf.Max(longestEdge, previousPixel.magnitude);
            return longestEdge >= MinCoveragePixels && Mathf.Abs(twiceArea) >= 2f * MinCoveragePixels * longestEdge;
        }

        public void Hide() => _object.SetActive(false);

        public void Dispose()
        {
            Destroy(_object);
            Destroy(_mesh);
            Destroy(_material);
            Destroy(_texture);
        }

        static void Destroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        // Soft purple with chevrons pointing along +v: the direction to walk.
        static Texture2D ChevronTexture()
        {
            const int width = 64, height = 128;
            var body = new Color(.44f, .24f, .95f);
            var mark = new Color(.88f, .82f, 1f);
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var u = (x + .5f) / width;
                    var v = (y + .5f) / height;
                    var edge = Mathf.Clamp01(Mathf.Min(u, 1f - u) / .12f);
                    var line = .72f - Mathf.Abs(u - .5f) * .8f;
                    var chevron = Mathf.Clamp01(1f - (Mathf.Abs(v - line) - .05f) / .035f);
                    var color = Color.Lerp(body, mark, chevron);
                    color.a = Mathf.Lerp(.55f, .92f, chevron) * edge;
                    pixels[y * width + x] = color;
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4, name = "Route chevrons" };
            texture.wrapModeV = TextureWrapMode.Repeat;
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
