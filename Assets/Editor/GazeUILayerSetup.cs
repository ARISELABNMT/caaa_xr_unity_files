using UnityEngine;
using UnityEditor;

/// <summary>Creates the "GazeUI" physics layer used to isolate hands-free button colliders from other raycasts.</summary>
public static class GazeUILayerSetup
{
    private const string LayerName = "GazeUI";

    public static void EnsureLayerExists()
    {
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layerSP = layers.GetArrayElementAtIndex(i);
            if (layerSP.stringValue == LayerName) return;

            if (string.IsNullOrEmpty(layerSP.stringValue))
            {
                layerSP.stringValue = LayerName;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"Created layer '{LayerName}' at index {i}.");
                return;
            }
        }

        Debug.LogWarning($"Could not create layer '{LayerName}' — no empty layer slots available (8-31 all in use). Assign one manually in Edit > Project Settings > Tags and Layers, named exactly '{LayerName}'.");
    }
}
