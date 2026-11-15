using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IndoorAr;
using IndoorAr.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.ARKit;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.XR.ARKit;

public class IndoorArIosSettingsTests
{
    [Test]
    public void EditorBridgeReturnsTheStandInAndLogsTheResult()
    {
        var previous = IndoorArBridge.EditorRequestJson;
        try
        {
            IndoorArBridge.EditorRequestJson = "{\"schemaVersion\":1}";
            Assert.AreEqual("{\"schemaVersion\":1}", IndoorArBridge.ReadRequestJson());
            LogAssert.Expect(LogType.Log, new Regex(@"\[IndoorAr\] result .*cancelled"));
            IndoorArBridge.Deliver("{\"status\":\"cancelled\",\"localizationQueries\":0,\"durationMs\":0}");
        }
        finally
        {
            IndoorArBridge.EditorRequestJson = previous;
        }
    }

    [Test]
    public void ArCoreKeyComesFromOutsideTheProjectAndIsNotCommitted()
    {
        Assert.AreEqual("", IndoorArExport.ReadIosKey(Path.Combine(Path.GetTempPath(), "navipet-no-such-key")));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, " not-a-real-key\n");
            Assert.AreEqual("not-a-real-key", IndoorArExport.ReadIosKey(path));
        }
        finally
        {
            File.Delete(path);
        }
        var settings = JsonUtility.FromJson<ArCoreSettings>(File.ReadAllText("ProjectSettings/ARCoreExtensionsProjectSettings.json"));
        Assert.AreEqual("", settings.IOSCloudServicesApiKey);
        Assert.AreEqual("", settings.AndroidCloudServicesApiKey);
    }

    [Serializable]
    class ArCoreSettings
    {
        public string IOSCloudServicesApiKey;
        public string AndroidCloudServicesApiKey;
    }

    [Test]
    public void VisionPluginNamesItsFrameworkTheWayUnityLinksIt()
    {
        // Unity appends ".framework" itself: "Vision.framework" made the linker look for Vision.framework.framework.
        var plugin = (PluginImporter)AssetImporter.GetAtPath("Assets/Plugins/iOS/IndoorRoomVisionOcr.mm");
        Assert.That(plugin.GetPlatformData(BuildTarget.iOS, "FrameworkDependencies"), Is.EqualTo("Vision;"));
    }

    [Test]
    public void IosLoadsArKitAndAndroidStillLoadsArCore()
    {
        var ios = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.iOS);
        Assert.IsNotNull(ios);
        Assert.IsNotNull(ios.Manager);
        Assert.IsTrue(ios.Manager.activeLoaders.OfType<ARKitLoader>().Any());
        Assert.AreEqual(ARKitSettings.Requirement.Optional, ARKitSettings.GetOrCreateSettings().requirement);
        Assert.IsFalse(ARKitSettings.GetOrCreateSettings().faceTracking);

        var android = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        Assert.IsNotNull(android);
        Assert.IsTrue(android.Manager.activeLoaders.Any(loader => loader.GetType().Name == "ARCoreLoader"));
        Assert.IsFalse(android.Manager.activeLoaders.OfType<ARKitLoader>().Any());
    }

    [Test]
    public void IosPlayerTargetsArm64MetalAndDeclaresTheCamera()
    {
        Assert.GreaterOrEqual(Version.Parse(PlayerSettings.iOS.targetOSVersionString), new Version(11, 0));
        Assert.AreEqual("15.0", PlayerSettings.iOS.targetOSVersionString);
        Assert.IsFalse(string.IsNullOrEmpty(PlayerSettings.iOS.cameraUsageDescription));
        Assert.AreEqual(
            "NaviPet uses your location while scanning a room for indoor AR.",
            PlayerSettings.iOS.locationUsageDescription);
        StringAssert.Contains(
            "UNITY_XR_ARKIT_LOADER_ENABLED",
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS));
        Assert.AreEqual(1, PlayerSettings.GetArchitecture(NamedBuildTarget.iOS));
        Assert.AreEqual(ScriptingImplementation.IL2CPP, PlayerSettings.GetScriptingBackend(NamedBuildTarget.iOS));
        // Without the iOS player module, GetUseDefaultGraphicsAPIs stays true.
        // The saved player setting is still Metal only.
        CollectionAssert.AreEqual(new[] { GraphicsDeviceType.Metal }, PlayerSettings.GetGraphicsAPIs(BuildTarget.iOS));

        Assert.IsFalse(PlayerSettings.SplashScreen.show);
        Assert.IsFalse(PlayerSettings.SplashScreen.showUnityLogo);

        Assert.AreEqual(AndroidSdkVersions.AndroidApiLevel26, PlayerSettings.Android.minSdkVersion);
        Assert.AreEqual(AndroidArchitecture.ARM64, PlayerSettings.Android.targetArchitectures);
        Assert.IsFalse(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android));
        CollectionAssert.AreEqual(new[] { GraphicsDeviceType.OpenGLES3 }, PlayerSettings.GetGraphicsAPIs(BuildTarget.Android));
    }
}
