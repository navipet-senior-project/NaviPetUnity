using System.Reflection;
using System.Text.RegularExpressions;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;

namespace OutdoorAr.Tests
{
    public class OutdoorExitTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        // Exit (X and back), arrival, and both failures all end in Finish.
        [TestCase("cancelled")]
        [TestCase("arrived")]
        [TestCase("unavailable")]
        [TestCase("localizationFailed")]
        public void EveryExitStopsTheSessionAndForgetsTheRequestSoTheNextLaunchEnters(string status)
        {
            var go = new GameObject("Outdoor exit");
            var previous = IndoorArBridge.EditorRequestJson;
            try
            {
                var session = go.AddComponent<ARSession>();
                var demo = go.AddComponent<OutdoorGeospatialDemo>();
                typeof(OutdoorGeospatialDemo).GetField("session", Private).SetValue(demo, session);
                typeof(OutdoorGeospatialDemo).GetField("_requestJson", Private).SetValue(demo, "{\"mode\":\"outdoor\"}");
                var result = status switch
                {
                    "cancelled" => IndoorArContract.Cancelled(0, 1),
                    "arrived" => IndoorArContract.Arrived(3f, 0, 1),
                    "unavailable" => IndoorArContract.Unavailable(0, 1),
                    _ => IndoorArContract.LocalizationFailed(0, 1),
                };
                LogAssert.Expect(LogType.Log, new Regex($"\\[IndoorAr\\] result .*\"status\":\"{status}\""));
                typeof(OutdoorGeospatialDemo).GetMethod("Finish", Private).Invoke(demo, new object[] { result });

                Assert.IsFalse(session.enabled);
                Assert.IsTrue((bool)typeof(OutdoorGeospatialDemo).GetField("_finished", Private).GetValue(demo));
                // A null request makes the same destination, launched again, a new request.
                Assert.IsNull(typeof(OutdoorGeospatialDemo).GetField("_requestJson", Private).GetValue(demo));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                IndoorArBridge.EditorRequestJson = previous;
                Object.DestroyImmediate(go);
            }
        }
    }
}
