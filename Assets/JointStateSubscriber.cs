using UnityEngine;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public class JointStateSubscriber : MonoBehaviour
{
    [SerializeField] string topic = "/joint_states";
    [SerializeField] float smoothingSpeed = 15f;

    Dictionary<string, ArticulationBody> joints = new Dictionary<string, ArticulationBody>();
    Dictionary<string, float> targetAngles = new Dictionary<string, float>();

    void Start()
    {
        foreach (var ab in GetComponentsInChildren<ArticulationBody>())
        {
            joints[ab.name] = ab;
            targetAngles[ab.name] = 0f;
        }

        ROSConnection.GetOrCreateInstance().Subscribe<JointStateMsg>(topic, OnJointState);
    }

    void OnJointState(JointStateMsg msg)
    {
        for (int i = 0; i < msg.name.Length; i++)
        {
            string linkName = msg.name[i].Replace("joint", "link");
            if (targetAngles.ContainsKey(linkName))
                targetAngles[linkName] = (float)msg.position[i] * Mathf.Rad2Deg;
        }
    }

    void FixedUpdate()
    {
        foreach (var kvp in joints)
        {
            if (!targetAngles.ContainsKey(kvp.Key)) continue;
            var ab = kvp.Value;
            var drive = ab.xDrive;
            drive.target = Mathf.LerpAngle(drive.target, targetAngles[kvp.Key], Time.fixedDeltaTime * smoothingSpeed);
            ab.xDrive = drive;
        }
    }
}
