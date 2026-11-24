using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace IndoorAr.Tests
{
    public class IndoorRoutePathTests
    {
        GameObject _map;
        Camera _camera;
        IndoorRoutePath _path;
        RenderTexture _target;

        [SetUp]
        public void SetUp()
        {
            _map = new GameObject("MapSpace");
            _camera = new GameObject("Camera").AddComponent<Camera>();
            _camera.fieldOfView = 60f;
            _target = new RenderTexture(720, 1280, 24);
            _camera.targetTexture = _target;
            _camera.aspect = 9f / 16f;
            _path = new IndoorRoutePath(_map.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _path.Dispose();
            Object.DestroyImmediate(_camera.gameObject);
            Object.DestroyImmediate(_target);
            Object.DestroyImmediate(_map);
        }

        Mesh Mesh => _path.Root.GetComponent<MeshFilter>().sharedMesh;

        void AimAt(Vector3 start, Vector3 end)
        {
            _camera.transform.position = _map.transform.TransformPoint(start + Vector3.up * 1.5f);
            _camera.transform.LookAt(_map.transform.TransformPoint((start + end) * .5f));
        }

        [Test]
        public void RibbonStaysOnTheFloorAndFollowsMapSpace()
        {
            _map.transform.SetPositionAndRotation(new Vector3(5f, 0f, 2f), Quaternion.Euler(0f, 90f, 0f));
            var start = new Vector3(0f, 1.6f, 0f);
            var end = new Vector3(0f, 0f, 5f);
            Assert.IsFalse(_path.Root.activeSelf);
            _path.Place(start, end);
            Assert.IsTrue(_path.Root.activeSelf);
            Assert.AreSame(_map.transform, _path.Root.transform.parent);
            var transform = _path.Root.transform;
            var vertices = Mesh.vertices.Select(vertex => _map.transform.InverseTransformPoint(transform.TransformPoint(vertex))).ToArray();
            Assert.IsTrue(vertices.All(v => Mathf.Approximately(v.y, IndoorRoutePath.LiftM)));
            Assert.AreEqual(IndoorRoutePath.WidthM, vertices.Max(v => v.x) - vertices.Min(v => v.x), 1e-4f);
            Assert.AreEqual(0f, vertices.Min(v => v.z), 1e-4f);
            Assert.AreEqual(5f, vertices.Max(v => v.z), 1e-4f);
            Assert.AreEqual(6, Mesh.triangles.Length);
            _path.Hide();
            Assert.IsFalse(_path.Root.activeSelf);
        }

        [Test]
        public void ShortRibbonKeepsAFullChevron()
        {
            var end = Vector3.forward * .25f;
            _path.Place(Vector3.zero, end);
            var uv = Mesh.uv;
            Assert.AreEqual(1f, uv.Max(v => v.y) - uv.Min(v => v.y));
            CollectionAssert.AreEqual(new[] { Color.white, Color.white, Color.white, Color.white }, Mesh.colors);
            _path.Place(Vector3.zero, end);
            CollectionAssert.AreEqual(uv, Mesh.uv);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DiagonalRibbonAvoidsTheShippedBlockedCellInEitherDirection(bool reverse)
        {
            var grid = IndoorWalkGrid.ForFloor(Resources.Load<TextAsset>("IndoorWalkGrid").text, 1);
            var start = new Vector3(11.375f, grid.FloorY, 21.625f);
            var end = new Vector3(11.875f, grid.FloorY, 21.375f);
            if (reverse) (start, end) = (end, start);
            Assert.IsFalse(grid.IsWalkable(178, 84));
            _path.Place(start, end, grid);
            Assert.IsTrue(_path.Root.activeSelf);
            Assert.Less(_path.Root.transform.localScale.x, IndoorRoutePath.WidthM);
            Assert.AreEqual(Vector3.Distance(start, end), _path.Root.transform.localScale.z, 1e-4f);
            var transform = _path.Root.transform;
            for (var x = 0; x <= 20; x++)
                for (var z = 0; z <= 20; z++)
                    Assert.IsTrue(grid.IsWalkableAt(transform.TransformPoint(new Vector3(x / 20f - .5f, 0f, z / 20f))));
        }

        [Test]
        public void ZeroLengthAndNonFiniteSegmentsHideTheRibbon()
        {
            var end = Vector3.forward * 5f;
            var invalidSegments = new[]
            {
                (end, end),
                (Vector3.zero, Vector3.up * 5f),
                (new Vector3(float.NaN, 0f, 0f), end),
                (Vector3.zero, new Vector3(0f, float.PositiveInfinity, 1f))
            };
            foreach (var (start, finish) in invalidSegments)
            {
                _path.Place(Vector3.zero, end);
                Assert.IsTrue(_path.Root.activeSelf);
                _path.Place(start, finish);
                Assert.IsFalse(_path.Root.activeSelf);
            }
        }

        [Test]
        public void DisposeReleasesTheObjectMeshMaterialAndTexture()
        {
            var path = new IndoorRoutePath(_map.transform);
            var mesh = path.Root.GetComponent<MeshFilter>().sharedMesh;
            var material = path.Root.GetComponent<MeshRenderer>().sharedMaterial;
            var texture = material.mainTexture;
            path.Dispose();
            Assert.IsTrue(path.Root == null);
            Assert.IsTrue(mesh == null);
            Assert.IsTrue(material == null);
            Assert.IsTrue(texture == null);
            Assert.AreEqual(1, _map.transform.childCount);
        }

        [Test]
        public void RenderPoseSwitchesBetweenRibbonAndArrowWithoutReplacingControls()
        {
            var root = new GameObject("HUD");
            try
            {
                var end = Vector3.forward * .25f;
                AimAt(Vector3.zero, end);
                _path.Place(Vector3.zero, end);
                var renderer = _path.Root.GetComponent<MeshRenderer>();
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(_camera);
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1);
                view.FollowingRoute = true;
                hud.Show(view, _path);
                var area = root.transform.Find("HUD Canvas/Safe area");
                var arrow = area.Find("Guide arrow");
                var title = area.Find("Title").GetComponent<UnityEngine.UI.Text>();
                var exit = area.Find("Exit");
                var here = area.Find("Here");
                Assert.IsTrue(renderer.enabled);
                Assert.IsFalse(arrow.gameObject.activeSelf);
                Assert.AreEqual("Follow the route", title.text);

                _camera.transform.rotation = Quaternion.identity;
                RenderPose(hud);
                Assert.IsFalse(renderer.enabled);
                Assert.IsTrue(arrow.gameObject.activeSelf);
                Assert.AreEqual("Follow the arrow", title.text);
                Assert.AreSame(exit, area.Find("Exit"));
                Assert.AreSame(here, area.Find("Here"));

                hud.Show(view, _path);
                Assert.AreSame(arrow, area.Find("Guide arrow"));
                Assert.AreSame(exit, area.Find("Exit"));
                Assert.AreSame(here, area.Find("Here"));

                AimAt(Vector3.zero, end);
                RenderPose(hud);
                Assert.IsTrue(renderer.enabled);
                Assert.IsFalse(arrow.gameObject.activeSelf);
                Assert.AreEqual("Follow the route", title.text);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SubpixelRibbonAtTheViewportEdgeKeepsTheArrow()
        {
            var root = new GameObject("HUD");
            try
            {
                var end = Vector3.forward * .25f;
                _camera.orthographic = true;
                _camera.orthographicSize = .5f;
                _camera.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -1f), Quaternion.Euler(45f, 0f, 0f));
                var tip = end + Vector3.up * IndoorRoutePath.LiftM;
                var offset = _camera.WorldToViewportPoint(tip).y - .25f / _target.height;
                _camera.transform.position += _camera.transform.up * offset;
                Assert.That(_camera.WorldToViewportPoint(tip).y * _target.height, Is.EqualTo(.25f).Within(.01f));

                _path.Place(Vector3.zero, end);
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(_camera);
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1);
                view.FollowingRoute = true;
                hud.Show(view, _path);
                var arrow = root.transform.Find("HUD Canvas/Safe area/Guide arrow").gameObject;
                Assert.IsTrue(arrow.activeSelf);

                _camera.transform.position -= _camera.transform.up * .05f;
                RenderPose(hud);
                Assert.IsFalse(arrow.activeSelf, "several visible pixels restore the ribbon");
            }
            finally { Object.DestroyImmediate(root); }
        }

        void RenderPose(IndoorHud hud) =>
            typeof(IndoorHud).GetMethod("RefreshForRender", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hud, new object[] { default(UnityEngine.Rendering.ScriptableRenderContext), _camera });
    }
}
