using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// Host access. Android reads the launch intent extra and answers through
    /// UnityHostActivity.deliverResult. iOS reads and answers through IndoorArHost
    /// in the embedded UnityFramework. Editor and other players use the stand-in.
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
#elif UNITY_IOS && !UNITY_EDITOR
            return Marshal.PtrToStringUTF8(IndoorArReadRequest());
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
            using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                intent.Call("removeExtra", IndoorArContract.RequestExtra);
            activity.Call("deliverResult", resultJson);
#elif UNITY_IOS && !UNITY_EDITOR
            IndoorArDeliverResult(resultJson);
#else
            EditorRequestJson = null;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern IntPtr IndoorArReadRequest();

        [DllImport("__Internal")]
        static extern void IndoorArDeliverResult([MarshalAs(UnmanagedType.LPUTF8Str)] string resultJson);
#endif
    }
}
