using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class IndoorMapExporter
{
    [Serializable]
    private class IndoorMapFile
    {
        public string buildingId;
        public List<IndoorObjectData> objects = new List<IndoorObjectData>();
    }

    [Serializable]
    private class IndoorObjectData
    {
        public string id;
        public string type;
        public string floorId;
        public CoordinateData position;
        public float rotationY;
    }

    [Serializable]
    private class CoordinateData
    {
        public float x;
        public float y;
        public float z;
    }

    [MenuItem("Indoor Navigation/Export Scene Objects")]
    public static void ExportSceneObjects()
    {
        IndoorMapOrigin origin =
            UnityEngine.Object.FindObjectOfType<IndoorMapOrigin>();

        if (origin == null)
        {
            EditorUtility.DisplayDialog(
                "Missing Building Origin",
                "Add an IndoorMapOrigin component to the BuildingOrigin object.",
                "OK"
            );

            return;
        }

        IndoorMapMarker[] markers =
            UnityEngine.Object.FindObjectsOfType<IndoorMapMarker>(true);

        if (markers.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "No Markers Found",
                "Add at least one IndoorMapMarker to the scene.",
                "OK"
            );

            return;
        }

        IndoorMapFile output = new IndoorMapFile
        {
            buildingId = "VEC"
        };

        HashSet<string> usedIds = new HashSet<string>();

        foreach (IndoorMapMarker marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker.objectId) ||
                usedIds.Contains(marker.objectId))
            {
                Undo.RecordObject(marker, "Generate indoor marker ID");
                marker.objectId = Guid.NewGuid().ToString("N");
                EditorUtility.SetDirty(marker);
            }

            usedIds.Add(marker.objectId);

            Vector3 localPosition =
                origin.transform.InverseTransformPoint(
                    marker.transform.position
                );

            Quaternion localRotation =
                Quaternion.Inverse(origin.transform.rotation) *
                marker.transform.rotation;

            IndoorObjectData objectData = new IndoorObjectData
            {
                id = marker.objectId,
                type = marker.objectType,
                floorId = marker.floorId,

                position = new CoordinateData
                {
                    x = localPosition.x,
                    y = localPosition.y,
                    z = localPosition.z
                },

                rotationY = localRotation.eulerAngles.y
            };

            output.objects.Add(objectData);
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
        }

        string savePath = EditorUtility.SaveFilePanel(
            "Export Indoor Map Objects",
            Application.dataPath,
            "vec_indoor_objects",
            "json"
        );

        if (string.IsNullOrEmpty(savePath))
        {
            return;
        }

        string json = JsonUtility.ToJson(output, true);
        File.WriteAllText(savePath, json);

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Export Complete",
            $"Exported {output.objects.Count} objects to:\n{savePath}",
            "OK"
        );
    }
}