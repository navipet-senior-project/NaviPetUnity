using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// Host access. Android reads the launch intent extra and answers through
    /// UnityHostActivity.deliverResult, and posts progress through its optional postStatus. iOS reads, answers and
    /// posts through IndoorArHost in the embedded UnityFramework. Editor and other players use the stand-in.
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

        /// <summary>Editor stand-in for the host's status handler: the last status posted.</summary>
        public static string LastStatusJson;
#if UNITY_ANDROID && !UNITY_EDITOR
        static bool s_noStatusHost;
#endif

        /// <summary>
        /// Sends one IndoorArContract.Status message to the host. It may carry the pose, so it is never logged.
        /// A host without a status handler drops it.
        /// </summary>
        public static void PostStatus(string statusJson)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (s_noStatusHost) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("postStatus", statusJson);
            }
            catch (AndroidJavaException)
            {
                s_noStatusHost = true; // a host activity without postStatus
            }
#elif UNITY_IOS && !UNITY_EDITOR
            IndoorArPostStatus(statusJson);
#else
            LastStatusJson = statusJson;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void IndoorArPostStatus([MarshalAs(UnmanagedType.LPUTF8Str)] string statusJson);

        [DllImport("__Internal")]
        static extern IntPtr IndoorArReadRequest();

        [DllImport("__Internal")]
        static extern void IndoorArDeliverResult([MarshalAs(UnmanagedType.LPUTF8Str)] string resultJson);
#endif
    }
}
