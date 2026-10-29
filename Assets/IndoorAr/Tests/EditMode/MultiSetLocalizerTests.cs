using IndoorAr;
using MultiSet;
using NUnit.Framework;
using UnityEngine;

public class MultiSetLocalizerTests
{
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
        session.OnArSupport(true);
        session.OnAuthenticated();
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
}
