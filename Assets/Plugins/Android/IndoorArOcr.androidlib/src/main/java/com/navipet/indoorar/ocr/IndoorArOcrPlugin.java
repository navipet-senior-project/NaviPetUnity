package com.navipet.indoorar.ocr;

import android.graphics.Bitmap;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraManager;
import android.view.Surface;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;

public final class IndoorArOcrPlugin {
    private static final TextRecognizer RECOGNIZER =
            TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);
    private static boolean busy;

    private IndoorArOcrPlugin() {}

    public static synchronized boolean recognize(byte[] rgba, int width, int height,
            String callbackObject) {
        if (busy || rgba == null || width < 1 || height < 1
                || width > 1280 || height > 720
                || (long) width * height * 4 != rgba.length) return false;
        busy = true;
        Bitmap bitmap = null;
        try {
            bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
            bitmap.copyPixelsFromBuffer(java.nio.ByteBuffer.wrap(rgba));
            InputImage image = InputImage.fromBitmap(bitmap,
                    cameraRotationDegrees(unityActivity()));
            Bitmap submittedBitmap = bitmap;
            RECOGNIZER.process(image)
                    .addOnSuccessListener(result -> sendUnityMessage(
                            callbackObject, "OnMlKitText", result.getText()))
                    .addOnFailureListener(error -> sendUnityMessage(
                            callbackObject, "OnMlKitError", error.getMessage()))
                    .addOnCompleteListener(task -> {
                        submittedBitmap.recycle();
                        synchronized (IndoorArOcrPlugin.class) { busy = false; }
                    });
        } catch (Exception error) {
            if (bitmap != null) bitmap.recycle();
            busy = false;
            sendUnityMessage(callbackObject, "OnMlKitError", error.getMessage());
            return false;
        }
        return true;
    }

    private static android.app.Activity unityActivity() throws Exception {
        Class<?> unityPlayer = Class.forName("com.unity3d.player.UnityPlayer");
        return (android.app.Activity) unityPlayer.getField("currentActivity").get(null);
    }

    private static void sendUnityMessage(String object, String method, String value) {
        try {
            Class<?> unityPlayer = Class.forName("com.unity3d.player.UnityPlayer");
            unityPlayer.getMethod("UnitySendMessage", String.class, String.class, String.class)
                    .invoke(null, object, method, value == null ? "" : value);
        } catch (Exception ignored) { }
    }

    private static int cameraRotationDegrees(android.app.Activity activity)
            throws CameraAccessException {
        int displayRotation;
        switch (activity.getWindowManager().getDefaultDisplay().getRotation()) {
            case Surface.ROTATION_90: displayRotation = 90; break;
            case Surface.ROTATION_180: displayRotation = 180; break;
            case Surface.ROTATION_270: displayRotation = 270; break;
            default: displayRotation = 0;
        }
        CameraManager cameras = (CameraManager) activity.getSystemService(android.content.Context.CAMERA_SERVICE);
        for (String id : cameras.getCameraIdList()) {
            CameraCharacteristics characteristics = cameras.getCameraCharacteristics(id);
            Integer facing = characteristics.get(CameraCharacteristics.LENS_FACING);
            Integer sensor = characteristics.get(CameraCharacteristics.SENSOR_ORIENTATION);
            if (facing != null && facing == CameraCharacteristics.LENS_FACING_BACK && sensor != null)
                return (sensor - displayRotation + 360) % 360;
        }
        return 0;
    }
}
