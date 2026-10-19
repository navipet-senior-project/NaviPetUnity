using UnityEngine;

public class IndoorMapMarker : MonoBehaviour
{
    [Tooltip("Generated automatically during export.")]
    public string objectId;

    [Tooltip("Examples: door, elevator, stairs, entrance, exit, bathroom")]
    public string objectType = "door";

    [Tooltip("The floor containing this object.")]
    public string floorId = "floor_";
}