using IndoorAr;
using NUnit.Framework;
using UnityEngine;

// The user's confirmed starting floor, vouched for by the host's pressure summary. It only vetoes a MultiSet pose on
// another floor while it is trusted, and gives way to a sign, tracked motion, pressure, or repeated disagreement.
public class IndoorStartFloorTests
{
    const float Step = 0.47f;
    static readonly (int Floor, float Y)[] Floors = { (1, -2f), (2, 2.2f) };
    const float Floor1CameraY = 0f, Floor2CameraY = 4f;
    float _pressureTime;
    static readonly IndoorRoomLandmark Sign215 = new IndoorRoomLandmark("VEC215", "VEC", "floor_2");
    const string HostRequest = "{\"schemaVersion\":2,\"room\":\"215\",\"doorKey\":\"VEC215\",\"floor\":\"2\"," +
        "\"position\":{\"x\":1,\"y\":2,\"z\":3},\"headingDeg\":0,\"mapCode\":\"MSET_X\",\"frame\":\"multiset-mapset\"," +
        "\"floorEvidenceVersion\":1,\"startFloor\":\"1\",\"startFloorAgeS\":42,\"pressureSinceStart\":{\"settled\":true,\"moved\":false,\"sampleAgeS\":1.5}}";

    static IndoorArRequest Request(string startFloor = "1", float ageS = 30f, bool settled = true, bool moved = false,
        float sampleAgeS = 1f, bool summary = true, int version = 1, double? deltaHpa = null) => new IndoorArRequest
    {
        schemaVersion = 2, room = "215", doorKey = "VEC215", floor = "2",
        position = new IndoorArVector3 { x = 1f, y = 2f, z = 3f }, headingDeg = 0f, mapCode = "MSET_X", frame = "multiset-mapset",
        floorEvidenceVersion = version, startFloor = startFloor, startFloorAgeS = ageS,
        pressureSinceStart = summary ? new IndoorArPressureSummary
        {
            settled = settled, moved = moved, sampleAgeS = sampleAgeS, deltaHpa = deltaHpa ?? 0, deltaValid = deltaHpa.HasValue
        } : null,
    };

    IndoorArSession Localizing(IndoorArRequest request)
    {
        _pressureTime = 0f;
        var s = new IndoorArSession(request, "MSET_X") { PressureClock = () => _pressureTime };
        s.OnArSupport(true);
        s.OnAuthenticated();
        return s;
    }

    void Feed(IndoorArSession s, float level, float seconds)
    {
        for (var t = 0f; t < seconds; t += 1f)
        {
            _pressureTime += 1f;
            s.OnPressure(level + (t % 2 == 0 ? 0.01f : -0.01f), _pressureTime);
        }
    }

    [Test]
    public void AWrongFloorMultiSetMatchIsRejectedWhenTheUserConfirmedAnotherFloorWithUnchangedPressure()
    {
        var s = Localizing(Request());
        Assert.AreEqual(1, s.TrustedStartFloor);
        Assert.IsTrue(s.PoseConflictsWithSign(Floor2CameraY, Floors), "the cold-start wrong-floor match");
        Assert.IsFalse(s.PoseConflictsWithSign(Floor1CameraY, Floors));
    }

    [Test]
    public void TheDestinationFloorIsNeverAStartFloor()
    {
        var s = Localizing(Request(startFloor: null));
        Assert.AreEqual(0, s.TrustedStartFloor);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors), "same as today without evidence");
    }

    [TestCase("0", 30f, true, false, 1f, true, TestName = "floor zero")]
    [TestCase("abc", 30f, true, false, 1f, true, TestName = "not a number")]
    [TestCase("1", 30f, true, true, 1f, true, TestName = "pressure moved")]
    [TestCase("1", 30f, false, false, 1f, true, TestName = "pressure not settled")]
    [TestCase("1", 30f, true, false, 1f, false, TestName = "no pressure summary")]
    [TestCase("1", 1300f, true, false, 1f, true, TestName = "confirmation too old")]
    [TestCase("1", -5f, true, false, 1f, true, TestName = "negative age")]
    [TestCase("1", 30f, true, false, 60f, true, TestName = "stale pressure sample")]
    [TestCase("1", float.NaN, true, false, 1f, true)]
    [TestCase("1", 30f, true, false, float.NaN, true)]
    [TestCase("1", float.PositiveInfinity, true, false, 1f, true)]
    [TestCase("1", 30f, true, false, float.NegativeInfinity, true)]
    public void AnUnvouchedStartFloorIsIgnored(string floor, float ageS, bool settled, bool moved, float sampleAgeS, bool summary)
    {
        var s = Localizing(Request(floor, ageS, settled, moved, sampleAgeS, summary));
        Assert.AreEqual(0, s.TrustedStartFloor);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [TestCase(0, TestName = "version missing")]
    [TestCase(2, TestName = "future version")]
    public void AnUnknownEvidenceVersionIsIgnored(int version)
    {
        var s = Localizing(Request(version: version));
        Assert.AreEqual(0, s.TrustedStartFloor);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [Test]
    public void AWrongConfirmationCostsTwoAttemptsNotTheSession()
    {
        var s = Localizing(Request()); // user said floor 1 but is on floor 2
        s.OnAttemptStarted();
        s.NoteStartFloorConflict();
        s.NoteStartFloorConflict(); // same attempt counts once
        Assert.AreEqual(1, s.TrustedStartFloor);
        s.OnAttemptStarted();
        s.NoteStartFloorConflict();
        Assert.AreEqual(0, s.TrustedStartFloor);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors), "back to today's behaviour");
    }

    [Test]
    public void PressureThatMovesAfterLaunchDropsTheStartFloor()
    {
        var s = Localizing(Request());
        s.OnLocalized(); // Pinned has no timeout, so the long trace does not end the session
        Feed(s, 0f, 40f);
        Assert.AreEqual(1, s.TrustedStartFloor, "unchanged keeps it");
        _pressureTime += 25f;
        Feed(s, -Step, 40f);
        Assert.AreEqual(0, s.TrustedStartFloor);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [Test]
    public void NoSensorAfterLaunchKeepsTheHostsUnchangedClaim()
    {
        var s = Localizing(Request());
        s.Tick(300f);
        Assert.AreEqual(1, s.TrustedStartFloor);
    }

    [Test]
    public void AConfirmedSignTakesOverFromTheStartFloorForGood()
    {
        var s = Localizing(Request());
        s.OnLocalized();
        Feed(s, 0f, 30f);
        s.OnSignConfirmed(Sign215);
        Assert.AreEqual(0, s.TrustedStartFloor);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors), "the sign's floor wins");
        Assert.IsTrue(s.PoseConflictsWithSign(Floor1CameraY, Floors));
        Feed(s, 0f, 30f);
        Feed(s, -Step, 40f); // sign released by pressure
        Assert.IsNull(s.FloorSign);
        Assert.AreEqual(0, s.TrustedStartFloor, "the start floor does not come back");
    }

    [Test]
    public void TrackedMotionToAnotherFloorDropsTheStartFloor()
    {
        var s = Localizing(Request());
        s.OnLocalized();
        s.OnTrackedPose(Floor1CameraY, Floors);
        Assert.AreEqual(1, s.TrustedStartFloor);
        s.OnTrackedPose(Floor2CameraY, Floors);
        Assert.AreEqual(0, s.TrustedStartFloor);
    }

    [Test]
    public void StartFloorFieldsParseFromTheHostRequestAndLegacyHostsStillParse()
    {
        Assert.IsTrue(IndoorArContract.TryParseRequest(HostRequest, out var request));
        Assert.AreEqual(1, Localizing(request).TrustedStartFloor);
        Assert.IsTrue(IndoorArContract.TryParseRequest(IndoorArBridge.EditorRequestJson, out var legacy));
        Assert.AreEqual(0, Localizing(legacy).TrustedStartFloor, "an old host sends nothing and nothing changes");
    }

    [TestCase(",\"floorEvidenceVersion\":1", "")]
    [TestCase(",\"startFloor\":\"1\"", "")]
    [TestCase(",\"startFloorAgeS\":42", "")]
    [TestCase(",\"moved\":false", "")]
    [TestCase(",\"sampleAgeS\":1.5", "")]
    [TestCase("\"settled\":true,", "")]
    [TestCase("\"settled\":true,\"moved\":false,\"sampleAgeS\":1.5", "\"settled\":true")]
    [TestCase("\"startFloorAgeS\":42", "\"startFloorAgeS\":\"NaN\"")]
    [TestCase("\"sampleAgeS\":1.5", "\"sampleAgeS\":\"Infinity\"")]
    public void IncompleteOrInvalidHostEvidenceDoesNotVetoAPose(string field, string replacement)
    {
        Assert.IsTrue(IndoorArContract.TryParseRequest(HostRequest.Replace(field, replacement), out var request));
        var session = Localizing(request);
        Assert.AreEqual(0, session.TrustedStartFloor);
        Assert.IsFalse(session.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [Test]
    public void OnlyTheFirstAttemptIsHintedWithTheTrustedStartFloor()
    {
        var s = Localizing(Request(deltaHpa: 0.05));
        Assert.AreEqual(1, s.HintFloor(0));
        s.OnAttemptStarted();
        Assert.AreEqual(0, s.HintFloor(0), "every later request is unhinted");
    }

    [Test]
    public void AFailedHintedAttemptIsRetriedUnhintedWithoutWaitingForTheTimeout()
    {
        var s = Localizing(Request(deltaHpa: 0.05));
        var retried = 0;
        s.LocalizeRequested += () => retried++;
        Assert.AreEqual(1, s.HintFloor(0));
        s.OnAttemptStarted();
        s.OnNotLocalized();
        s.Tick(IndoorArSession.NoPoseRetryS);
        Assert.AreEqual(1, retried);
        Assert.AreEqual(0, s.HintFloor(0));
    }

    [Test]
    public void TheDestinationFloorNeverBecomesAHint()
    {
        Assert.AreEqual(0, Localizing(Request(startFloor: null, deltaHpa: 0.05)).HintFloor(0), "destination is floor 2");
    }

    [TestCase(true, true, 30f, TestName = "no hint: pressure moved")]
    [TestCase(false, false, 30f, TestName = "no hint: pressure not settled")]
    [TestCase(true, false, 1300f, TestName = "no hint: confirmation too old")]
    public void UnvouchedStartEvidenceGivesNoHint(bool settled, bool moved, float ageS)
    {
        var delta = settled && !moved ? 0.05 : (double?)null;
        Assert.AreEqual(0, Localizing(Request(settled: settled, moved: moved, ageS: ageS, deltaHpa: delta)).HintFloor(0));
    }

    [Test]
    public void PressureMovedBeforeTheFirstAttemptDropsTheHint()
    {
        var s = Localizing(Request(deltaHpa: 0.05));
        Assert.AreEqual(1, s.HintFloor(0));
        Feed(s, 0f, 30f);
        _pressureTime += 25f;
        Feed(s, -Step, 40f);
        Assert.AreEqual(0, s.HintFloor(0));
    }

    [TestCase("-0.47", "1", 2)]
    [TestCase("0.47", "2", 1)]
    [TestCase("-0.30", "1", 2)]
    [TestCase("0.30", "2", 1)]
    [TestCase("-0.95", "1", 3)]
    [TestCase("-1.15", "1", 0)]
    [TestCase("1.15", "4", 0)]
    [TestCase("-0.67", "1", 0)]
    [TestCase("0.47", "1", 0)]
    [TestCase("-0.65", "1", 2)]
    [TestCase("0.65", "4", 3)]
    [TestCase("-0.65000001", "1", 0)]
    [TestCase("0.65000001", "4", 0)]
    [TestCase("-0.69999999", "1", 0)]
    [TestCase("0.69999999", "4", 0)]
    [TestCase("-0.70", "1", 3)]
    [TestCase("0.70", "4", 2)]
    [TestCase("-1.09999999", "1", 3)]
    [TestCase("1.09999999", "4", 2)]
    [TestCase("-1.10", "1", 0)]
    [TestCase("1.10", "4", 0)]
    [TestCase("-1.20", "1", 0)]
    [TestCase("1.20", "4", 0)]
    [TestCase("-1.20000001", "1", 4)]
    [TestCase("1.20000001", "4", 1)]
    public void ParsedPressureDeltaMustFitExactlyOneStoreyCount(string delta, string start, int hintFloor)
    {
        var json = HostRequest.Replace("\"startFloor\":\"1\"", $"\"startFloor\":\"{start}\"")
            .Replace("\"moved\":false", $"\"moved\":true,\"deltaHpa\":{delta}");
        Assert.IsTrue(IndoorArContract.TryParseRequest(json, out var request));
        var session = Localizing(request);
        Assert.IsTrue(request.pressureSinceStart.deltaValid);
        Assert.AreEqual(hintFloor, session.HintFloor(0));
        Assert.AreEqual(0, session.TrustedStartFloor);
        session.OnSignConfirmed(Sign215);
        Assert.AreEqual(0, session.HintFloor(0));
    }

    [TestCase("false", "-0.47")]
    [TestCase("false", "0.30")]
    [TestCase("true", "-0.299999999")]
    [TestCase("true", "-3.01")]
    [TestCase("false", "null")]
    [TestCase("false", "\"-0.47\"")]
    public void InvalidSuppliedDeltaOnlyDisablesTheHeightHint(string moved, string delta)
    {
        var json = HostRequest.Replace("\"moved\":false", $"\"moved\":{moved},\"deltaHpa\":{delta}");
        Assert.IsTrue(IndoorArContract.TryParseRequest(json, out var request));
        var session = Localizing(request);
        Assert.AreEqual(0, session.HintFloor(0));
        Assert.IsFalse(request.pressureSinceStart.deltaValid);
        Assert.AreEqual(moved == "false" ? 1 : 0, session.TrustedStartFloor);
        Assert.IsTrue(session.PressureSamplingAllowed);
    }

    [TestCase(1, 1, "\"settled\":true,\"deltaHpa\":-0.47")]
    [TestCase(2, 2, "\"settled\":true,\"deltaHpa\":-0.47")]
    [TestCase(2, 2, "\"moved\":true,\"deltaHpa\":-0.47")]
    [TestCase(2, 1, "\"settled\":true,\"deltaHpa\":-0.47")]
    [TestCase(2, 1, "\"settled\":true,\"moved\":\"true\",\"deltaHpa\":-0.47")]
    public void UnsupportedAndIncompleteEvidenceIsIgnored(int schema, int version, string summary)
    {
        var json = HostRequest.Replace("\"schemaVersion\":2", $"\"schemaVersion\":{schema}")
            .Replace("\"floorEvidenceVersion\":1", $"\"floorEvidenceVersion\":{version}")
            .Replace("\"settled\":true,\"moved\":false", summary);
        Assert.IsTrue(IndoorArContract.TryParseRequest(json, out var request));
        Assert.IsNull(request.pressureSinceStart);
        Assert.AreEqual(0, Localizing(request).HintFloor(0));
    }

    [Test]
    public void DeltaValidFlagCannotComeFromTheWire()
    {
        var json = HostRequest.Replace("\"moved\":false", "\"moved\":true,\"deltaValid\":true");
        Assert.IsTrue(IndoorArContract.TryParseRequest(json, out var request));
        Assert.IsFalse(request.pressureSinceStart.deltaValid);
        Assert.AreEqual(0, Localizing(request).HintFloor(0));
    }
}
