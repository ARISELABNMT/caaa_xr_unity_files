using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Meta.XR.MRUtilityKit;
using static PanelBuilderUtils;

public static class BuildPassthroughQRAligner
{
    [MenuItem("XR Panels/Build Passthrough QR Aligner")]
    public static void Build()
    {
        Transform xrRoot = FindOrCreate("XRPanelSystem", null).transform;

        if (!ConfirmAndClearExisting(xrRoot, "PassthroughQRAligner")) return;

        GameObject go = new GameObject("PassthroughQRAligner");
        Undo.RegisterCreatedObjectUndo(go, "Build Panel");
        go.transform.SetParent(xrRoot, false);

        // MRUK is a scene-wide singleton — reuse it if already present rather than creating a second.
        MRUK mruk = Object.FindObjectOfType<MRUK>();
        if (mruk == null)
        {
            GameObject mrukGo = new GameObject("MRUK");
            Undo.RegisterCreatedObjectUndo(mrukGo, "Build Panel");
            mruk = mrukGo.AddComponent<MRUK>();
            Debug.Log("BuildPassthroughQRAligner: created MRUK component.");
        }
        // SceneSettings has no inline default — a freshly-added MRUK component may not have it
        // initialized yet (only guaranteed non-null after Unity's serialization round-trips it).
        if (mruk.SceneSettings == null)
            mruk.SceneSettings = new MRUK.MRUKSettings();

        var trackerConfig = mruk.SceneSettings.TrackerConfiguration;
        trackerConfig.QRCodeTrackingEnabled = true;
        mruk.SceneSettings.TrackerConfiguration = trackerConfig;

        // EnableWorldLock (on by default) continuously re-adjusts the OVRCameraRig/TrackingSpace
        // transform every frame to keep MRUK's own anchors self-consistent. That fights our one-shot
        // "snap the robot to wherever the marker currently is" approach: the robot is placed correctly
        // at the moment of Save, but then visibly drifts away as MRUK keeps nudging the tracking space
        // afterward. We don't need MRUK's continuous world-locking for this, so disable it.
        mruk.EnableWorldLock = false;

        PassthroughQRAligner aligner = go.AddComponent<PassthroughQRAligner>();

        GameObject robotRoot = GameObject.Find("XRRobotRoot");
        if (robotRoot != null)
            aligner.robotRoot = robotRoot.transform;
        else
            Debug.LogWarning("PassthroughQRAligner: could not find 'XRRobotRoot' in the scene — assign PassthroughQRAligner.robotRoot manually in the Inspector.");

        // The floating QRAlignerStatus panel (and its Start/Save buttons) was removed — the toggle
        // button on Control Panel is the only control now, and its label text ("Start Aligning" /
        // "Save Position") is sufficient feedback on its own. Clean up any leftover panel from an
        // earlier build so it doesn't linger in the scene showing stale text forever.
        for (int i = xrRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = xrRoot.GetChild(i);
            if (child.name == "QRAlignerStatus")
                Undo.DestroyObjectImmediate(child.gameObject);
        }

        // The Vuforia/ArUco path is superseded by this marker-based approach and never actually ran
        // (no VuforiaBehaviour/ARCamera exists in this scene) — disable it to reduce clutter/confusion.
        // Re-enable manually in the Hierarchy if you want to revisit it.
        GameObject oldImageTarget = GameObject.Find("ImageTarget");
        if (oldImageTarget != null && oldImageTarget.activeSelf)
        {
            oldImageTarget.SetActive(false);
            Debug.Log("BuildPassthroughQRAligner: disabled the old dormant 'ImageTarget' (Vuforia/ArUco) GameObject — superseded by PassthroughQRAligner.");
        }

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Passthrough QR Aligner built under XRPanelSystem/PassthroughQRAligner, using MRUK QR code trackable detection. " +
            "Run 'XR Panels > Configure MRUK Scene Support' once if you haven't already (sets Scene Support = Required, Anchor Support = Enabled in the project config).");
    }
}
