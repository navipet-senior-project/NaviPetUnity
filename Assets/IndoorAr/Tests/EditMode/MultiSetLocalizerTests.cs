using IndoorAr;
using MultiSet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

public class MultiSetLocalizerTests
{

    GameObject _host;
    GameObject _sdkHost;
    GameObject _mapHost;
    GameObject _proxy;
    SingleFrameLocalizationManager _manager;
    MultiSetLocalizer _adapter;

    [SetUp]
    public void SetUp()
    {
        _sdkHost = new GameObject("SDK");
        _sdkHost.SetActive(false);
        var sdk = _sdkHost.AddComponent<IndoorArSdkManager>();
        _manager = _sdkHost.AddComponent<SingleFrameLocalizationManager>();
        _mapHost = new GameObject("Navigation Map Space");
        _host = new GameObject("Adapter");
        _host.SetActive(false);
        _adapter = _host.AddComponent<MultiSetLocalizer>();
        _adapter.Configure(sdk, _manager, _mapHost.transform);
        _proxy = _adapter.SdkMapSpace.gameObject;
        _host.SetActive(true);
        typeof(MultiSetLocalizer).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
    }

    [TearDown]
    public void TearDown()
    {
        if (_adapter != null) typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Object.DestroyImmediate(_host);
        Object.DestroyImmediate(_mapHost);
        Object.DestroyImmediate(_proxy);
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
        Assert.AreSame(_mapHost.transform, _adapter.MapSpace);
        Assert.AreNotSame(_adapter.MapSpace, _adapter.SdkMapSpace);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        Assert.AreSame(_adapter.SdkMapSpace.gameObject,
            typeof(SingleFrameLocalizationManager).GetField("mapSpace", flags).GetValue(_manager));
        Assert.IsFalse((bool)typeof(SingleFrameLocalizationManager).GetField("enableBlurCheck", flags).GetValue(_manager));
        var mode = typeof(SingleFrameLocalizationManager).GetField("queryMode");
        if (mode != null) Assert.AreEqual("Vps1", mode.GetValue(_manager).ToString());
        else Assert.AreEqual(1, typeof(SingleFrameLocalizationManager).GetField("_requestAttempts").GetValue(_manager));
    }

    [Test]
    public void SdkProxyAncestorCannotMoveNavigationMap()
    {
        var ancestor = new GameObject("Former SDK Parent");
        var oldProxy = _proxy;
        _mapHost.transform.SetParent(ancestor.transform, true);
        var position = _mapHost.transform.position;
        var rotation = _mapHost.transform.rotation;
        _adapter.Configure(_sdkHost.GetComponent<IndoorArSdkManager>(), _manager, _mapHost.transform, ancestor.transform);
        _proxy = _adapter.SdkMapSpace.gameObject;
        Object.DestroyImmediate(oldProxy);
        try
        {
            Assert.AreNotSame(ancestor.transform, _adapter.SdkMapSpace);
            Assert.IsFalse(_adapter.SdkMapSpace.IsChildOf(_adapter.MapSpace));
            Assert.IsFalse(_adapter.MapSpace.IsChildOf(_adapter.SdkMapSpace));
            _adapter.SdkMapSpace.SetPositionAndRotation(new Vector3(20f, 30f, 40f), Quaternion.Euler(0f, 90f, 0f));
            Assert.AreEqual(position, _mapHost.transform.position);
            Assert.AreEqual(rotation, _mapHost.transform.rotation);
        }
        finally
        {
            _mapHost.transform.SetParent(null, true);
            Object.DestroyImmediate(ancestor);
        }
    }

    [Test]
    public void SdkResponseCarriesConfidenceAndUnsubscribes()
    {
        var localized = 0;
        float? confidence = null;
        _adapter.CandidatePoseValidator = _ => true;
        _adapter.Localized += value => { localized++; confidence = value; };
        LocalizeWithoutEditorFailure();
        _adapter.SdkMapSpace.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 45f, 0f));
        ReportSuccess();
        Assert.AreEqual(1, localized);
        Assert.AreEqual(new Vector3(3f, 4f, 5f), _adapter.MapSpace.position);
        Assert.AreEqual(Quaternion.Euler(0f, 45f, 0f), _adapter.MapSpace.rotation);
        if (typeof(SingleFrameLocalizationManager).GetEvent("OnLocalizationWithResponse") != null)
            Assert.AreEqual(0.73f, confidence);
        else Assert.IsNull(confidence, "legacy SDK has no public response callback");
        // EditMode does not invoke runtime lifecycle callbacks for this test object.
        typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Object.DestroyImmediate(_host);
        ReportSuccess();
        Assert.AreEqual(1, localized);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RejectedCandidateNeverCommitsThroughEitherSuccessCallback(bool legacyCallback)
    {
        var localized = 0;
        var rejectedAttempt = 0;
        _adapter.Localized += _ => localized++;
        _adapter.NotLocalized += attempt => rejectedAttempt = attempt;
        _adapter.CandidatePoseValidator = _ => false;
        LocalizeWithoutEditorFailure();
        _mapHost.transform.SetPositionAndRotation(new Vector3(6f, 2f, 8f), Quaternion.Euler(0f, 20f, 0f));
        _adapter.SdkMapSpace.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));

        if (legacyCallback)
            typeof(MultiSetLocalizer).GetMethod("OnLocalizedWithoutConfidence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        else
        {
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("\\[IndoorAr\\] localization mapCodes=.*"));
            ReportSuccess();
        }

        Assert.AreEqual(0, localized);
        Assert.AreEqual(1, rejectedAttempt);
        Assert.AreEqual(new Vector3(6f, 2f, 8f), _mapHost.transform.position);
        Assert.That(Quaternion.Angle(Quaternion.Euler(0f, 20f, 0f), _mapHost.transform.rotation), Is.LessThan(0.01f));
        Assert.IsFalse(_adapter.HasLiveRequest, "a genuine rejected SDK completion releases single-flight ownership");
    }

    [Test]
    public void LegacySuccessCommitsAfterCandidateValidation()
    {
        var localized = 0;
        _adapter.CandidatePoseValidator = _ => true;
        _adapter.Localized += _ => localized++;
        LocalizeWithoutEditorFailure();
        _adapter.SdkMapSpace.position = new Vector3(4f, 5f, 6f);
        typeof(MultiSetLocalizer).GetMethod("OnLocalizedWithoutConfidence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Assert.AreEqual(1, localized);
        Assert.AreEqual(new Vector3(4f, 5f, 6f), _mapHost.transform.position);
        Assert.IsFalse(_adapter.HasLiveRequest);
    }

    [Test]
    public void SuccessWithoutRunnerFloorValidatorFailsClosed()
    {
        var localized = 0;
        var rejected = 0;
        _adapter.Localized += _ => localized++;
        _adapter.NotLocalized += _ => rejected++;
        LocalizeWithoutEditorFailure();
        _adapter.SdkMapSpace.position = new Vector3(4f, 5f, 6f);
        LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("\\[IndoorAr\\] localization mapCodes=.*"));
        ReportSuccess();
        Assert.AreEqual(0, localized);
        Assert.AreEqual(1, rejected);
        Assert.AreEqual(Vector3.zero, _mapHost.transform.position);
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
        _adapter.CandidatePoseValidator = _ => true;
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
        LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        TickWithoutEditorFailure(0f);
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
        _adapter.CandidatePoseValidator = _ => true;
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
        LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        TickWithoutEditorFailure(0f);
        Assert.AreEqual(2, attempts);

        ReportSuccess();
        Assert.AreEqual(1, localized);
        Assert.AreEqual(0, notLocalized);
    }

    [Test]
    public void UnresolvedRequestTimesOutAndLateResponseCannotLocalize()
    {
        _adapter.CandidatePoseValidator = _ => true;
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

        _adapter.SdkMapSpace.position = new Vector3(9f, 8f, 7f);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        ReportSuccess();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(Vector3.zero, _adapter.MapSpace.position, "late SDK transform writes remain isolated from navigation");
        Assert.AreEqual(new Vector3(9f, 8f, 7f), _adapter.SdkMapSpace.position);
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
        _adapter.SdkMapSpace.position = new Vector3(1f, 2f, 3f);
        ReportSuccess();
        Assert.AreEqual(new Vector3(1f, 2f, 3f), _adapter.MapSpace.position);
    }

    [Test]
    public void SignLocalizationSucceedsWhileOldMultiSetRequestDrains()
    {
        var attempts = 0;
        _adapter.AttemptStarted += _ => attempts++;
        LocalizeWithoutEditorFailure();
        _adapter.AbandonPending();

        var first = new SignFirstLocalizer(_adapter, IndoorSignTrailTests.ValidatedHere(out var sighting, out _));
        var session = new IndoorArSession(new IndoorArRequest
        {
            frame = IndoorArContract.Frame, mapCode = _adapter.MapCode,
            position = new IndoorArVector3(),
        }, _adapter.MapCode);
        session.LocalizeRequested += first.Localize;
        first.Localized += session.OnLocalized;
        session.OnArSupport(true);
        session.OnAuthenticated();
        // MultiSet's authentication lands behind the sign hold.
        ((System.Action)typeof(MultiSetLocalizer).GetField("Authenticated", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic).GetValue(_adapter)).Invoke();
        first.Tick(SignFirstLocalizer.HoldS);
        Assert.AreEqual(1, attempts, "the new MultiSet request waits for the old callback");
        Assert.AreEqual("waiting", first.FallbackStatus);

        Assert.AreEqual(IndoorSignPoseStatus.Ok, first.ObservePose(sighting));
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        Assert.IsTrue(first.FromSigns);

        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        Assert.AreEqual(1, attempts, "sign success removes the deferred MultiSet request");
    }

    [Test]
    public void ProcessWideRequestLockPreventsOverlapAcrossLocalizers()
    {
        LocalizeWithoutEditorFailure();
        var otherSdkHost = new GameObject("Other SDK");
        otherSdkHost.SetActive(false);
        var otherSdk = otherSdkHost.AddComponent<IndoorArSdkManager>();
        var otherManager = otherSdkHost.AddComponent<SingleFrameLocalizationManager>();
        var otherMap = new GameObject("Other Navigation Map");
        var otherHost = new GameObject("Other Adapter");
        otherHost.SetActive(false);
        var other = otherHost.AddComponent<MultiSetLocalizer>();
        other.Configure(otherSdk, otherManager, otherMap.transform);
        var otherProxy = other.SdkMapSpace.gameObject;
        otherHost.SetActive(true);
        typeof(MultiSetLocalizer).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(other, null);
        var attempts = 0;
        other.AttemptStarted += _ => attempts++;
        try
        {
            other.Localize();
            Assert.AreEqual(0, attempts);
            Assert.AreEqual("waiting", other.FallbackStatus);
            LogAssert.Expect(LogType.Log, "[IndoorAr] multiset fallback unavailable: prior request unresolved");
            other.Tick(MultiSetLocalizer.RequestTimeoutS);
            Assert.AreEqual("unavailable", other.FallbackStatus);
            ReportFailureAndStartDeferredRequest(false);
            other.Tick(0f);
            Assert.AreEqual(0, attempts, "timeout does not release the process-wide request lock");
            Assert.AreEqual("unavailable", other.FallbackStatus);
        }
        finally
        {
            typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(other, null);
            Object.DestroyImmediate(otherHost);
            Object.DestroyImmediate(otherMap);
            Object.DestroyImmediate(otherProxy);
            Object.DestroyImmediate(otherSdkHost);
        }
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

    void ReportFailureAndStartDeferredRequest(bool abandoned = true)
    {
        var callback = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), _adapter,
            typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        _manager.LocalizationFailure.RemoveListener(callback);
        if (abandoned) LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Assert.IsFalse(_adapter.HasLiveRequest, "the owner's genuine failure callback releases the process-wide lock");
        _manager.LocalizationFailure.AddListener(callback);
    }

    void TickWithoutEditorFailure(float deltaTime)
    {
        var callback = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), _adapter,
            typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        _manager.LocalizationFailure.RemoveListener(callback);
        _adapter.Tick(deltaTime);
        _manager.LocalizationFailure.AddListener(callback);
    }

    void ReportNoPoseAndStartDeferredRequest()
    {
        var callback = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), _adapter,
            typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        _manager.LocalizationFailure.RemoveListener(callback);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        typeof(SingleFrameLocalizationManager).GetMethod("LocalizationSuccessCallback").Invoke(_manager,
            new object[] { new LocalizationSuccessResponse { poseFound = false, confidence = 0.73f } });
        _manager.LocalizationFailure.AddListener(callback);
    }
}
