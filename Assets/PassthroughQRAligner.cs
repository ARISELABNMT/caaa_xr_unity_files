using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Meta.XR.MRUtilityKit;
using TMPro;

/// <summary>
/// Places the virtual robot to match the real robot's base, using Meta's own MR Utility Kit QR code
/// trackable detection (Quest's native tracking, not a hand-rolled camera-frame decode) to find a printed
/// QR marker fixed to the real robot base. This replaced an earlier ZXing.Net + Passthrough Camera Access
/// implementation that reconstructed depth from the marker's known physical size — that approach required
/// manual marker-size measurement and was unstable at oblique angles / while the headset moved, because it
/// was re-deriving world-space position from a single 2D camera frame each detection. MRUK's QR trackable
/// gives an already-correct, continuously-updating world-space Transform directly from the OS, with no
/// physical size configuration needed.
///
/// Tap "Start Aligning" (or hold <see cref="alignHoldButton"/> on a physical controller, if one is in
/// hand) and look at the marker to watch the robot track live, then tap "Stop & Save" (or release the
/// controller button) to lock it at the last tracked pose.
///
/// Requires: MRUK component in the scene with SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled =
/// true (see BuildPassthroughQRAligner.cs), Scene Support = Required + Anchor Support = Enabled in the
/// project config (XR Panels > Configure MRUK Scene Support), and the com.oculus.permission.USE_SCENE
/// runtime permission (requested by CameraPermission.cs).
/// </summary>
public class PassthroughQRAligner : MonoBehaviour
{
    [Header("Robot")]
    public Transform robotRoot;

    [Header("Mounting offset (marker plane -> robot base)")]
    public Vector3 markerToBaseOffset = Vector3.zero;
    [Tooltip("Extra rotation applied on top of the marker's own detected orientation, as Euler angles in " +
        "the marker's local frame (X = around the marker's red/right axis, Y = around its green/up axis, " +
        "Z = around its blue/forward axis). E.g. X=90 rotates the robot 90 degrees clockwise (viewed from " +
        "the positive X axis looking back toward the marker) around the marker's own X axis.")]
    public Vector3 markerToBaseRotationOffsetEuler = new Vector3(90f, 0f, 0f);

    [Header("Controls")]
    [Tooltip("Single toggle button (gaze/finger-poke or pointer) — first click starts alignment, second click saves the current pose and stops. Its label text updates automatically. Lives on Control Panel.")]
    public Button alignToggleButton;
    [Tooltip("Optional: hold this physical controller button as an alternate way to start/stop aligning, if a controller happens to be in hand.")]
    public OVRInput.Button alignHoldButton = OVRInput.Button.SecondaryHandTrigger;

    private const string IdleLabel = "Start Aligning";
    private const string AligningLabel = "Save Position";

    [Header("UI")]
    public TMP_Text statusText;

    [Header("Debug Visualization")]
    [Tooltip("Length of each axis line drawn at the detected marker's center.")]
    public float axisLength = 0.05f;

    private readonly List<ArticulationBody> _rootBodies = new();
    private readonly List<(ArticulationBody body, Vector3 localPos, Quaternion localRot)> _bodyOffsets = new();
    private ArticulationBody _primaryRootBody;
    private Vector3 _primaryLocalPos;
    private Quaternion _primaryLocalRot;
    private bool _isAligning;
    private string _lastResultText = "";
    private MRUKTrackable _qrTrackable;
    private LineRenderer _boundaryLine;
    private LineRenderer _axisX, _axisY, _axisZ;
    private LineRenderer _robotAxisX, _robotAxisY, _robotAxisZ;

    void Start()
    {
        foreach (var body in FindObjectsOfType<ArticulationBody>())
        {
            if (!body.isRoot) continue;
            _rootBodies.Add(body);
            // This scene has both a real and a "Ghost_Robot" preview robot sharing robotRoot as a common
            // ancestor. The debug gizmo should reflect the REAL robot's actual resulting position, not
            // robotRoot's raw target — robotRoot is just an organizational parent and isn't guaranteed to
            // coincide with the real robot's own ArticulationBody root origin.
            if (_primaryRootBody == null && !IsUnderGhost(body.transform))
                _primaryRootBody = body;
        }
        if (_primaryRootBody == null && _rootBodies.Count > 0)
            _primaryRootBody = _rootBodies[0];

        if (alignToggleButton) alignToggleButton.onClick.AddListener(ToggleAligning);

        CreateDebugVisuals();
        StartCoroutine(SubscribeToMRUK());

        SetButtonLabel(IdleLabel);
        SetStatus("Tap 'Start Aligning' on Control Panel (or hold controller grip) and look at the robot base marker.");
    }

    static bool IsUnderGhost(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (p.name == "Ghost_Robot") return true;
        return false;
    }

    IEnumerator SubscribeToMRUK()
    {
        // MRUK.Instance may not be initialized yet depending on script execution order.
        yield return new WaitUntil(() => MRUK.Instance != null);

        MRUK.Instance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        MRUK.Instance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);

        if (!MRUK.Instance.QRCodeTrackingSupported)
            Debug.LogWarning("[PassthroughQRAligner] QR code tracking is not supported on this device/runtime.");
    }

    void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            return;

        Debug.Log($"[PassthroughQRAligner] QR trackable added: '{trackable.MarkerPayloadString}'");
        _qrTrackable = trackable;
    }

    void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable == _qrTrackable)
        {
            Debug.Log("[PassthroughQRAligner] QR trackable lost.");
            _qrTrackable = null;
        }
    }

    void CreateDebugVisuals()
    {
        _boundaryLine = CreateDebugLine("QRBoundary", Color.yellow, 5, 0.003f);
        _axisX = CreateDebugLine("QRAxisX", Color.red, 2, 0.004f);
        _axisY = CreateDebugLine("QRAxisY", Color.green, 2, 0.004f);
        _axisZ = CreateDebugLine("QRAxisZ", Color.blue, 2, 0.004f);

        // Same convention as the marker's axes, drawn at wherever the robot base actually gets placed —
        // lets you visually compare the two instead of just trusting the numbers.
        _robotAxisX = CreateDebugLine("RobotAxisX", Color.red, 2, 0.004f);
        _robotAxisY = CreateDebugLine("RobotAxisY", Color.green, 2, 0.004f);
        _robotAxisZ = CreateDebugLine("RobotAxisZ", Color.blue, 2, 0.004f);
    }

    LineRenderer CreateDebugLine(string name, Color color, int pointCount, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = pointCount;
        lr.startWidth = lr.endWidth = width;
        lr.material = new Material(Shader.Find("Unlit/Color")) { color = color };
        lr.startColor = lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    public void ToggleAligning()
    {
        if (_isAligning) StopAligning();
        else StartAligning();
    }

    public void StartAligning()
    {
        _isAligning = true;
        CacheBodyOffsets();
        SetButtonLabel(AligningLabel);
        SetStatus("Aligning... look at the marker.");
    }

    /// <summary>
    /// Captures each root body's offset from robotRoot once, in robotRoot's own local space, at the
    /// moment alignment starts. ApplyToRobot then reapplies this fixed offset every frame instead of
    /// re-deriving it from body.transform.position each time — re-deriving it every frame let any lag
    /// between calling TeleportRoot and its effect actually landing on body.transform (physics-driven
    /// transforms aren't guaranteed to update instantly) compound into significant drift over the many
    /// frames alignment stays active before Save is tapped, since each frame's small error fed into the
    /// next frame's "current" reading.
    /// </summary>
    void CacheBodyOffsets()
    {
        _bodyOffsets.Clear();
        Vector3 rp = robotRoot.position;
        Quaternion invRr = Quaternion.Inverse(robotRoot.rotation);
        foreach (var body in _rootBodies)
        {
            Vector3 localPos = invRr * (body.transform.position - rp);
            Quaternion localRot = invRr * body.transform.rotation;
            _bodyOffsets.Add((body, localPos, localRot));
            Debug.Log($"[PassthroughQRAligner] cached offset for root body '{body.gameObject.name}': localPos={localPos:F3}");

            if (body == _primaryRootBody)
            {
                _primaryLocalPos = localPos;
                _primaryLocalRot = localRot;
            }
        }
    }

    public void StopAligning()
    {
        _isAligning = false;
        SetButtonLabel(IdleLabel);
        if (!string.IsNullOrEmpty(_lastResultText))
            SetStatus(_lastResultText + "\n(Saved)");
    }

    void SetButtonLabel(string label)
    {
        if (!alignToggleButton) return;
        var text = alignToggleButton.GetComponentInChildren<TMP_Text>();
        if (text) text.text = label;
    }

    void Update()
    {
        if (OVRInput.GetDown(alignHoldButton)) StartAligning();
        if (OVRInput.GetUp(alignHoldButton)) StopAligning();

        if (!_isAligning)
            return;

        if (_qrTrackable == null || !_qrTrackable.IsTracked)
            return;

        Transform markerT = _qrTrackable.transform;
        Vector3 markerCenter = GetMarkerCenter(_qrTrackable, markerT);
        Quaternion markerRot = markerT.rotation;

        UpdateDebugVisuals(_qrTrackable, markerCenter, markerRot);

        Quaternion worldRot = markerRot * Quaternion.Euler(markerToBaseRotationOffsetEuler);
        Vector3 worldPos = markerCenter + worldRot * markerToBaseOffset;

        ApplyToRobot(worldPos, worldRot);
    }

    /// <summary>
    /// MRUKTrackable.transform.position is the QR anchor's raw origin, which MRUK does not guarantee is
    /// centered on the printed code — it can sit toward one edge/corner of PlaneRect, showing up as the
    /// debug axis (and thus the whole alignment) floating slightly off the physical marker. PlaneRect's
    /// own midpoint is the actual geometric center of the tracked plane, so prefer that when available.
    /// </summary>
    static Vector3 GetMarkerCenter(MRUKTrackable trackable, Transform markerT)
    {
        if (trackable.PlaneRect.HasValue)
        {
            Rect r = trackable.PlaneRect.Value;
            Vector3 localCenter = new Vector3((r.xMin + r.xMax) * 0.5f, (r.yMin + r.yMax) * 0.5f, 0f);
            return markerT.TransformPoint(localCenter);
        }
        return markerT.position;
    }

    void UpdateDebugVisuals(MRUKTrackable trackable, Vector3 center, Quaternion rot)
    {
        if (trackable.PlaneRect.HasValue)
        {
            Rect r = trackable.PlaneRect.Value;
            Vector3 c0 = trackable.transform.TransformPoint(new Vector3(r.xMin, r.yMin, 0f));
            Vector3 c1 = trackable.transform.TransformPoint(new Vector3(r.xMax, r.yMin, 0f));
            Vector3 c2 = trackable.transform.TransformPoint(new Vector3(r.xMax, r.yMax, 0f));
            Vector3 c3 = trackable.transform.TransformPoint(new Vector3(r.xMin, r.yMax, 0f));
            _boundaryLine.enabled = true;
            _boundaryLine.SetPositions(new[] { c0, c1, c2, c3, c0 });
        }
        else
        {
            _boundaryLine.enabled = false;
        }

        _axisX.enabled = true;
        _axisX.SetPositions(new[] { center, center + rot * Vector3.right * axisLength });

        _axisY.enabled = true;
        _axisY.SetPositions(new[] { center, center + rot * Vector3.up * axisLength });

        _axisZ.enabled = true;
        _axisZ.SetPositions(new[] { center, center + rot * Vector3.forward * axisLength });
    }

    void ApplyToRobot(Vector3 worldPos, Quaternion worldRot)
    {
        robotRoot.SetPositionAndRotation(worldPos, worldRot);

        foreach (var (body, localPos, localRot) in _bodyOffsets)
        {
            Vector3 newBodyPos = worldPos + worldRot * localPos;
            Quaternion newBodyRot = worldRot * localRot;
            body.TeleportRoot(newBodyPos, newBodyRot);
        }

        // Drawn at the real robot's actual resulting body position, not the raw robotRoot target —
        // robotRoot is just an organizational parent and its origin isn't guaranteed to coincide with
        // where the real robot's own ArticulationBody root (and thus its visible mesh) ends up.
        Vector3 robotGizmoPos = worldPos;
        Quaternion robotGizmoRot = worldRot;
        if (_primaryRootBody != null)
        {
            robotGizmoPos = worldPos + worldRot * _primaryLocalPos;
            robotGizmoRot = worldRot * _primaryLocalRot;
        }

        _robotAxisX.enabled = true;
        _robotAxisX.SetPositions(new[] { robotGizmoPos, robotGizmoPos + robotGizmoRot * Vector3.right * axisLength });
        _robotAxisY.enabled = true;
        _robotAxisY.SetPositions(new[] { robotGizmoPos, robotGizmoPos + robotGizmoRot * Vector3.up * axisLength });
        _robotAxisZ.enabled = true;
        _robotAxisZ.SetPositions(new[] { robotGizmoPos, robotGizmoPos + robotGizmoRot * Vector3.forward * axisLength });

        Vector3 e = worldRot.eulerAngles;
        _lastResultText = $"QR aligned (live)\nPos X:{worldPos.x:F3} Y:{worldPos.y:F3} Z:{worldPos.z:F3}\nRot X:{e.x:F1} Y:{e.y:F1} Z:{e.z:F1}";
        SetStatus(_lastResultText);
    }

    void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        Debug.Log("[PassthroughQRAligner] " + msg);
    }
}
