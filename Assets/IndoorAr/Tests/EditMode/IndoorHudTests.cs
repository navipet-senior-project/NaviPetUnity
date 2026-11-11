using NUnit.Framework;
using System.Linq;
using UnityEngine.UI;
using UnityEngine;

namespace IndoorAr.Tests
{
    public class IndoorHudTests
    {
        static string[] Texts(GameObject root) => root.GetComponentsInChildren<Text>().Select(t => t.text).ToArray();

        [Test]
        public void MockPreviewProvidesBothFloorsAndEveryAgreedState()
        {
            var states = new System.Collections.Generic.Dictionary<IndoorHudPreview, IndoorHudState>
            {
                { IndoorHudPreview.Scanning, IndoorHudState.Scanning },
                { IndoorHudPreview.LocationFound, IndoorHudState.Localized },
                { IndoorHudPreview.DirectionToRoom, IndoorHudState.Straight },
                { IndoorHudPreview.OffScreenDirection, IndoorHudState.Straight },
                { IndoorHudPreview.WrongFloor, IndoorHudState.Straight },
                { IndoorHudPreview.LocalizationFailure, IndoorHudState.NotFound },
                { IndoorHudPreview.Arrival, IndoorHudState.Arrived },
            };
            foreach (IndoorHudPreview preview in System.Enum.GetValues(typeof(IndoorHudPreview)))
            {
                var view = MockIndoorRoute.Snapshot(preview, 1);
                Assert.AreEqual(states[preview], view.State);
                Assert.AreEqual("VEC 119", view.Destination);
            }
            Assert.AreEqual("VEC 220", MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 2).Destination);
            Assert.IsFalse(GuidanceCue.FacingWaypoint(MockIndoorRoute.Snapshot(IndoorHudPreview.OffScreenDirection, 1).ArrowYaw));
            Assert.IsTrue(MockIndoorRoute.Snapshot(IndoorHudPreview.WrongFloor, 1).WrongFloor);
            Assert.IsNotNull(MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1, Vector3.forward).LabelAnchor);
            Assert.IsNull(MockIndoorRoute.Snapshot(IndoorHudPreview.OffScreenDirection, 1, Vector3.forward).LabelAnchor);
        }

        [Test]
        public void EveryAgreedStateRendersTheLavishOverlayWithoutAMiniMapOrDistance()
        {
            var titles = new System.Collections.Generic.Dictionary<IndoorHudPreview, string>
            {
                { IndoorHudPreview.Scanning, "Scanning the hallway" },
                { IndoorHudPreview.LocationFound, "Location found" },
                { IndoorHudPreview.DirectionToRoom, "VEC 119 is ahead" },
                { IndoorHudPreview.OffScreenDirection, "VEC 119 is to your right" },
                { IndoorHudPreview.WrongFloor, "VEC 119 is on Floor 1" },
                { IndoorHudPreview.LocalizationFailure, "Couldn't find you" },
                { IndoorHudPreview.Arrival, "You've arrived" },
            };
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                foreach (IndoorHudPreview preview in System.Enum.GetValues(typeof(IndoorHudPreview)))
                {
                    hud.Show(MockIndoorRoute.Snapshot(preview, 1));
                    var texts = Texts(root);
                    var names = root.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
                    CollectionAssert.Contains(texts, titles[preview], preview.ToString());
                    CollectionAssert.Contains(texts, "VEC 119 · Floor 1", preview.ToString());
                    Assert.AreEqual(1, names.Count(n => n == "Room pill"), preview.ToString());
                    Assert.AreEqual(1, names.Count(n => n == "Exit"), preview.ToString());
                    foreach (var removed in new[] { "Mini map", "Instruction", "IconBackground", "Destination" })
                        CollectionAssert.DoesNotContain(names, removed, preview.ToString());
                    foreach (var text in root.GetComponentsInChildren<Text>(true).Select(t => t.text))
                        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text, @"\d\s*(m|meters?|ft)\b"), preview + " shows a distance: " + text);

                    bool guiding = preview == IndoorHudPreview.DirectionToRoom || preview == IndoorHudPreview.OffScreenDirection || preview == IndoorHudPreview.WrongFloor;
                    Assert.AreEqual(guiding, texts.Contains("I'm here"), preview.ToString());
                    Assert.AreEqual(guiding, names.Contains("Guide arrow"), preview.ToString());
                    Assert.AreEqual(preview == IndoorHudPreview.OffScreenDirection, texts.Any(t => t.Contains("Look this way")), preview.ToString());
                    Assert.AreEqual(preview == IndoorHudPreview.Arrival, names.Contains("Arrival card"), preview.ToString());
                    Assert.AreEqual(preview == IndoorHudPreview.Arrival, texts.Contains("Done"), preview.ToString());
                    Assert.AreEqual(preview == IndoorHudPreview.LocalizationFailure, texts.Contains("Try again"), preview.ToString());
                    Assert.AreEqual(preview == IndoorHudPreview.LocalizationFailure, texts.Contains("Back to map"), preview.ToString());
                }
                CollectionAssert.Contains(Texts(root), "VEC 119 · Floor 1");
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudPreview.WrongFloor, 2));
                CollectionAssert.Contains(Texts(root), "VEC 220 is on Floor 2");
                CollectionAssert.Contains(Texts(root), "You are on a different floor");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void GuideArrowIsWhiteWithANavyOutline()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1));
                var image = root.transform.Find("HUD Canvas/Safe area/Guide arrow").GetComponent<Image>();
                Assert.AreEqual(Color.white, image.color);
                var texture = image.sprite.texture;
                Assert.AreEqual((Color32)Color.white, (Color32)texture.GetPixel(texture.width / 2, texture.height / 2));
                Assert.AreEqual(new Color32(0, 43, 91, 255), (Color32)texture.GetPixel(texture.width / 2, (int)(texture.height * .09f)));
                Assert.AreEqual(0, ((Color32)texture.GetPixel(2, 2)).a);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void ArrivalAndRetryButtonsRaiseTheirEvents()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                int here = 0, done = 0, retry = 0, exit = 0;
                hud.HereRequested += () => here++;
                hud.DoneRequested += () => done++;
                hud.RetryRequested += () => retry++;
                hud.ExitRequested += () => exit++;
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1));
                CollectionAssert.DoesNotContain(Texts(root), "You've arrived");
                root.transform.Find("HUD Canvas/Safe area/Here").GetComponent<Button>().onClick.Invoke();
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudPreview.Arrival, 1));
                root.transform.Find("HUD Canvas/Safe area/Arrival card/Done").GetComponent<Button>().onClick.Invoke();
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudPreview.LocalizationFailure, 1));
                root.transform.Find("HUD Canvas/Safe area/Failure card/Retry").GetComponent<Button>().onClick.Invoke();
                root.transform.Find("HUD Canvas/Safe area/Failure card/Back").GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(new[] { 1, 1, 1, 1 }, new[] { here, done, retry, exit });
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void RoomLabelFollowsThePinOnlyWhileItIsInFrontOfTheCamera()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1, new Vector3(0f, 0f, 5f));
                hud.Show(view);
                var label = root.transform.Find("HUD Canvas/Safe area/Pin label");
                Assert.IsTrue(label.gameObject.activeSelf);
                Assert.AreEqual("VEC 119", label.GetComponentInChildren<Text>().text);
                view.LabelAnchor = new Vector3(0f, 0f, -5f);
                hud.Show(view);
                Assert.IsFalse(label.gameObject.activeSelf);
                view.LabelAnchor = null;
                hud.Show(view);
                Assert.IsFalse(label.gameObject.activeSelf);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
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
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudPreview.Scanning, 1));
                CollectionAssert.Contains(Texts(root), "Scanning the hallway");
                hud.Show(MockIndoorRoute.Snapshot(IndoorHudPreview.LocalizationFailure, 1));
                var messages = Texts(root);
                CollectionAssert.Contains(messages, "Couldn't find you");
                CollectionAssert.DoesNotContain(messages, "Scanning the hallway");
                bool exited = false;
                hud.ExitRequested += () => exited = true;
                root.GetComponentInChildren<Button>().onClick.Invoke();
                Assert.IsTrue(exited);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
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
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1);
                hud.Show(view);
                var button = root.GetComponentInChildren<Button>();
                Assert.AreEqual("Exit", button.name);
                view.ArrowYaw = 12f;
                view.LabelAnchor = new Vector3(0f, 0f, 4f);
                hud.Show(view);
                Assert.AreSame(button, root.GetComponentInChildren<Button>());
                CollectionAssert.Contains(Texts(root), "Arrow points to the room");
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
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1);
                hud.Show(view);
                var arrow = root.transform.Find("HUD Canvas/Safe area/Guide arrow");
                Assert.AreEqual(1, root.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Guide arrow"));
                Assert.IsNull(root.GetComponentInChildren<MeshRenderer>(true));
                view.ArrowYaw = 90f;
                hud.Show(view);
                Assert.AreSame(arrow, root.transform.Find("HUD Canvas/Safe area/Guide arrow"));
                Assert.AreEqual(-90f, Mathf.DeltaAngle(0f, arrow.GetComponent<RectTransform>().localEulerAngles.z), 0.5f);
                Assert.IsTrue(root.transform.Find("HUD Canvas/Safe area/Look right").gameObject.activeSelf);
                Assert.IsFalse(root.transform.Find("HUD Canvas/Safe area/Look left").gameObject.activeSelf);
                CollectionAssert.Contains(Texts(root), "VEC 119 is to your right");
                view.ArrowYaw = -90f;
                hud.Show(view);
                Assert.IsTrue(root.transform.Find("HUD Canvas/Safe area/Look left").gameObject.activeSelf);
                Assert.IsFalse(root.transform.Find("HUD Canvas/Safe area/Look right").gameObject.activeSelf);
                CollectionAssert.Contains(Texts(root), "VEC 119 is to your left");
                view.ArrowYaw = 170f;
                hud.Show(view);
                CollectionAssert.Contains(Texts(root), "VEC 119 is behind you");
                view.ArrowYaw = 0f;
                hud.Show(view);
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, arrow.GetComponent<RectTransform>().localEulerAngles.z), 0.5f);
                Assert.IsFalse(Texts(root).Any(t => t.Contains("Look this way")));
                CollectionAssert.Contains(Texts(root), "VEC 119 is ahead");
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
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.Scanning, 1);
                hud.Show(view);
                var title = root.transform.Find("HUD Canvas/Safe area/Title");
                CollectionAssert.DoesNotContain(Texts(root), "Slow down");
                view.ScanTooFast = true;
                hud.Show(view);
                CollectionAssert.Contains(Texts(root), "Slow down");
                CollectionAssert.Contains(Texts(root), "Move your phone more slowly");
                Assert.AreSame(title, root.transform.Find("HUD Canvas/Safe area/Title"));
                view.ScanTooFast = false;
                hud.Show(view);
                CollectionAssert.DoesNotContain(Texts(root), "Slow down");
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
