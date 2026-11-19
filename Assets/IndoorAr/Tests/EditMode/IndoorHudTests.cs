using NUnit.Framework;
using System.Linq;
using System.Reflection;
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

        static string[] OutdoorTexts(IndoorHudView view)
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                hud.Show(view);
                return Texts(root);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void OutdoorRouteGuidanceUsesTheSameHudWithADistance()
        {
            var texts = OutdoorTexts(new IndoorHudView
            {
                State = IndoorHudState.Straight, Outdoor = true, FollowingRoute = true,
                Destination = "Veterans Memorial", DistanceM = 150, ArrowYaw = 80f
            });
            CollectionAssert.Contains(texts, "Turn right");
            CollectionAssert.Contains(texts, "492 ft · Veterans Memorial");
            Assert.IsFalse(texts.Any(t => t.Contains("Floor")));
        }

        [Test]
        public void OutdoorWithoutARouteSaysItIsOnlyADirection()
        {
            var texts = OutdoorTexts(new IndoorHudView
            {
                State = IndoorHudState.Straight, Outdoor = true, Destination = "Veterans Memorial", DistanceM = 150
            });
            CollectionAssert.Contains(texts, "Veterans Memorial is ahead");
            Assert.IsTrue(texts.Any(t => t.Contains("Direction only, not a route")));
        }

        [Test]
        public void OutdoorOffRouteSaysSo()
        {
            var texts = OutdoorTexts(new IndoorHudView
            {
                State = IndoorHudState.Straight, Outdoor = true, FollowingRoute = true, OffRoute = true,
                Destination = "Veterans Memorial", DistanceM = 400
            });
            CollectionAssert.Contains(texts, "You're off the route");
            Assert.IsTrue(texts.Any(t => t.EndsWith("Walk back to the route")));
        }

        [TestCase(0, "0 ft")]
        [TestCase(10, "33 ft")]
        [TestCase(300, "984 ft")]
        [TestCase(1609.344, "1.0 mi")]
        [TestCase(8046.72, "5.0 mi")]
        public void DistanceIsShownInFeetThenMiles(double meters, string expected)
        {
            Assert.AreEqual(expected, IndoorHud.DistanceLabel(meters));
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
        public void RoomPinAndLabelDockTogetherWhenBehindTheCamera()
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            var worldPin = new GameObject("World pin");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(cameraObject.GetComponent<Camera>());
                worldPin.transform.position = new Vector3(0f, 0f, 5f);
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1, worldPin.transform.position);
                view.WorldPin = worldPin.transform;
                hud.Show(view);
                var label = root.transform.Find("HUD Canvas/Safe area/Pin label");
                var dockedPin = root.transform.Find("HUD Canvas/Safe area/Docked pin");
                Assert.IsTrue(label.gameObject.activeSelf);
                Assert.IsTrue(worldPin.activeSelf);
                Assert.IsFalse(dockedPin.gameObject.activeSelf);
                StringAssert.Contains("VEC 119", label.GetComponentInChildren<Text>().text);
                worldPin.transform.position = new Vector3(5f, 0f, -5f);
                view.LabelAnchor = worldPin.transform.position;
                hud.Show(view);
                Assert.IsTrue(label.gameObject.activeSelf);
                Assert.IsFalse(worldPin.activeSelf);
                Assert.IsTrue(dockedPin.gameObject.activeSelf);
                Assert.Greater(((RectTransform)dockedPin).rect.height, 0f);
                Assert.AreEqual("VEC 119", label.GetComponentInChildren<Text>().text);
                view.LabelAnchor = null;
                hud.Show(view);
                Assert.IsFalse(label.gameObject.activeSelf);
                Assert.IsFalse(dockedPin.gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(worldPin);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [TestCase(-.25f, .5f)]
        [TestCase(1.25f, .82f)]
        [TestCase(.5f, -.25f)]
        [TestCase(.5f, 1.25f)]
        [TestCase(-.25f, 1.25f)]
        [TestCase(1.25f, 1.25f)]
        [TestCase(-.25f, -.25f)]
        [TestCase(1.25f, -.25f)]
        public void VisibleTipsStayProjectedAndDockSmoothlyInBothDirections(float x, float y)
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            var worldPin = new GameObject("Room pin");
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(camera);
                Canvas.ForceUpdateCanvases();
                var area = (RectTransform)root.transform.Find("HUD Canvas/Safe area");
                var offscreen = new Vector2(x, y);
                var edge = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
                var step = (offscreen - edge).normalized * .001f;
                foreach (var outdoor in new[] { true, false })
                {
                    var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1);
                    view.Outdoor = outdoor;
                    view.PinScreenHeight = .2f;
                    view.WorldPin = outdoor ? null : worldPin.transform;
                    void ShowAt(Vector2 viewport)
                    {
                        var anchor = camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, 5f));
                        if (!outdoor) worldPin.transform.position = anchor;
                        view.LabelAnchor = outdoor ? anchor : anchor + Vector3.up * .45f;
                        hud.Show(view);
                    }

                    ShowAt(edge - step);
                    var pin = (RectTransform)area.Find(outdoor ? "Building pin" : "Docked pin");
                    var pill = (RectTransform)area.Find("Pin label");
                    var label = pill.GetComponentInChildren<Text>();
                    Assert.AreEqual(view.Destination, label.text);
                    Assert.AreEqual(outdoor, pin.gameObject.activeSelf);
                    AssertLabelFits(area, pill);
                    if (outdoor)
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(area,
                            camera.WorldToScreenPoint(view.LabelAnchor.Value), null, out var projected);
                        Assert.That(Vector2.Distance(projected, pin.anchoredPosition), Is.LessThan(.01f));
                    }

                    var visiblePin = pin.anchoredPosition;
                    var visibleLabel = pill.anchoredPosition;
                    ShowAt(edge + step);
                    Assert.IsTrue(pin.gameObject.activeSelf);
                    if (y <= 1f) // a pin docking at the top edge must flip to hang below its tip; its upright core was already off screen
                        Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, pin.localEulerAngles.z)), Is.LessThan(5f), "the body does not snap on crossing a side or bottom edge");
                    Assert.That(Vector2.Distance(visiblePin, pin.anchoredPosition), Is.LessThan(12f));
                    Assert.That(Vector2.Distance(visibleLabel, pill.anchoredPosition), Is.LessThan(12f));
                    AssertLabelFits(area, pill);
                    AssertPinCoreVisible(camera, pin);

                    ShowAt(edge + step * 50f);
                    AssertPinCoreVisible(camera, pin);
                    ShowAt(offscreen);
                    Assert.AreEqual(view.Destination, label.text);
                    Assert.That(Vector2.Distance(visiblePin, pin.anchoredPosition), Is.GreaterThan(12f));
                    AssertLabelFits(area, pill);
                    AssertPinCoreVisible(camera, pin);
                    if (x > 1f)
                    {
                        var inset = Mathf.Max(pill.rect.width, pin.rect.width) * .5f + 24f;
                        Assert.AreEqual(area.rect.xMax - inset, pin.anchoredPosition.x, .01f);
                    }
                    ShowAt(edge + step);
                    Assert.That(Vector2.Distance(visiblePin, pin.anchoredPosition), Is.LessThan(12f));
                    AssertPinCoreVisible(camera, pin);
                    ShowAt(edge - step);
                    Assert.AreEqual(visiblePin, pin.anchoredPosition);
                    Assert.AreEqual(visibleLabel, pill.anchoredPosition);
                    Assert.AreEqual(outdoor, pin.gameObject.activeSelf);
                }
            }
            finally
            {
                Object.DestroyImmediate(worldPin);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
            }
        }

        static void AssertPinCoreVisible(Camera camera, RectTransform pin)
        {
            var sprite = pin.GetComponent<Image>().sprite;
            var texel = new Vector2Int(sprite.texture.width / 2, Mathf.FloorToInt(sprite.texture.height * .65f));
            Assert.That(sprite.texture.GetPixel(texel.x, texel.y).a, Is.GreaterThan(.9f));
            var uv = new Vector2((texel.x + .5f) / sprite.texture.width, (texel.y + .5f) / sprite.texture.height);
            var point = pin.TransformPoint(pin.rect.min + Vector2.Scale(pin.rect.size, uv));
            Assert.IsTrue(camera.pixelRect.Contains(RectTransformUtility.WorldToScreenPoint(null, point)),
                "The docked pin core must be inside the camera viewport.");
        }

        [TestCase(.05f)]
        [TestCase(30f)]
        public void AnIndoorPinOutsideTheDepthRangeHasAProjectedScreenMarker(float depth)
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            var worldPin = new GameObject("World pin");
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 20f;
                worldPin.transform.position = Vector3.forward * depth;
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(camera);
                Canvas.ForceUpdateCanvases();
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1, worldPin.transform.position);
                view.WorldPin = worldPin.transform;
                hud.Show(view);
                var area = (RectTransform)root.transform.Find("HUD Canvas/Safe area");
                var pin = (RectTransform)area.Find("Docked pin");
                Assert.IsFalse(worldPin.activeSelf);
                Assert.IsTrue(pin.gameObject.activeSelf);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(area, camera.pixelRect.center, null, out var center);
                Assert.That(Vector2.Distance(center, pin.anchoredPosition), Is.LessThan(.01f));
            }
            finally
            {
                Object.DestroyImmediate(worldPin);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RenderCameraPoseReprojectsThePinAfterTheViewWasSubmitted(bool outdoor)
        {
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var root = new GameObject("HUD");
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(camera);
                Canvas.ForceUpdateCanvases();
                var area = (RectTransform)root.transform.Find("HUD Canvas/Safe area");
                var view = MockIndoorRoute.Snapshot(IndoorHudPreview.DirectionToRoom, 1, Vector3.forward * 5);
                view.Outdoor = outdoor;
                view.PinScreenHeight = .2f;
                hud.Show(view);
                var projectedMarker = (RectTransform)area.Find(outdoor ? "Building pin" : "Pin label");
                var before = projectedMarker.anchoredPosition.x;
                camera.transform.position = Vector3.right;
                typeof(IndoorHud).GetMethod("ProjectPin", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hud, new object[] { default(UnityEngine.Rendering.ScriptableRenderContext), camera });
                RectTransformUtility.ScreenPointToLocalPointInRectangle(area,
                    camera.WorldToScreenPoint(view.LabelAnchor.Value), null, out var at);
                Assert.Less(projectedMarker.anchoredPosition.x, before);
                Assert.AreEqual(at.x, projectedMarker.anchoredPosition.x, .01f);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void OutdoorPinStaysHiddenUntilItHasACameraAnchor()
        {
            var root = new GameObject("HUD");
            try
            {
                var hud = root.AddComponent<IndoorHud>();
                hud.Initialize(null);
                Canvas.ForceUpdateCanvases();
                var view = new IndoorHudView { Outdoor = true, Destination = "VEC", PinLabel = "VEC building center", PinScreenHeight = .2f };
                hud.Show(view);
                var area = (RectTransform)root.transform.Find("HUD Canvas/Safe area");
                var pin = (RectTransform)area.Find("Building pin");
                var label = (RectTransform)area.Find("Pin label");
                Assert.IsFalse(pin.gameObject.activeSelf);
                Assert.IsFalse(label.gameObject.activeSelf);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void AssertLabelFits(RectTransform area, RectTransform label)
        {
            var bounds = label.rect;
            bounds.position += label.anchoredPosition;
            Assert.GreaterOrEqual(bounds.xMin + .01f, area.rect.xMin);
            Assert.LessOrEqual(bounds.xMax - .01f, area.rect.xMax);
            Assert.GreaterOrEqual(bounds.yMin + .01f, area.rect.yMin);
            Assert.LessOrEqual(bounds.yMax - .01f, area.rect.yMax);
        }

        [Test]
        public void EdgeDockKeepsTheDirectionInsideTheAvailableArea()
        {
            var limits = Rect.MinMaxRect(-200f, -300f, 200f, 300f);
            Assert.AreEqual(new Vector2(200f, 100f), IndoorHud.DockPin(new Vector2(400f, 200f), limits));
            Assert.AreEqual(new Vector2(0f, -300f), IndoorHud.DockPin(new Vector2(0f, -900f), limits));
            Assert.AreEqual(new Vector2(200f, 0f), IndoorHud.DockPin(Vector2.zero, limits));
        }

        [Test]
        public void PinIgnoresTinyCorrectionsAndEasesLargerOnes()
        {
            var at = Vector3.zero;
            var target = at;
            var velocity = Vector3.zero;
            Assert.AreEqual(at, IndoorHud.EasePin(at, Vector3.right * .01f, ref target, ref velocity, .1f));
            at = IndoorHud.EasePin(at, Vector3.right, ref target, ref velocity, .1f);
            Assert.That(at.x, Is.InRange(.01f, .99f));
            for (var i = 0; i < 20; i++)
                at = IndoorHud.EasePin(at, Vector3.right, ref target, ref velocity, .1f);
            Assert.AreEqual(1f, at.x, .001f);
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
        public void LiveViewScansThenBlocksMissingRouteAndFailsClosed()
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
            Assert.IsTrue(guiding.RouteBlocked);
            Assert.IsFalse(guiding.FollowingRoute);
            Assert.AreEqual("Route unavailable", guiding.TitleOverride);
            Assert.AreEqual(0f, guiding.ArrowYaw);

            session.OnTrackingLost();
            session.Tick(IndoorArSession.LocalizeTimeoutS);
            session.Tick(IndoorArSession.LocalizeTimeoutS);
            Assert.AreEqual(IndoorHudState.NotFound, LiveDoorHud.Compose(session, camera, Vector3.forward, 0f).State);
        }

        [Test]
        public void FrozenCameraHidesTheArrowAndAsksForARestartWhilePinned()
        {
            var request = JsonUtility.FromJson<IndoorArRequest>(IndoorArBridge.EditorRequestJson);
            var session = new IndoorArSession(request, request.mapCode);
            session.OnArSupport(true);
            session.OnAuthenticated();
            session.OnLocalized(0.5f);
            var camera = new Vector3(1f, 4f, 3f);
            Assert.AreEqual(IndoorHudState.Straight, LiveDoorHud.Compose(session, camera, Vector3.forward, LiveDoorHud.LocationFoundHoldS).State);

            var frozen = LiveDoorHud.Compose(session, camera, Vector3.forward, LiveDoorHud.LocationFoundHoldS, cameraFrozen: true);
            Assert.AreEqual(IndoorHudState.Localized, frozen.State, "no arrow and no I'm here button outside Straight");
            Assert.IsFalse(frozen.FollowingRoute);
            Assert.IsFalse(frozen.RouteBlocked, "a frozen camera is not a route fault");
            Assert.AreEqual(IndoorRouteBlockedCause.None, frozen.RouteBlockedCause);
            Assert.AreEqual(-1, frozen.RouteWaypointIndex);
            Assert.AreEqual(0f, frozen.ArrowYaw);
            Assert.AreEqual(LiveDoorHud.CameraFrozenTitle, frozen.TitleOverride);
            StringAssert.Contains("open it again", frozen.DetailOverride);
        }

        [Test]
        public void FrozenFlagPromptsRestartInEverySessionState()
        {
            var request = JsonUtility.FromJson<IndoorArRequest>(IndoorArBridge.EditorRequestJson);
            var session = new IndoorArSession(request, request.mapCode);
            var camera = new Vector3(1f, 4f, 3f);
            void AssertFrozen(IndoorArState state)
            {
                Assert.AreEqual(state, session.State);
                var view = LiveDoorHud.Compose(session, camera, Vector3.forward, 0f, cameraFrozen: true);
                Assert.AreEqual(IndoorHudState.Localized, view.State);
                Assert.IsFalse(view.FollowingRoute);
                Assert.IsFalse(view.RouteBlocked);
                Assert.AreEqual(LiveDoorHud.CameraFrozenTitle, view.TitleOverride);
                Assert.AreEqual(LiveDoorHud.CameraFrozenDetail, view.DetailOverride);
            }
            AssertFrozen(IndoorArState.Init);
            session.OnArSupport(true);
            AssertFrozen(IndoorArState.Auth);
            session.OnAuthenticated();
            AssertFrozen(IndoorArState.Localizing);
            session.OnLocalized(0.5f);
            AssertFrozen(IndoorArState.Pinned);
            session.ImHere(camera);
            Assert.AreEqual(IndoorArState.Arrived, session.State);
            var arrived = LiveDoorHud.Compose(session, camera, Vector3.forward, 0f, cameraFrozen: true);
            Assert.AreEqual(IndoorHudState.Localized, arrived.State);
            Assert.IsFalse(arrived.FollowingRoute);
            Assert.IsFalse(arrived.RouteBlocked);
            Assert.AreEqual(-1, arrived.RouteWaypointIndex);
            Assert.AreEqual(0f, arrived.ArrowYaw);
            Assert.AreEqual(LiveDoorHud.CameraFrozenTitle, arrived.TitleOverride);
            Assert.AreEqual(LiveDoorHud.CameraFrozenDetail, arrived.DetailOverride);
            Assert.AreEqual(IndoorHudState.Arrived, LiveDoorHud.Compose(session, camera, Vector3.forward, 0f).State);
            session.ConfirmArrival();
            AssertFrozen(IndoorArState.Done);
        }

        [Test]
        public void FrozenFlagOverridesVisibleFailureUntilCameraMoves()
        {
            var request = JsonUtility.FromJson<IndoorArRequest>(IndoorArBridge.EditorRequestJson);
            var session = new IndoorArSession(request, request.mapCode);
            session.OnArSupport(true);
            session.OnAuthFailed(false);
            var frozen = LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 0f, cameraFrozen: true);
            Assert.AreEqual(IndoorHudState.Localized, frozen.State);
            Assert.AreEqual(LiveDoorHud.CameraFrozenTitle, frozen.TitleOverride);
            Assert.AreEqual(LiveDoorHud.CameraFrozenDetail, frozen.DetailOverride);
            Assert.AreEqual("AR sign-in failed", LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 0f).TitleOverride);
        }

        static IndoorHudView SignView(string id, string floor, bool localizeOnly = false)
        {
            var request = JsonUtility.FromJson<IndoorArRequest>(IndoorArBridge.EditorRequestJson);
            request.mode = localizeOnly ? "localize" : request.mode;
            var session = new IndoorArSession(request, request.mapCode);
            session.OnArSupport(true);
            session.OnAuthenticated();
            session.OnSignConfirmed(new IndoorRoomLandmark(id, "VEC", floor));
            return LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 0f);
        }

        [Test]
        public void RoomSignCopyIsApproximateAndCatchesTheWrongFloor()
        {
            var near = SignView("VEC120", "floor_1");
            Assert.AreEqual(IndoorHudState.Scanning, near.State, "a sign never shows the arrow");
            Assert.AreEqual("Room VEC 120 detected · Floor 1", near.TitleOverride);
            Assert.AreEqual("From the room sign. AR is still aligning", near.DetailOverride);

            var upstairs = SignView("VEC215", "floor_2");
            Assert.AreEqual("Room VEC 215 detected · Floor 2", upstairs.TitleOverride);
            Assert.AreEqual("VEC 119 is on Floor 1", upstairs.DetailOverride);
            Assert.IsTrue(upstairs.WrongFloor, "the sign's floor, not the camera height, decides");

            var door = SignView("VEC119", "floor_1");
            Assert.AreEqual(IndoorHudState.Arrived, door.State);
            Assert.AreEqual("Found the VEC 119 sign · Floor 1", door.DetailOverride);

            var localize = SignView("VEC215", "floor_2", localizeOnly: true);
            Assert.AreEqual("Room VEC 215 detected · Floor 2", localize.TitleOverride, "localize mode has no wrong floor");
            Assert.AreEqual("From the room sign. AR is still aligning", localize.DetailOverride);
        }
}
}
