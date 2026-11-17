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
        static readonly Regex VecName = new Regex(@"\AVEC(?:[ \t]*[0-9]{3})?\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static (string Building, double Latitude, double Longitude) PinTarget(IndoorArDestination destination)
        {
            var name = destination.name?.Trim() ?? "";
            return VecName.IsMatch(name) ? ("VEC", VecLatitude, VecLongitude)
                : (null, destination.latitude, destination.longitude);
        }

        public static float PinHeight(double distanceM) =>
            Mathf.Lerp(.20f, .24f, 1f - Mathf.Clamp01((float)distanceM / 30f));

        public static IndoorHudView Compose(IndoorArRequest request, OutdoorArState state, bool hasPose,
            bool arrived, double distanceM, double routeRemainingM, double turnDeg, bool offRoute,
            float readyForS, Vector3? labelAnchor, string arrivalFeedback = null,
            string pinBuilding = null, double pinDistanceM = double.NaN)
        {
            var destination = Name(request);
            var pinName = pinBuilding == null ? destination : pinBuilding + " building center";
            var hasPinDistance = hasPose && OutdoorGeospatialGate.CanGuide(state)
                && pinDistanceM >= 0d && !double.IsInfinity(pinDistanceM);
            var pending = !labelAnchor.HasValue && !arrived && (OutdoorGeospatialGate.CanGuide(state)
                || state is OutdoorArState.VpsCheckRequired or OutdoorArState.TrackingUncertain);
            var pinLabel = pinName;
            if (hasPinDistance)
                pinLabel += (state == OutdoorArState.Approximate ? " · approx. " : " · ") + IndoorHud.DistanceLabel(pinDistanceM);
            if (pending) pinLabel += " · Pin unplaced";
            var view = new IndoorHudView
            {
                Outdoor = true,
                Destination = destination,
                PinLabel = pending || labelAnchor.HasValue ? pinLabel : null,
                PinScreenHeight = hasPinDistance ? PinHeight(pinDistanceM) : .20f,
                FollowingRoute = request.HasRoute,
                OffRoute = offRoute,
                ArrowYaw = (float)turnDeg,
                LabelAnchor = labelAnchor,
                DistanceM = !hasPose ? double.NaN : request.HasRoute ? routeRemainingM : distanceM
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
