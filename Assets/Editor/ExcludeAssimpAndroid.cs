#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public class ExcludeAssimpAndroid : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;

        string[] guids = AssetDatabase.FindAssets("assimp");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("AssimpNet/Native/win")) continue;

            PluginImporter imp = AssetImporter.GetAtPath(path) as PluginImporter;
            if (imp == null) continue;

            imp.SetCompatibleWithAnyPlatform(false);
            imp.SetCompatibleWithPlatform(BuildTarget.Android, false);
            imp.SetCompatibleWithEditor(true);
            imp.SaveAndReimport();
        }
    }
}
#endif

