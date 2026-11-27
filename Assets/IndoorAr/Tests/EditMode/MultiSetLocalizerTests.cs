using System.Linq;
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
    double _clock;
    System.Func<double> _originalClock;
    static readonly System.Reflection.FieldInfo ClockField = typeof(MultiSetLocalizer).GetField("Clock", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

    [SetUp]
    public void SetUp()
    {
        _clock = 0;
        _originalClock = (System.Func<double>)ClockField.GetValue(null);
        ClockField.SetValue(null, (System.Func<double>)(() => _clock));
        _sdkHost = new GameObject("SDK");
        _sdkHost.SetActive(false);
        var sdk = _sdkHost.AddComponent<IndoorArSdkManager>();
        _manager = _sdkHost.AddComponent<SingleFrameLocalizationManager>();
        _mapHost = new GameObject("Navigation Map Space");
        _host = new GameObject("Adapter");
        _host.SetActive(false);
        _adapter = _host.AddComponent<MultiSetLocalizer>();
        _adapter.TrackingReady = () => true;
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
        ClockField.SetValue(null, _originalClock);
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
        string candidateHash = null;
        _adapter.CandidateLocalized += (_, hash, _) => candidateHash = hash;
        _adapter.Localized += _ => localized++;
        LocalizeWithoutEditorFailure();
        _adapter.SdkMapSpace.position = new Vector3(4f, 5f, 6f);
        typeof(MultiSetLocalizer).GetMethod("OnLocalizedWithoutConfidence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Assert.AreEqual(1, localized);
        Assert.AreEqual("unavailable", candidateHash);
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

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void DestroyedOwnerKeepsGlobalLockUntilItsRealSdkCompletion(bool sdkFailure, bool sdkManagerDestroyedFirst)
    {
        var ownerLocalized = 0;
        var ownerRejected = 0;
        _adapter.CandidatePoseValidator = _ => true;
        _adapter.Localized += _ => ownerLocalized++;
        _adapter.NotLocalized += _ => ownerRejected++;
        LocalizeWithoutEditorFailure();
        Assert.IsTrue(_adapter.HasLiveRequest);

        var pin = new GameObject("Destination pin").transform;
        pin.SetParent(_mapHost.transform, false);
        pin.localPosition = new Vector3(2f, 1f, 3f);
        var hud = new GameObject("Indoor HUD").transform;
        hud.SetParent(_mapHost.transform, false);
        hud.localPosition = new Vector3(-2f, 4f, 1f);
        var rootPosition = _mapHost.transform.position;
        var rootRotation = _mapHost.transform.rotation;
        var pinPosition = pin.position;
        var pinRotation = pin.rotation;
        var hudPosition = hud.position;
        var hudRotation = hud.rotation;
        var sdkMapSpace = _adapter.SdkMapSpace;
        // The retained SDK object can still complete after its component is destroyed.
        if (sdkManagerDestroyedFirst) Object.DestroyImmediate(_manager);
        typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Object.DestroyImmediate(_adapter);
        var ownerDrained = false;

        GameObject otherSdkHost = null;
        GameObject otherMap = null;
        GameObject otherHost = null;
        GameObject otherProxy = null;
        SingleFrameLocalizationManager otherManager = null;
        MultiSetLocalizer other = null;
        try
        {
            otherSdkHost = new GameObject("Next SDK");
            otherSdkHost.SetActive(false);
            var otherSdk = otherSdkHost.AddComponent<IndoorArSdkManager>();
            otherManager = otherSdkHost.AddComponent<SingleFrameLocalizationManager>();
            otherMap = new GameObject("Next Navigation Map");
            otherHost = new GameObject("Next Adapter");
            otherHost.SetActive(false);
            other = otherHost.AddComponent<MultiSetLocalizer>();
            other.TrackingReady = () => true;
            other.Configure(otherSdk, otherManager, otherMap.transform);
            otherProxy = other.SdkMapSpace.gameObject;
            otherHost.SetActive(true);
            typeof(MultiSetLocalizer).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(other, null);
            other.CandidatePoseValidator = _ => true;
            var attempts = 0;
            var localized = 0;
            other.AttemptStarted += _ => attempts++;
            other.Localized += _ => localized++;

            other.Localize();
            Assert.AreEqual(0, attempts, "new session cannot overlap the destroyed owner's live SDK request");
            Assert.AreEqual("waiting", other.FallbackStatus);
            sdkMapSpace.SetPositionAndRotation(new Vector3(20f, 30f, 40f), Quaternion.Euler(0f, 90f, 0f));
            Assert.AreEqual(rootPosition, _mapHost.transform.position);
            Assert.AreEqual(rootRotation, _mapHost.transform.rotation);
            Assert.AreEqual(pinPosition, pin.position);
            Assert.AreEqual(pinRotation, pin.rotation);
            Assert.AreEqual(hudPosition, hud.position);
            Assert.AreEqual(hudRotation, hud.rotation);

            if (sdkFailure)
            {
                LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
                _manager.LocalizationFailure.Invoke();
            }
            else
            {
                LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
                ReportSuccess(_manager);
            }
            ownerDrained = true;
            Assert.AreEqual(0, ownerLocalized);
            Assert.AreEqual(0, ownerRejected);
            Assert.AreEqual(rootPosition, _mapHost.transform.position);
            Assert.AreEqual(rootRotation, _mapHost.transform.rotation);
            Assert.AreEqual(pinPosition, pin.position);
            Assert.AreEqual(pinRotation, pin.rotation);
            Assert.AreEqual(hudPosition, hud.position);
            Assert.AreEqual(hudRotation, hud.rotation);

            LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
            TickWithoutEditorFailure(other, otherManager, 0f);
            Assert.AreEqual(1, attempts, "only genuine SDK completion lets the deferred request start");
            other.SdkMapSpace.SetPositionAndRotation(new Vector3(4f, 5f, 6f), Quaternion.Euler(0f, 30f, 0f));
            ReportSuccess(otherManager);
            Assert.AreEqual(1, localized);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), other.MapSpace.position);
        }
        finally
        {
            if (!ownerDrained)
            {
                LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
                _manager.LocalizationFailure.Invoke();
            }
            if (other != null && other.HasLiveRequest && otherManager != null) otherManager.LocalizationFailure.Invoke();
            if (other != null)
                typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(other, null);
            if (otherHost != null) Object.DestroyImmediate(otherHost);
            if (otherMap != null) Object.DestroyImmediate(otherMap);
            if (otherProxy != null) Object.DestroyImmediate(otherProxy);
            if (otherSdkHost != null) Object.DestroyImmediate(otherSdkHost);
        }
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
    public void AbandonedRequestDoesNotConsumeTheNextSignHoldTimeout()
    {
        var attempts = 0;
        _adapter.AttemptStarted += _ => attempts++;
        _adapter.CandidatePoseValidator = _ => true;
        LocalizeWithoutEditorFailure();
        _adapter.Tick(MultiSetLocalizer.RequestTimeoutS - 1f);
        _adapter.AbandonPending();
        var first = new SignFirstLocalizer(_adapter);
        first.Localize();
        ((System.Action)typeof(MultiSetLocalizer).GetField("Authenticated", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic).GetValue(_adapter)).Invoke();

        _adapter.Tick(SignFirstLocalizer.HoldS - 1f);
        first.Tick(SignFirstLocalizer.HoldS - 1f);
        Assert.IsTrue(first.FallbackAvailable);
        Assert.IsTrue(_adapter.HasLiveRequest);
        Assert.AreEqual(1, attempts);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        ReportSuccess();
        LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        var callback = FailureCallback(_adapter);
        _manager.LocalizationFailure.RemoveListener(callback);
        try { first.Tick(1f); }
        finally { _manager.LocalizationFailure.AddListener(callback); }
        Assert.AreEqual(2, attempts);
        ReportSuccess();
        Assert.IsFalse(_adapter.HasLiveRequest);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NewGenerationQueueHasItsOwnTimeoutWhileOldRequestDrains(bool timeout)
    {
        var attempts = 0;
        var localized = 0;
        _adapter.AttemptStarted += _ => attempts++;
        _adapter.Localized += _ => localized++;
        _adapter.CandidatePoseValidator = _ => true;
        LocalizeWithoutEditorFailure();
        _adapter.Tick(MultiSetLocalizer.RequestTimeoutS - 1f);
        _adapter.AbandonPending();
        _adapter.Localize();
        _adapter.Tick(MultiSetLocalizer.RequestTimeoutS - 1f);
        Assert.IsTrue(_adapter.FallbackAvailable);
        Assert.IsTrue(_adapter.HasLiveRequest);
        Assert.AreEqual("waiting", _adapter.FallbackStatus);
        Assert.AreEqual(1, attempts);
        if (timeout)
        {
            LogAssert.Expect(LogType.Log, "[IndoorAr] multiset fallback unavailable: prior request unresolved");
            _adapter.Tick(1f);
            Assert.IsFalse(_adapter.FallbackAvailable);
            Assert.IsTrue(_adapter.HasLiveRequest);
        }
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        ReportSuccess();
        Assert.AreEqual(0, localized);
        if (!timeout) LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        TickWithoutEditorFailure(0f);
        Assert.AreEqual(timeout ? 1 : 2, attempts);
        if (!timeout)
        {
            ReportSuccess();
            Assert.AreEqual(1, localized);
        }
        Assert.IsFalse(_adapter.HasLiveRequest);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ManualRetryResetsTimeoutWithoutReleasingOutstandingRequest(bool signsFirst, bool completedBeforeRetry)
    {
        var first = signsFirst ? new SignFirstLocalizer(_adapter) : null;
        var localizer = (IIndoorArLocalizer)first ?? _adapter;
        var session = new IndoorArSession(new IndoorArRequest
        {
            frame = IndoorArContract.Frame, mapCode = _adapter.MapCode,
            position = new IndoorArVector3()
        }, _adapter.MapCode);
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(_adapter, null, null);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(IndoorArRunner).GetField("_session", flags).SetValue(runner, session);
        typeof(IndoorArRunner).GetField("_signFirst", flags).SetValue(runner, first);
        localizer.Localized += session.OnLocalized;
        if (first != null)
        {
            session.FallbackStopped += first.StopFallback;
            ((System.Action)typeof(MultiSetLocalizer).GetField("Authenticated", flags).GetValue(_adapter)).Invoke();
        }
        else session.FallbackStopped += _adapter.DropQueuedFallback;
        session.OnArSupport(true);
        session.OnAuthenticated();
        _adapter.CandidatePoseValidator = _ => true;
        var attempts = 0;
        _adapter.AttemptStarted += _ => attempts++;
        LocalizeWithoutEditorFailure();
        LogAssert.Expect(LogType.Log, "[IndoorAr] multiset fallback unavailable: prior request unresolved");
        _adapter.Tick(MultiSetLocalizer.RequestTimeoutS);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.IsTrue(LiveDoorHud.LocalizationFailed(session));
        if (completedBeforeRetry)
        {
            LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
            _manager.LocalizationFailure.Invoke();
        }
        Assert.IsFalse(_adapter.FallbackAvailable);
        session.LocalizeRequested += localizer.Localize;
        var callback = FailureCallback(_adapter);
        _manager.LocalizationFailure.RemoveListener(callback);
        try
        {
            if (!signsFirst && completedBeforeRetry)
                LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
            typeof(IndoorArRunner).GetMethod("OnHudRetry", flags).Invoke(runner, null);
            Assert.IsTrue(_adapter.FallbackAvailable);
            Assert.AreEqual(IndoorArState.Localizing, session.State);
            Assert.IsNull(session.ResultJson);
            if (!completedBeforeRetry)
            {
                _adapter.Tick(1f);
                Assert.IsTrue(_adapter.FallbackAvailable);
                Assert.IsTrue(_adapter.HasLiveRequest);
                Assert.AreEqual(1, attempts);
                LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
                ReportSuccess();
                Assert.AreEqual(IndoorArState.Localizing, session.State);
            }
            if (signsFirst)
            {
                first.Tick(SignFirstLocalizer.HoldS - 1f);
                Assert.AreEqual(1, attempts);
                LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
                first.Tick(1f);
            }
            else if (!completedBeforeRetry)
            {
                LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
                _adapter.Tick(0f);
            }
            Assert.AreEqual(2, attempts);
            ReportSuccess();
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            Assert.IsFalse(_adapter.HasLiveRequest);
        }
        finally { _manager.LocalizationFailure.AddListener(callback); }
    }

    [Test]
    public void SignLocalizationSucceedsWhileOldMultiSetRequestDrains()
    {
        var attempts = 0;
        _adapter.AttemptStarted += _ => attempts++;
        LocalizeWithoutEditorFailure();
        _adapter.AbandonPending();

        var first = new SignFirstLocalizer(_adapter, IndoorSignPoseTests.ValidatedHere(out var sighting, out _));
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
        other.TrackingReady = () => true;
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
    public void DeliveredSessionDoesNotStartItsQueuedFallbackAfterAnotherRequestCompletes()
    {
        LocalizeWithoutEditorFailure();
        var otherSdkHost = new GameObject("Queued SDK");
        otherSdkHost.SetActive(false);
        var otherSdk = otherSdkHost.AddComponent<IndoorArSdkManager>();
        var otherManager = otherSdkHost.AddComponent<SingleFrameLocalizationManager>();
        var otherMap = new GameObject("Queued Navigation Map");
        var otherHost = new GameObject("Queued Adapter");
        otherHost.SetActive(false);
        var other = otherHost.AddComponent<MultiSetLocalizer>();
        other.TrackingReady = () => true;
        other.Configure(otherSdk, otherManager, otherMap.transform);
        var otherProxy = other.SdkMapSpace.gameObject;
        otherHost.SetActive(true);
        typeof(MultiSetLocalizer).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(other, null);
        var attempts = 0;
        other.AttemptStarted += _ => attempts++;
        var runnerHost = new GameObject("Delivered queued session");
        var runner = runnerHost.AddComponent<IndoorArRunner>();
        var signFirst = new SignFirstLocalizer(other, new IndoorSignCalibration());
        typeof(IndoorArRunner).GetField("_localizer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(runner, signFirst);
        try
        {
            other.Localize();
            Assert.AreEqual("waiting", other.FallbackStatus);
            LogAssert.Expect(LogType.Log, "[IndoorAr] result {\"result\":\"cancelled\"}");
            typeof(IndoorArRunner).GetMethod("Deliver", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(runner, new object[] { "{\"result\":\"cancelled\"}" });

            ReportFailureAndStartDeferredRequest(false);
            other.Tick(0f);
            Assert.AreEqual(0, attempts, "the delivered session's queued VPS request was removed");
            Assert.AreEqual("available", other.FallbackStatus);
        }
        finally
        {
            typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(other, null);
            Object.DestroyImmediate(runnerHost);
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

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SessionDeadlineDropsQueuedIntentButKeepsSdkOwnershipUntilCallback(bool recoverSigns, bool success)
    {
        var session = new IndoorArSession(new IndoorArRequest
        {
            frame = IndoorArContract.Frame, mapCode = _adapter.MapCode,
            position = new IndoorArVector3()
        }, _adapter.MapCode, recoverSigns);
        session.FallbackStopped += _adapter.DropQueuedFallback;
        _adapter.CandidatePoseValidator = _ => true;
        _adapter.Localized += session.OnLocalized;
        var attempts = 0;
        _adapter.AttemptStarted += _ => attempts++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        LocalizeWithoutEditorFailure();
        _adapter.Localize();
        Assert.AreEqual(1, attempts);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(recoverSigns ? IndoorArState.Localizing : IndoorArState.Done, session.State);
        Assert.IsTrue(_adapter.HasLiveRequest);
        _adapter.Tick(0f);
        Assert.AreEqual(1, attempts);
        _adapter.SdkMapSpace.SetPositionAndRotation(new Vector3(30f, 4f, 20f), Quaternion.Euler(0f, 80f, 0f));
        Assert.AreEqual(Vector3.zero, _adapter.MapSpace.position);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        if (success) ReportSuccess();
        else _manager.LocalizationFailure.Invoke();
        Assert.IsFalse(_adapter.HasLiveRequest);
        Assert.AreEqual(Vector3.zero, _adapter.MapSpace.position);
        Assert.AreEqual(recoverSigns ? IndoorArState.Localizing : IndoorArState.Done, session.State);
        _adapter.Tick(0f);
        Assert.AreEqual(1, attempts, "the cancelled queue must not dispatch after the real callback");
        _adapter.AbandonPending();
        LocalizeWithoutEditorFailure();
        Assert.IsTrue(_adapter.HasLiveRequest);
        Assert.AreEqual(2, attempts, "only genuine completion makes a fresh session eligible");
        _manager.LocalizationFailure.Invoke();
        Assert.IsFalse(_adapter.HasLiveRequest);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void QueuedAndDelayedRequestsWaitForTracking(bool delayedAuth)
    {
        var tracking = true;
        _adapter.TrackingReady = () => tracking;
        var attempts = 0;
        _adapter.AttemptStarted += _ => attempts++;
        LocalizeWithoutEditorFailure();
        _adapter.AbandonPending();
        var first = new SignFirstLocalizer(_adapter);
        first.Localize();
        var authenticated = (System.Action)typeof(MultiSetLocalizer).GetField("Authenticated", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic).GetValue(_adapter);
        if (!delayedAuth) authenticated();
        tracking = false;
        first.Tick(SignFirstLocalizer.HoldS);
        if (delayedAuth) authenticated();
        ReportFailureAndStartDeferredRequest();
        _adapter.Tick(MultiSetLocalizer.RequestTimeoutS + 1f);
        Assert.AreEqual(1, attempts);
        Assert.IsFalse(_adapter.HasLiveRequest);
        Assert.IsTrue(_adapter.FallbackAvailable);
        tracking = true;
        LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        TickWithoutEditorFailure(0f);
        Assert.AreEqual(2, attempts);
        ReportSuccess();
    }

    [Test]
    public void AcceptedRequestLogsItsOutcomeAndTiming()
    {
        _adapter.CandidatePoseValidator = _ => true;
        LocalizeWithoutEditorFailure();
        TickWithoutEditorFailure(2.5f);
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=2500 outcome=Ok lockBusy=false lockWaitMs=0 sdkMs=2500");
        ReportSuccess();
    }

    static string ManagerHint(SingleFrameLocalizationManager manager, string field)
    {
        var info = typeof(SingleFrameLocalizationManager).GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (info == null) Assert.Ignore("SDK has no " + field);
        var value = info.GetValue(manager);
        return value is System.Collections.IEnumerable list && value is not string ? string.Join(",", list.Cast<object>()) : (string)value;
    }

    [Test]
    public void FloorHintBecomesAYBandForThatRequestOnlyAndIsClearedNextTime()
    {
        MultiSetHint? hint = new MultiSetHint(-2f);
        _adapter.HintProvider = () => hint;
        LocalizeWithoutEditorFailure();
        Assert.AreEqual("[-2.3, 1.3]", ManagerHint(_manager, "hintFloorHeight"));
        Assert.AreEqual("", ManagerHint(_manager, "hintMapCodes"), "no map codes are configured");
        TickWithoutEditorFailure(2f);
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=2000 outcome=GenericFailure lockBusy=false lockWaitMs=0 sdkMs=2000 hint=Height");
        _manager.LocalizationFailure.Invoke();

        hint = null; // the retry
        LocalizeWithoutEditorFailure();
        Assert.AreEqual("", ManagerHint(_manager, "hintFloorHeight"), "SDK fields persist, so the retry must clear them");
        Assert.AreEqual("", ManagerHint(_manager, "hintMapCodes"));
        TickWithoutEditorFailure(1f);
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=2 kind=MultiSet elapsedMs=1000 outcome=GenericFailure lockBusy=false lockWaitMs=0 sdkMs=1000");
        _manager.LocalizationFailure.Invoke();
    }

    [Test]
    public void TakingOwnershipClearsInspectorMapCodes()
    {
        var field = typeof(SingleFrameLocalizationManager).GetField("hintMapCodes",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(System.Collections.Generic.List<string>)) Assert.Ignore("SDK has no hintMapCodes list");
        field.SetValue(_manager, new System.Collections.Generic.List<string> { "TEST_CODE" });
        typeof(MultiSetLocalizer).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Assert.AreEqual("", ManagerHint(_manager, "hintMapCodes"));
    }

    [TestCase(1, 42, "false", ",\"deltaHpa\":0.05", 0, false, false, 1)]
    [TestCase(1, 42, "true", ",\"deltaHpa\":-0.47", 0, false, false, 2)]
    [TestCase(1, 42, "false", ",\"deltaHpa\":0.05", 15, false, false, 0)]
    [TestCase(1, 42, "true", ",\"deltaHpa\":-0.47", 15, false, false, 0)]
    [TestCase(1, 1199, "false", ",\"deltaHpa\":0.05", 2, false, false, 0)]
    [TestCase(1, 1199, "true", ",\"deltaHpa\":-0.47", 2, false, false, 0)]
    [TestCase(1, 42, "true", ",\"deltaHpa\":-1.15", 0, false, false, 0)]
    [TestCase(4, 42, "true", ",\"deltaHpa\":1.15", 0, false, false, 0)]
    [TestCase(3, 42, "true", ",\"deltaHpa\":1.15", 0, false, false, 0)]
    [TestCase(3, 42, "true", ",\"deltaHpa\":-0.47", 0, false, false, 0)]
    [TestCase(1, 42, "false", ",\"deltaHpa\":-0.47", 0, false, false, 0)]
    [TestCase(1, 42, "false", ",\"deltaHpa\":0.05", 0, true, false, 0)]
    [TestCase(1, 42, "true", ",\"deltaHpa\":-0.47", 0, true, false, 0)]
    [TestCase(0, 42, "false", ",\"deltaHpa\":0.05", 0, false, true, 0)]
    [TestCase(1, 42, "false", ",\"deltaHpa\":0.05", 0, false, true, 0)]
    public void RunnerDispatchUsesOnlyFreshUnambiguousHostEvidence(int startFloor, int startAge, string moved,
        string deltaField, int dispatchDelay, bool spendRetry, bool confirmSign, int expectedFloor) =>
        AssertRunnerHint(startFloor, startAge, moved, deltaField, dispatchDelay, spendRetry, confirmSign, expectedFloor);

    [TestCase(100, 0.2, false, 1)]
    [TestCase(100, 1, false, 0)]
    [TestCase(100, 1, true, 0)]
    [TestCase(99.4, 0, false, 0)]
    [TestCase(100.1, 0.2, false, 0)]
    [TestCase(-1, 0, false, 0)]
    public void TransportAndEntryWaitCountAgainstEvidenceAge(double sentAt, double initDelay, bool moved, int expectedFloor)
    {
        AssertRunnerHint(1, 42, moved ? "true" : "false", moved ? ",\"deltaHpa\":-0.47" : ",\"deltaHpa\":0.05",
            0, false, false, expectedFloor, 9.5, sentAt < 0 ? null : sentAt, initDelay);
    }

    [TestCase("")]
    [TestCase(",\"deltaHpa\":null")]
    [TestCase(",\"deltaHpa\":NaN")]
    [TestCase(",\"deltaHpa\":Infinity")]
    [TestCase(",\"deltaHpa\":3.01")]
    [TestCase(",\"deltaHpa\":-0.47")]
    public void UnusableDeltaLeavesTheFirstRequestUnhintedAndRejectsTheWrongFloor(string deltaField)
    {
        AssertRunnerHint(1, 42, "false", deltaField, 0, false, false, 0, afterFirst: session =>
        {
            Assert.IsTrue(session.PressureSamplingAllowed);
            var grids = IndoorWalkGrid.LoadFloors(Resources.Load<TextAsset>("IndoorWalkGrid").text);
            _adapter.SdkMapSpace.position = Vector3.down * (grids[2].FloorY + 1.5f);
            ReportSuccess();
            Assert.AreEqual(1, session.TrustedStartFloor);
            Assert.AreEqual(IndoorArState.Localizing, session.State);
        });
    }

    // The SDK reports a dropped connection and a server no-pose as the same failure, so a failed hinted request
    // never releases the start-floor veto: a retry pose on another floor stays rejected.
    [TestCase(true, true, false, false, 2, IndoorArState.Localizing)]
    [TestCase(true, true, true, false, 2, IndoorArState.Localizing)]
    [TestCase(true, false, false, false, 2, IndoorArState.Localizing)]
    [TestCase(false, true, false, false, 2, IndoorArState.Localizing)]
    [TestCase(true, true, false, true, 2, IndoorArState.Localizing)]
    [TestCase(true, true, false, false, 0, IndoorArState.Localizing)]
    [TestCase(true, true, false, false, 1, IndoorArState.Pinned)]
    public void FailedFirstRequestKeepsTheStartFloorVeto(bool hasDelta, bool hasStamp, bool noPoseResponse, bool confirmSign,
        int retryFloor, IndoorArState expected)
    {
        AssertRunnerHint(1, 42, "false", hasDelta ? ",\"deltaHpa\":0.05" : "", 0, false, false,
            hasDelta && hasStamp ? 1 : 0, sentAt: hasStamp ? 100 : null,
            afterFirst: session =>
            {
                Assert.IsTrue(session.LaunchedWithFloorEvidence);
                if (noPoseResponse) ReportSuccess(poseFound: false);
                else _manager.LocalizationFailure.Invoke();
                Assert.AreEqual(1, session.TrustedStartFloor);
                if (confirmSign) session.OnSignConfirmed(new IndoorRoomLandmark("VEC120", "VEC", "floor_1"));

                LocalizeWithoutEditorFailure(() => session.Tick(IndoorArSession.NoPoseRetryS));
                Assert.AreEqual(2, session.Queries);
                Assert.AreEqual("", ManagerHint(_manager, "hintFloorHeight"));
                var grids = IndoorWalkGrid.LoadFloors(Resources.Load<TextAsset>("IndoorWalkGrid").text);
                var cameraY = retryFloor > 0 ? grids[retryFloor].FloorY + 1.5f : 1000f;
                _adapter.SdkMapSpace.position = Vector3.down * cameraY;
                ReportSuccess();
                Assert.AreEqual(expected, session.State);
            });
    }

    void AssertRunnerHint(int startFloor, int startAge, string moved, string deltaField, int dispatchDelay,
        bool spendRetry, bool confirmSign, int expectedFloor, double sampleAge = 1, double? sentAt = 100,
        double initDelay = 0, System.Action<IndoorArSession> afterFirst = null)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var arState = UnityEngine.XR.ARFoundation.ARSession.state;
        var setArState = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previousRequest = IndoorArBridge.EditorRequestJson;
        var runner = _host.AddComponent<IndoorArRunner>();
        var requestClock = 100.0;
        runner.RequestClock = () => requestClock;
        runner.Configure(_adapter, null, _host.AddComponent<Camera>());
        var sendTime = sentAt.HasValue ? System.FormattableString.Invariant($",\"requestSentAtBootS\":{sentAt.Value}") : "";
        IndoorArBridge.EditorRequestJson = System.FormattableString.Invariant($"{{\"schemaVersion\":2,\"mode\":\"localize\",\"mapCode\":\"{_adapter.MapCode}\",") +
            $"\"frame\":\"multiset-mapset\",\"floorEvidenceVersion\":1,\"startFloor\":\"{startFloor}\",\"startFloorAgeS\":{startAge}," +
            System.FormattableString.Invariant($"\"pressureSinceStart\":{{\"settled\":true,\"moved\":{moved},\"sampleAgeS\":{sampleAge}{deltaField}}}{sendTime}}}");
        try
        {
            setArState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.None });
            var startup = (System.Collections.IEnumerator)typeof(IndoorArRunner).GetMethod("Start", flags).Invoke(runner, null);
            startup.MoveNext();
            if (initDelay > 0)
            {
                var armEntry = (System.Collections.IEnumerator)typeof(IndoorArRunner).GetMethod("ArmEntry", flags)
                    .Invoke(runner, new object[] { IndoorArBridge.EditorRequestJson, requestClock });
                requestClock += initDelay;
                armEntry.MoveNext();
            }
            var session = (IndoorArSession)typeof(IndoorArRunner).GetField("_session", flags).GetValue(runner);
            session.OnArSupport(true);
            session.OnAuthenticated();
            if (confirmSign)
                session.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));
            if (spendRetry) session.Tick(IndoorArSession.LocalizeTimeoutS);
            Assert.AreEqual(0, session.Queries);
            requestClock += dispatchDelay;
            setArState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });

            LocalizeWithoutEditorFailure(() => typeof(IndoorArRunner).GetMethod("RequestLocalization", flags).Invoke(runner, null));

            Assert.AreEqual(1, session.Queries);
            var expectedBand = "";
            if (expectedFloor > 0)
            {
                var grids = IndoorWalkGrid.LoadFloors(Resources.Load<TextAsset>("IndoorWalkGrid").text);
                var band = new MultiSetHint(grids[expectedFloor].FloorY);
                expectedBand = System.FormattableString.Invariant($"[{band.MinY:0.##}, {band.MaxY:0.##}]");
            }
            Assert.AreEqual(expectedBand, ManagerHint(_manager, "hintFloorHeight"));
            Assert.AreEqual("", ManagerHint(_manager, "hintMapCodes"));
            afterFirst?.Invoke(session);
        }
        finally
        {
            if (_adapter.HasLiveRequest) _manager.LocalizationFailure.Invoke();
            Object.DestroyImmediate(runner);
            IndoorArBridge.EditorRequestJson = previousRequest;
            setArState.Invoke(null, new object[] { arState });
        }
    }

    [Test]
    public void FailedSdkRequestLogsGenericFailure()
    {
        LocalizeWithoutEditorFailure();
        TickWithoutEditorFailure(2f);
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=2000 outcome=GenericFailure lockBusy=false lockWaitMs=0 sdkMs=2000");
        _manager.LocalizationFailure.Invoke();
    }

    [Test]
    public void DestroyedRequestMeasuresUntilItsLateCallback()
    {
        LocalizeWithoutEditorFailure();
        _clock = 5;
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=5000 outcome=Cancelled lockBusy=false lockWaitMs=0 sdkMs=5000");
        typeof(MultiSetLocalizer).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        _clock = 20;
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=20000 outcome=Late lockBusy=false lockWaitMs=0 sdkMs=20000");
        ReportFailureAndStartDeferredRequest();
    }

    [Test]
    public void QueuedPredecessorCountsAsLockWaitWithoutTicks()
    {
        _adapter.TrackingReady = () => false;
        _adapter.Localize();
        var sdkHost = new GameObject("Other SDK");
        sdkHost.SetActive(false);
        var sdk = sdkHost.AddComponent<IndoorArSdkManager>();
        var manager = sdkHost.AddComponent<SingleFrameLocalizationManager>();
        var map = new GameObject("Other map");
        var host = new GameObject("Other adapter");
        host.SetActive(false);
        var other = host.AddComponent<MultiSetLocalizer>();
        other.TrackingReady = () => true;
        other.Configure(sdk, manager, map.transform);
        var proxy = other.SdkMapSpace.gameObject;
        try
        {
            other.Localize();
            _clock = 8;
            LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=8000 outcome=Cancelled lockBusy=true lockWaitMs=8000 sdkMs=0");
            other.DropQueuedFallback();
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(proxy);
            Object.DestroyImmediate(sdkHost);
            _adapter.DropQueuedFallback();
        }
    }

    [Test]
    public void ActiveTimeoutCancelsYoungerQueuedAttempt()
    {
        LocalizeWithoutEditorFailure();
        TickWithoutEditorFailure(29f);
        _adapter.Localize();
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=30000 outcome=Timeout lockBusy=false lockWaitMs=0 sdkMs=30000");
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=2 kind=MultiSet elapsedMs=1000 outcome=Cancelled lockBusy=true lockWaitMs=1000 sdkMs=0");
        LogAssert.Expect(LogType.Log, "[IndoorAr] multiset fallback unavailable: prior request unresolved");
        TickWithoutEditorFailure(1f);
        ReportFailureAndStartDeferredRequest();
    }

    [Test]
    public void QueuedAttemptReportsItsOwnTimeout()
    {
        LocalizeWithoutEditorFailure();
        _adapter.AbandonPending();
        _adapter.Localize();
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=2 kind=MultiSet elapsedMs=30000 outcome=Timeout lockBusy=true lockWaitMs=30000 sdkMs=0");
        LogAssert.Expect(LogType.Log, "[IndoorAr] multiset fallback unavailable: prior request unresolved");
        TickWithoutEditorFailure(30f);
        ReportFailureAndStartDeferredRequest();
    }

    [Test]
    public void UnansweredRequestLogsTimeoutThenLateWhileTheLockStaysHeld()
    {
        LocalizeWithoutEditorFailure();
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=30000 outcome=Timeout lockBusy=false lockWaitMs=0 sdkMs=30000");
        LogAssert.Expect(LogType.Log, "[IndoorAr] multiset fallback unavailable: prior request unresolved");
        TickWithoutEditorFailure(MultiSetLocalizer.RequestTimeoutS);
        TickWithoutEditorFailure(15f);
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=MultiSet elapsedMs=45000 outcome=Late lockBusy=false lockWaitMs=0 sdkMs=45000");
        ReportFailureAndStartDeferredRequest();
    }

    void ReportSuccess(bool poseFound = true)
    {
        ReportSuccess(_manager, poseFound);
    }

    void ReportSuccess(SingleFrameLocalizationManager manager, bool poseFound = true)
    {
        var callback = typeof(SingleFrameLocalizationManager).GetMethod("LocalizationSuccessCallback");
        callback.Invoke(manager, callback.GetParameters().Length == 0 ? null
            : new object[] { new LocalizationSuccessResponse { poseFound = poseFound, confidence = 0.73f } });
    }

    void LocalizeWithoutEditorFailure(System.Action dispatch = null)
    {
        var callback = FailureCallback(_adapter);
        _manager.LocalizationFailure.RemoveListener(callback);
        if (!_adapter.HasLiveRequest)
            LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
        if (dispatch == null) _adapter.Localize();
        else dispatch();
        _manager.LocalizationFailure.AddListener(callback);
    }

    void ReportFailureAndStartDeferredRequest(bool abandoned = true)
    {
        var callback = FailureCallback(_adapter);
        _manager.LocalizationFailure.RemoveListener(callback);
        if (abandoned) LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_adapter, null);
        Assert.IsFalse(_adapter.HasLiveRequest, "the owner's genuine failure callback releases the process-wide lock");
        _manager.LocalizationFailure.AddListener(callback);
    }

    void TickWithoutEditorFailure(float deltaTime)
    {
        TickWithoutEditorFailure(_adapter, _manager, deltaTime);
    }

    void TickWithoutEditorFailure(MultiSetLocalizer localizer, SingleFrameLocalizationManager manager, float deltaTime)
    {
        var callback = FailureCallback(localizer);
        manager.LocalizationFailure.RemoveListener(callback);
        _clock += deltaTime;
        localizer.Tick(deltaTime);
        manager.LocalizationFailure.AddListener(callback);
    }

    void ReportNoPoseAndStartDeferredRequest()
    {
        var callback = FailureCallback(_adapter);
        _manager.LocalizationFailure.RemoveListener(callback);
        LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
        typeof(SingleFrameLocalizationManager).GetMethod("LocalizationSuccessCallback").Invoke(_manager,
            new object[] { new LocalizationSuccessResponse { poseFound = false, confidence = 0.73f } });
        _manager.LocalizationFailure.AddListener(callback);
    }

    static UnityAction FailureCallback(MultiSetLocalizer localizer)
    {
        var field = typeof(MultiSetLocalizer).GetField("_completionListener",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var listener = field.GetValue(localizer);
        return (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), listener,
            listener.GetType().GetMethod("OnNotLocalized"));
    }
}
