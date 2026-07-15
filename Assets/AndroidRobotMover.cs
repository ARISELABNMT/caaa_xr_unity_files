using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public class AndroidRobotMover : MonoBehaviour
{
    [SerializeField] private float smoothSpeed = 20f;

    private ArticulationBody articulationRoot;
    private List<ArticulationBody> articulationChain = new List<ArticulationBody>();
    private Dictionary<int, string> posIndexToJoint = new Dictionary<int, string>();
    private Dictionary<string, float> jointTargets = new Dictionary<string, float>();
    private List<float> currentPositions = new List<float>();
    private bool initialized = false;

    void Awake()
    {
#if UNITY_ANDROID
        var existing = GetComponent<JointStateSubscriber>();
        if (existing != null) existing.enabled = false;
#endif
    }

    void Start()
    {
#if UNITY_ANDROID
        articulationChain.AddRange(GetComponentsInChildren<ArticulationBody>());
        articulationRoot = articulationChain.Count > 0 ? articulationChain[0] : null;
        if (articulationRoot == null) { Debug.LogError("AndroidRobotMover: no root"); return; }

        articulationRoot.GetJointPositions(currentPositions);

        int posIndex = 0;
        foreach (var body in articulationChain)
        {
            if (body.isRoot) continue;
            string n = body.gameObject.name;
            if (IsRobotLink(n))
                posIndexToJoint[posIndex] = "joint" + n.Substring(4);
            posIndex += body.dofCount;
        }

        Debug.Log("AndroidRobotMover mapped " + posIndexToJoint.Count + " joints");
        initialized = true;
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
        ROSConnection.GetOrCreateInstance().Subscribe<JointStateMsg>("/joint_states", msg =>
        {
            for (int i = 0; i < msg.name.Length; i++)
                jointTargets[msg.name[i]] = (float)msg.position[i];
        });
        Debug.Log("AndroidRobotMover subscribed");
    }

    void FixedUpdate()
    {
#if UNITY_ANDROID
        if (!initialized || articulationRoot == null || jointTargets.Count == 0) return;

        articulationRoot.GetJointPositions(currentPositions);
        float lerpFactor = 1f - Mathf.Exp(-smoothSpeed * Time.fixedDeltaTime);

        foreach (var kvp in posIndexToJoint)
        {
            if (!jointTargets.TryGetValue(kvp.Value, out float target)) continue;
            if (kvp.Key >= currentPositions.Count) continue;
            currentPositions[kvp.Key] = Mathf.Lerp(currentPositions[kvp.Key], target, lerpFactor);
        }

        articulationRoot.SetJointPositions(currentPositions);
#endif
    }
}
