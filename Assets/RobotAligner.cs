using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class RobotAligner : MonoBehaviour
{
    [Header("Target")]
    public Transform robotRoot;

    [Header("Speed")]
    public float translateSpeed = 0.5f;
    public float rotateSpeed    = 40f;

    [Header("UI")]
    public TMP_Text statusText;

    [Header("Locomotion Override (drag manually if auto-find fails)")]
    public MonoBehaviour locomotionOverride;

    private List<ArticulationBody> rootBodies     = new List<ArticulationBody>();
    private List<MonoBehaviour>    locomotionComps = new List<MonoBehaviour>();

    private const string KEY_X="Robot_X", KEY_Y="Robot_Y", KEY_Z="Robot_Z", KEY_ROT="Robot_RotY";

    void Start()
    {
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

    void FindRootBodies()
    {
        rootBodies.Clear();
        if (robotRoot == null) return;
        foreach (var b in robotRoot.GetComponentsInChildren<ArticulationBody>())
            if (b.isRoot) rootBodies.Add(b);
    }

    void FindLocomotionComponents()
    {
        locomotionComps.Clear();

        var pc = FindObjectOfType<OVRPlayerController>();
        if (pc != null) locomotionComps.Add(pc);

        // Target SampleInputManager on Avatar SDK (causes avatar thumbstick movement)
        var avatarGo = GameObject.Find("AvatarSdkManagerStyle2Meta");
        if (avatarGo != null)
            foreach (var c in avatarGo.GetComponents<MonoBehaviour>())
                if (c.GetType().Name == "SampleInputManager")
                    locomotionComps.Add(c);

        var rig = FindObjectOfType<OVRCameraRig>();
        if (rig != null)
            foreach (var c in rig.GetComponents<MonoBehaviour>())
            {
                string t = c.GetType().Name.ToLower();
                if (t.Contains("locomot") || t.Contains("movement") || t.Contains("player"))
                    locomotionComps.Add(c);
            }

        if (locomotionOverride != null && !locomotionComps.Contains(locomotionOverride))
            locomotionComps.Add(locomotionOverride);
    }

    void Update()
    {
        bool aligning = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger);

        SetLocomotion(!aligning);

        if (aligning && statusText != null)
            statusText.text = "ALIGN MODE\nR-Stick: Move XZ | L-Stick Y: Up/Down\nX: Rot Left  Y: Rot Right  B: Save";

        if (!aligning || robotRoot == null) return;

        Vector2 right = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        if (right.magnitude > 0.1f)
            MoveRobots(new Vector3(right.x, 0f, right.y) * translateSpeed * Time.deltaTime);

        float upDown = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y;
        if (Mathf.Abs(upDown) > 0.1f)
            MoveRobots(Vector3.up * upDown * translateSpeed * Time.deltaTime);

        if (OVRInput.Get(OVRInput.Button.Three))
            RotateRobots(-rotateSpeed * Time.deltaTime);
        if (OVRInput.Get(OVRInput.Button.Four))
            RotateRobots(rotateSpeed * Time.deltaTime);

        if (OVRInput.GetDown(OVRInput.Button.Two))
        {
            SavePosition();
            if (statusText != null) statusText.text = "Saved!";
        }
    }

    void MoveRobots(Vector3 delta)
    {
        robotRoot.position += delta;
        foreach (var b in rootBodies)
            b.TeleportRoot(b.transform.position + delta, b.transform.rotation);
    }

    void RotateRobots(float yAngle)
    {
        Vector3 pivot = Vector3.zero;
        if (rootBodies.Count > 0)
        {
            foreach (var b in rootBodies) pivot += b.transform.position;
            pivot /= rootBodies.Count;
        }
        else pivot = robotRoot.position;

        Quaternion rot = Quaternion.AngleAxis(yAngle, Vector3.up);
        robotRoot.position = pivot + rot * (robotRoot.position - pivot);
        robotRoot.Rotate(0f, yAngle, 0f, Space.World);
        foreach (var b in rootBodies)
            b.TeleportRoot(pivot + rot * (b.transform.position - pivot), rot * b.transform.rotation);
    }

    void SetLocomotion(bool on)
    {
        foreach (var c in locomotionComps)
            if (c != null) c.enabled = on;
    }

    void SavePosition()
    {
        PlayerPrefs.SetFloat(KEY_X,   robotRoot.position.x);
        PlayerPrefs.SetFloat(KEY_Y,   robotRoot.position.y);
        PlayerPrefs.SetFloat(KEY_Z,   robotRoot.position.z);
        PlayerPrefs.SetFloat(KEY_ROT, robotRoot.eulerAngles.y);
        PlayerPrefs.Save();
    }

    void LoadPosition()
    {
        if (!PlayerPrefs.HasKey(KEY_X)) return;
        Vector3 saved = new Vector3(
            PlayerPrefs.GetFloat(KEY_X),
            PlayerPrefs.GetFloat(KEY_Y),
            PlayerPrefs.GetFloat(KEY_Z));
        MoveRobots(saved - robotRoot.position);
        float rotDelta = PlayerPrefs.GetFloat(KEY_ROT) - robotRoot.eulerAngles.y;
        if (Mathf.Abs(rotDelta) > 0.01f) RotateRobots(rotDelta);
    }
}
