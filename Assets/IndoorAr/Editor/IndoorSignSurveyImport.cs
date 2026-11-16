using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IndoorAr.Editor
{
    /// <summary>
    /// Writes Resources/IndoorSignCalibration.json from a sign survey (see OCR-POC-prerequisites.md). Only signs whose
    /// surveyed sightings pass IndoorSignPose.Validate can pose the camera, and only on the survey's device model.
    /// </summary>
    public static class IndoorSignSurveyImport
    {
        public const string CalibrationPath = "Assets/IndoorAr/Resources/IndoorSignCalibration.json";

        [MenuItem("Indoor AR/Import Sign Survey")]
        public static void Import()
        {
            var path = EditorUtility.OpenFilePanel("Sign survey", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            var report = new List<string>();
            var calibration = IndoorSignSurvey.ToCalibration(File.ReadAllText(path), report);
            File.WriteAllText(CalibrationPath, JsonUtility.ToJson(calibration, true) + "\n");
            AssetDatabase.ImportAsset(CalibrationPath);
            Debug.Log("[IndoorAr] sign survey imported\n" + string.Join("\n", report));
        }
    }
}
