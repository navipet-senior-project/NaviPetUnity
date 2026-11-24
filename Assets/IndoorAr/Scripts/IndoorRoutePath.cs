using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// A purple ribbon laid on the floor along the hallway route, from the camera's place on the route to its end.
    /// The route is already in the MapSpace frame, so the ribbon is parented to MapSpace and follows its alignment.
    /// It draws only the route it is given: no route, no ribbon.
    /// </summary>
    public sealed class IndoorRoutePath : IDisposable
    {
        public const float WidthM = .7f;
        public const float LiftM = .02f;
        public const float TileM = 1f;
        public const float FadeInM = 1.5f;
        public const float FlowMetersPerSecond = .8f;
        const float MinSegmentM = .001f;

        readonly GameObject _object;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly Texture2D _texture;
        readonly MeshRenderer _renderer;

        public GameObject Root => _object;

        public IndoorRoutePath(Transform mapSpace)
        {
            _object = new GameObject("Route path");
            _object.transform.SetParent(mapSpace, false);
            _mesh = new Mesh { name = "Route path" };
            _mesh.MarkDynamic();
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
            _object.SetActive(false);
        }

        /// <summary>Draws the route from <paramref name="from"/> onward. False, and nothing drawn, when there is no usable route.</summary>
        public bool Show(Vector3[] route, Vector3 from, float timeS)
        {
            var shown = _material != null && TryBuild(route, from, timeS, _mesh);
            _object.SetActive(shown);
            return shown;
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

        /// <summary>
        /// Fills <paramref name="mesh"/> with a flat ribbon from the point of the route nearest <paramref name="from"/> (on the floor plane)
        /// to the route's end. False, with the mesh cleared, for a null, short, non-finite or zero-length route, or when
        /// <paramref name="from"/> is already at the end. Chevrons are fixed to the route, and flow toward its end with time.
        /// </summary>
        public static bool TryBuild(Vector3[] route, Vector3 from, float timeS, Mesh mesh)
        {
            mesh.Clear();
            if (route == null || !Finite(from) || !Finite(timeS)) return false;
            var points = new List<Vector3>(route.Length);
            var along = new List<float>(route.Length);
            foreach (var point in route)
            {
                if (!Finite(point)) return false;
                if (points.Count > 0)
                {
                    var step = Flat(point - points[points.Count - 1]);
                    if (step < MinSegmentM) continue;
                    along.Add(along[along.Count - 1] + step);
                }
                else along.Add(0f);
                points.Add(point);
            }
            if (points.Count < 2) return false;

            // The nearest point on the route, so the ribbon starts where the user is and shrinks as they walk it.
            var nearestSegment = 0;
            var nearestT = 0f;
            var nearest = float.MaxValue;
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i];
                var edge = points[i + 1] - a;
                edge.y = 0f;
                var offset = from - a;
                offset.y = 0f;
                var t = Mathf.Clamp01(Vector3.Dot(offset, edge) / edge.sqrMagnitude);
                var distance = (offset - edge * t).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                nearestSegment = i;
                nearestT = t;
            }
            var start = Vector3.Lerp(points[nearestSegment], points[nearestSegment + 1], nearestT);
            var startAlong = Mathf.Lerp(along[nearestSegment], along[nearestSegment + 1], nearestT);
            var path = new List<Vector3> { start };
            var pathAlong = new List<float> { startAlong };
            for (var i = nearestSegment + 1; i < points.Count; i++)
            {
                if (Flat(points[i] - path[path.Count - 1]) < MinSegmentM) continue;
                path.Add(points[i]);
                pathAlong.Add(along[i]);
            }
            if (path.Count < 2) return false;

            var vertices = new List<Vector3>(path.Count * 2);
            var uvs = new List<Vector2>(path.Count * 2);
            var colors = new List<Color>(path.Count * 2);
            var triangles = new List<int>((path.Count - 1) * 6);
            for (var i = 0; i < path.Count; i++)
            {
                var side = SideAt(path, i, out var widen);
                var lifted = path[i] + Vector3.up * LiftM;
                var half = side * (WidthM * .5f * widen);
                vertices.Add(lifted - half);
                vertices.Add(lifted + half);
                var v = (pathAlong[i] - timeS * FlowMetersPerSecond) / TileM;
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
                var fade = Mathf.Clamp01((pathAlong[i] - startAlong) / FadeInM);
                colors.Add(new Color(1f, 1f, 1f, fade));
                colors.Add(new Color(1f, 1f, 1f, fade));
                if (i == 0) continue;
                var k = (i - 1) * 2;
                triangles.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return true;
        }

        // Unit vector across the path at a point, with the miter factor that keeps the edges parallel through a turn.
        static Vector3 SideAt(List<Vector3> path, int i, out float widen)
        {
            var before = i > 0 ? Normal(path[i] - path[i - 1]) : Vector3.zero;
            var after = i + 1 < path.Count ? Normal(path[i + 1] - path[i]) : Vector3.zero;
            var side = before + after;
            if (side.sqrMagnitude < 1e-6f) side = before == Vector3.zero ? after : before;
            side.Normalize();
            var reference = before == Vector3.zero ? after : before;
            widen = 1f / Mathf.Max(Vector3.Dot(side, reference), .5f);
            return side;
        }

        static Vector3 Normal(Vector3 direction)
        {
            direction.y = 0f;
            direction.Normalize();
            return new Vector3(-direction.z, 0f, direction.x);
        }

        static float Flat(Vector3 offset) => new Vector2(offset.x, offset.z).magnitude;

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
