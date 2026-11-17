using System.Text.RegularExpressions;
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
        // Mean of the four surveyed corners in vec_building_corners.json.
        public const double VecLatitude = 33.78282420612733;
        public const double VecLongitude = -118.11040173365433;
        static readonly Regex VecName = new Regex(@"^VEC(?=\d|\W|$)\s*(?:·\s*)?(?:Room\s+)?(\d+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static (string Building, string TopLabel, double Latitude, double Longitude) PinTarget(IndoorArDestination destination)
        {
            var name = destination.name?.Trim() ?? "";
            var match = VecName.Match(name);
            if (!match.Success) return (null, null, destination.latitude, destination.longitude);
            var room = match.Groups[1].Value;
            return ("VEC", room.Length > 0 ? "VEC · Room " + room : null, VecLatitude, VecLongitude);
        }

        public static float PinHeight(double distanceM) =>
            Mathf.Lerp(.20f, .24f, 1f - Mathf.Clamp01((float)distanceM / 30f));

        public static IndoorHudView Compose(IndoorArRequest request, OutdoorArState state, bool hasPose,
            bool arrived, double distanceM, double routeRemainingM, double turnDeg, bool offRoute,
            float readyForS, Vector3? labelAnchor, string arrivalFeedback = null,
            string pinBuilding = null, string topLabel = null, double pinDistanceM = double.NaN)
        {
            var view = new IndoorHudView
            {
                Outdoor = true,
                Destination = Name(request),
                TopLabel = topLabel,
                PinLabel = double.IsNaN(pinDistanceM) ? null
                    : (pinBuilding ?? Name(request)) + " · " + IndoorHud.DistanceLabel(pinDistanceM),
                PinScreenHeight = PinHeight(double.IsNaN(pinDistanceM) ? distanceM : pinDistanceM),
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
