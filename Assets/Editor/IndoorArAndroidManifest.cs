using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Android;

namespace IndoorAr.Editor
{
    // The host app supports devices without AR; only the AR session requires the camera.
    public sealed class IndoorArAndroidManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 3; // After ARCore's manifest processor (2).

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!EditorUserBuildSettings.exportAsGoogleAndroidProject) return;
            var manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
            File.WriteAllText(manifest, Patch(File.ReadAllText(manifest)));
        }

        public static string Patch(string xml)
        {
            var document = XDocument.Parse(xml);
            XNamespace android = "http://schemas.android.com/apk/res/android";
            foreach (var name in new[] { "android.hardware.camera.ar", "android.hardware.camera", "android.hardware.camera.autofocus" })
            {
                var feature = document.Root.Elements("uses-feature")
                    .FirstOrDefault(f => (string)f.Attribute(android + "name") == name);
                if (feature == null)
                {
                    feature = new XElement("uses-feature", new XAttribute(android + "name", name));
                    document.Root.Add(feature);
                }
                feature.SetAttributeValue(android + "required", "false");
            }
            return document.ToString();
        }
    }
}
