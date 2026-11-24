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

        [SetUp]
        public void SetUp()
        {
            _map = new GameObject("MapSpace");
            _camera = new GameObject("Camera").AddComponent<Camera>();
            _camera.fieldOfView = 60f;
            _camera.aspect = 9f / 16f;
            _path = new IndoorRoutePath(_map.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _path.Dispose();
            Object.DestroyImmediate(_camera.gameObject);
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
            AimAt(Vector3.zero, end);
            Assert.IsFalse(_path.Root.activeSelf);
            Assert.IsTrue(_path.Show(start, end, _camera));
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
        public void ShortRibbonNeedsCameraViewAndKeepsAFullChevron()
        {
            var start = Vector3.zero;
            var end = Vector3.forward * .25f;
            _camera.transform.position = Vector3.up * 1.5f;
            Assert.IsFalse(_path.Show(start, end, _camera));
            Assert.IsFalse(_path.Root.activeSelf);
            AimAt(start, end);
            Assert.IsTrue(_path.Show(start, end, _camera));
            var uv = Mesh.uv;
            Assert.AreEqual(1f, uv.Max(v => v.y) - uv.Min(v => v.y));
            CollectionAssert.AreEqual(new[] { Color.white, Color.white, Color.white, Color.white }, Mesh.colors);
            Assert.IsTrue(_path.Show(start, end, _camera));
            CollectionAssert.AreEqual(uv, Mesh.uv);
            _camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
            Assert.IsFalse(_path.Show(start, end, _camera));
            Assert.IsFalse(_path.Root.activeSelf);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DiagonalRibbonAvoidsTheShippedBlockedCellInEitherDirection(bool reverse)
        {
            var grid = IndoorWalkGrid.ForFloor(Resources.Load<TextAsset>("IndoorWalkGrid").text, 1);
            var start = new Vector3(11.375f, grid.FloorY, 21.625f);
            var end = new Vector3(11.875f, grid.FloorY, 21.375f);
            if (reverse) (start, end) = (end, start);
            AimAt(start, end);
            Assert.IsFalse(grid.IsWalkable(178, 84));
            Assert.IsTrue(_path.Show(start, end, _camera, grid));
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
            AimAt(Vector3.zero, end);
            Assert.IsTrue(_path.Show(Vector3.zero, end, _camera));
            Assert.IsFalse(_path.Show(end, end, _camera));
            Assert.IsFalse(_path.Root.activeSelf);
            Assert.IsFalse(_path.Show(Vector3.zero, new Vector3(0f, 5f, 0f), _camera));
            Assert.IsFalse(_path.Show(new Vector3(float.NaN, 0f, 0f), end, _camera));
            Assert.IsFalse(_path.Show(Vector3.zero, new Vector3(0f, float.PositiveInfinity, 1f), _camera));
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
