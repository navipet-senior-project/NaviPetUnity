# Prints the OpenCV fixtures in IndoorSignPoseTests: what IndoorSignPnp.cpp returns for known camera poses.
# Needs opencv-python-headless==4.14.0.94 (the release the native solver links) and numpy, e.g. in a throwaway venv.
import cv2
import numpy as np

FX, FY, CX, CY = 1500.0, 1500.0, 960.0, 540.0
HW, HH = 0.15, 0.075  # IndoorSignPoseTests.Sign(): 0.30 m x 0.15 m
PLANE = np.array([[-HW, HH], [HW, HH], [HW, -HH], [-HW, -HH]])

# Camera position in the sign frame (x right, y up, z into the wall), Unity yaw and pitch relative to the sign, and
# whether the corners are whole pixels as OCR reports them. The poses are the ones IndoorSignPoseTests already uses.
CASES = [
    ((0.0, 0.1, -1.2), 0.0, 0.0, False),
    ((-0.3, -0.1, -1.2), 15.0, -5.0, False),
    ((0.4, 0.3, -1.2), -20.0, 8.0, False),
    ((0.0, 0.1, -1.2), 0.0, 0.0, True),
    ((0.0, 0.1, -4.0), 10.0, 0.0, True),
]


def unity_rotation(yaw, pitch):
    y, p = np.radians(yaw), np.radians(pitch)
    ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]])
    rx = np.array([[1, 0, 0], [0, np.cos(p), -np.sin(p)], [0, np.sin(p), np.cos(p)]])
    return ry @ rx  # Quaternion.Euler(pitch, yaw, 0)


def columns(rvec, tvec):
    r, _ = cv2.Rodrigues(rvec)
    return list(r[:, 0]) + list(r[:, 1]) + list(tvec.ravel())


for camera, yaw, pitch, whole in CASES:
    rotation = unity_rotation(yaw, pitch)
    corners = []
    for x, y in PLANE:
        p = rotation.T @ (np.array([x, y, 0.0]) - np.array(camera))
        u, v = CX + FX * p[0] / p[2], CY - FY * p[1] / p[2]
        corners += [round(u), round(v)] if whole else [round(u, 3), round(v, 3)]
    obj = np.array([[x, y, 0.0] for x, y in PLANE])
    img = np.array(corners, dtype=np.float64).reshape(4, 2)
    k = np.array([[FX, 0, CX], [0, FY, CY], [0, 0, 1]])
    # Same calls as IndoorSignPnp.cpp: both IPPE solutions, then each refined.
    count, rvecs, tvecs, _ = cv2.solvePnPGeneric(obj, img, k, None, flags=cv2.SOLVEPNP_IPPE)
    count = min(count, 2)
    out = [0.0] * 36
    for s in range(count):
        out[9 * s:9 * s + 9] = columns(rvecs[s], tvecs[s])
        r, t = rvecs[s].copy(), tvecs[s].copy()
        cv2.solvePnPRefineLM(obj, img, k, None, r, t)
        out[9 * (2 + s):9 * (3 + s)] = columns(r, t)
    f = lambda values: ", ".join(f"{v:.9g}" for v in values)
    print(f"(new Vector3({', '.join(f'{c:g}f' for c in camera)}), {yaw:g}f, {pitch:g}f,\n"
          f"    new[] {{ {', '.join(f'{c:g}f' for c in corners)} }}, {count}, new[]\n"
          f"    {{ {f(out)} }}),")
