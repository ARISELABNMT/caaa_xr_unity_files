using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

[System.Serializable]
class TrajectoryPoint
{
    public float[] positions;
    public int time_sec;
    public int time_nanosec;
}

[System.Serializable]
class TrajectoryData
{
    public string[] joint_names;
    public TrajectoryPoint[] points;
}

public class GhostRobotController : MonoBehaviour
{
    [SerializeField] string topic = "/unity_planned_trajectory";
    [SerializeField] float speedMultiplier = 1.0f;
    [SerializeField] Material ghostMaterial;

    Dictionary<string, ArticulationBody> joints = new Dictionary<string, ArticulationBody>();
    Coroutine animationCoroutine;

    void Start()
    {
        if (ghostMaterial != null)
        {
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                var mats = new Material[r.materials.Length];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = ghostMaterial;
                r.materials = mats;
            }
            Debug.Log("[Ghost] Material applied");
        }

        foreach (var ab in GetComponentsInChildren<ArticulationBody>())
            joints[ab.name] = ab;

        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>(topic, OnTrajectoryReceived);
        Debug.Log("[Ghost] Subscribed to " + topic);
    }

    void OnTrajectoryReceived(StringMsg msg)
    {
        var data = JsonUtility.FromJson<TrajectoryData>(msg.data);
        if (data == null || data.points == null || data.points.Length == 0) return;

        Debug.Log("[Ghost] Received " + data.points.Length + " waypoints");

        if (animationCoroutine != null)
            StopCoroutine(animationCoroutine);

        animationCoroutine = StartCoroutine(AnimateTrajectory(data));
    }

    IEnumerator AnimateTrajectory(TrajectoryData data)
    {
        float startTime = Time.time;

        for (int i = 0; i < data.points.Length; i++)
        {
            var point = data.points[i];
            float targetTime = (point.time_sec + point.time_nanosec / 1e9f) / speedMultiplier;

            while ((Time.time - startTime) < targetTime)
                yield return null;

            for (int j = 0; j < data.joint_names.Length && j < point.positions.Length; j++)
            {
                string linkName = data.joint_names[j].Replace("joint", "link");
                if (joints.TryGetValue(linkName, out ArticulationBody ab))
                {
                    var drive = ab.xDrive;
                    drive.target = point.positions[j] * Mathf.Rad2Deg;
                    ab.xDrive = drive;
                }
            }
        }

        Debug.Log("[Ghost] Animation complete");
    }
}
