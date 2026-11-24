using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace IndoorAr.Tests
{
    public class IndoorRoutePathTests
    {
        const float FloorY = -1.2f;
        static readonly Vector3[] LShape =
        {
            new Vector3(0f, FloorY, 0f), new Vector3(0f, FloorY, 4f), new Vector3(3f, FloorY, 4f)
        };

        static Mesh Build(Vector3[] route, Vector3 from, float time, out bool ok)
        {
            var mesh = new Mesh();
            ok = IndoorRoutePath.TryBuild(route, from, time, mesh);
            return mesh;
        }

        [Test]
        public void RibbonLiesFlatOnTheFloorAndIsAsWideAsConfigured()
        {
            var mesh = Build(new[] { new Vector3(0f, FloorY, 0f), new Vector3(0f, FloorY, 5f) }, new Vector3(0f, 1.6f, 0f), 0f, out var ok);
            Assert.IsTrue(ok);
            Assert.AreEqual(4, mesh.vertexCount);
            Assert.IsTrue(mesh.vertices.All(v => Mathf.Approximately(v.y, FloorY + IndoorRoutePath.LiftM)));
            Assert.AreEqual(IndoorRoutePath.WidthM, mesh.vertices.Max(v => v.x) - mesh.vertices.Min(v => v.x), 1e-4f);
            Assert.AreEqual(5f, mesh.vertices.Max(v => v.z), 1e-4f);
            Assert.AreEqual(6, mesh.triangles.Length);
        }

        [Test]
        public void RibbonStartsWhereTheCameraIsOnTheRouteAndShrinksAsTheUserWalks()
        {
            var atStart = Build(LShape, new Vector3(0f, 1.6f, 0f), 0f, out _);
            var walked = Build(LShape, new Vector3(.2f, 1.6f, 3f), 0f, out var ok);
            Assert.IsTrue(ok);
            Assert.AreEqual(0f, atStart.vertices.Min(v => v.z), 1e-4f);
            Assert.AreEqual(3f, walked.vertices.Min(v => v.z), 1e-4f, "the part behind the user is gone");
            Assert.AreEqual(3f, walked.vertices.Max(v => v.x), IndoorRoutePath.WidthM, "it still runs to the end");
        }

        [Test]
        public void RibbonStaysOnTheRouteThroughATurn()
        {
            var mesh = Build(LShape, new Vector3(0f, 1.6f, 0f), 0f, out var ok);
            Assert.IsTrue(ok);
            Assert.AreEqual(6, mesh.vertexCount);
            var half = IndoorRoutePath.WidthM / 2f;
            // The corner pair sits on the diagonal through the corner, never farther than the miter limit.
            var corner = mesh.vertices.Skip(2).Take(2).ToArray();
            Assert.AreEqual(0f, (corner[0] + corner[1]).x / 2f, 1e-4f);
            Assert.AreEqual(4f, (corner[0] + corner[1]).z / 2f, 1e-4f);
            Assert.LessOrEqual(Vector3.Distance(corner[0], corner[1]) / 2f, half * 2f + 1e-4f);
        }

        [Test]
        public void ChevronsAreFixedToTheRouteAndFlowTowardItsEnd()
        {
            var route = new[] { new Vector3(0f, FloorY, 0f), new Vector3(0f, FloorY, 5f) };
            var before = Build(route, new Vector3(0f, 1.6f, 0f), 0f, out _);
            var closer = Build(route, new Vector3(0f, 1.6f, 2f), 0f, out _);
            var later = Build(route, new Vector3(0f, 1.6f, 0f), 1f, out _);
            var uv = new System.Collections.Generic.List<Vector2>();
            before.GetUVs(0, uv);
            var farEnd = uv.Max(u => u.y);
            closer.GetUVs(0, uv);
            Assert.AreEqual(farEnd, uv.Max(u => u.y), 1e-4f, "walking does not move the chevrons");
            later.GetUVs(0, uv);
            Assert.AreEqual(farEnd - IndoorRoutePath.FlowMetersPerSecond / IndoorRoutePath.TileM, uv.Max(u => u.y), 1e-4f,
                "texture slides back, so chevrons move forward");
        }

        [Test]
        public void RibbonFadesInFromTheCamera()
        {
            var mesh = Build(new[] { new Vector3(0f, FloorY, 0f), new Vector3(0f, FloorY, 5f) }, new Vector3(0f, 1.6f, 0f), 0f, out _);
            var colors = mesh.colors;
            Assert.AreEqual(0f, colors[0].a, 1e-4f);
            Assert.AreEqual(1f, colors[colors.Length - 1].a, 1e-4f);
        }

        [Test]
        public void NoUsableRouteDrawsNothingAndClearsAnOldRibbon()
        {
            var mesh = new Mesh();
            Assert.IsTrue(IndoorRoutePath.TryBuild(LShape, Vector3.zero, 0f, mesh));
            Assert.Greater(mesh.vertexCount, 0);
            var bad = new Vector3[][]
            {
                null,
                new Vector3[0],
                new[] { Vector3.zero },
                new[] { Vector3.zero, Vector3.zero },
                new[] { Vector3.zero, new Vector3(float.NaN, 0f, 1f) },
                new[] { Vector3.zero, new Vector3(0f, float.PositiveInfinity, 1f) },
                new[] { Vector3.zero, new Vector3(0f, 5f, 0f) }, // straight up: no floor length
            };
            foreach (var route in bad)
            {
                Assert.IsTrue(IndoorRoutePath.TryBuild(LShape, Vector3.zero, 0f, mesh));
                Assert.IsFalse(IndoorRoutePath.TryBuild(route, Vector3.zero, 0f, mesh), route == null ? "null" : string.Join(",", route));
                Assert.AreEqual(0, mesh.vertexCount);
            }
            Assert.IsFalse(IndoorRoutePath.TryBuild(LShape, new Vector3(float.NaN, 0f, 0f), 0f, mesh));
            Assert.IsFalse(IndoorRoutePath.TryBuild(LShape, Vector3.zero, float.NaN, mesh));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void RepeatedPointsDoNotBreakTheRibbon()
        {
            var route = new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 3f), new Vector3(0f, 0f, 3f), new Vector3(2f, 0f, 3f) };
            var mesh = Build(route, Vector3.zero, 0f, out var ok);
            Assert.IsTrue(ok);
            Assert.AreEqual(6, mesh.vertexCount);
            Assert.IsTrue(mesh.vertices.All(v => !float.IsNaN(v.x) && !float.IsNaN(v.z)));
        }

        [Test]
        public void CameraAtTheEndOfTheRouteHidesTheRibbon()
        {
            var mesh = Build(LShape, new Vector3(3f, 1.6f, 4f), 0f, out var ok);
            Assert.IsFalse(ok);
            Assert.AreEqual(0, mesh.vertexCount);
        }

        [Test]
        public void PathFollowsMapSpaceAndShowsOnlyForAUsableRoute()
        {
            var map = new GameObject("MapSpace");
            map.transform.position = new Vector3(5f, 0f, 2f);
            map.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var path = new IndoorRoutePath(map.transform);
            try
            {
                Assert.AreSame(map.transform, path.Root.transform.parent);
                Assert.AreEqual(Vector3.zero, path.Root.transform.localPosition);
                Assert.IsFalse(path.Root.activeSelf, "nothing before a route");
                Assert.IsTrue(path.Show(LShape, Vector3.zero, 0f));
                Assert.IsTrue(path.Root.activeSelf);
                Assert.IsNotNull(path.Root.GetComponent<MeshRenderer>().sharedMaterial);
                Assert.IsFalse(path.Show(null, Vector3.zero, 0f));
                Assert.IsFalse(path.Root.activeSelf, "an absent route is not invented");
                Assert.IsTrue(path.Show(LShape, Vector3.zero, 0f));
                path.Hide();
                Assert.IsFalse(path.Root.activeSelf);
            }
            finally { path.Dispose(); Object.DestroyImmediate(map); }
        }

        [Test]
        public void DisposeReleasesTheObjectMeshMaterialAndTexture()
        {
            var map = new GameObject("MapSpace");
            var path = new IndoorRoutePath(map.transform);
            path.Show(LShape, Vector3.zero, 0f);
            var renderer = path.Root.GetComponent<MeshRenderer>();
            var mesh = path.Root.GetComponent<MeshFilter>().sharedMesh;
            var material = renderer.sharedMaterial;
            var texture = material.mainTexture;
            path.Dispose();
            Assert.IsTrue(path.Root == null);
            Assert.IsTrue(mesh == null);
            Assert.IsTrue(material == null);
            Assert.IsTrue(texture == null);
            Assert.AreEqual(0, map.transform.childCount);
            Object.DestroyImmediate(map);
        }

        [Test]
        public void HudHidesTheScreenArrowWhileThePathIsShownButKeepsTheGuidanceText()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.OffScreenDirection, 1);
                view.PathShown = false;
                hud.Show(view);
                var arrowTexts = root.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray();
                CollectionAssert.Contains(root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray(), "Guide arrow");

                view.PathShown = true;
                hud.Show(view);
                var names = root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
                CollectionAssert.DoesNotContain(names, "Guide arrow");
                CollectionAssert.AreEqual(arrowTexts, root.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray(),
                    "only the arrow is replaced; title, detail and the look-this-way cue stay");
                CollectionAssert.Contains(arrowTexts, "I'm here");
                Assert.IsTrue(arrowTexts.Any(t => t.Contains("Look this way")));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }
    }
}
