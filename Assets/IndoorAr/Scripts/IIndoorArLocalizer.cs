using System;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>What IndoorArRunner needs from MultiSet. MultiSetLocalizer adapts the SDK.</summary>
    public interface IIndoorArLocalizer
    {
        event Action Authenticated;
        /// <summary>true when the failure was no network, false for rejected credentials or any other auth error.</summary>
        event Action<bool> AuthFailed;
        /// <summary>One localization request sent to MultiSet (one API request).</summary>
        event Action QuerySent;
        event Action Localized;

        /// <summary>Transform at the MapSet origin; the SDK moves it on every localization.</summary>
        Transform MapSpace { get; }
        string MapCode { get; }

        void Authenticate();
        /// <summary>Starts one localization attempt against MapCode in MapSet mode.</summary>
        void Localize();
    }
}
