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
    }
}
