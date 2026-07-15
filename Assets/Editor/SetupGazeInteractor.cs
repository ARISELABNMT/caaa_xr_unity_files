using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupGazeInteractor
{
    [MenuItem("XR Panels/Setup Gaze Interactor")]
    public static void Build()
    {
        GazeUILayerSetup.EnsureLayerExists();

        GameObject cameraObj = GameObject.Find("CenterEyeAnchor");
        if (cameraObj == null && Camera.main != null)
            cameraObj = Camera.main.gameObject;

        if (cameraObj == null)
        {
            Debug.LogError("Setup Gaze Interactor: could not find CenterEyeAnchor or Camera.main in the scene. Add GazeInteractor manually to your XR camera.");
            return;
        }

        if (cameraObj.GetComponent<GazeInteractor>() == null)
        {
            Undo.AddComponent<GazeInteractor>(cameraObj);
            Debug.Log($"GazeInteractor added to {cameraObj.name}.");
        }
        else
        {
            Debug.Log($"GazeInteractor already present on {cameraObj.name}.");
        }

        EditorSceneManager.MarkSceneDirty(cameraObj.scene);
    }
}
