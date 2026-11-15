using IndoorAr;
using MultiSet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

public class MultiSetLocalizerTests
{
    static readonly IndoorRoomLandmark West = new IndoorRoomLandmark("VEC1", "VEC", "floor_1", new Vector3(0f, -2f, 20f), 180f);
    static readonly IndoorRoomLandmark East = new IndoorRoomLandmark("VEC2", "VEC", "floor_1", new Vector3(10f, -2f, 20f), 180f);

    GameObject _host;
    GameObject _sdkHost;
    SingleFrameLocalizationManager _manager;
    MultiSetLocalizer _adapter;

    [SetUp]
    public void SetUp()
    {
        _sdkHost = new GameObject("SDK");
        _sdkHost.SetActive(false);
        var sdk = _sdkHost.AddComponent<IndoorArSdkManager>();
        _manager = _sdkHost.AddComponent<SingleFrameLocalizationManager>();
        _host = new GameObject("Adapter");
        _host.SetActive(false);
        _adapter = _host.AddComponent<MultiSetLocalizer>();
        _adapter.Configure(sdk, _manager, _sdkHost.transform);
        _host.SetActive(true);
        typeof(MultiSetLocalizer).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
    }

    [TearDown]
    public void TearDown()
    {
        if (_adapter != null) typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Object.DestroyImmediate(_host);
        Object.DestroyImmediate(_sdkHost);
    }

    [Test]
    public void SessionOwnsSingleFrameAttemptsAndRetries()
    {
        Assert.AreEqual(LocalizationType.Mapset, _manager.localizationType);
        Assert.AreEqual("MSET_XTBY7AM7ZK77", _manager.mapOrMapsetCode);
        Assert.IsFalse(_manager.autoLocalize);
        Assert.IsFalse(_manager.backgroundLocalization);
        Assert.IsFalse(_manager.relocalization);
        Assert.IsFalse(_manager.firstLocalizationUntilSuccess);
        Assert.IsFalse(_manager.confidenceCheck);
        Assert.IsFalse(_manager.showAlert);
        Assert.IsFalse(_manager.enabled);
        Assert.AreSame(_sdkHost.transform, _adapter.MapSpace);
        var mode = typeof(SingleFrameLocalizationManager).GetField("queryMode");
        if (mode != null) Assert.AreEqual("Vps1", mode.GetValue(_manager).ToString());
        else Assert.AreEqual(1, typeof(SingleFrameLocalizationManager).GetField("_requestAttempts").GetValue(_manager));
    }

    [Test]
    public void SdkResponseCarriesConfidenceAndUnsubscribes()
    {
        var localized = 0;
        float? confidence = null;
        _adapter.Localized += value => { localized++; confidence = value; };
        LocalizeWithoutEditorFailure();
        ReportSuccess();
        Assert.AreEqual(1, localized);
        if (typeof(SingleFrameLocalizationManager).GetEvent("OnLocalizationWithResponse") != null)
            Assert.AreEqual(0.73f, confidence);
        else Assert.IsNull(confidence, "legacy SDK has no public response callback");
        // EditMode does not invoke runtime lifecycle callbacks for this test object.
        typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Object.DestroyImmediate(_host);
        ReportSuccess();
        Assert.AreEqual(1, localized);
    }

    [Test]
    public void AnAbandonedQueryCannotPinTheNextSession()
    {
        var localized = 0;
        var notLocalized = 0;
        _adapter.Localized += _ => localized++;
        _adapter.NotLocalized += _ => notLocalized++;
        _adapter.AbandonPending();
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        ReportSuccess();
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        ReportSuccess(false);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        _manager.LocalizationFailure.Invoke();
        Assert.AreEqual(0, localized, "the previous session's answer belongs to the old AR world");
        Assert.AreEqual(0, notLocalized);
    }

    [Test]
    public void OldFailureAfterNewLocalizeIsIgnoredAndNewResponseIsAccepted()
    {
        var localized = 0;
        var notLocalized = 0;
        var attempts = 0;
        _adapter.Localized += _ => localized++;
        _adapter.NotLocalized += _ => notLocalized++;
        _adapter.AttemptStarted += _ => attempts++;

        LocalizeWithoutEditorFailure();
        _adapter.AbandonPending();
        LocalizeWithoutEditorFailure();
        Assert.AreEqual(1, attempts, "the new attempt waits while the old request is live");
        Assert.IsTrue(_adapter.HasLiveRequest);
        Assert.IsTrue(_adapter.FallbackAvailable);
        Assert.AreEqual("waiting", _adapter.FallbackStatus);

        ReportFailureAndStartDeferredRequest();
        Assert.AreEqual(0, localized);
        Assert.AreEqual(0, notLocalized);
        Assert.AreEqual(2, attempts);
        Assert.IsTrue(_adapter.HasLiveRequest);
        Assert.AreEqual("inFlight", _adapter.FallbackStatus);

        ReportSuccess();
        Assert.AreEqual(1, localized);
        Assert.AreEqual(0, notLocalized);
        Assert.IsFalse(_adapter.HasLiveRequest);
    }

    [Test]
    public void OldResponseWithoutPoseAfterNewLocalizeIsIgnoredAndNewResponseIsAccepted()
    {
        if (typeof(SingleFrameLocalizationManager).GetEvent("OnLocalizationWithResponse") == null)
            Assert.Ignore("Legacy SDK has no response event");
        var localized = 0;
        var notLocalized = 0;
        var attempts = 0;
        _adapter.Localized += _ => localized++;
        _adapter.NotLocalized += _ => notLocalized++;
        _adapter.AttemptStarted += _ => attempts++;

        LocalizeWithoutEditorFailure();
        _adapter.AbandonPending();
        LocalizeWithoutEditorFailure();
        Assert.AreEqual(1, attempts);

        ReportNoPoseAndStartDeferredRequest();
        Assert.AreEqual(0, localized);
        Assert.AreEqual(0, notLocalized);
        Assert.AreEqual(2, attempts);

        ReportSuccess();
        Assert.AreEqual(1, localized);
        Assert.AreEqual(0, notLocalized);
    }

    [Test]
    public void UnresolvedRequestTimesOutAndLateResponseCannotLocalize()
    {
        var session = new IndoorArSession(new IndoorArRequest
        {
            frame = IndoorArContract.Frame, mapCode = _adapter.MapCode,
            position = new IndoorArVector3(),
        }, _adapter.MapCode);
        session.OnArSupport(true);
        session.OnAuthenticated();
        _adapter.Localized += session.OnLocalized;
        var attempts = 0;
        _adapter.AttemptStarted += _ => attempts++;
        LocalizeWithoutEditorFailure();

        _adapter.Tick(MultiSetLocalizer.RequestTimeoutS - 1f);
        Assert.IsTrue(_adapter.FallbackAvailable);
        LogAssert.Expect(LogType.Log, "[IndoorAr] multiset fallback unavailable: prior request unresolved");
        _adapter.Tick(1f);
        Assert.IsFalse(_adapter.FallbackAvailable);
        Assert.IsTrue(_adapter.HasLiveRequest);
        Assert.AreEqual("unavailable", _adapter.FallbackStatus);

        _sdkHost.transform.position = new Vector3(9f, 8f, 7f);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        ReportSuccess();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(new Vector3(9f, 8f, 7f), _sdkHost.transform.position);
        Assert.IsFalse(_adapter.HasLiveRequest);
        Assert.IsFalse(_adapter.FallbackAvailable, "late completion does not restore fallback within this session");
        Assert.AreEqual(1, attempts);
        _adapter.Localize();
        Assert.AreEqual(1, attempts, "timed-out fallback remains disabled for this session");

        _adapter.AbandonPending();
        Assert.IsTrue(_adapter.FallbackAvailable, "a later session can use the fallback after the request drained");
        Assert.AreEqual("available", _adapter.FallbackStatus);
        LocalizeWithoutEditorFailure();
        Assert.AreEqual(2, attempts);
    }

    [Test]
    public void SignLocalizationSucceedsWhileOldMultiSetRequestDrains()
    {
        var attempts = 0;
        _adapter.AttemptStarted += _ => attempts++;
        LocalizeWithoutEditorFailure();
        _adapter.AbandonPending();

        var first = new SignFirstLocalizer(_adapter);
        var session = new IndoorArSession(new IndoorArRequest
        {
            frame = IndoorArContract.Frame, mapCode = _adapter.MapCode,
            position = new IndoorArVector3(),
        }, _adapter.MapCode);
        session.LocalizeRequested += first.Localize;
        first.Localized += session.OnLocalized;
        session.OnArSupport(true);
        session.OnAuthenticated();
        first.Tick(SignFirstLocalizer.HoldS);
        Assert.AreEqual(1, attempts, "the new MultiSet request waits for the old callback");
        Assert.AreEqual("waiting", first.FallbackStatus);

        var truth = new GameObject("truth").transform;
        try
        {
            truth.SetPositionAndRotation(new Vector3(2f, 0f, 5f), Quaternion.Euler(0f, 40f, 0f));
            first.Observe(West, Reader(truth, West));
            Assert.IsTrue(first.Observe(East, Reader(truth, East)).Usable);
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            Assert.IsTrue(first.FromSigns);

            LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
            typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            Assert.AreEqual(1, attempts, "sign success removes the deferred MultiSet request");
        }
        finally { Object.DestroyImmediate(truth.gameObject); }
    }

    [Test]
    public void MissingPoseDoesNotPinAndTheSessionRetries()
    {
        if (typeof(SingleFrameLocalizationManager).GetEvent("OnLocalizationWithResponse") == null)
            Assert.Ignore("Legacy SDK has no response event");
        var session = new IndoorArSession(new IndoorArRequest
        {
            frame = IndoorArContract.Frame, mapCode = _adapter.MapCode,
            position = new IndoorArVector3(),
        }, _adapter.MapCode);
        var attempts = 0;
        session.LocalizeRequested += () => { attempts++; session.OnAttemptStarted(); };
        _adapter.Localized += session.OnLocalized;
        _adapter.NotLocalized += _ => session.OnNotLocalized();
        session.OnArSupport(true);
        session.OnAuthenticated();
        LocalizeWithoutEditorFailure();
        ReportSuccess(false);
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(1, session.Queries);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(2, session.Queries);
        Assert.IsNull(session.ResultJson);
    }

    void ReportSuccess(bool poseFound = true)
    {
        var callback = typeof(SingleFrameLocalizationManager).GetMethod("LocalizationSuccessCallback");
        callback.Invoke(_manager, callback.GetParameters().Length == 0 ? null
            : new object[] { new LocalizationSuccessResponse { poseFound = poseFound, confidence = 0.73f } });
    }

    static Vector3 Reader(Transform map, IndoorRoomLandmark room) => map.TransformPoint(
        room.DoorPositionInMap + new Vector3(0f, IndoorSignTrail.CameraHeightM, IndoorSignTrail.StandoffM));

    void LocalizeWithoutEditorFailure()
    {
        var callback = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), _adapter,
            typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        _manager.LocalizationFailure.RemoveListener(callback);
        if (!_adapter.HasLiveRequest)
            LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        _adapter.Localize();
        _manager.LocalizationFailure.AddListener(callback);
    }

    void ReportFailureAndStartDeferredRequest()
    {
        var callback = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), _adapter,
            typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        _manager.LocalizationFailure.RemoveListener(callback);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        _manager.LocalizationFailure.AddListener(callback);
    }

    void ReportNoPoseAndStartDeferredRequest()
    {
        var callback = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), _adapter,
            typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        _manager.LocalizationFailure.RemoveListener(callback);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        typeof(SingleFrameLocalizationManager).GetMethod("LocalizationSuccessCallback").Invoke(_manager,
            new object[] { new LocalizationSuccessResponse { poseFound = false, confidence = 0.73f } });
        _manager.LocalizationFailure.AddListener(callback);
    }
}
