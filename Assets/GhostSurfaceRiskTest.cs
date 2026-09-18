using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The canonical XR-based Risk Assessment module — computes R(t) = P(t) x I(t) x S from ISO/TS 15066 using
/// true surface-to-surface distance on both sides: the actual URDF collision meshes for the robot (the live
/// scene's own MeshCollider components had no mesh assigned and no sibling MeshFilter to borrow from, so
/// these are rebuilt from the source collision prefabs at Start), and DigitalHumanTracker's capsule segments
/// (joint pairs + radius, mating at shared joints) for the human side — rather than link-origin-to-joint-point
/// distance, which an earlier, now-removed version of this module used. Ghost robot only, by design — no
/// real-robot (UF_ROBOT) computation, since predictive/ghost risk is what the rest of the system consumes.
///
/// Setup: drag each of the 7 collision prefabs from Assets/URDF/xarm_description/meshes/lite6/collision/
/// into linkMeshSources, pairing each with its link name ("link_base", "link1" .. "link6"). At Start, this
/// spawns one invisible trigger MeshCollider child (local-identity, matching the URDF meshes' own authored
/// space) under the matching ghost link, holding the real collision mesh — isTrigger so it can never affect
/// the ghost's ArticulationBody physics, only serve ClosestPoint() queries.
///
/// This is the sole source for SystemStatusUI's risk/distance/nearest-body-part display, and the intended
/// source for any future mode-switching logic that needs the risk score.
/// </summary>
public class GhostSurfaceRiskTest : MonoBehaviour
{
    [System.Serializable]
    public struct LinkMeshSource
    {
        public string linkName;
        public GameObject meshPrefab;
    }

    [Header("Ghost robot root (auto-found by name if left empty)")]
    public Transform ghostRobotRoot;

    [Header("Human tracking (auto-found in scene if left empty)")]
    public DigitalHumanTracker humanTracker;

    [Header("Collision meshes (drag prefabs from Assets/URDF/xarm_description/meshes/lite6/collision/)")]
    public LinkMeshSource[] linkMeshSources;

    [Header("ISO/TS 15066 protective separation (Eq. 3, Table II)")]
    public float humanApproachSpeed = 1.6f;
    public float sensorResponseTime = 0.05f;
    public float robotStoppingTime = 0.08f;
    public float humanReactionTime = 0.10f;
    public float intrusionDistance = 0.08f;
    public float positionUncertainty = 0.03f;

    [Header("Probability factor P(t) (Eq. 2)")]
    public float alphaDecay = 4.0f;

    [Header("Impact factor I(t) (Eq. 4, Table II)")]
    public float effectiveMass = 5.4f;
    public float keMax = 2.0f;

    [Header("Ghost look-ahead")]
    public float predictiveLookAheadWindow = 0.5f;

    [Header("Predictive worst-case assumption")]
    public float ratedMaxTcpSpeed = 1.5f;

    [Header("Human capsule model")]
    [Tooltip("Points sampled along each body-segment capsule's centerline when measuring distance to the " +
        "robot surface. Higher = more accurate for long segments (e.g. forearm), more ClosestPoint() calls.")]
    public int samplesPerSegment = 5;

    [Header("Status panel (auto-found in scene if left empty)")]
    public SystemStatusUI statusUI;
    public float uiUpdateInterval = 0.1f;

    [Header("Diagnostics")]
    public float diagInterval = 1f;

    public float Risk01 { get; private set; }
    public float Probability01 { get; private set; }
    public float Impact01 { get; private set; }
    public float Severity01 { get; private set; }
    public float ProtectiveSeparationMeters { get; private set; }
    public float MinDistance { get; private set; } = float.PositiveInfinity;
    public string NearestSegmentName { get; private set; } = "";

    readonly List<Collider> _surfaceColliders = new();
    Transform _ghostEef;
    Vector3 _lastEefPos;
    float _eefSpeed;
    bool _haveLastEef;

    readonly List<(float time, float dist)> _distHistory = new();

    // Latency stats for just the Eq. 1-4 math (sProt/P/I/S/Risk01) — deliberately excludes
    // FindNearestCapsule's ClosestPoint() physics queries above it, since that's a separate cost the paper's
    // Table XV isn't claiming to measure. ResetLatencyStats() is meant to be called once per trial (e.g. by
    // a results logger at kit-start) so GetLatencyStatsMs() reports per-trial stats, not lifetime-cumulative.
    readonly System.Diagnostics.Stopwatch _riskStopwatch = new();
    long _latencyCount;
    double _latencySumMs, _latencySumSqMs, _latencyMaxMs;

    bool _built;
    float _diagTimer;
    float _uiTimer;

    void Start()
    {
        if (!ghostRobotRoot) { var go = GameObject.Find("Ghost_Robot"); if (go) ghostRobotRoot = go.transform; }
        if (!humanTracker) humanTracker = FindObjectOfType<DigitalHumanTracker>();
        if (!statusUI) statusUI = FindObjectOfType<SystemStatusUI>();
        if (ghostRobotRoot) _ghostEef = FindDeepChild(ghostRobotRoot, "link_eef");

        BuildSurfaceColliders();
    }

    void BuildSurfaceColliders()
    {
        if (!ghostRobotRoot) return;

        for (int idx = 0; idx < linkMeshSources.Length; idx++)
        {
            var source = linkMeshSources[idx];
            if (string.IsNullOrEmpty(source.linkName) || !source.meshPrefab)
            {
                Debug.LogWarning($"[GhostSurfaceRiskTest] linkMeshSources[{idx}] is incomplete (linkName='{source.linkName}', meshPrefab={(source.meshPrefab ? source.meshPrefab.name : "null")}) — skipping.");
                continue;
            }

            var linkTransform = FindDeepChild(ghostRobotRoot, source.linkName);
            if (!linkTransform)
            {
                Debug.LogWarning($"[GhostSurfaceRiskTest] Ghost link '{source.linkName}' not found under {ghostRobotRoot.name}.");
                continue;
            }

            var meshFilter = source.meshPrefab.GetComponentInChildren<MeshFilter>();
            if (!meshFilter || !meshFilter.sharedMesh)
            {
                Debug.LogWarning($"[GhostSurfaceRiskTest] '{source.meshPrefab.name}' has no MeshFilter/mesh to use for '{source.linkName}'.");
                continue;
            }

            var proxy = new GameObject($"SurfaceCollisionProxy_{source.linkName}");
            proxy.transform.SetParent(linkTransform, false); // local identity — matches the URDF mesh's own authored space

            var col = proxy.AddComponent<MeshCollider>();
            col.sharedMesh = meshFilter.sharedMesh;
            col.convex = true;
            col.isTrigger = true; // query-only — never affects the ghost's ArticulationBody physics

            _surfaceColliders.Add(col);
        }

        _built = _surfaceColliders.Count > 0 && humanTracker != null;
        if (_built)
            Debug.Log($"[GhostSurfaceRiskTest] Built {_surfaceColliders.Count} surface colliders for ghost robot.");
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
        if (!_built) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        _eefSpeed = UpdateSpeed(_ghostEef, ref _lastEefPos, ref _haveLastEef, _eefSpeed, dt);

        var bodySegments = humanTracker.Segments;
        var (dRaw, region, nearestCollider, segmentName) = FindNearestCapsule(_surfaceColliders, bodySegments, samplesPerSegment);

        _distHistory.Add((Time.time, dRaw));
        while (_distHistory.Count > 0 && Time.time - _distHistory[0].time > predictiveLookAheadWindow)
            _distHistory.RemoveAt(0);
        float d = dRaw;
        for (int i = 0; i < _distHistory.Count; i++)
            if (_distHistory[i].dist < d) d = _distHistory[i].dist;

        float vForRisk = Mathf.Max(_eefSpeed, ratedMaxTcpSpeed);

        _riskStopwatch.Restart();
        float sProt = ProtectiveSeparation(vForRisk);
        float p = Probability(d, sProt);
        float i2 = Impact(vForRisk);
        float s = Severity(region);
        Risk01 = p * i2 * s;
        _riskStopwatch.Stop();
        RecordLatency(_riskStopwatch.Elapsed.TotalMilliseconds);

        Probability01 = p;
        Impact01 = i2;
        Severity01 = s;
        ProtectiveSeparationMeters = sProt;
        MinDistance = d;
        NearestSegmentName = segmentName;

        _uiTimer += dt;
        if (_uiTimer >= uiUpdateInterval)
        {
            _uiTimer = 0f;
            if (statusUI)
            {
                statusUI.SetRisk(Risk01);
                statusUI.SetHumanDistance(MinDistance);
                statusUI.SetNearestBodyPart(NearestSegmentName);
                statusUI.SetHumanStatus(FriendlyBodyPartName(NearestSegmentName));
            }
        }

        _diagTimer += dt;
        if (_diagTimer >= diagInterval)
        {
            _diagTimer = 0f;
            string colName = nearestCollider ? nearestCollider.transform.parent.name : "none";
            Debug.Log($"[GhostSurfaceRiskTest][diag] surfaceColliders={_surfaceColliders.Count} bodySegments={bodySegments.Count} tracking={humanTracker.IsTracking} | " +
                $"d={d:F3} region={region} nearestSegment={segmentName} nearestLink={colName} sProt={sProt:F3} P={p:F3} I={i2:F3} S={s:F2} R={Risk01:F3}");
        }
    }

    void RecordLatency(double ms)
    {
        _latencyCount++;
        _latencySumMs += ms;
        _latencySumSqMs += ms * ms;
        if (ms > _latencyMaxMs) _latencyMaxMs = ms;
    }

    /// <summary>Call at the start of a trial (e.g. from a results logger on kit-start) so
    /// GetLatencyStatsMs() reports stats for that trial only, not accumulated since app launch.</summary>
    public void ResetLatencyStats()
    {
        _latencyCount = 0;
        _latencySumMs = 0;
        _latencySumSqMs = 0;
        _latencyMaxMs = 0;
    }

    public (double meanMs, double maxMs, double stdMs, long count) GetLatencyStatsMs()
    {
        if (_latencyCount == 0) return (0, 0, 0, 0);
        double mean = _latencySumMs / _latencyCount;
        double variance = (_latencySumSqMs / _latencyCount) - (mean * mean);
        double std = variance > 0 ? Math.Sqrt(variance) : 0;
        return (mean, _latencyMaxMs, std, _latencyCount);
    }

    static float UpdateSpeed(Transform eef, ref Vector3 lastPos, ref bool haveLast, float smoothedSpeed, float dt)
    {
        if (!eef) return 0f;
        if (!haveLast)
        {
            lastPos = eef.position;
            haveLast = true;
            return 0f;
        }
        float raw = (eef.position - lastPos).magnitude / dt;
        lastPos = eef.position;
        float lerp = 1f - Mathf.Exp(-10f * dt);
        return Mathf.Lerp(smoothedSpeed, raw, lerp);
    }

    /// <summary>
    /// Distance from the robot mesh surface to the nearest human capsule surface: samples points along
    /// each segment's centerline, finds the closest robot-surface point to each sample via ClosestPoint(),
    /// then subtracts that segment's radius (clamped at 0) to convert centerline distance into surface
    /// distance. There's no single Unity API for true collider-to-capsule minimum distance, so this
    /// sampling approach is the practical substitute — cheap, since ClosestPoint() itself is a fast PhysX
    /// query and segment counts/sample counts here are small.
    /// </summary>
    static (float dist, DigitalHumanTracker.BodyRegion region, Collider collider, string segmentName)
        FindNearestCapsule(List<Collider> colliders, IReadOnlyList<DigitalHumanTracker.BodySegment> segments, int samples)
    {
        float min = float.PositiveInfinity;
        var region = DigitalHumanTracker.BodyRegion.ChestSternum;
        Collider nearestCollider = null;
        string segmentName = "";
        samples = Mathf.Max(1, samples);
        foreach (var col in colliders)
        {
            if (!col) continue;
            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                for (int k = 0; k < samples; k++)
                {
                    float t = samples == 1 ? 0f : (float)k / (samples - 1);
                    Vector3 samplePoint = Vector3.Lerp(seg.start, seg.end, t);
                    Vector3 closest = col.ClosestPoint(samplePoint);
                    float d = Vector3.Distance(closest, samplePoint) - seg.radius;
                    if (d < 0f) d = 0f;
                    if (d < min) { min = d; region = seg.region; nearestCollider = col; segmentName = seg.name; }
                }
            }
        }
        return (min, region, nearestCollider, segmentName);
    }

    /// <summary>Maps a raw segment name (e.g. "L_UpperArm") to a plain display label ("Upper Arm") for the
    /// status panel — drops the left/right prefix since the panel just needs the body-part category.</summary>
    static string FriendlyBodyPartName(string segmentName) => segmentName switch
    {
        "L_UpperArm" or "R_UpperArm" => "Upper Arm",
        "L_Forearm" or "R_Forearm" => "Forearm",
        "L_Hand" or "R_Hand" => "Hand",
        "Neck_Head" => "Head",
        "Chest" => "Chest",
        _ => string.IsNullOrEmpty(segmentName) ? "--" : segmentName,
    };

    float ProtectiveSeparation(float vr) =>
        (vr + humanApproachSpeed) * (sensorResponseTime + robotStoppingTime + humanReactionTime)
        + intrusionDistance + positionUncertainty;

    float Probability(float dH, float dMin) =>
        Mathf.Exp(-alphaDecay * Mathf.Max(dH - dMin, 0f));

    float Impact(float vr) =>
        Mathf.Clamp01((0.5f * effectiveMass * vr * vr) / keMax);

    static float Severity(DigitalHumanTracker.BodyRegion region) => region switch
    {
        DigitalHumanTracker.BodyRegion.HandFinger => 0.3f,
        DigitalHumanTracker.BodyRegion.WristForearm => 0.5f,
        DigitalHumanTracker.BodyRegion.UpperArmShoulder => 0.7f,
        DigitalHumanTracker.BodyRegion.HeadNeck => 0.9f,
        DigitalHumanTracker.BodyRegion.ChestSternum => 1.0f,
        _ => 1.0f,
    };
}
