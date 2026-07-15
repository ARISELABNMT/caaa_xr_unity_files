using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupFingerPokeInteractors
{
    [MenuItem("XR Panels/Setup Finger Poke Interactors")]
    public static void Build()
    {
        GazeUILayerSetup.EnsureLayerExists();

        OVRSkeleton[] skeletons = Object.FindObjectsOfType<OVRSkeleton>();
        if (skeletons.Length == 0)
        {
            Debug.LogWarning("Setup Finger Poke Interactors: no OVRSkeleton found in the scene. " +
                "Add the Meta 'Hand Tracking' Building Block first (same way you added Camera Rig / " +
                "Passthrough / Spatial Anchor Core), then re-run this command.");
            return;
        }

        int added = 0;
        foreach (var skeleton in skeletons)
        {
            if (skeleton.GetComponent<FingerPokeInteractor>() == null)
            {
                Undo.AddComponent<FingerPokeInteractor>(skeleton.gameObject);
                added++;
            }
        }

        Debug.Log($"Finger Poke Interactor added to {added} hand(s) (found {skeletons.Length} OVRSkeleton in scene).");
        EditorSceneManager.MarkSceneDirty(skeletons[0].gameObject.scene);
    }
}
