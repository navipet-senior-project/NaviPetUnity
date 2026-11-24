using IndoorAr;
using NUnit.Framework;

// The user's confirmed starting floor, vouched for by the host's pressure summary. It only vetoes a MultiSet pose on
// another floor while it is trusted, and gives way to a sign, tracked motion, pressure, or repeated disagreement.
public class IndoorStartFloorTests
{
    const float Step = 0.47f;
    static readonly (int Floor, float Y)[] Floors = { (1, -2f), (2, 2.2f) };
    const float Floor1CameraY = 0f, Floor2CameraY = 4f;
    static readonly IndoorRoomLandmark Sign215 = new IndoorRoomLandmark("VEC215", "VEC", "floor_2");

    static IndoorArRequest Request(string startFloor = "1", float ageS = 30f, bool settled = true, bool moved = false,
        float sampleAgeS = 1f, bool summary = true) => new IndoorArRequest
    {
        schemaVersion = 2, room = "215", doorKey = "VEC215", floor = "2",
        position = new IndoorArVector3 { x = 1f, y = 2f, z = 3f }, headingDeg = 0f, mapCode = "MSET_X", frame = "multiset-mapset",
        startFloor = startFloor, startFloorAgeS = ageS,
        pressureSinceStart = summary ? new IndoorArPressureSummary { settled = settled, moved = moved, sampleAgeS = sampleAgeS } : null,
    };

    static IndoorArSession Localizing(IndoorArRequest request)
    {
        var s = new IndoorArSession(request, "MSET_X");
        s.OnArSupport(true);
        s.OnAuthenticated();
        return s;
    }

    static void Feed(IndoorArSession s, float level, float seconds)
    {
        for (var t = 0f; t < seconds; t += 1f)
        {
            s.Tick(1f);
            s.OnPressure(level + (t % 2 == 0 ? 0.01f : -0.01f));
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
    public void AnUnvouchedStartFloorIsIgnored(string floor, float ageS, bool settled, bool moved, float sampleAgeS, bool summary)
    {
        var s = Localizing(Request(floor, ageS, settled, moved, sampleAgeS, summary));
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
        s.Tick(25f);
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
        const string host = "{\"schemaVersion\":2,\"room\":\"215\",\"doorKey\":\"VEC215\",\"floor\":\"2\"," +
            "\"position\":{\"x\":1,\"y\":2,\"z\":3},\"headingDeg\":0,\"mapCode\":\"MSET_X\",\"frame\":\"multiset-mapset\"," +
            "\"startFloor\":\"1\",\"startFloorAgeS\":42,\"pressureSinceStart\":{\"settled\":true,\"moved\":false,\"sampleAgeS\":1.5}}";
        Assert.IsTrue(IndoorArContract.TryParseRequest(host, out var request));
        Assert.AreEqual(1, Localizing(request).TrustedStartFloor);
        Assert.IsTrue(IndoorArContract.TryParseRequest(host.Replace(",\"startFloor\":\"1\"", ",\"x\":0"), out var noFloor));
        Assert.AreEqual(0, Localizing(noFloor).TrustedStartFloor);
        Assert.IsTrue(IndoorArContract.TryParseRequest(IndoorArBridge.EditorRequestJson, out var legacy));
        Assert.AreEqual(0, Localizing(legacy).TrustedStartFloor, "an old host sends nothing and nothing changes");
    }
}
