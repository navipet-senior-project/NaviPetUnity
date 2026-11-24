using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorArDiagnosticsTests
{
    // The capture filter keeps a "key=value" token only when the value matches this.
    static readonly Regex Value = new Regex("^[A-Za-z0-9._-]{1,40}$");
    static readonly Regex Float = new Regex(@"-?\d+\.\d+");

    static void AssertFilterSafe(string line)
    {
        StringAssert.StartsWith("[", line);
        var body = line.Substring(line.IndexOf(']') + 1).Trim();
        var tokens = body.Split(' ');
        Assert.IsTrue(tokens.Any(t => !t.Contains("=")), "starts with fixed label words: " + line);
        foreach (var token in tokens.Where(t => t.Contains("=")))
        {
            var pair = token.Split('=');
            Assert.AreEqual(2, pair.Length, token);
            StringAssert.IsMatch("^[A-Za-z]+$", pair[0]);
            Assert.IsTrue(Value.IsMatch(pair[1]), line + " : " + token);
        }
    }

    static List<string> AllLines()
    {
        var floors = new List<(int, float)> { (1, 0f), (2, 4f) };
        var devices = new List<IndoorArDiagnostics.XrDeviceInfo>
        {
            new IndoorArDiagnostics.XrDeviceInfo(true, true, false, true, false)
        };
        var lines = IndoorArDiagnostics.XrInputLines("1s", devices, 1, 1, 0, 3, -1);
        lines.Add(IndoorArDiagnostics.CameraFrozenLine(true, 2100, 340));
        lines.Add(IndoorArDiagnostics.FloorChangeLine(1, 0, IndoorArDiagnostics.EyeHeightBucket(1.7f, 1, floors)));
        lines.Add(IndoorArDiagnostics.TrackingLimitedBeginLine("Relocalizing"));
        lines.Add(IndoorArDiagnostics.TrackingLimitedEndLine(1800, true, "Relocalizing", LimitedEpisodeEnd.TrackingRestored));
        lines.Add(IndoorArDiagnostics.WaypointChurnLine(4));
        lines.Add(IndoorArDiagnostics.RouteBlockedLine(true, IndoorRouteBlockedCause.CameraCellBlocked));
        lines.Add(IndoorArDiagnostics.RepinJumpLine(1.234f, -7.89f));
        lines.Add(IndoorArDiagnostics.PinModeLine(IndoorPinMode.Clipped, 25f, IndoorPinViewClass.InView));
        lines.Add(IndoorArDiagnostics.LocalizationResultLine(IndoorArDiagnostics.MapHash("MSET_ABC"), 2, 5.7f, floors, .59f));
        lines.Add(IndoorArDiagnostics.LocalizationRequestLine(2, IndoorArDiagnostics.LocalizationRequestKind.MultiSet, 2500,
            IndoorArDiagnostics.LocalizationOutcome.Ok, true, 40000, 2400));
        lines.Add(IndoorArDiagnostics.LocalizationRequestLine(1, IndoorArDiagnostics.LocalizationRequestKind.Sign, 15000,
            IndoorArDiagnostics.LocalizationOutcome.Timeout, null, null, null));
        lines.Add(IndoorArDiagnostics.LocalizationRequestLine(1, IndoorArDiagnostics.LocalizationRequestKind.MultiSet, 900,
            IndoorArDiagnostics.LocalizationOutcome.GenericFailure, false, 0, 900, IndoorArDiagnostics.LocalizationHint.Height));
        return lines;
    }

    [Test]
    public void EveryLineIsFixedLabelsWithFilterSafeValues()
    {
        foreach (var line in AllLines()) AssertFilterSafe(line);
    }

    [Test]
    public void LineFormatsAreStable()
    {
        Assert.AreEqual("[IndoorAr] camera frozen=true stillMs=2100 frames=340", IndoorArDiagnostics.CameraFrozenLine(true, 2100, 340));
        Assert.AreEqual("[IndoorAr] floor change from=1 to=unknown eyeM=1.5-2.0",
            IndoorArDiagnostics.FloorChangeLine(1, 0, "1.5-2.0"));
        Assert.AreEqual("[IndoorAr] tracking limited phase=begin notTracking=Relocalizing",
            IndoorArDiagnostics.TrackingLimitedBeginLine("Relocalizing"));
        Assert.AreEqual("[IndoorAr] tracking limited phase=end durationMs=1800 relocalize=true notTracking=Relocalizing end=TrackingRestored",
            IndoorArDiagnostics.TrackingLimitedEndLine(1800, true, "Relocalizing", LimitedEpisodeEnd.TrackingRestored));
        Assert.AreEqual("[IndoorAr] route churn changesPerS=4", IndoorArDiagnostics.WaypointChurnLine(4));
        Assert.AreEqual("[IndoorAr] route blocked=true cause=NoClearLine",
            IndoorArDiagnostics.RouteBlockedLine(true, IndoorRouteBlockedCause.NoClearLine));
        Assert.AreEqual("[IndoorAr] route blocked=false cause=None",
            IndoorArDiagnostics.RouteBlockedLine(false, IndoorRouteBlockedCause.None));
        Assert.AreEqual("[IndoorAr] localization request attempt=2 kind=MultiSet elapsedMs=42500 outcome=Ok lockBusy=true lockWaitMs=40000 sdkMs=2400",
            IndoorArDiagnostics.LocalizationRequestLine(2, IndoorArDiagnostics.LocalizationRequestKind.MultiSet, 42500,
                IndoorArDiagnostics.LocalizationOutcome.Ok, true, 40000, 2400));
        Assert.AreEqual("[IndoorAr] localization request attempt=1 kind=Sign elapsedMs=15000 outcome=Timeout lockBusy=na lockWaitMs=na sdkMs=na",
            IndoorArDiagnostics.LocalizationRequestLine(1, IndoorArDiagnostics.LocalizationRequestKind.Sign, 15000,
                IndoorArDiagnostics.LocalizationOutcome.Timeout, null, null, null));
        Assert.AreEqual("[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=900 outcome=GenericFailure lockBusy=false lockWaitMs=0 sdkMs=900 hint=Height",
            IndoorArDiagnostics.LocalizationRequestLine(1, IndoorArDiagnostics.LocalizationRequestKind.MultiSet, 900,
                IndoorArDiagnostics.LocalizationOutcome.GenericFailure, false, 0, 900, IndoorArDiagnostics.LocalizationHint.Height));
        Assert.AreEqual("[IndoorAr] re-pin jump shiftM=1.23 turnDeg=-7.9", IndoorArDiagnostics.RepinJumpLine(1.234f, -7.89f));
        Assert.AreEqual("[IndoorAr] pin mode=Docked distM=3-6 view=Behind",
            IndoorArDiagnostics.PinModeLine(IndoorPinMode.Docked, 4.27f, IndoorPinViewClass.Behind));
        Assert.AreEqual("0-1", IndoorArDiagnostics.PinDistanceBucket(.99f));
        Assert.AreEqual("10-20", IndoorArDiagnostics.PinDistanceBucket(19.99f));
        Assert.AreEqual("20-up", IndoorArDiagnostics.PinDistanceBucket(20f));
    }

    [Test]
    public void LocalizationResultLineCarriesHashFloorDeltaAndConfidence()
    {
        var floors = new List<(int, float)> { (1, 0f), (3, 8f) };
        Assert.AreEqual("[IndoorAr] localization result map=abcd1234 floor=3 deltaM=1.70 confidence=0.59",
            IndoorArDiagnostics.LocalizationResultLine("abcd1234", 3, 9.7f, floors, .59f));
        Assert.AreEqual("[IndoorAr] localization result map=unavailable floor=unknown deltaM=unknown confidence=unknown",
            IndoorArDiagnostics.LocalizationResultLine("unavailable", 0, 9.7f, floors, null));
        Assert.AreEqual("[IndoorAr] localization result map=unavailable floor=unsupported deltaM=unknown confidence=0.10",
            IndoorArDiagnostics.LocalizationResultLine("unavailable", -1, 30f, floors, .1f));
    }

    [Test]
    public void MapHashIsStableShortAndNotTheCode()
    {
        var hash = IndoorArDiagnostics.MapHash("MSET_ABC");
        StringAssert.IsMatch("^[0-9a-f]{8}$", hash);
        Assert.AreEqual(hash, IndoorArDiagnostics.MapHash("MSET_ABC"));
        Assert.AreNotEqual(hash, IndoorArDiagnostics.MapHash("MSET_ABD"));
        Assert.AreEqual("unavailable", IndoorArDiagnostics.MapHash(""));
        Assert.AreEqual("unavailable", IndoorArDiagnostics.MapHash(null));
    }

    [Test]
    public void Dx1ListsDevicesWithoutFreeText()
    {
        var devices = new[]
        {
            new IndoorArDiagnostics.XrDeviceInfo(false, false, false, false, false)
        };
        var lines = IndoorArDiagnostics.XrInputLines("start", devices, 0, 0, 0, -1, -1);
        Assert.AreEqual(2, lines.Count);
        StringAssert.Contains("at=start legacyDevices=1 inputSystemDevices=0 inputSystemAdded=0 inputSystemRemoved=0 positionControls=none rotationControls=none", lines[0]);
        StringAssert.Contains("valid=false centerEyePos=false", lines[1]);
        foreach (var line in lines) AssertFilterSafe(line);
    }

    [Test]
    public void Dx1CapsDeviceLines()
    {
        var devices = Enumerable.Range(0, 20).Select(i => new IndoorArDiagnostics.XrDeviceInfo(true, true, true, true, true)).ToList();
        var lines = IndoorArDiagnostics.XrInputLines("1s", devices, 20, 0, 0, 1, 1);
        Assert.AreEqual(1 + IndoorArDiagnostics.MaxXrDevices, lines.Count);
        StringAssert.Contains("legacyDevices=20", lines[0]);
    }

    [Test]
    public void TokenKeepsOnlyFilterSafeCharactersAndLength()
    {
        Assert.AreEqual("none", IndoorArDiagnostics.Token(null));
        Assert.AreEqual("none", IndoorArDiagnostics.Token("  "));
        Assert.AreEqual("a.b.c", IndoorArDiagnostics.Token(" a b!c "));
        Assert.AreEqual(40, IndoorArDiagnostics.Token(new string('x', 100)).Length);
        Assert.IsTrue(Value.IsMatch(IndoorArDiagnostics.Token("é=\n; \"quoted\" (x, y)")));
    }

    [Test]
    public void LinesCarryNoPositionOrFreeText()
    {
        // Floats appear only as shiftM/turnDeg (a distance and an angle) and the eye-height bucket, never a coordinate.
        foreach (var line in AllLines())
        {
            var body = Float.Replace(line, "N");
            Assert.IsFalse(Regex.IsMatch(body, @"(^|\s)(x|y|z|pos|position|key|token|text|image)="), line);
            Assert.IsFalse(Regex.IsMatch(line, @"\(|\)|,"), line);
        }
    }

    [Test]
    public void EyeHeightBucketUsesTheReportedFloor()
    {
        var floors = new List<(int Floor, float Y)> { (1, 0f), (2, 4f) };
        Assert.AreEqual("1.5-2.0", IndoorArDiagnostics.EyeHeightBucket(1.7f, 1, floors));
        Assert.AreEqual("0.5-1.0", IndoorArDiagnostics.EyeHeightBucket(4.6f, 2, floors));
        Assert.AreEqual("4.0-up", IndoorArDiagnostics.EyeHeightBucket(4.1f, 1, new List<(int, float)> { (1, 0f) }));
        Assert.AreEqual("below", IndoorArDiagnostics.EyeHeightBucket(-1f, 1, floors));
        Assert.AreEqual("below", IndoorArDiagnostics.EyeHeightBucket(1f, 2, floors));
        Assert.AreEqual("unknown", IndoorArDiagnostics.EyeHeightBucket(1f, 3, floors));
        Assert.AreEqual("unknown", IndoorArDiagnostics.EyeHeightBucket(1f, 0, floors));
    }

    [Test]
    public void FrozenNeedsTrackingFramesAndAStillCamera()
    {
        var d = new FrozenCameraDetector();
        var p = new Vector3(1, 2, 3);
        var q = Quaternion.identity;
        var frames = 0;
        var changed = false;
        for (var i = 0; i < 25; i++) changed |= d.Observe(.1f, p, q, ++frames, true);
        Assert.IsTrue(changed);
        Assert.IsTrue(d.Frozen);
        Assert.GreaterOrEqual(d.StillMs, 2000);
        Assert.IsTrue(d.Observe(.1f, p + Vector3.right * .01f, q, ++frames, true), "movement clears it");
        Assert.IsFalse(d.Frozen);
    }

    [Test]
    public void StoppedFramesOrLostTrackingAreNotFrozen()
    {
        var d = new FrozenCameraDetector();
        var p = Vector3.zero;
        for (var i = 0; i < 40; i++) d.Observe(.1f, p, Quaternion.identity, 7, true); // frames never advance
        Assert.IsFalse(d.Frozen);
        d.Reset();
        var frames = 0;
        for (var i = 0; i < 40; i++) d.Observe(.1f, p, Quaternion.identity, ++frames, false);
        Assert.IsFalse(d.Frozen);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InterruptedStillnessRequiresANewInterval(bool stopFrames)
    {
        var detector = new FrozenCameraDetector();
        var frames = 0;
        for (var i = 0; i < 15; i++) detector.Observe(.1f, Vector3.zero, Quaternion.identity, ++frames, true);
        for (var i = 0; i < 25; i++) detector.Observe(.1f, Vector3.zero, Quaternion.identity, stopFrames ? frames : ++frames, stopFrames);
        detector.Observe(.1f, Vector3.zero, Quaternion.identity, ++frames, true);
        Assert.IsFalse(detector.Frozen);
        for (var i = 0; i < 21; i++) detector.Observe(.1f, Vector3.zero, Quaternion.identity, ++frames, true);
        Assert.IsTrue(detector.Frozen);
    }

    [Test]
    public void SmallTurnAloneIsNotFrozen()
    {
        var d = new FrozenCameraDetector();
        var frames = 0;
        for (var i = 0; i < 40; i++) d.Observe(.1f, Vector3.zero, Quaternion.Euler(0, i * .5f, 0), ++frames, true);
        Assert.IsFalse(d.Frozen);
    }

    [Test]
    public void StationaryUserWithPoseNoiseIsNotFrozen()
    {
        var d = new FrozenCameraDetector();
        var frames = 0;
        for (var i = 0; i < 100; i++) // 10 s standing still: the pose still wobbles by a millimetre
            d.Observe(.1f, new Vector3(0f, 1.5f, 0f) + Vector3.right * (i % 2 * .001f), Quaternion.identity, ++frames, true);
        Assert.IsFalse(d.Frozen);
        Assert.AreEqual(0, d.StillMs);
    }

    [Test]
    public void FrozenStartsAtTwoSecondsNotBefore()
    {
        var d = new FrozenCameraDetector();
        var frames = 0;
        var observations = 0;
        while (!d.Frozen && observations < 100)
        {
            d.Observe(.1f, Vector3.one, Quaternion.identity, ++frames, true);
            observations++;
            if (!d.Frozen) Assert.Less(d.StillMs, 2000);
        }
        Assert.IsTrue(d.Frozen);
        Assert.That(d.StillMs, Is.InRange(2000, 2100));
    }

    [Test]
    public void LostTrackingOrStoppedFramesReleaseAFrozenCameraAndItNeedsANewInterval()
    {
        foreach (var release in new[] { "tracking", "frames" })
        {
            var d = new FrozenCameraDetector();
            var frames = 0;
            for (var i = 0; i < 25; i++) d.Observe(.1f, Vector3.one, Quaternion.identity, ++frames, true);
            Assert.IsTrue(d.Frozen, release);
            for (var i = 0; i < 10; i++)
                d.Observe(.1f, Vector3.one, Quaternion.identity, release == "frames" ? frames : ++frames, release == "frames");
            Assert.IsFalse(d.Frozen, release);
            for (var i = 0; i < 15; i++) d.Observe(.1f, Vector3.one, Quaternion.identity, ++frames, true);
            Assert.IsFalse(d.Frozen, release + ": the old stillness does not carry over");
        }
    }

    [Test]
    public void FloorTransitionIsReportedOnlyOnChange()
    {
        var t = new FloorTransitionTracker();
        Assert.IsFalse(t.Observe(1, out _));
        Assert.IsFalse(t.Observe(1, out _));
        Assert.IsTrue(t.Observe(0, out var from));
        Assert.AreEqual(1, from);
        Assert.IsTrue(t.Observe(2, out from));
        Assert.AreEqual(0, from);
        t.Reset();
        Assert.IsFalse(t.Observe(3, out _));
    }

    [Test]
    public void LimitedEpisodeHasDurationAndRelocalizeFlag()
    {
        var t = new LimitedEpisodeTracker();
        Assert.AreEqual(LimitedEpisodePhase.None, t.Observe(false, false, false, .1f), "not pinned: no episode");
        Assert.AreEqual(LimitedEpisodePhase.Begin, t.Observe(false, true, false, .1f));
        Assert.AreEqual(LimitedEpisodePhase.None, t.Observe(false, false, false, .6f));
        Assert.AreEqual(LimitedEpisodePhase.None, t.Observe(false, false, true, 1.1f));
        Assert.AreEqual(LimitedEpisodePhase.End, t.Observe(true, false, true, 1.2f));
        Assert.AreEqual(1100, t.DurationMs, 1);
        Assert.IsTrue(t.Relocalized);
        Assert.AreEqual(LimitedEpisodePhase.None, t.Observe(true, true, false, .1f));
        t.Observe(false, true, false, 2f);
        t.Observe(true, false, false, 2.1f);
        Assert.AreEqual(100, t.DurationMs, 1);
        Assert.IsFalse(t.Relocalized, "a short dip that never relocalized");
    }

    [Test]
    public void LimitedEpisodeEndCountsElapsedTimeWithoutDoubleCounting()
    {
        var tracker = new LimitedEpisodeTracker();
        tracker.Observe(false, true, false, 10f);
        tracker.Observe(false, false, true, 10.5f);
        tracker.End(11f);
        Assert.AreEqual(1000, tracker.DurationMs);
        Assert.IsTrue(tracker.Relocalized);
        tracker.End(12f);
        Assert.AreEqual(1000, tracker.DurationMs);
    }

    [Test]
    public void ChurnCountsVisibleIndexChangesAcrossReroutes()
    {
        var m = new WaypointChurnMeter();
        int n;
        Assert.IsFalse(m.Observe(1, .25f, out n));
        Assert.IsFalse(m.Observe(2, .25f, out n));
        Assert.IsFalse(m.Observe(1, .25f, out n));
        Assert.IsTrue(m.Observe(2, .25f, out n));
        Assert.AreEqual(3, n);
        Assert.IsFalse(m.Observe(-1, .25f, out n));
        Assert.IsFalse(m.Observe(0, .25f, out n));
        Assert.IsTrue(m.Observe(1, .5f, out n));
        Assert.AreEqual(1, n);
    }

    [Test]
    public void RouteBlockReportsTransitionsAndCauseChanges()
    {
        var t = new RouteBlockTracker();
        Assert.IsFalse(t.Observe(false, IndoorRouteBlockedCause.None));
        Assert.IsTrue(t.Observe(true, IndoorRouteBlockedCause.NoRoute));
        Assert.IsFalse(t.Observe(true, IndoorRouteBlockedCause.NoRoute));
        Assert.IsTrue(t.Observe(true, IndoorRouteBlockedCause.CameraCellBlocked));
        Assert.IsTrue(t.Observe(false, IndoorRouteBlockedCause.CameraCellBlocked));
        Assert.IsFalse(t.Observe(false, IndoorRouteBlockedCause.None));
    }

    // ---- wiring in the runner ---------------------------------------------------------------------------------

    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    [TestCase("Deliver", LimitedEpisodeEnd.SessionEnded)]
    [TestCase("BeginSession", LimitedEpisodeEnd.SessionReplaced)]
    [TestCase("OnDestroy", LimitedEpisodeEnd.Destroyed)]
    public void RunnerClosesLimitedEpisodeAtSessionBoundary(string boundary, LimitedEpisodeEnd end)
    {
        var host = new GameObject("Limited episode test");
        var previousRequest = IndoorArBridge.EditorRequestJson;
        var runner = host.AddComponent<IndoorArRunner>();
        var lines = new List<string>();
        runner.DiagnosticSink = lines.Add;
        var tracker = Field<LimitedEpisodeTracker>(runner, "_limited");
        var now = Time.realtimeSinceStartup;
        tracker.Observe(false, true, false, now - 1f);
        tracker.Observe(false, false, true, now - .5f);
        try
        {
            if (boundary == "OnDestroy") Call(runner, boundary);
            else if (boundary == "BeginSession") Call(runner, boundary, null, runner.RequestClock());
            else Call(runner, boundary, (object)null);
            var line = lines.Single();
            StringAssert.Contains("relocalize=true", line);
            Assert.GreaterOrEqual(long.Parse(line.Split(' ').Single(part => part.StartsWith("durationMs=")).Substring(11)), 999);
            StringAssert.Contains("end=" + end, line);
            AssertFilterSafe(line);
            if (host != null) Object.DestroyImmediate(host);
            Assert.AreEqual(1, lines.Count);
        }
        finally
        {
            if (host != null) Object.DestroyImmediate(host);
            IndoorArBridge.EditorRequestJson = previousRequest;
        }
    }

    [Test]
    public void RunnerReportsLimitedEpisodeAndRepinJumpThroughTheSink()
    {
        var request = "{\"schemaVersion\":1,\"room\":\"119\",\"doorKey\":\"VEC119\",\"floor\":\"1\"," +
            "\"position\":{\"x\":-5.7651,\"y\":-1.8186,\"z\":22.6689},\"headingDeg\":185.36," +
            "\"mapCode\":\"MSET_XTBY7AM7ZK77\",\"frame\":\"multiset-mapset\"}";
        IndoorArBridge.EditorRequestJson = request;
        var state = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        var host = new GameObject("Diagnostics runner test");
        var pin = new GameObject("Pin");
        try
        {
            host.AddComponent<RunnerFakeLocalizer>();
            var runner = host.AddComponent<IndoorArRunner>();
            var camera = new GameObject("AR Camera").AddComponent<Camera>();
            camera.transform.SetParent(host.transform, false);
            camera.transform.localPosition = new Vector3(-5.625f, -0.4f, 23.625f);
            runner.Configure(host.GetComponent<RunnerFakeLocalizer>(), pin, camera);
            var lines = new List<string>();
            runner.DiagnosticSink = lines.Add;
            Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
            var session = Field<IndoorArSession>(runner, "_session");
            session.OnArSupport(true);
            Call(runner, "Update");
            session.OnAuthenticated();
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            Call(runner, "Update");
            Assert.AreEqual(1, lines.RemoveAll(l => l.StartsWith("[IndoorAr] pin mode=")), "the first pin placement logs its mode once");
            Assert.AreEqual(1, lines.Count, "a steady pin logs only the localization result");
            StringAssert.StartsWith("[IndoorAr] localization result map=unavailable floor=1 deltaM=1.60 confidence=0.73", lines[0]);
            lines.Clear();

            host.GetComponent<RunnerFakeLocalizer>().DeferAnswer = true;
            state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.None });
            Call(runner, "Update");
            Assert.AreEqual(1, lines.Count(l => l.StartsWith("[IndoorAr] tracking limited phase=begin")));

            host.transform.position += new Vector3(1.5f, 0f, 0f);
            host.GetComponent<RunnerFakeLocalizer>().CompleteLocalized(0.8f);
            state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
            Call(runner, "Update");
            Assert.IsTrue(lines.Any(l => l.StartsWith("[IndoorAr] re-pin jump shiftM=1.50 ")), string.Join("\n", lines));
            var end = lines.Single(l => l.StartsWith("[IndoorAr] tracking limited phase=end"));
            StringAssert.Contains("relocalize=true", end);
            foreach (var line in lines) AssertFilterSafe(line);
        }
        finally
        {
            state.Invoke(null, new object[] { previous });
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(pin);
        }
    }
}
