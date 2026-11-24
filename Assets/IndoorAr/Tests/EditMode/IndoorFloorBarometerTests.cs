using IndoorAr;
using NUnit.Framework;

// Synthetic traces shaped by aggregate measurements (storey step ~0.42-0.53 hPa, arrival overshoot ~0.15, posture shift
// ~0.12, drift ~0.3 hPa/h, 1.07 s sample interval). No recorded values are stored here.
public class IndoorFloorBarometerTests
{
    const float Dt = 1.07f;
    const float Step = 0.47f;

    // Deterministic +-noise so a "settled" hold is not a flat line.
    static float Noise(int i, float amp) => (i % 4) switch { 0 => amp, 1 => -amp, 2 => amp / 2, _ => -amp / 2 };

    sealed class Rig
    {
        public readonly IndoorFloorBarometer B = new IndoorFloorBarometer();
        public float T;
        int _i;

        public void Hold(float level, float seconds, float noise = 0.01f)
        {
            for (var end = T + seconds; T < end; T += Dt) B.Sample(level + Noise(_i++, noise), T);
        }

        // Pressure ramp while the user moves, sampled like a held hold.
        public void Ramp(float from, float to, float seconds)
        {
            var start = T;
            for (var end = T + seconds; T < end; T += Dt) B.Sample(from + (to - from) * (T - start) / seconds, T);
        }

        public void Silence(float seconds) => T += seconds;
        public IndoorBarometerStatus Status => B.Status(T);
    }

    static Rig AnchoredAt(float level)
    {
        var r = new Rig();
        r.Hold(level, 25f);
        r.B.Anchor(r.T);
        return r;
    }

    [Test]
    public void SameFloorNoiseStaysUnchanged()
    {
        var r = AnchoredAt(0f);
        r.Hold(0.02f, 120f, 0.02f);
        Assert.AreEqual(IndoorBarometerStatus.Unchanged, r.Status);
    }

    [TestCase(-Step, "stairs up one storey")]
    [TestCase(Step, "stairs down one storey")]
    [TestCase(-2 * Step, "two storeys")]
    public void StairsWithTravelGapAreMovedOnceSettledAtTheNewLevel(float delta, string _)
    {
        var r = AnchoredAt(0f);
        r.Silence(25f); // Start-moving style gap: no samples while travelling
        r.Hold(delta, 10f);
        Assert.AreEqual(IndoorBarometerStatus.Unknown, r.Status, "10 s after arrival is not settled");
        r.Hold(delta, 25f);
        Assert.AreEqual(IndoorBarometerStatus.Moved, r.Status);
    }

    [Test]
    public void ElevatorOvershootAndSeatedRecoveryIsNotTrustedUntilSettled()
    {
        var r = AnchoredAt(0f);
        r.Ramp(0f, -Step - 0.15f, 20f); // cab ride, then overshoot of the final level
        r.Ramp(-Step - 0.15f, -Step, 20f); // sits down, pressure recovers
        Assert.AreEqual(IndoorBarometerStatus.Unknown, r.Status, "still settling");
        r.Hold(-Step, 30f);
        Assert.AreEqual(IndoorBarometerStatus.Moved, r.Status);
    }

    [Test]
    public void PostureShiftAndDriftOnTheSameFloorNeverMove()
    {
        var r = AnchoredAt(0f);
        r.Ramp(0f, -0.10f, 900f); // ~0.4 hPa/h, above the observed 0.3 hPa/h
        r.Hold(-0.10f - 0.12f, 40f); // stands up before leaving: phone ~0.12 hPa lower, same direction: 0.22 total
        Assert.AreEqual(IndoorBarometerStatus.Unchanged, r.Status);
        r.Hold(-0.10f - 0.05f, 40f); // sits again
        Assert.AreEqual(IndoorBarometerStatus.Unchanged, r.Status);
    }

    [Test]
    public void ARealStepHiddenByOpposingErrorIsMissedNotGuessed()
    {
        var r = AnchoredAt(0f);
        r.Hold(-0.415f + 0.22f, 40f); // smallest step seen, offset by worst stacked error
        Assert.AreEqual(IndoorBarometerStatus.Unchanged, r.Status);
    }

    [Test]
    public void WanderingHoldIsNeverSettledSoNeverAnchorsOrMoves()
    {
        var r = new Rig();
        r.Hold(0f, 60f, 0.07f); // 0.14 hPa peak to peak, like the worst measured hold
        r.B.Anchor(r.T);
        Assert.AreEqual(IndoorBarometerStatus.Unanchored, r.Status);
        r.Hold(-Step, 60f, 0.07f);
        Assert.AreEqual(IndoorBarometerStatus.Unanchored, r.Status);
    }

    [Test]
    public void AnchorWhileTravellingIsRefused()
    {
        var r = new Rig();
        r.Ramp(0f, -Step, 20f);
        r.B.Anchor(r.T);
        Assert.AreEqual(IndoorBarometerStatus.Unanchored, r.Status);
    }

    [Test]
    public void ANewAnchorDropsTheOlderBaseline()
    {
        var r = AnchoredAt(0f);
        r.Ramp(0f, -Step, 20f);
        r.B.Anchor(r.T); // unsettled: must not keep comparing against the old level
        r.Hold(-Step, 40f);
        Assert.AreEqual(IndoorBarometerStatus.Unanchored, r.Status);
    }

    [Test]
    public void MissingSensorIsUnanchoredAndLossAfterAnchorIsUnknown()
    {
        var none = new IndoorFloorBarometer();
        none.Anchor(30f);
        Assert.AreEqual(IndoorBarometerStatus.Unanchored, none.Status(30f));

        var r = AnchoredAt(0f);
        r.Silence(IndoorFloorBarometer.MaxSampleGapS + 1f);
        Assert.AreEqual(IndoorBarometerStatus.Unknown, r.Status);
        r.Hold(-Step, 5f); // sensor back, window restarted by the gap
        Assert.AreEqual(IndoorBarometerStatus.Unknown, r.Status);
        r.Hold(-Step, 30f);
        Assert.AreEqual(IndoorBarometerStatus.Moved, r.Status);
    }

    [Test]
    public void SettlementExpiresWhenRecentSamplesNoLongerSpanTheRequiredTime()
    {
        var barometer = new IndoorFloorBarometer();
        for (var t = 1; t <= 21; t++) barometer.Sample(0f, t);
        barometer.Anchor(21f);

        Assert.AreEqual(IndoorBarometerStatus.Unchanged, barometer.Status(23f));
        Assert.AreEqual(IndoorBarometerStatus.Unknown, barometer.Status(29f));
    }

    [Test]
    public void AnOldAnchorIsNotComparedBecauseDriftWasNeverMeasuredBeyondTwentyMinutes()
    {
        var r = AnchoredAt(0f);
        r.Hold(-Step, IndoorFloorBarometer.MaxAnchorAgeS + 60f);
        Assert.AreEqual(IndoorBarometerStatus.Unknown, r.Status);
    }

    [Test]
    public void NonFiniteReadingsAreIgnoredAndASpikeBlocksSettling()
    {
        var r = AnchoredAt(0f);
        r.B.Sample(float.NaN, r.T);
        r.B.Sample(float.PositiveInfinity, r.T);
        r.Hold(0f, 30f);
        Assert.AreEqual(IndoorBarometerStatus.Unchanged, r.Status);
        r.B.Sample(0.5f, r.T);
        Assert.AreEqual(IndoorBarometerStatus.Unknown, r.Status);
    }

    // ---- session integration: pressure may only release a stale sign ----

    static readonly IndoorRoomLandmark Sign120 = new IndoorRoomLandmark("VEC120", "VEC", "floor_1");
    static readonly (int Floor, float Y)[] Floors = { (1, -2f), (2, 2.2f) };
    const float Floor1CameraY = 0f, Floor2CameraY = 4f;
    double _pressureTime;

    IndoorArSession Pinned(bool confirmedFloor = true, float startFloorAgeS = 0f, float sampleAgeS = 0f)
    {
        _pressureTime = 0f;
        var session = new IndoorArSession(new IndoorArRequest
        {
            schemaVersion = 2, room = "119", doorKey = "VEC119", floor = "1",
            position = new IndoorArVector3 { x = -5.7651f, y = -1.8186f, z = 22.6689f },
            headingDeg = 185.36f, mapCode = "MSET_XTBY7AM7ZK77", frame = "multiset-mapset",
            floorEvidenceVersion = confirmedFloor ? IndoorArContract.FloorEvidenceVersion : 0,
            startFloor = confirmedFloor ? "1" : null,
            startFloorAgeS = startFloorAgeS,
            pressureSinceStart = confirmedFloor ? new IndoorArPressureSummary { settled = true, sampleAgeS = sampleAgeS } : null,
        }, "MSET_XTBY7AM7ZK77") { PressureClock = () => _pressureTime };
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnLocalized(); // Pinned has no timeout, so long traces do not end the session
        return session;
    }

    void Advance(IndoorArSession s, float seconds)
    {
        _pressureTime += seconds;
        s.Tick(seconds);
    }

    void Feed(IndoorArSession s, float level, float seconds, float noise = 0.01f)
    {
        for (var t = 0f; t < seconds; t += 1f)
        {
            Advance(s, 1f);
            s.OnPressure(level + Noise((int)t, noise), _pressureTime);
        }
    }

    [TestCase(true, 0f, 0f, true)]
    [TestCase(false, 0f, 0f, false)]
    [TestCase(true, 1201f, 0f, false)]
    [TestCase(true, 0f, 11f, false)]
    public void SettledMoveReleasesTheStaleSignOnlyWithFreshStartFloorConfirmation(
        bool confirmedFloor, float startFloorAgeS, float sampleAgeS, bool releaseSign)
    {
        var s = Pinned(confirmedFloor, startFloorAgeS, sampleAgeS);
        Feed(s, 0f, 30f);
        s.OnSignConfirmed(Sign120);
        Assert.IsTrue(s.PoseConflictsWithSign(Floor2CameraY, Floors), "stale-sign rejection is the bug being fixed");
        Feed(s, 0f, 60f);
        Assert.AreSame(Sign120, s.FloorSign, "same level keeps the sign");
        Advance(s, 25f); // travel, no samples
        Feed(s, -Step, 40f);
        Assert.AreSame(releaseSign ? null : Sign120, s.FloorSign);
        Assert.AreSame(Sign120, s.SignRoom);
        Assert.AreEqual(!releaseSign, s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [Test]
    public void PressureNeverNamesAFloorSoAWrongSignStillWinsOverAPoseAtSameLevel()
    {
        var s = Pinned();
        Feed(s, 0f, 30f);
        s.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2")); // misread: user is on floor 1
        Feed(s, 0f, 120f);
        Assert.IsTrue(s.PoseConflictsWithSign(Floor1CameraY, Floors), "no barometer override of a bad anchor");
    }

    [Test]
    public void NoSensorChangesNothing()
    {
        var s = Pinned();
        s.OnSignConfirmed(Sign120);
        for (var i = 0; i < 600; i++) s.Tick(1f);
        Assert.AreSame(Sign120, s.FloorSign);
    }

    [Test]
    public void SignReadWhileMovingIsNotAnchoredSoLaterPressureCannotSupersedeIt()
    {
        var s = Pinned();
        for (var t = 0f; t < 20f; t++)
        {
            Advance(s, 1f);
            s.OnPressure(-0.02f * t, _pressureTime);
        }
        s.OnSignConfirmed(Sign120);
        Feed(s, -Step, 60f);
        Assert.AreSame(Sign120, s.FloorSign);
    }

    [Test]
    public void SignConfirmedDuringBriefSensorSilenceDoesNotAnchorToThePreviousFloor()
    {
        var s = Pinned();
        Feed(s, 0f, 21f);
        _pressureTime += 8f;
        var sign = new IndoorRoomLandmark("VEC215", "VEC", "floor_2");
        s.OnSignConfirmed(sign);
        Feed(s, -Step, 40f);

        Assert.AreSame(sign, s.FloorSign);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [Test]
    public void RetryAfterChangingFloorsOnTheFailureScreenDropsTheStartFloorAndPressureAnchor()
    {
        var s = Pinned();
        s.OnTrackingLost();
        Advance(s, IndoorArSession.LocalizeTimeoutS);
        Feed(s, 0f, IndoorArSession.LocalizeTimeoutS - 1f);
        Advance(s, 1f);
        Assert.IsTrue(LiveDoorHud.LocalizationFailed(s));
        Assert.IsTrue(s.PoseConflictsWithSign(Floor2CameraY, Floors));

        Feed(s, -Step, 25f);
        Assert.IsTrue(s.PoseConflictsWithSign(Floor2CameraY, Floors));
        s.Retry();
        Assert.AreEqual(IndoorArState.Localizing, s.State);
        Assert.AreEqual(0, s.TrustedStartFloor);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors));
        var sign = new IndoorRoomLandmark("VEC215", "VEC", "floor_2");
        s.OnSignConfirmed(sign);
        s.OnLocalized();
        Feed(s, -Step, 40f);
        Feed(s, 0f, 40f);

        Assert.AreSame(sign, s.FloorSign);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [TestCase(true, false)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void RetryKeepsSignPolicyAndDiscardsOnlyPressureBasedSignRelease(bool confirmedFloor, bool trackedMove)
    {
        var s = Pinned(confirmedFloor);
        Feed(s, 0f, 30f);
        s.OnSignConfirmed(Sign120);
        if (confirmedFloor)
        {
            Feed(s, -Step, 40f);
            Assert.IsNull(s.FloorSign);
            if (trackedMove)
            {
                s.OnTrackedPose(Floor1CameraY, Floors);
                s.OnTrackedPose(Floor2CameraY, Floors);
            }
        }
        s.OnTrackingLost();
        Advance(s, IndoorArSession.LocalizeTimeoutS);
        Advance(s, IndoorArSession.LocalizeTimeoutS);
        Assert.IsTrue(LiveDoorHud.LocalizationFailed(s));
        Feed(s, -Step, 25f);
        Assert.AreSame(confirmedFloor ? null : Sign120, s.FloorSign);

        s.Retry();
        Assert.AreSame(trackedMove ? null : Sign120, s.FloorSign);
        Assert.AreEqual(!trackedMove, s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ReadingBeforeTheNextTickUsesTheCurrentTimeAfterAFrameGap(bool confirmSign)
    {
        var s = Pinned();
        Feed(s, 0f, 30f);
        if (confirmSign) s.OnSignConfirmed(Sign120);
        Advance(s, 25f);
        Feed(s, -Step, 18f);
        s.Tick(1f);

        _pressureTime += 16f;
        s.OnPressure(-Step, _pressureTime);
        Assert.IsTrue(s.PoseConflictsWithSign(Floor2CameraY, Floors));
        Feed(s, -Step, 20f);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DelayedDeliveryCannotHideACaptureGap(bool confirmSign)
    {
        var s = Pinned();
        Feed(s, 0f, 30f);
        if (confirmSign) s.OnSignConfirmed(Sign120);
        for (_pressureTime = 92.5; _pressureTime <= 99.5; _pressureTime++)
            s.OnPressure(-Step, _pressureTime);

        _pressureTime = 101;
        s.OnPressure(-Step, 100.1);
        _pressureTime = 110.5;
        s.OnPressure(-Step, 110.5);
        Assert.IsTrue(s.PoseConflictsWithSign(Floor2CameraY, Floors));

        Feed(s, -Step, 20f);
        Assert.IsFalse(s.PoseConflictsWithSign(Floor2CameraY, Floors));
    }

    [Test]
    public void ARereadSignAfterTheMoveRestoresItAndReanchors()
    {
        var s = Pinned();
        Feed(s, 0f, 30f);
        s.OnSignConfirmed(Sign120);
        Feed(s, -Step, 60f);
        Assert.IsNull(s.FloorSign);
        s.OnSignConfirmed(Sign120);
        Assert.AreSame(Sign120, s.FloorSign);
        Feed(s, -Step, 60f);
        Assert.AreSame(Sign120, s.FloorSign, "the new level is the new baseline");
    }

    [Test]
    public void ReadingsAfterTheSessionIsDoneAreIgnored()
    {
        var s = Pinned();
        s.Cancel();
        s.OnPressure(0f, _pressureTime);
        Assert.AreEqual(IndoorArState.Done, s.State);
    }
}
