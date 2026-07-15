using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using static PanelBuilderUtils;

public static class BuildRobotTaskManager
{
    [MenuItem("XR Panels/Build Robot Task Manager")]
    public static void Build()
    {
        Transform xrRoot = FindOrCreate("XRPanelSystem", null).transform;

        if (!ConfirmAndClearExisting(xrRoot, "RobotTaskManager")) return;

        GameObject go = new GameObject("RobotTaskManager");
        Undo.RegisterCreatedObjectUndo(go, "Build Panel");
        go.transform.SetParent(xrRoot, false);

        RobotTaskManager manager = go.AddComponent<RobotTaskManager>();
        manager.taskPanel = Object.FindObjectOfType<TaskPanelUI>();
        manager.rosBridge = Object.FindObjectOfType<RosXRBridge>();

        if (manager.taskPanel == null || manager.rosBridge == null)
            Debug.LogWarning("Robot Task Manager: one or more dependencies (Task Panel, ROS XR Bridge) were not found in the scene — build those first, or use Build All Panels, then re-run this if any reference is still missing. Without a RosXRBridge, kitting runs will fall back to a local timed simulation instead of commanding the real robot.");

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Robot Task Manager built under XRPanelSystem/RobotTaskManager.");
    }
}
