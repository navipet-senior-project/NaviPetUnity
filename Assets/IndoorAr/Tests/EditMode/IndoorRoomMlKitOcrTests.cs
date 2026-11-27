using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class IndoorRoomMlKitOcrTests
{
    /// <summary>Starts a frame on the adapter as a camera capture does and returns the native reply to it.</summary>
    public static string Reply(MonoBehaviour adapter, string payload, IndoorOcrCapture capture = default,
        float startedAt = 0f) => Job(adapter).Start(startedAt, capture) + "|" + payload;

    static IndoorOcrJob Job(MonoBehaviour adapter) => (IndoorOcrJob)adapter.GetType()
        .GetField("_job", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);

    static void Send(MonoBehaviour adapter, string method, params object[] args) => adapter.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(adapter, args);

    // A portrait frame reading VEC 119 with corners, and the camera that took it.
    const string Sign = "{\"text\":\"VEC\\n119\",\"width\":1440,\"height\":1920,\"rotation\":90,\"tokens\":["
        + "{\"text\":\"119\",\"corners\":[700,900,760,901,761,930,699,929]}]}";

    static IndoorOcrCapture CameraAt(float x) => new IndoorOcrCapture
    {
        Fx = 1500f, Fy = 1490f, Cx = 950f, Cy = 730f, Width = 1920, Height = 1440,
        CameraWorldPosition = new Vector3(x, 0f, 0f), CameraWorldRotation = Quaternion.identity
    };

    sealed class Adapter
    {
        public MonoBehaviour Component;
        public string Warning;
        public readonly List<(IndoorRoomMatch Match, float StartedAt, IndoorSignObservation Seen)> Results = new();
        public void Text(string reply) => Send(Component, "OnMlKitText", reply);
        public void Error(string reply) => Send(Component, "OnMlKitError", reply);
    }

    static Adapter Add(GameObject host, bool ios)
    {
        var adapter = new Adapter();
        void Record(string _, IndoorRoomMatch match, float startedAt, IndoorSignObservation seen) =>
            adapter.Results.Add((match, startedAt, seen));
        var directory = new TextAsset(File.ReadAllText(Path.Combine(
            Application.dataPath, "IndoorNavigation/Data/vec_floors12345.json")));
        if (ios)
        {
            var ocr = host.AddComponent<IndoorRoomMlKitOcr>();
            ocr.Configure(null, directory);
            ocr.Result += Record;
            adapter.Component = ocr;
            adapter.Warning = "[IndoorAr OCR] ML Kit unavailable: ";
        }
        else
        {
            var ocr = host.AddComponent<IndoorRoomOcrDemo>();
            ocr.Configure(null, directory);
            ocr.Result += Record;
            adapter.Component = ocr;
            adapter.Warning = "[IndoorAr OCR] recognition failed: ";
        }
        return adapter;
    }

    // A reply is paired only with the frame it answers: its own start time and camera pose, never a later frame's.
    [TestCase(true)]
    [TestCase(false)]
    public void ReplyToAFrameFromADisabledSessionIsDroppedAndTheCurrentFramePairsWithItsOwnCamera(bool ios)
    {
        var host = new GameObject("IndoorRoomOcr");
        try
        {
            var ocr = Add(host, ios);
            var old = Reply(ocr.Component, Sign, CameraAt(1f), 1f);
            Send(ocr.Component, "OnDisable"); // the runner turned OCR off; the native job keeps running
            var current = Reply(ocr.Component, Sign, CameraAt(2f), 2f);

            ocr.Error(old.Split('|')[0] + "|late failure");
            ocr.Text(old);
            ocr.Text("VEC 119");
            Assert.IsEmpty(ocr.Results, "replies to the earlier frame, or without an id, publish nothing");
            Assert.IsTrue(Job(ocr.Component).Busy, "and leave the current frame waiting");

            ocr.Text(current);
            Assert.AreEqual(1, ocr.Results.Count);
            Assert.AreEqual("VEC119", ocr.Results[0].Match.Landmark.Id);
            Assert.AreEqual(2f, ocr.Results[0].StartedAt);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), ocr.Results[0].Seen.CameraWorldPosition);
            Assert.IsFalse(Job(ocr.Component).Busy);

            ocr.Error(current.Split('|')[0] + "|second reply");
            ocr.Text(current);
            Assert.AreEqual(1, ocr.Results.Count, "a frame is answered once, whichever reply comes second");
            LogAssert.NoUnexpectedReceived();
        }
        finally { Object.DestroyImmediate(host); }
    }

    // A new scene's adapter has the old one's GameObject name, so UnitySendMessage hands it the old job's reply.
    [TestCase(true)]
    [TestCase(false)]
    public void ReplyToAReplacedAdapterIsDroppedByItsSuccessor(bool ios)
    {
        var first = new GameObject("IndoorRoomOcr");
        var second = new GameObject("IndoorRoomOcr");
        try
        {
            var old = Reply(Add(first, ios).Component, Sign, CameraAt(1f), 1f);
            Object.DestroyImmediate(first);
            var ocr = Add(second, ios);
            var current = Reply(ocr.Component, Sign, CameraAt(2f), 2f);

            ocr.Text(old);
            Assert.IsEmpty(ocr.Results);
            ocr.Text(current);
            Assert.AreEqual(1, ocr.Results.Count);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), ocr.Results[0].Seen.CameraWorldPosition);
        }
        finally
        {
            if (first != null) Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    // A native busy rejection cancels the frame (no reply follows); the busy job's own reply is then stale too.
    [TestCase(true)]
    [TestCase(false)]
    public void ReplyAfterTheRecognizerRejectedTheFrameAsBusyPublishesNothing(bool ios)
    {
        var host = new GameObject("IndoorRoomOcr");
        try
        {
            var ocr = Add(host, ios);
            var rejected = Reply(ocr.Component, Sign, CameraAt(1f), 1f);
            Job(ocr.Component).Cancel(); // what the adapter does when recognize returns false / 0
            ocr.Text(rejected);
            ocr.Error(rejected.Split('|')[0] + "|busy");
            Assert.IsEmpty(ocr.Results);
            Assert.IsFalse(Job(ocr.Component).Busy, "the next frame can start");
            LogAssert.NoUnexpectedReceived();
        }
        finally { Object.DestroyImmediate(host); }
    }

    // An async conversion that finishes after its frame was abandoned neither fails nor submits the current one.
    [TestCase(true)]
    [TestCase(false)]
    public void ConversionOfAnAbandonedFrameLeavesTheCurrentFrameAlone(bool ios)
    {
        var host = new GameObject("IndoorRoomOcr");
        try
        {
            var ocr = Add(host, ios);
            var old = int.Parse(Reply(ocr.Component, "", CameraAt(1f), 1f).Split('|')[0]);
            Send(ocr.Component, "OnDisable");
            var current = int.Parse(Reply(ocr.Component, "", CameraAt(2f), 2f).Split('|')[0]);
            object[] Converted(int id)
            {
                var parameters = ocr.Component.GetType()
                    .GetMethod("OnFrameConverted", BindingFlags.Instance | BindingFlags.NonPublic).GetParameters();
                var args = new object[parameters.Length];
                args[0] = id;
                args[1] = Enum.Parse(parameters[1].ParameterType, "Failed");
                for (var i = 2; i < args.Length; i++) args[i] = Activator.CreateInstance(parameters[i].ParameterType);
                return args;
            }

            Send(ocr.Component, "OnFrameConverted", Converted(old));
            Assert.IsEmpty(ocr.Results);
            Assert.IsTrue(Job(ocr.Component).IsCurrent(current));

            LogAssert.Expect(LogType.Warning, ocr.Warning + "frame_conversion_failed");
            Send(ocr.Component, "OnFrameConverted", Converted(current));
            Assert.AreEqual(1, ocr.Results.Count);
            Assert.AreEqual(IndoorRoomMatchStatus.RecognizerUnavailable, ocr.Results[0].Match.Status);
            Assert.AreEqual(2f, ocr.Results[0].StartedAt);
            Assert.IsFalse(Job(ocr.Component).Busy);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void RgbaContractAcceptsOnlyBoundedCompleteFrames()
    {
        Assert.IsTrue(IndoorRoomMlKitOcr.IsValidRgbaFrame(640, 360, 640 * 360 * 4));
        Assert.IsFalse(IndoorRoomMlKitOcr.IsValidRgbaFrame(0, 360, 0));
        Assert.IsFalse(IndoorRoomMlKitOcr.IsValidRgbaFrame(1921, 360, 1921 * 360 * 4));
        Assert.IsFalse(IndoorRoomMlKitOcr.IsValidRgbaFrame(640, 360, 640 * 360 * 3));
    }

    [TestCase(ScreenOrientation.Portrait, 6)]
    [TestCase(ScreenOrientation.PortraitUpsideDown, 8)]
    [TestCase(ScreenOrientation.LandscapeLeft, 1)]
    [TestCase(ScreenOrientation.LandscapeRight, 3)]
    public void DisplayOrientationMapsToMlKitRotation(ScreenOrientation screen, int rotation)
    {
        Assert.AreEqual(rotation, IndoorRoomMlKitOcr.MlKitOrientation(screen));
    }

    [Test]
    public void MlKitTextUsesRoomMatcherAndReturnsIdentityOnly()
    {
        var host = new GameObject("OCR adapter test");
        try
        {
            var ocr = host.AddComponent<IndoorRoomMlKitOcr>();
            var directory = new TextAsset(File.ReadAllText(Path.Combine(
                Application.dataPath, "IndoorNavigation/Data/vec_floors12345.json")));
            ocr.Configure(null, directory);
            string text = null;
            IndoorRoomMatch match = null;
            ocr.Result += (recognizedText, roomMatch, _, _) => { text = recognizedText; match = roomMatch; };

            ocr.OnMlKitText(Reply(ocr, "VEC\n119"));

            Assert.AreEqual("VEC\n119", text);
            Assert.AreEqual(IndoorRoomMatchStatus.Matched, match.Status);
            Assert.AreEqual("VEC119", match.Landmark.Id);
            ocr.OnMlKitText(Reply(ocr, "{\"text\":\"VEC 120\"}"));
            Assert.AreEqual("VEC120", match.Landmark.Id);
            ocr.OnMlKitText(Reply(ocr, "{\"text\":\"\"}"));
            Assert.AreEqual(IndoorRoomMatchStatus.NoText, match.Status);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, "[IndoorAr OCR] ML Kit unavailable: recognizer_unavailable");
            ocr.OnMlKitError(Reply(ocr, "recognizer_unavailable"));
            Assert.AreEqual(IndoorRoomMatchStatus.RecognizerUnavailable, match.Status);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void BothPlatformAdaptersReportNoTextAndUnavailableWithoutAnotherRecognizer()
    {
        var host = new GameObject("OCR status test");
        try
        {
            IndoorRoomMatch iosMatch = null;
            var ios = host.AddComponent<IndoorRoomMlKitOcr>();
            ios.Result += (_, result, _, _) => iosMatch = result;
            ios.OnMlKitText(Reply(ios, "{\"text\":\"\"}"));
            Assert.AreEqual(IndoorRoomMatchStatus.NoText, iosMatch.Status);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                "[IndoorAr OCR] ML Kit unavailable: recognizer_unavailable");
            ios.OnMlKitError(Reply(ios, "recognizer_unavailable"));
            Assert.AreEqual(IndoorRoomMatchStatus.RecognizerUnavailable, iosMatch.Status);

            IndoorRoomMatch androidMatch = null;
            var android = host.AddComponent<IndoorRoomOcrDemo>();
            android.Result += (_, result, _, _) => androidMatch = result;
            android.OnMlKitText(Reply(android, "{\"text\":\"\"}"));
            Assert.AreEqual(IndoorRoomMatchStatus.NoText, androidMatch.Status);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                "[IndoorAr OCR] recognition failed: recognizer_unavailable");
            android.OnMlKitError(Reply(android, "recognizer_unavailable"));
            Assert.AreEqual(IndoorRoomMatchStatus.RecognizerUnavailable, androidMatch.Status);
        }
        finally { Object.DestroyImmediate(host); }
    }
}
