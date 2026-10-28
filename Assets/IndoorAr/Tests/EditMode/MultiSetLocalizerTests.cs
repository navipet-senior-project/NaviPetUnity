using IndoorAr;
using MultiSet;
using NUnit.Framework;
using UnityEngine;

public class MultiSetLocalizerTests
{
    GameObject _host;
    GameObject _sdkHost;
    MapLocalizationManager _manager;
    MultiSetLocalizer _adapter;

    [SetUp]
    public void SetUp()
    {
        _sdkHost = new GameObject("SDK");
        _sdkHost.SetActive(false);
        var sdk = _sdkHost.AddComponent<IndoorArSdkManager>();
        _manager = _sdkHost.AddComponent<MapLocalizationManager>();
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
        Object.DestroyImmediate(_host);
        Object.DestroyImmediate(_sdkHost);
    }

    [Test]
    public void SessionOwnsAllLocalizationTriggers()
    {
        Assert.AreEqual(LocalizationType.Mapset, _manager.localizationType);
        Assert.AreEqual("MSET_XTBY7AM7ZK77", _manager.mapOrMapsetCode);
        Assert.IsFalse(_manager.autoLocalize);
        Assert.IsFalse(_manager.backgroundLocalization);
        Assert.IsFalse(_manager.relocalization);
        Assert.IsFalse(_manager.firstLocalizationUntilSuccess);
        Assert.IsFalse(_manager.showAlert);
        Assert.IsFalse(_manager.enabled, "no auth listener or capture until authenticated");
        Assert.AreSame(_sdkHost.transform, _adapter.MapSpace);
    }

    [Test]
    public void SdkEventsCountOneRequestAndReportSuccess()
    {
        var queries = 0;
        var localized = 0;
        _adapter.QuerySent += () => queries++;
        _adapter.Localized += () => localized++;
        _manager.LocalizationRequested.Invoke();
        _manager.LocalizationSuccess.Invoke();
        Assert.AreEqual(1, queries);
        Assert.AreEqual(1, localized);
        Object.DestroyImmediate(_host);
        _manager.LocalizationRequested.Invoke();
        _manager.LocalizationSuccess.Invoke();
        Assert.AreEqual(1, queries, "destroyed adapter must unsubscribe");
        Assert.AreEqual(1, localized);
    }
}
