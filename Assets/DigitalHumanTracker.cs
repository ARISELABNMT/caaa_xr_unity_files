using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads Meta Movement SDK's live body-tracking skeleton (OVRSkeleton fed by OVRBody, both required on
/// this same GameObject) and exposes just the joint positions relevant to ISO/TS 15066 Table IV body
/// regions. No avatar mesh or Animator retargeting involved — this is the raw tracked joint data only,
/// meant to be invisible and used purely for proximity/severity estimation.
/// </summary>
public class DigitalHumanTracker : MonoBehaviour
{
    public enum BodyRegion { HandFinger, WristForearm, UpperArmShoulder, HeadNeck, ChestSternum }

    [Header("Calibration")]
    [Tooltip("Your real standing height in meters. Without an explicit override, Meta's body-tracking model " +
        "can assume the wrong height, producing a systematic vertical offset in every joint position.")]
    public float userHeightMeters = 1.80f; // 5'11"

    public struct BodyPoint
    {
        public Vector3 position;
        public BodyRegion region;
        public OVRSkeleton.BoneId boneId;
    }

    /// <summary>A limb/torso segment as a capsule: a line from start to end, with a radius approximating
    /// that body part's real thickness. Distance-to-surface = point-to-segment distance minus radius.</summary>
    public struct BodySegment
    {
        public Vector3 start;
        public Vector3 end;
        public float radius;
        public BodyRegion region;
        public string name;
    }

    OVRSkeleton _skeleton;
    readonly List<BodyPoint> _points = new();
    public IReadOnlyList<BodyPoint> Points => _points;
    readonly List<BodySegment> _segments = new();
    public IReadOnlyList<BodySegment> Segments => _segments;
    public bool IsTracking { get; private set; }

    static readonly Dictionary<OVRSkeleton.BoneId, BodyRegion> RegionMap = new()
    {
        { OVRSkeleton.BoneId.Body_Head, BodyRegion.HeadNeck },
        { OVRSkeleton.BoneId.Body_Neck, BodyRegion.HeadNeck },
        { OVRSkeleton.BoneId.Body_SpineUpper, BodyRegion.ChestSternum },
        { OVRSkeleton.BoneId.Body_LeftShoulder, BodyRegion.UpperArmShoulder },
        { OVRSkeleton.BoneId.Body_RightShoulder, BodyRegion.UpperArmShoulder },
        { OVRSkeleton.BoneId.Body_LeftArmUpper, BodyRegion.UpperArmShoulder },
        { OVRSkeleton.BoneId.Body_RightArmUpper, BodyRegion.UpperArmShoulder },
        { OVRSkeleton.BoneId.Body_LeftArmLower, BodyRegion.WristForearm },
        { OVRSkeleton.BoneId.Body_RightArmLower, BodyRegion.WristForearm },
        { OVRSkeleton.BoneId.Body_LeftHandWrist, BodyRegion.WristForearm },
        { OVRSkeleton.BoneId.Body_RightHandWrist, BodyRegion.WristForearm },
        { OVRSkeleton.BoneId.Body_LeftHandThumbDistal, BodyRegion.HandFinger },
        { OVRSkeleton.BoneId.Body_RightHandThumbDistal, BodyRegion.HandFinger },
        { OVRSkeleton.BoneId.Body_LeftHandIndexDistal, BodyRegion.HandFinger },
        { OVRSkeleton.BoneId.Body_RightHandIndexDistal, BodyRegion.HandFinger },
        { OVRSkeleton.BoneId.Body_LeftHandMiddleDistal, BodyRegion.HandFinger },
        { OVRSkeleton.BoneId.Body_RightHandMiddleDistal, BodyRegion.HandFinger },
    };

    struct SegmentDef
    {
        public OVRSkeleton.BoneId a, b;
        public float radius;
        public BodyRegion region;
        public string name;
    }

    // Radii are rough anatomical defaults (meters), not measured — each segment spans two adjacent
    // tracked joints, since Meta's bone transforms sit at each bone's proximal (parent) end.
    static readonly SegmentDef[] SegmentDefs =
    {
        new() { a = OVRSkeleton.BoneId.Body_LeftArmUpper,  b = OVRSkeleton.BoneId.Body_LeftArmLower,        radius = 0.05f, region = BodyRegion.UpperArmShoulder, name = "L_UpperArm" },
        new() { a = OVRSkeleton.BoneId.Body_RightArmUpper, b = OVRSkeleton.BoneId.Body_RightArmLower,       radius = 0.05f, region = BodyRegion.UpperArmShoulder, name = "R_UpperArm" },
        new() { a = OVRSkeleton.BoneId.Body_LeftArmLower,  b = OVRSkeleton.BoneId.Body_LeftHandWrist,       radius = 0.04f, region = BodyRegion.WristForearm,     name = "L_Forearm" },
        new() { a = OVRSkeleton.BoneId.Body_RightArmLower, b = OVRSkeleton.BoneId.Body_RightHandWrist,      radius = 0.04f, region = BodyRegion.WristForearm,     name = "R_Forearm" },
        new() { a = OVRSkeleton.BoneId.Body_LeftHandWrist,  b = OVRSkeleton.BoneId.Body_LeftHandMiddleDistal,  radius = 0.03f, region = BodyRegion.HandFinger,   name = "L_Hand" },
        new() { a = OVRSkeleton.BoneId.Body_RightHandWrist, b = OVRSkeleton.BoneId.Body_RightHandMiddleDistal, radius = 0.03f, region = BodyRegion.HandFinger,   name = "R_Hand" },
        new() { a = OVRSkeleton.BoneId.Body_Neck,       b = OVRSkeleton.BoneId.Body_Head, radius = 0.08f, region = BodyRegion.HeadNeck,     name = "Neck_Head" },
        new() { a = OVRSkeleton.BoneId.Body_Hips, b = OVRSkeleton.BoneId.Body_Neck, radius = 0.14f, region = BodyRegion.ChestSternum, name = "Chest" },
    };

    readonly Dictionary<OVRSkeleton.BoneId, Vector3> _boneWorldPos = new();

    float _diagTimer;

    void Start()
    {
        _skeleton = GetComponent<OVRSkeleton>();
        if (!_skeleton)
            Debug.LogError("[DigitalHumanTracker] No OVRSkeleton on this GameObject — add OVRBody + OVRSkeleton (Skeleton Type = Body) here.");

        StartCoroutine(CalibrateHeight());
    }

    IEnumerator CalibrateHeight()
    {
        yield return new WaitForSeconds(1f);
        bool ok = OVRBody.SuggestBodyTrackingCalibrationOverride(userHeightMeters);
        Debug.Log($"[DigitalHumanTracker] Height calibration override ({userHeightMeters:F2}m) {(ok ? "succeeded" : "FAILED")}.");
    }

    void Update()
    {
        _points.Clear();
        _segments.Clear();
        IsTracking = false;

        if (_skeleton && _skeleton.IsInitialized)
        {
            var bones = _skeleton.Bones;
            if (bones != null && bones.Count > 0)
            {
                IsTracking = true;
                _boneWorldPos.Clear();
                foreach (var bone in bones)
                {
                    var id = (OVRSkeleton.BoneId)bone.Id;
                    _boneWorldPos[id] = bone.Transform.position;
                    if (RegionMap.TryGetValue(id, out var region))
                        _points.Add(new BodyPoint { position = bone.Transform.position, region = region, boneId = id });
                }

                foreach (var def in SegmentDefs)
                {
                    if (_boneWorldPos.TryGetValue(def.a, out var pa) && _boneWorldPos.TryGetValue(def.b, out var pb))
                        _segments.Add(new BodySegment { start = pa, end = pb, radius = def.radius, region = def.region, name = def.name });
                }
            }
        }

        _diagTimer += Time.deltaTime;
        if (_diagTimer >= 1f)
        {
            _diagTimer = 0f;
            LogDiag();
        }
    }

    void LogDiag()
    {
        if (!_skeleton)
        {
            Debug.LogWarning("[DigitalHumanTracker][diag] no OVRSkeleton component.");
            return;
        }
        string head = "n/a", wrist = "n/a";
        foreach (var p in _points)
        {
            if (p.boneId == OVRSkeleton.BoneId.Body_Head) head = p.position.ToString("F2");
            if (p.boneId == OVRSkeleton.BoneId.Body_RightHandWrist) wrist = p.position.ToString("F2");
        }
        int totalBones = _skeleton.IsInitialized && _skeleton.Bones != null ? _skeleton.Bones.Count : 0;
        Debug.Log($"[DigitalHumanTracker][diag] initialized={_skeleton.IsInitialized} tracking={IsTracking} totalBones={totalBones} mappedPoints={_points.Count} segments={_segments.Count} head={head} rightWrist={wrist}");
    }
}
