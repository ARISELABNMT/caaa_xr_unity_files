using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(LineRenderer))]
public class RobotRayDragger : MonoBehaviour
{
    [Header("References")]
    public Transform robotRoot;
    public Transform rightControllerAnchor;

    [Header("Settings")]
    public float rayLength   = 10f;
    public float rotateSpeed = 60f;
    public float depthSpeed  = 1.5f;

    [Header("UI")]
    public TMP_Text statusText;

    private LineRenderer lr;
    private bool isDragging = false;
    private Vector3 dragOffset;
    private float hitDistance;
    private Vector3 cachedOrigin;
    private Vector3 cachedDir;

    private List<ArticulationBody> rootBodies       = new List<ArticulationBody>();
    private List<MonoBehaviour>    locomotionComps   = new List<MonoBehaviour>();

    private readonly Color idleColor  = new Color(0f, 0.8f, 1f);
    private readonly Color hoverColor = Color.green;
    private readonly Color dragColor  = Color.yellow;

    private const string KEY_X="Robot_X", KEY_Y="Robot_Y", KEY_Z="Robot_Z", KEY_ROT="Robot_RotY";

    void Start()
    {
        SetupLineRenderer();
        FindControllerAnchor();
        FindLocomotionComponents();
        StartCoroutine(InitAfterPhysics());
    }

    IEnumerator InitAfterPhysics()
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        FindRootBodies();
        yield return new WaitForFixedUpdate();
        LoadPosition();
    }

    void FindControllerAnchor()
    {
        if (rightControllerAnchor != null) return;
        var go = GameObject.Find("RightHandAnchor") ?? GameObject.Find("RightControllerAnchor");
        if (go != null) rightControllerAnchor = go.transform;
        else Debug.LogWarning("RobotRayDragger: RightHandAnchor not found");
    }

    void FindRootBodies()
    {
        rootBodies.Clear();
        if (robotRoot == null) return;
        foreach (var b in robotRoot.GetComponentsInChildren<ArticulationBody>())
            if (b.isRoot) rootBodies.Add(b);
        Debug.Log("RobotRayDragger: " + rootBodies.Count + " root bodies");
    }

    void FindLocomotionComponents()
    {
        locomotionComps.Clear();

        var pc = FindObjectOfType<OVRPlayerController>();
        if (pc != null) locomotionComps.Add(pc);

        // Search Building Block locomotion by common names
        foreach (string n in new[]{"Locomotion","PlayerController","MovementController","LocomotionController"})
        {
            var go = GameObject.Find(n);
            if (go == null) continue;
            foreach (var c in go.GetComponents<MonoBehaviour>())
                if (c != null) locomotionComps.Add(c);
        }

        // Also check OVRCameraRig children for locomotion
        var rig = FindObjectOfType<OVRCameraRig>();
        if (rig != null)
        {
            foreach (var c in rig.GetComponents<MonoBehaviour>())
            {
                string t = c.GetType().Name.ToLower();
                if (t.Contains("locomot") || t.Contains("movement") || t.Contains("player"))
                    locomotionComps.Add(c);
            }
        }

        Debug.Log("RobotRayDragger: " + locomotionComps.Count + " locomotion components found");
    }

    void SetupLineRenderer()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth    = 0.004f;
        lr.endWidth      = 0.001f;
        lr.useWorldSpace = true;
        lr.material      = new Material(Shader.Find("Sprites/Default"));
        lr.enabled       = false;
    }

    void Update()
    {
        if (rightControllerAnchor == null || robotRoot == null) return;

        // FIX 2: Ray only visible when controller is physically held (grip slightly pressed)
        float gripForce = OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger);
        bool holding = gripForce > 0.05f || isDragging;
        lr.enabled = holding;

        if (!holding) { SetLocomotion(true); return; }

        Vector3 origin = rightControllerAnchor.position;
        Vector3 dir    = rightControllerAnchor.forward;
        cachedOrigin   = origin;
        cachedDir      = dir;

        if (isDragging)
        {
            SetLocomotion(false);
            float depth = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y;
            hitDistance = Mathf.Clamp(hitDistance + depth * depthSpeed * Time.deltaTime, 0.3f, rayLength);
            DrawRay(origin, origin + dir * hitDistance, dragColor);

            if (OVRInput.GetUp(OVRInput.RawButton.RIndexTrigger))
            { isDragging = false; SetLocomotion(true); SetStatus(""); }
        }
        else
        {
            RaycastHit hit;
            bool hitRobot = Physics.Raycast(origin, dir, out hit, rayLength) && IsChildOfRobot(hit.transform);
            DrawRay(origin, origin + dir * (hitRobot ? hit.distance : rayLength), hitRobot ? hoverColor : idleColor);
            SetStatus(hitRobot ? "Pull Trigger to grab" : "");

            if (hitRobot && OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
            {
                isDragging  = true;
                hitDistance = hit.distance;
                dragOffset  = GetCenter() - hit.point;
                SetLocomotion(false);
                SetStatus("Dragging | L-Stick Y: Depth | R-Grip+Stick: Rotate | B: Save");
            }

            // Rotate while not dragging: right grip + right thumbstick X
            if (OVRInput.Get(OVRInput.Button.SecondaryHandTrigger))
            {
                SetLocomotion(false);
                float rx = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).x;
                if (Mathf.Abs(rx) > 0.1f) RotateRobots(rx * rotateSpeed * Time.deltaTime);
            }
        }

        if (OVRInput.GetDown(OVRInput.Button.Two)) { SavePosition(); SetStatus("Saved!"); }
    }

    // FIX 1: Apply drag in FixedUpdate so physics doesn't fight TeleportRoot
    void FixedUpdate()
    {
        if (!isDragging) return;
        Vector3 target = cachedOrigin + cachedDir * hitDistance + dragOffset;
        Vector3 delta  = target - GetCenter();
        MoveRobots(delta);
    }

    Vector3 GetCenter() =>
        rootBodies.Count > 0 ? rootBodies[0].transform.position : robotRoot.position;

    // FIX 1: Use TeleportRoot instead of setting transform.position
    void MoveRobots(Vector3 delta)
    {
        robotRoot.position += delta;
        foreach (var b in rootBodies)
            b.TeleportRoot(b.transform.position + delta, b.transform.rotation);
    }

    void RotateRobots(float yAngle)
    {
        robotRoot.Rotate(0f, yAngle, 0f, Space.World);
        Vector3 pivot = robotRoot.position;
        Quaternion rot = Quaternion.AngleAxis(yAngle, Vector3.up);
        foreach (var b in rootBodies)
            b.TeleportRoot(pivot + rot * (b.transform.position - pivot), rot * b.transform.rotation);
    }

    // FIX 3: Disable locomotion while aligning
    void SetLocomotion(bool on)
    {
        foreach (var c in locomotionComps)
            if (c != null) c.enabled = on;
    }

    void DrawRay(Vector3 s, Vector3 e, Color c)
    {
        lr.SetPosition(0, s); lr.SetPosition(1, e);
        lr.startColor = c;
        lr.endColor   = new Color(c.r, c.g, c.b, 0f);
    }

    bool IsChildOfRobot(Transform t)
    {
        while (t != null) { if (t == robotRoot) return true; t = t.parent; }
        return false;
    }

    void SetStatus(string msg) { if (statusText != null) statusText.text = msg; }

    void SavePosition()
    {
        PlayerPrefs.SetFloat(KEY_X, robotRoot.position.x);
        PlayerPrefs.SetFloat(KEY_Y, robotRoot.position.y);
        PlayerPrefs.SetFloat(KEY_Z, robotRoot.position.z);
        PlayerPrefs.SetFloat(KEY_ROT, robotRoot.eulerAngles.y);
        PlayerPrefs.Save();
    }

    void LoadPosition()
    {
        if (!PlayerPrefs.HasKey(KEY_X)) return;
        Vector3 saved = new Vector3(PlayerPrefs.GetFloat(KEY_X), PlayerPrefs.GetFloat(KEY_Y), PlayerPrefs.GetFloat(KEY_Z));
        float savedRot = PlayerPrefs.GetFloat(KEY_ROT);
        MoveRobots(saved - robotRoot.position);
        RotateRobots(savedRot - robotRoot.eulerAngles.y);
    }
}

