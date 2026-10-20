using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// Android host access. The request comes from the launch intent set by the
    /// app's IndoorArChannel; the result goes back through
    /// UnityHostActivity.deliverResult, which finishes the activity.
    /// </summary>
    public static class IndoorArBridge
    {
        /// <summary>Editor stand-in for the intent extra: the VEC 119 door.</summary>
        public static string EditorRequestJson =
            "{\"schemaVersion\":1,\"room\":\"119\",\"doorKey\":\"VEC119\",\"floor\":\"1\"," +
            "\"position\":{\"x\":-5.7651,\"y\":-1.8186,\"z\":22.6689},\"headingDeg\":185.36," +
            "\"mapCode\":\"MSET_XTBY7AM7ZK77\",\"frame\":\"multiset-mapset\"}";

        public static string ReadRequestJson()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent.Call<string>("getStringExtra", IndoorArContract.RequestExtra);
#else
            return EditorRequestJson;
#endif
        }

        public static void Deliver(string resultJson)
        {
            Debug.Log($"[IndoorAr] result {resultJson}");
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call("deliverResult", resultJson);
#endif
        }
    }
}
