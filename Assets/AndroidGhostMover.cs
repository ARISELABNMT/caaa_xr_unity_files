using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class AndroidGhostMover : MonoBehaviour
{
    [Header("Visual")]
    public Material ghostMaterial;

    [Header("Playback")]
    public float speedMultiplier = 1f;

    private Dictionary<string, ArticulationBody> jointBodies = new Dictionary<string, ArticulationBody>();
    private Coroutine playbackCoroutine;

    void Awake()
    {
#if UNITY_ANDROID
        var existing = GetComponent<GhostRobotController>();
        if (existing != null) existing.enabled = false;
#endif
    }

    void Start()
    {
#if UNITY_ANDROID
        if (ghostMaterial != null)
        {
            foreach (var rend in GetComponentsInChildren<MeshRenderer>())
                rend.material = ghostMaterial;
        }

        foreach (var body in GetComponentsInChildren<ArticulationBody>())
        {
            string n = body.gameObject.name;
            if (IsRobotLink(n))
            {
                string jointName = "joint" + n.Substring(4);
                jointBodies[jointName] = body;
            }
        }
        Debug.Log("AndroidGhostMover joints found: " + jointBodies.Count);
        StartCoroutine(Subscribe());
#endif
    }

    bool IsRobotLink(string name)
    {
        if (!name.StartsWith("link")) return false;
        string suffix = name.Substring(4);
        return suffix.Length >= 1 && suffix.Length <= 2 && int.TryParse(suffix, out _);
    }

    IEnumerator Subscribe()
    {
        yield return new WaitForSeconds(3f);
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>("/unity_planned_trajectory", OnTrajectory);
        Debug.Log("AndroidGhostMover subscribed");
    }

    void OnTrajectory(StringMsg msg)
    {
        var data = JsonUtility.FromJson<TrajectoryData>(msg.data);
        if (data == null || data.points == null || data.points.Length == 0) return;
        if (playbackCoroutine != null) StopCoroutine(playbackCoroutine);
        playbackCoroutine = StartCoroutine(PlayTrajectory(data));
    }

    IEnumerator PlayTrajectory(TrajectoryData data)
    {
        float prevTime = 0f;
        float speed = Mathf.Max(0.01f, speedMultiplier);
        for (int i = 0; i < data.points.Length; i++)
        {
            var pt = data.points[i];
            float t = pt.time_sec + pt.time_nanosec * 1e-9f;
            float wait = (t - prevTime) / speed;
            if (wait > 0) yield return new WaitForSeconds(wait);
            prevTime = t;

            for (int j = 0; j < data.joint_names.Length && j < pt.positions.Length; j++)
            {
                if (!jointBodies.TryGetValue(data.joint_names[j], out ArticulationBody body)) continue;
                var drive = body.xDrive;
                drive.stiffness = 10000f;
                drive.damping = 100f;
                drive.forceLimit = 1000f;
                drive.target = pt.positions[j] * Mathf.Rad2Deg;
                body.xDrive = drive;
            }
        }
    }

    [System.Serializable] class TrajectoryData { public string[] joint_names; public TrajectoryPoint[] points; }
    [System.Serializable] class TrajectoryPoint { public float[] positions; public int time_sec; public int time_nanosec; }
}
