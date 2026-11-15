using IndoorAr;
using UnityEngine;

namespace OutdoorAr
{
    /// <summary>
    /// Maps the outdoor session onto the indoor IndoorHudView, so one HUD draws both.
    /// The arrow is the turn from the camera heading to the next point of the route,
    /// or to the destination itself when the request carries no route.
    /// </summary>
    public static class OutdoorGuidanceHud
    {
        public const int MaxNameLength = 28;
        public const string ApproximateNote = "Approximate · scan the buildings";

        public static IndoorHudView Compose(IndoorArRequest request, OutdoorArState state, bool hasPose,
            bool arrived, double distanceM, double routeRemainingM, double turnDeg, bool offRoute,
            float readyForS, Vector3? labelAnchor, string arrivalFeedback = null)
        {
            var view = new IndoorHudView
            {
                Outdoor = true,
                Destination = Name(request),
                FollowingRoute = request.HasRoute,
                OffRoute = offRoute,
                ArrowYaw = (float)turnDeg,
                LabelAnchor = labelAnchor,
                DistanceM = request.HasRoute ? routeRemainingM : distanceM
            };
            if (arrived) view.State = IndoorHudState.Arrived;
            else if (hasPose)
            {
                view.State = readyForS < LiveDoorHud.LocationFoundHoldS ? IndoorHudState.Localized : IndoorHudState.Straight;
                if (!string.IsNullOrEmpty(arrivalFeedback)) view.DetailOverride = arrivalFeedback;
                // GPS-class accuracy: say so beside the distance. Off the route (30 m) outweighs the 10 m error, so that text stays.
                if (string.IsNullOrEmpty(view.DetailOverride) && state == OutdoorArState.Approximate && view.State == IndoorHudState.Straight && !offRoute)
                    view.DetailOverride = (double.IsNaN(view.DistanceM) ? "" : IndoorHud.DistanceLabel(view.DistanceM) + " · ") + ApproximateNote;
            }
            else
            {
                view.State = state == OutdoorArState.VpsCheckFailed ? IndoorHudState.NotFound : IndoorHudState.Scanning;
                view.LabelAnchor = null;
                if (state == OutdoorArState.VpsUnavailable)
                {
                    view.TitleOverride = "No outdoor AR coverage here";
                    view.DetailOverride = "Keep following your route.";
                }
                else if (state == OutdoorArState.CameraPermissionRequired || state == OutdoorArState.LocationPermissionRequired)
                {
                    view.TitleOverride = "Allow camera and location";
                    view.DetailOverride = "Outdoor AR needs both to guide you.";
                }
            }
            return view;
        }

        static string Name(IndoorArRequest request)
        {
            var name = request.destination == null ? null : request.destination.name?.Trim();
            if (string.IsNullOrEmpty(name)) return "Your destination";
            return name.Length <= MaxNameLength ? name : name.Substring(0, MaxNameLength - 1) + "…";
        }
    }
}
