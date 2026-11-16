using IndoorAr;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.XR;

public class WorldStaticPinTests
{
    [Test]
    public void PinStaysWhereItIsWhileTheCameraMovesAndTurns()
    {
        var map = new GameObject("Map Space");
        var camera = new GameObject("Camera");
        var pin = IndoorArRunner.CreatePin(map.transform, out var amber, out var core);
        try
        {
            map.transform.SetPositionAndRotation(new Vector3(2f, 0f, -1f), Quaternion.Euler(0f, 30f, 0f));
            pin.localPosition = new Vector3(3f, 0f, 4f);
            var placed = pin.position;
            var door = pin.localPosition;
            camera.transform.position = Vector3.zero;
            var firstYaw = Yaw(map.transform, camera.transform, door);

            camera.transform.SetPositionAndRotation(new Vector3(-5f, 1.4f, 2f), Quaternion.Euler(10f, 140f, 0f));

            Assert.AreEqual(0f, Vector3.Distance(placed, pin.position), 1e-5f);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(firstYaw, Yaw(map.transform, camera.transform, door))), 1f);
        }
        finally
        {
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(camera);
            Object.DestroyImmediate(amber);
            Object.DestroyImmediate(core);
        }
    }

    [Test]
    public void ReposingMapSpaceMovesThePinSoTheShiftIsReported()
    {
        var was = new Vector3(1f, 0f, 1f);
        Assert.IsFalse(IndoorArRunner.MapShifted(was, Quaternion.identity, was, Quaternion.identity, out _, out _));
        Assert.IsFalse(IndoorArRunner.MapShifted(was, Quaternion.identity, was + Vector3.right * .01f,
            Quaternion.Euler(0f, .2f, 0f), out _, out _));
        Assert.IsTrue(IndoorArRunner.MapShifted(was, Quaternion.identity, was + Vector3.right * .5f,
            Quaternion.identity, out var meters, out _));
        Assert.AreEqual(.5f, meters, 1e-4f);
        Assert.IsTrue(IndoorArRunner.MapShifted(was, Quaternion.identity, was, Quaternion.Euler(0f, 3f, 0f), out _, out var degrees));
        Assert.AreEqual(3f, degrees, 1e-3f);
    }

    [Test]
    public void ArCyclingIncludesThePoseThatDrivesTheCamera()
    {
        var go = new GameObject("AR camera");
        try
        {
            var driver = go.AddComponent<TrackedPoseDriver>();
            ArSessionLifecycle.RestartArComponents();
            Assert.IsTrue(driver.enabled);
            ArSessionLifecycle.SetArComponents(false);
            Assert.IsFalse(driver.enabled);
            ArSessionLifecycle.SetArComponents(true);
            Assert.IsTrue(driver.enabled);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static float Yaw(Transform map, Transform camera, Vector3 doorInMap) =>
        GuidanceCue.ArrowYawDegrees(map.InverseTransformDirection(camera.forward),
            map.InverseTransformPoint(camera.position), doorInMap);
}
