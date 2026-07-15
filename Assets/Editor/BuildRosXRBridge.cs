using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using static PanelBuilderUtils;

public static class BuildRosXRBridge
{
    // Box footprint each marker represents (marker = bottom-center of the bin).
    private const float BinHalfWidth = 0.06f;
    private const float BinHalfDepth = 0.06f;
    private const float BinHeight = 0.08f;

    private static readonly string[] MarkerNames =
        { "BinA_Marker", "BinB_Marker", "BinC_Marker", "BinD_Marker", "PlaceLocation_Marker" };

    [MenuItem("XR Panels/Build ROS XR Bridge")]
    public static void Build()
    {
        Transform xrRoot = FindOrCreate("XRPanelSystem", null).transform;

        if (!ConfirmAndClearExisting(xrRoot, "RosXRBridge")) return;

        GameObject go = new GameObject("RosXRBridge");
        Undo.RegisterCreatedObjectUndo(go, "Build Panel");
        go.transform.SetParent(xrRoot, false);

        RosXRBridge bridge = go.AddComponent<RosXRBridge>();

        Transform robotBase = FindLinkBaseUnderXRRobotRoot();
        bridge.robotBaseFrame = robotBase;
        if (robotBase == null)
            Debug.LogWarning("RosXRBridge: could not find 'link_base' under 'XRRobotRoot' — assign RosXRBridge.robotBaseFrame manually in the Inspector before publishing bin poses.");

        Transform markerParent = robotBase != null ? robotBase : go.transform;

        // Markers are parented under link_base, outside the RosXRBridge GameObject that
        // ConfirmAndClearExisting manages above — so previous runs' markers (and any earlier
        // pre-visual placeholder markers) must be cleaned up here explicitly, or rebuilding
        // silently accumulates duplicates.
        DestroyExistingMarkers(markerParent);

        // Laid out on a table in front of the robot (+X = forward reach direction): the place/kitting
        // location sits furthest forward, front-center; the 4 pick bins form a row behind it.
        bridge.placeTransform = CreateBinMarker(markerParent, "PlaceLocation", new Vector3(0f, 0f, 0.38f), Color.white);
        bridge.binATransform = CreateBinMarker(markerParent, "BinA", new Vector3(0.25f, 0f, 0.2f), Color.red);
        bridge.binBTransform = CreateBinMarker(markerParent, "BinB", new Vector3(0.1f, 0f, 0.2f), Color.green);
        bridge.binCTransform = CreateBinMarker(markerParent, "BinC", new Vector3(-0.1f, 0f, 0.2f), new Color(0.3f, 0.6f, 1f));
        bridge.binDTransform = CreateBinMarker(markerParent, "BinD", new Vector3(-0.25f, 0f, 0.2f), new Color(1f, 0.6f, 0f));

        Debug.LogWarning("RosXRBridge: bin/place markers were arranged on a placeholder table layout in front of the robot — " +
            "reposition the BinA/BinB/BinC/BinD/PlaceLocation marker objects in the Hierarchy to match your physical robot cell before relying on published poses.");

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("ROS XR Bridge built under XRPanelSystem/RosXRBridge.");
    }

    private static void DestroyExistingMarkers(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (System.Array.IndexOf(MarkerNames, child.name) >= 0)
                Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static Transform FindLinkBaseUnderXRRobotRoot()
    {
        GameObject robotRoot = GameObject.Find("XRRobotRoot");
        return robotRoot != null ? FindDeepChild(robotRoot.transform, "link_base") : null;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>Creates a marker Transform (bottom-center of the bin) plus a wireframe box showing its
    /// footprint and a fixed-rotation name label floating above it.</summary>
    private static Transform CreateBinMarker(Transform parent, string label, Vector3 localPosition, Color color)
    {
        GameObject marker = new GameObject(label + "_Marker");
        Undo.RegisterCreatedObjectUndo(marker, "Build Panel");
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPosition;
        marker.transform.localRotation = Quaternion.identity;

        AddWireframeBox(marker.transform, color);
        AddLabel(marker.transform, label, color);

        return marker.transform;
    }

    private static void AddWireframeBox(Transform parent, Color color)
    {
        float hx = BinHalfWidth, hz = BinHalfDepth, hy = BinHeight;

        Vector3 p0 = new Vector3(-hx, 0, -hz);
        Vector3 p1 = new Vector3(hx, 0, -hz);
        Vector3 p2 = new Vector3(hx, 0, hz);
        Vector3 p3 = new Vector3(-hx, 0, hz);
        Vector3 p4 = new Vector3(-hx, hy, -hz);
        Vector3 p5 = new Vector3(hx, hy, -hz);
        Vector3 p6 = new Vector3(hx, hy, hz);
        Vector3 p7 = new Vector3(-hx, hy, hz);

        // One continuous path covering all 12 box edges (a cube's edge graph has no Eulerian path, so
        // 3 of the 16 segments below immediately retrace an edge just drawn — harmless, same line twice).
        Vector3[] points = { p0, p1, p2, p3, p0, p4, p5, p1, p5, p6, p2, p6, p7, p3, p7, p4 };

        GameObject boxObj = new GameObject("BoundaryBox");
        Undo.RegisterCreatedObjectUndo(boxObj, "Build Panel");
        boxObj.transform.SetParent(parent, false);

        LineRenderer lr = boxObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = points.Length;
        lr.SetPositions(points);
        lr.startWidth = lr.endWidth = 0.004f;
        lr.material = new Material(Shader.Find("Unlit/Color")) { color = color };
        lr.startColor = lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    private static void AddLabel(Transform parent, string text, Color color)
    {
        GameObject labelObj = new GameObject("Label");
        Undo.RegisterCreatedObjectUndo(labelObj, "Build Panel");
        labelObj.transform.SetParent(parent, false);
        labelObj.transform.localPosition = Vector3.zero;
        labelObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        labelObj.transform.localScale = Vector3.one * 0.008f;

        TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 30;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
    }
}
