using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// Carries a pose from the Unity exporter frame (vec_floors12345.json, relative to IndoorMapOrigin) into the
    /// MultiSet MapSet frame that vec_destination_points and the Flutter request use. The 17 door rows in the
    /// backend match the 17 exported doors under this one rigid transform with 0.0001 m residual, so every marker
    /// in the JSON (entrances, stairs and so on) maps the same way. Re-derive these numbers if the MapSet is
    /// re-aligned (map_version changes); IndoorMapFrameTests fails first.
    /// </summary>
    public static class IndoorMapFrame
    {
        public const float YawDeg = 5.356265f;
        public static readonly Vector3 Offset = new Vector3(0.27940662f, -2.2116f, 0.8375566f);

        public static Vector3 ToMapSet(Vector3 exporterPosition) =>
            Quaternion.Euler(0f, YawDeg, 0f) * exporterPosition + Offset;

        public static float HeadingToMapSet(float exporterRotationYDeg) =>
            Mathf.Repeat(exporterRotationYDeg + YawDeg, 360f);
    }
}
