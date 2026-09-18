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

    [Header("Trail")]
    [Tooltip("Name of the end-effector link whose live motion gets traced.")]
    public string endEffectorLinkName = "link_eef";
    public Color trailColor = new Color(0f, 1f, 1f, 0.8f);
    public float trailWidth = 0.006f;
    [Tooltip("Only record a new trail point once the end effector has moved at least this far from the last one — keeps the trail from filling up with redundant points while barely moving.")]
    public float minPointSpacing = 0.005f;
    [Tooltip("Oldest points are dropped once the trail has this many, so it doesn't grow forever.")]
    public int maxTrailPoints = 300;

    private Dictionary<string, ArticulationBody> jointBodies = new Dictionary<string, ArticulationBody>();
    private Coroutine playbackCoroutine;
    private Transform _eefLink;
    private LineRenderer _trailRenderer;
    private readonly List<Vector3> _trailPoints = new List<Vector3>();
    private bool _isMoving = false;

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

        _eefLink = FindDeepChild(transform, endEffectorLinkName);
        _trailRenderer = CreateTrailRenderer();

        StartCoroutine(Subscribe());
#endif
    }

    LineRenderer CreateTrailRenderer()
    {
        var go = new GameObject("EndEffectorTrail");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 0;
        lr.startWidth = lr.endWidth = trailWidth;
        lr.material = new Material(Shader.Find("Unlit/Color")) { color = trailColor };
        lr.startColor = lr.endColor = trailColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    void Update()
    {
        if (!_isMoving || _eefLink == null || _trailRenderer == null) return;

        Vector3 pos = _eefLink.position;
        if (_trailPoints.Count == 0 || Vector3.Distance(_trailPoints[_trailPoints.Count - 1], pos) >= minPointSpacing)
        {
            _trailPoints.Add(pos);
            if (_trailPoints.Count > maxTrailPoints)
                _trailPoints.RemoveAt(0);

            _trailRenderer.positionCount = _trailPoints.Count;
            _trailRenderer.SetPositions(_trailPoints.ToArray());
        }
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

        _trailPoints.Clear();
        if (_trailRenderer != null) _trailRenderer.positionCount = 0;
    }

    IEnumerator PlayTrajectory(TrajectoryData data)
    {
        _isMoving = true;
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
        _isMoving = false;
    }

    [System.Serializable] class TrajectoryData { public string[] joint_names; public TrajectoryPoint[] points; }
    [System.Serializable] class TrajectoryPoint { public float[] positions; public int time_sec; public int time_nanosec; }
}
