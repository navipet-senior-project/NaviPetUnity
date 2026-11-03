using NUnit.Framework;
using System.Linq;
using UnityEngine.UI;
using UnityEngine;

namespace IndoorAr.Tests
{
    public class IndoorHudTests
    {
        [Test]
        public void MockRouteProvidesBothFloorsAndEveryState()
        {
            foreach (IndoorHudState state in System.Enum.GetValues(typeof(IndoorHudState)))
            {
                var view = MockIndoorRoute.Snapshot(state, 1);
                Assert.AreEqual(state, view.State);
                Assert.AreEqual("VEC 119", view.Destination);
                Assert.Greater(view.Waypoints.Length, 1);
            }
            Assert.AreEqual("VEC 220", MockIndoorRoute.Snapshot(IndoorHudState.Turn, 2).Destination);
        }

        [Test]
        public void StateUpdatesReplaceTheMessageAndExitRaisesAnEvent()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudState.Scanning, 1));
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudState.NotFound, 1));
                var messages = root.GetComponentsInChildren<Text>().Select(t => t.text).ToArray();
                CollectionAssert.Contains(messages, "Could not find your location");
                CollectionAssert.DoesNotContain(messages, "Scanning area...");
                bool exited = false;
                hud.ExitRequested += () => exited = true;
                root.GetComponentInChildren<Button>().onClick.Invoke();
                Assert.IsTrue(exited);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void RouteViewsAreIndependent()
        {
            var first = MockIndoorRoute.Snapshot(IndoorHudState.Straight, 1);
            first.Waypoints[0] = Vector3.one * 100;
            Assert.AreNotEqual(first.Waypoints[0], MockIndoorRoute.Snapshot(IndoorHudState.Straight, 1).Waypoints[0]);
        }

        [Test]
        public void SameCardKeepsTheExitButtonWhenThePoseChanges()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                var view = MockIndoorRoute.Snapshot(IndoorHudState.Straight, 1);
                hud.Show(view);
                var button = root.GetComponentInChildren<Button>();
                view.Distance = 4f;
                view.Waypoints = new[] { Vector3.zero, Vector3.forward * 4f };
                hud.Show(view);
                Assert.AreSame(button, root.GetComponentInChildren<Button>());
                CollectionAssert.Contains(root.GetComponentsInChildren<Text>().Select(t => t.text).ToArray(), "Follow the arrows ahead · 4 m");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void TinyPoseChangeKeepsTheRouteObjects()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                var view = MockIndoorRoute.Snapshot(IndoorHudState.Straight, 1);
                hud.Show(view);
                var trail = root.transform.Find("Route chevrons");
                view.Waypoints[0] += Vector3.forward * .05f;
                hud.Show(view);
                Assert.AreSame(trail, root.transform.Find("Route chevrons"));
                view.Waypoints[0] += Vector3.forward * 1f;
                hud.Show(view);
                Assert.AreNotSame(trail, root.transform.Find("Route chevrons"));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void ChevronShaderIsSpritesDefault()
        {
            var shader = IndoorHud.ChevronShader();
            Assert.IsNotNull(shader);
            Assert.AreEqual("Sprites/Default", shader.name);
        }

        [Test]
        public void LiveViewScansThenPointsAtTheDoorAndFailsClosed()
        {
            var request = JsonUtility.FromJson<IndoorArRequest>(IndoorArBridge.EditorRequestJson);
            var session = new IndoorArSession(request, request.mapCode);
            var camera = new Vector3(1f, 4f, 3f);
            Assert.AreEqual(IndoorHudState.Scanning, LiveDoorHud.Compose(session, camera, 0f).State);

            session.OnArSupport(true);
            session.OnAuthenticated();
            Assert.AreEqual(IndoorHudState.Scanning, LiveDoorHud.Compose(session, camera, 0f).State);
            session.OnLocalized(0.5f);
            var found = LiveDoorHud.Compose(session, camera, 0f);
            Assert.AreEqual(IndoorHudState.Localized, found.State);
            Assert.AreEqual("VEC 119", found.Destination);
            Assert.AreEqual(1, found.Floor);

            var guiding = LiveDoorHud.Compose(session, camera, LiveDoorHud.LocationFoundHoldS);
            Assert.AreEqual(IndoorHudState.Straight, guiding.State);
            Assert.AreEqual(new Vector3(camera.x, request.position.y, camera.z), guiding.Waypoints[0]);
            Assert.AreEqual(new Vector3(request.position.x, request.position.y, request.position.z), guiding.Waypoints[1]);
            Assert.AreEqual(session.HorizontalDistance(camera), guiding.Distance);

            session.OnTrackingLost();
            session.Tick(IndoorArSession.LocalizeTimeoutS);
            session.Tick(IndoorArSession.LocalizeTimeoutS);
            Assert.AreEqual(IndoorHudState.NotFound, LiveDoorHud.Compose(session, camera, 0f).State);
        }
    }
}
