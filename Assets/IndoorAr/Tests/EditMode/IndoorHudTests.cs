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
                CollectionAssert.Contains(root.GetComponentsInChildren<Text>().Select(t => t.text).ToArray(), "Arrow points to the saved room.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void OneArrowStaysPutAndTurnsWithTheHeading()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                var view = MockIndoorRoute.Snapshot(IndoorHudState.Straight, 1);
                hud.Show(view);
                var arrow = root.transform.Find("HUD Canvas/Guide arrow");
                Assert.AreEqual(1, root.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Guide arrow"));
                Assert.IsNull(root.GetComponentInChildren<MeshRenderer>(true));
                view.ArrowYaw = 90f;
                hud.Show(view);
                Assert.AreSame(arrow, root.transform.Find("HUD Canvas/Guide arrow"));
                Assert.AreEqual(-90f, Mathf.DeltaAngle(0f, arrow.GetComponent<RectTransform>().localEulerAngles.z), 0.5f);
                CollectionAssert.Contains(root.GetComponentsInChildren<Text>().Select(t => t.text).ToArray(), "Look this way");
                view.ArrowYaw = 0f;
                hud.Show(view);
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, arrow.GetComponent<RectTransform>().localEulerAngles.z), 0.5f);
                CollectionAssert.DoesNotContain(root.GetComponentsInChildren<Text>().Select(t => t.text).ToArray(), "Look this way");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void SlowScanReminderShowsOnlyWhileMovingTooFast()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                var view = MockIndoorRoute.Snapshot(IndoorHudState.Scanning, 1);
                hud.Show(view);
                var pill = root.transform.Find("HUD Canvas/Scan slowly");
                Assert.IsFalse(pill.gameObject.activeSelf);
                view.ScanTooFast = true;
                hud.Show(view);
                Assert.IsTrue(root.transform.Find("HUD Canvas/Scan slowly").gameObject.activeSelf);
                Assert.AreSame(pill, root.transform.Find("HUD Canvas/Scan slowly"));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void ArrowYawIsZeroAheadAndPositiveToTheRight()
        {
            Assert.AreEqual(0f, GuidanceCue.ArrowYawDegrees(Vector3.forward, Vector3.zero, new Vector3(0f, 3f, 8f)), 0.01f);
            var right = GuidanceCue.ArrowYawDegrees(Vector3.forward, Vector3.zero, new Vector3(4f, 9f, 0f));
            Assert.AreEqual(90f, right, 0.01f);
            Assert.AreEqual(-90f, GuidanceCue.ArrowScreenRotation(right), 0.01f);
            Assert.AreEqual(-90f, GuidanceCue.ArrowYawDegrees(Vector3.forward, Vector3.zero, new Vector3(-4f, 0f, 0f)), 0.01f);
            Assert.AreEqual(180f, Mathf.Abs(GuidanceCue.ArrowYawDegrees(Vector3.forward, Vector3.zero, new Vector3(0f, 1f, -5f))), 0.01f);
            Assert.AreEqual(0f, GuidanceCue.ArrowYawDegrees(Vector3.zero, Vector3.zero, Vector3.forward), 0.01f);
            Assert.AreEqual(0f, GuidanceCue.ArrowYawDegrees(Vector3.forward, Vector3.one, Vector3.one), 0.01f);
            Assert.IsTrue(GuidanceCue.FacingWaypoint(GuidanceCue.LookAheadDegrees));
            Assert.IsFalse(GuidanceCue.FacingWaypoint(GuidanceCue.LookAheadDegrees + 0.1f));
        }

        [Test]
        public void SlowScanThresholdTripsOnSpeedOrTurnRate()
        {
            Assert.IsFalse(GuidanceCue.ScanTooFast(GuidanceCue.SlowScanMetersPerSecond, 0f, 1f));
            Assert.IsTrue(GuidanceCue.ScanTooFast(GuidanceCue.SlowScanMetersPerSecond + 0.01f, 0f, 1f));
            Assert.IsFalse(GuidanceCue.ScanTooFast(0f, GuidanceCue.SlowScanDegreesPerSecond, 1f));
            Assert.IsTrue(GuidanceCue.ScanTooFast(0f, GuidanceCue.SlowScanDegreesPerSecond + 0.1f, 1f));
            Assert.IsTrue(GuidanceCue.ScanTooFast(0f, -(GuidanceCue.SlowScanDegreesPerSecond + 0.1f), 1f));
            Assert.IsFalse(GuidanceCue.ScanTooFast(10f, 90f, 0f));
            Assert.IsFalse(GuidanceCue.ScanTooFast(10f, 90f, -1f));
        }

        [Test]
        public void SlowScanPillHoldsAfterTheLastFastFrameThenClears()
        {
            var left = GuidanceCue.HoldSlowScan(0f, true, 0.016f);
            Assert.AreEqual(GuidanceCue.SlowScanHoldS, left);
            left = GuidanceCue.HoldSlowScan(left, false, 0.5f);
            Assert.Greater(left, 0f);
            left = GuidanceCue.HoldSlowScan(left, true, 0.016f);
            Assert.AreEqual(GuidanceCue.SlowScanHoldS, left);
            left = GuidanceCue.HoldSlowScan(left, false, 0.6f);
            Assert.Greater(left, 0f);
            left = GuidanceCue.HoldSlowScan(left, false, 0.6f);
            Assert.AreEqual(0f, left);
        }

        [Test]
        public void AuthFailureUsesTheSignInCopy()
        {
            var request = JsonUtility.FromJson<IndoorArRequest>(IndoorArBridge.EditorRequestJson);
            var session = new IndoorArSession(request, request.mapCode);
            session.OnArSupport(true);
            session.Tick(IndoorArSession.StartupTimeoutS);
            var view = LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 0f);
            Assert.AreEqual(IndoorHudState.NotFound, view.State);
            Assert.AreEqual("AR sign-in failed", view.TitleOverride);
            Assert.AreEqual("The session could not use the AR token.", view.DetailOverride);
        }

        [Test]
        public void DestinationShowsTheSearchedRoomNotTheGluedDoorKey()
        {
            Assert.AreEqual("VEC 119", LiveDoorHud.Destination(new IndoorArRequest { room = "119", doorKey = "VEC119" }));
            Assert.AreEqual("VEC 119", LiveDoorHud.Destination(new IndoorArRequest { room = "VEC119", doorKey = "VEC118" }));
            Assert.AreEqual("VEC 119", LiveDoorHud.Destination(new IndoorArRequest { doorKey = "VEC119" }));
            Assert.AreEqual("VEC 119", LiveDoorHud.Destination(new IndoorArRequest { room = "VEC 119", doorKey = "VEC119" }));
        }

        [Test]
        public void LiveViewScansThenPointsAtTheDoorAndFailsClosed()
        {
            var request = JsonUtility.FromJson<IndoorArRequest>(IndoorArBridge.EditorRequestJson);
            var session = new IndoorArSession(request, request.mapCode);
            var camera = new Vector3(1f, 4f, 3f);
            Assert.AreEqual(IndoorHudState.Scanning, LiveDoorHud.Compose(session, camera, Vector3.forward, 0f).State);

            session.OnArSupport(true);
            session.OnAuthenticated();
            Assert.AreEqual(IndoorHudState.Scanning, LiveDoorHud.Compose(session, camera, Vector3.forward, 0f).State);
            session.OnLocalized(0.5f);
            var found = LiveDoorHud.Compose(session, camera, Vector3.forward, 0f);
            Assert.AreEqual(IndoorHudState.Localized, found.State);
            Assert.AreEqual("VEC 119", found.Destination);
            Assert.AreEqual(1, found.Floor);

            var guiding = LiveDoorHud.Compose(session, camera, Vector3.forward, LiveDoorHud.LocationFoundHoldS);
            Assert.AreEqual(IndoorHudState.Straight, guiding.State);
            var door = new Vector3(request.position.x, request.position.y, request.position.z);
            Assert.AreEqual(GuidanceCue.ArrowYawDegrees(Vector3.forward, camera, door), guiding.ArrowYaw, 1e-3f);

            session.OnTrackingLost();
            session.Tick(IndoorArSession.LocalizeTimeoutS);
            session.Tick(IndoorArSession.LocalizeTimeoutS);
            Assert.AreEqual(IndoorHudState.NotFound, LiveDoorHud.Compose(session, camera, Vector3.forward, 0f).State);
        }
    }
}
