using System.Linq;
using System.Xml.Linq;
using IndoorAr.Editor;
using NUnit.Framework;

public class IndoorArManifestTests
{
    [Test]
    public void CameraFeaturesAreOptionalWithoutChangingGlesOrDuplicatingEntries()
    {
        const string input = "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\"><uses-feature android:glEsVersion=\"0x00030000\"/><uses-feature android:name=\"android.hardware.camera.ar\" android:required=\"true\"/><uses-feature android:name=\"com.google.ar.core.depth\" android:required=\"true\"/><application/></manifest>";
        var patched = IndoorArAndroidManifest.Patch(input);
        XNamespace android = "http://schemas.android.com/apk/res/android";
        var features = XDocument.Parse(patched).Root.Elements("uses-feature").ToArray();
        foreach (var name in new[] { "android.hardware.camera.ar", "android.hardware.camera", "android.hardware.camera.autofocus", "com.google.ar.core.depth" })
            Assert.AreEqual("false", (string)features.Single(f => (string)f.Attribute(android + "name") == name).Attribute(android + "required"));
        Assert.AreEqual("0x00030000", (string)features.Single(f => f.Attribute(android + "glEsVersion") != null).Attribute(android + "glEsVersion"));
        Assert.AreEqual(patched, IndoorArAndroidManifest.Patch(patched));
    }
}
