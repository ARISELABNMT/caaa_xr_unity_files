using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Visualization;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;
using RosMessageTypes.BuiltinInterfaces;

/// <summary>
/// Publishes DigitalHumanTracker's live capsule segments as a visualization_msgs/MarkerArray so RViz can
/// show the human capsule avatar alongside the MoveIt planning scene — visual only, not fed into planning.
/// Each capsule becomes 3 markers (a CYLINDER shaft + 2 end SPHEREs), the standard RViz workaround since
/// there's no native capsule primitive.
///
/// Each segment is built entirely independently, with its own two end-spheres pulled back into its own
/// body by its own radius (center = joint position -/+ direction*radius) rather than centered exactly on
/// the joint. That means each segment's own rounded cap reaches exactly to the joint and stops there, so
/// two segments meeting at a joint (e.g. an upper arm's elbow end and a forearm's elbow start) just touch
/// at that point instead of one sphere (sized for one segment) overlapping/swallowing the other's.
///
/// Positions are expressed relative to robotBaseFrame (matching ROS frame_id "link_base"), reusing the
/// exact same Unity-to-ROS conversion RosXRBridge.PublishPose already uses for the bin/place poses — so the
/// avatar lines up with the robot in RViz the same way those already-working markers do. Each capsule's
/// position AND orientation are both computed directly in ROS's FLU numeric space (via Vector3&lt;FLU&gt;/
/// Quaternion&lt;FLU&gt;), not computed in Unity space and converted afterwards — ROS's Z (up) axis is not
/// the same physical direction as Unity's Z (forward) axis, so a rotation built against Vector3.forward and
/// converted after the fact measures against the wrong reference axis and comes out wrong.
/// </summary>
public class HumanCapsuleRosPublisher : MonoBehaviour
{
    [Header("Human tracking (auto-found if left empty)")]
    public DigitalHumanTracker humanTracker;

    [Header("Robot base frame (must match ROS frame_id 'link_base') — auto-reused from RosXRBridge if left empty")]
    public Transform robotBaseFrame;

    [Header("Nearest-body-part highlight (auto-found if left empty)")]
    [Tooltip("Whichever segment GhostSurfaceRiskTest currently reports as nearest to the robot gets " +
        "highlightColor instead of capsuleColor, updated live every publish.")]
    public GhostSurfaceRiskTest ghostRiskTest;
    public Color highlightColor = new Color(0.4f, 0.75f, 1f, 1.0f); // light blue

    [Header("ROS")]
    public string topic = "/xr/human_capsules";
    [Tooltip("Throttled well below tracking rate — this is visualization only.")]
    public float publishRate = 15f;

    [Header("Appearance")]
    public Color capsuleColor = new Color(0.96f, 0.95f, 0.90f, 1.0f); // solid off-white

    float _timer;
    bool _ready;
    int _publishCount;
    float _diagTimer;

    void Start()
    {
        if (!humanTracker) humanTracker = FindObjectOfType<DigitalHumanTracker>();
        if (!ghostRiskTest) ghostRiskTest = FindObjectOfType<GhostSurfaceRiskTest>();
        if (!robotBaseFrame)
        {
            var bridge = FindObjectOfType<RosXRBridge>();
            if (bridge) robotBaseFrame = bridge.robotBaseFrame;
        }
        if (!robotBaseFrame)
            Debug.LogWarning("[HumanCapsuleRosPublisher] No robotBaseFrame assigned and none found via RosXRBridge — capsule positions would be wrong, not publishing.");

        ROSConnection.GetOrCreateInstance().RegisterPublisher<MarkerArrayMsg>(topic);
        _ready = robotBaseFrame != null;
        Debug.Log($"[HumanCapsuleRosPublisher] Start: ready={_ready}, topic={topic}");
    }

    void Update()
    {
        if (!_ready || !humanTracker) return;

        _timer += Time.deltaTime;
        if (_timer < 1f / publishRate) return;
        _timer = 0f;

        var segments = humanTracker.Segments;
        var markers = new List<MarkerMsg>(segments.Count * 3);
        for (int i = 0; i < segments.Count; i++)
            AddCapsuleMarkers(markers, segments[i], i);

        try
        {
            ROSConnection.GetOrCreateInstance().Publish(topic, new MarkerArrayMsg(markers.ToArray()));
            _publishCount++;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[HumanCapsuleRosPublisher] FAILED to publish: {e.Message}");
        }

        _diagTimer += Time.deltaTime;
        if (_diagTimer >= 1f)
        {
            _diagTimer = 0f;
            Debug.Log($"[HumanCapsuleRosPublisher][diag] tracking={humanTracker.IsTracking} segments={segments.Count} markersPerMsg={markers.Count} totalPublished={_publishCount}");
        }
    }

    void AddCapsuleMarkers(List<MarkerMsg> markers, DigitalHumanTracker.BodySegment seg, int index)
    {
        Vector3 localStart = robotBaseFrame.InverseTransformPoint(seg.start);
        Vector3 localEnd = robotBaseFrame.InverseTransformPoint(seg.end);

        // Do the whole computation in ROS's FLU numeric space, not Unity's — ROS's Z (up) axis is not the
        // same physical direction as Unity's Z (forward) axis, so computing a rotation in Unity space
        // against Vector3.forward and converting the *result* afterwards measures against the wrong
        // reference axis entirely, producing a systematically wrong cylinder orientation (looks like a
        // "zigzag" instead of a coherent limb). Quaternion<FLU>.FromToRotation accounts for the axis
        // remapping correctly because it round-trips both inputs through Unity space itself.
        Vector3<FLU> rosStart = localStart.To<FLU>();
        Vector3<FLU> rosEnd = localEnd.To<FLU>();
        Vector3<FLU> rosDelta = new Vector3<FLU>(rosEnd.x - rosStart.x, rosEnd.y - rosStart.y, rosEnd.z - rosStart.z);
        float fullLength = Mathf.Sqrt(rosDelta.x * rosDelta.x + rosDelta.y * rosDelta.y + rosDelta.z * rosDelta.z);

        Vector3<FLU> rosDir = fullLength > 1e-5f
            ? new Vector3<FLU>(rosDelta.x / fullLength, rosDelta.y / fullLength, rosDelta.z / fullLength)
            : new Vector3<FLU>(0f, 0f, 1f);

        // Pull each end-sphere's center back into this segment's own body by its own radius, instead of
        // centering it exactly on the joint. A sphere of radius r centered at (joint - dir*r) still has its
        // outer surface reach exactly to the joint along dir — so this segment's own rounded cap ends
        // precisely at the joint without needing to know the neighbouring segment's radius at all.
        Vector3<FLU> sphereStart = new Vector3<FLU>(
            rosStart.x + rosDir.x * seg.radius, rosStart.y + rosDir.y * seg.radius, rosStart.z + rosDir.z * seg.radius);
        Vector3<FLU> sphereEnd = new Vector3<FLU>(
            rosEnd.x - rosDir.x * seg.radius, rosEnd.y - rosDir.y * seg.radius, rosEnd.z - rosDir.z * seg.radius);
        // Below this, the pulled-back sphere centers have crossed over (radius too big for this segment's
        // own tracked length) — rendering a near-zero-length but full-diameter cylinder there just shows
        // its flat end-cap as a plate. Deleting the marker instead of adding a degenerate sliver lets the
        // two end-spheres alone (which still each reach exactly to the joint) stand in for that segment.
        // Neck_Head is exempted by request: always show its cylinder, sized to the actual (always
        // non-negative) distance between the two sphere centers rather than the signed subtraction, which
        // stays valid even once the centers have crossed over.
        const float minVisibleLength = 0.01f;
        float rawTrimmedLength = fullLength - seg.radius * 2f;
        float sdx = sphereEnd.x - sphereStart.x, sdy = sphereEnd.y - sphereStart.y, sdz = sphereEnd.z - sphereStart.z;
        float sphereCenterDistance = Mathf.Sqrt(sdx * sdx + sdy * sdy + sdz * sdz);

        bool isNeckHead = seg.name == "Neck_Head";
        bool cylinderVisible = isNeckHead || rawTrimmedLength >= minVisibleLength;
        float cylinderLength = isNeckHead ? sphereCenterDistance : rawTrimmedLength;

        bool isNearest = ghostRiskTest && seg.name == ghostRiskTest.NearestSegmentName;
        Color activeColor = isNearest ? highlightColor : capsuleColor;

        var header = new HeaderMsg { frame_id = "link_base" };
        var color = new ColorRGBAMsg(activeColor.r, activeColor.g, activeColor.b, activeColor.a);
        var identity = new QuaternionMsg(0, 0, 0, 1);
        var forever = new DurationMsg(0, 0);
        float diameter = seg.radius * 2f;

        if (cylinderVisible)
        {
            Vector3<FLU> rosMid = new Vector3<FLU>(
                (sphereStart.x + sphereEnd.x) * 0.5f, (sphereStart.y + sphereEnd.y) * 0.5f, (sphereStart.z + sphereEnd.z) * 0.5f);
            Quaternion<FLU> rosRot = Quaternion<FLU>.FromToRotation(new Vector3<FLU>(0f, 0f, 1f), rosDir);

            markers.Add(new MarkerMsg
            {
                header = header,
                ns = "human_capsules",
                id = index * 3 + 0,
                type = MarkerMsg.CYLINDER,
                action = MarkerMsg.ADD,
                pose = new PoseMsg { position = rosMid, orientation = rosRot },
                scale = new Vector3Msg(diameter, diameter, cylinderLength),
                color = color,
                lifetime = forever,
            });
        }
        else
        {
            markers.Add(new MarkerMsg
            {
                header = header,
                ns = "human_capsules",
                id = index * 3 + 0,
                type = MarkerMsg.CYLINDER,
                action = MarkerMsg.DELETE,
            });
        }
        markers.Add(new MarkerMsg
        {
            header = header,
            ns = "human_capsules",
            id = index * 3 + 1,
            type = MarkerMsg.SPHERE,
            action = MarkerMsg.ADD,
            pose = new PoseMsg { position = sphereStart, orientation = identity },
            scale = new Vector3Msg(diameter, diameter, diameter),
            color = color,
            lifetime = forever,
        });
        markers.Add(new MarkerMsg
        {
            header = header,
            ns = "human_capsules",
            id = index * 3 + 2,
            type = MarkerMsg.SPHERE,
            action = MarkerMsg.ADD,
            pose = new PoseMsg { position = sphereEnd, orientation = identity },
            scale = new Vector3Msg(diameter, diameter, diameter),
            color = color,
            lifetime = forever,
        });
    }
}
