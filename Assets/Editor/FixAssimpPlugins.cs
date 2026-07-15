
#if UNITY_EDITOR
using UnityEditor;

public class FixAssimpPlugins
{
    [MenuItem("Tools/Fix Assimp Android Build")]
    static void Fix()
    {
        string[] guids = AssetDatabase.FindAssets("assimp");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("AssimpNet/Native/win")) continue;

            PluginImporter importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null) continue;

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, false);
            importer.SetCompatibleWithEditor(true);

            if (path.Contains("x86_64"))
            {
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                importer.SetEditorData("CPU", "x86_64");
            }
            else
            {
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, true);
                importer.SetPlatformData(BuildTarget.StandaloneWindows, "CPU", "x86");
                importer.SetEditorData("CPU", "x86");
            }

            importer.SaveAndReimport();
            UnityEngine.Debug.Log("Fixed: " + path);
        }
    }
}
#endif
