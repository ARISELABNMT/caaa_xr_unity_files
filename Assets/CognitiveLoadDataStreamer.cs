using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Standalone data-exploration streamer for a cognitive-load classification model originally trained on
/// HoloLens 2 sensor data — captures broad head/hand tracking fields (not a minimal feature set yet, so the
/// PC side can see exactly what Quest's APIs expose before deciding how to map them to the model's input
/// schema) and streams them as newline-delimited JSON over a plain TCP socket to a Python listener. The
/// same connection is bidirectional: the PC periodically sends back a predicted cognitive-load class +
/// per-class probabilities, which gets converted into a continuous C(t) score (per the Low/Medium/High
/// range table) and pushed to SystemStatusUI's existing cognitive-load display.
///
/// Deliberately has zero code/topic overlap with RosXRBridge or the ROS-TCP-Connector setup used for the
/// xArm Lite6 pick-place work — different port, different transport (plain TCP, not rosbridge), own
/// GameObject, so this can be added/removed without touching anything robot-related.
/// </summary>
public class CognitiveLoadDataStreamer : MonoBehaviour
{
    [Header("Network (hotspot IP changes — edit per session)")]
    public string targetIP = "192.168.1.100";
    public int targetPort = 5555;
    public float reconnectIntervalSeconds = 3f;

    [Header("Capture rate")]
    public float sendRateHz = 30f;

    [Header("Head source (auto-found by name if left empty)")]
    public Transform headTransform;

    [Header("Hand sources (auto-found in scene if left empty)")]
    public OVRHand rightHand;
    public OVRSkeleton rightHandSkeleton;
    public OVRHand leftHand;
    public OVRSkeleton leftHandSkeleton;

    [Header("Status panel (auto-found in scene if left empty)")]
    public SystemStatusUI statusUI;

    [Header("Cognitive score C(t) range midpoints (per Low/Medium/High table)")]
    public float lowMidpoint = 0.20f;    // Low range 0.00-0.40
    public float mediumMidpoint = 0.55f; // Medium range 0.40-0.70
    public float highMidpoint = 0.85f;   // High range 0.70-1.00

    [Serializable]
    class HeadData
    {
        public float[] position;
        public float[] rotation_quat;
        public float[] forward;
        public float[] up;
    }

    // tracked=false means this hand's fields below are unpopulated zeros, not real data — the Python side
    // should check this flag before trusting wrist_position/etc, rather than us sending JSON null (which
    // JsonUtility can't produce for nested objects) or garbage stale values.
    [Serializable]
    class HandData
    {
        public bool tracked;
        public bool high_confidence;
        public float[] wrist_position;
        public float[] wrist_rotation_quat;
        public float[] palm_position;
        public float[] index_tip_position;
    }

    [Serializable]
    class FrameData
    {
        public double t;
        public HeadData head;
        public HandData hand_right;
        public HandData hand_left;
    }

    [Serializable]
    class CognitiveLoadProbs
    {
        public float low;
        public float medium;
        public float high;
    }

    [Serializable]
    class CognitiveLoadMessage
    {
        public string type;
        public double t;
        public string pred_class;
        public CognitiveLoadProbs probs;
    }

    public struct CognitiveLoadUpdate
    {
        public double t;
        public string predClass;
        public float probLow, probMedium, probHigh;
    }

    /// <summary>Fires on the main thread with the new pred_class ("low"/"medium"/"high") whenever a
    /// cognitive_load message is received and applied. Concrete (non-generic) subclass because Unity can't
    /// serialize/Inspector-wire an open generic UnityEvent&lt;T&gt; directly.</summary>
    [Serializable] public class CognitiveLoadEvent : UnityEvent<string> { }
    public CognitiveLoadEvent OnCognitiveLoadChanged = new();

    public string LatestPredClass { get; private set; } = "";
    public float LatestProbLow { get; private set; }
    public float LatestProbMedium { get; private set; }
    public float LatestProbHigh { get; private set; }
    /// <summary>Continuous C(t) in [0,1], the probability-weighted expectation over each class's range
    /// midpoint — reflects the model's full distribution rather than jumping discretely between 3 values
    /// each time the argmax class changes.</summary>
    public float CognitiveScore01 { get; private set; }

    readonly ConcurrentQueue<CognitiveLoadUpdate> _incoming = new();

    TcpClient _client;
    NetworkStream _stream;
    volatile bool _connected;
    volatile bool _running = true;
    Thread _connectThread;
    readonly object _lock = new();

    float _sendTimer;

    void Start()
    {
        if (!headTransform)
        {
            var go = GameObject.Find("CenterEyeAnchor");
            if (go) headTransform = go.transform;
        }
        if (!headTransform)
            Debug.LogError("[CognitiveLoadDataStreamer] CenterEyeAnchor not found — no head data source, not starting.");

        if (!statusUI) statusUI = FindObjectOfType<SystemStatusUI>();

        FindHandSources();

        _connectThread = new Thread(ConnectionWorker) { IsBackground = true };
        _connectThread.Start();
    }

    void FindHandSources()
    {
        foreach (var hand in FindObjectsOfType<OVRHand>())
        {
            var skeleton = hand.GetComponent<OVRSkeleton>();
            if (!skeleton) continue;
            var type = skeleton.GetSkeletonType();
            if (type == OVRSkeleton.SkeletonType.XRHandRight || type == OVRSkeleton.SkeletonType.HandRight)
            {
                if (!rightHand) rightHand = hand;
                if (!rightHandSkeleton) rightHandSkeleton = skeleton;
            }
            else if (type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.HandLeft)
            {
                if (!leftHand) leftHand = hand;
                if (!leftHandSkeleton) leftHandSkeleton = skeleton;
            }
        }
        if (!rightHand || !rightHandSkeleton)
            Debug.LogWarning("[CognitiveLoadDataStreamer] Right OVRHand/OVRSkeleton not found — hand_right will always report tracked=false.");
        if (!leftHand || !leftHandSkeleton)
            Debug.LogWarning("[CognitiveLoadDataStreamer] Left OVRHand/OVRSkeleton not found — hand_left will always report tracked=false.");
    }

    // Runs on a background thread: connect, then block reading incoming lines (NetworkStream.Read blocks
    // until data arrives or the socket closes) until disconnected, then loop back and reconnect. Sending
    // (Update(), main thread) and reading (this thread) share the same NetworkStream concurrently, which is
    // fine — TCP read and write are independent directions and .NET's NetworkStream supports that.
    void ConnectionWorker()
    {
        while (_running)
        {
            TcpClient client;
            try
            {
                client = new TcpClient();
                client.Connect(targetIP, targetPort);
                lock (_lock)
                {
                    _client = client;
                    _stream = client.GetStream();
                    _connected = true;
                }
                Debug.Log($"[CognitiveLoadDataStreamer] Connected to {targetIP}:{targetPort}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CognitiveLoadDataStreamer] Connect to {targetIP}:{targetPort} failed ({e.Message}) — retrying in {reconnectIntervalSeconds:F0}s");
                Thread.Sleep(Mathf.RoundToInt(reconnectIntervalSeconds * 1000f));
                continue;
            }

            try
            {
                using var reader = new StreamReader(_stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                while (_running && _connected)
                {
                    string line = reader.ReadLine();
                    if (line == null) break; // remote closed the connection
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ParseIncomingLine(line);
                }
            }
            catch (Exception e)
            {
                if (_running)
                    Debug.LogWarning($"[CognitiveLoadDataStreamer] Read loop ended ({e.Message}) — reconnecting.");
            }

            lock (_lock)
            {
                _connected = false;
                _stream?.Close();
                _client?.Close();
                _stream = null;
                _client = null;
            }
        }
    }

    // Runs on the background read thread — must not touch any Unity object here, only enqueue a plain
    // struct for the main thread to apply in Update().
    void ParseIncomingLine(string line)
    {
        try
        {
            var msg = JsonUtility.FromJson<CognitiveLoadMessage>(line);
            if (msg == null || msg.type != "cognitive_load") return;

            _incoming.Enqueue(new CognitiveLoadUpdate
            {
                t = msg.t,
                predClass = msg.pred_class,
                probLow = msg.probs?.low ?? 0f,
                probMedium = msg.probs?.medium ?? 0f,
                probHigh = msg.probs?.high ?? 0f,
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CognitiveLoadDataStreamer] Failed to parse incoming line ({e.Message}): {line}");
        }
    }

    void Update()
    {
        while (_incoming.TryDequeue(out var update))
            ApplyCognitiveLoadUpdate(update);

        if (!headTransform) return;

        _sendTimer += Time.deltaTime;
        if (_sendTimer < 1f / sendRateHz) return;
        _sendTimer = 0f;

        if (!_connected) return;

        var frame = new FrameData
        {
            t = Time.realtimeSinceStartupAsDouble,
            head = BuildHeadData(),
            hand_right = BuildHandData(rightHand, rightHandSkeleton),
            hand_left = BuildHandData(leftHand, leftHandSkeleton),
        };

        string json = JsonUtility.ToJson(frame);
        Send(json);
    }

    void ApplyCognitiveLoadUpdate(CognitiveLoadUpdate update)
    {
        LatestPredClass = update.predClass;
        LatestProbLow = update.probLow;
        LatestProbMedium = update.probMedium;
        LatestProbHigh = update.probHigh;

        float probSum = update.probLow + update.probMedium + update.probHigh;
        CognitiveScore01 = probSum > 0f
            ? (update.probLow * lowMidpoint + update.probMedium * mediumMidpoint + update.probHigh * highMidpoint) / probSum
            : 0f;

        if (statusUI) statusUI.SetCognitiveLoad(CognitiveScore01);

        Debug.Log($"[CognitiveLoadDataStreamer] Cognitive load: {LatestPredClass} (low={LatestProbLow:F2} med={LatestProbMedium:F2} high={LatestProbHigh:F2}) -> C(t)={CognitiveScore01:F3}");
        OnCognitiveLoadChanged?.Invoke(LatestPredClass);
    }

    HeadData BuildHeadData()
    {
        Vector3 pos = headTransform.position;
        Quaternion rot = headTransform.rotation;
        return new HeadData
        {
            position = new[] { pos.x, pos.y, pos.z },
            rotation_quat = new[] { rot.x, rot.y, rot.z, rot.w },
            forward = ToArray(headTransform.forward),
            up = ToArray(headTransform.up),
        };
    }

    static HandData BuildHandData(OVRHand hand, OVRSkeleton skeleton)
    {
        var data = new HandData
        {
            wrist_position = new float[3],
            wrist_rotation_quat = new float[4] { 0, 0, 0, 1 },
            palm_position = new float[3],
            index_tip_position = new float[3],
        };

        if (!hand || !skeleton || !hand.IsTracked || !skeleton.IsInitialized)
        {
            data.tracked = false;
            return data;
        }

        data.tracked = true;
        data.high_confidence = hand.IsDataHighConfidence;

        if (TryFindBone(skeleton, OVRSkeleton.BoneId.XRHand_Wrist, out Vector3 wristPos, out Quaternion wristRot))
        {
            data.wrist_position = ToArray(wristPos);
            data.wrist_rotation_quat = new[] { wristRot.x, wristRot.y, wristRot.z, wristRot.w };
        }
        if (TryFindBone(skeleton, OVRSkeleton.BoneId.XRHand_Palm, out Vector3 palmPos, out _))
            data.palm_position = ToArray(palmPos);
        if (TryFindBone(skeleton, OVRSkeleton.BoneId.XRHand_IndexTip, out Vector3 tipPos, out _))
            data.index_tip_position = ToArray(tipPos);

        return data;
    }

    static bool TryFindBone(OVRSkeleton skeleton, OVRSkeleton.BoneId id, out Vector3 position, out Quaternion rotation)
    {
        var bones = skeleton.Bones;
        if (bones != null)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                if ((OVRSkeleton.BoneId)bones[i].Id == id)
                {
                    position = bones[i].Transform.position;
                    rotation = bones[i].Transform.rotation;
                    return true;
                }
            }
        }
        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    static float[] ToArray(Vector3 v) => new[] { v.x, v.y, v.z };

    void Send(string json)
    {
        try
        {
            NetworkStream stream;
            lock (_lock) { stream = _stream; }
            if (stream == null) return;

            byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CognitiveLoadDataStreamer] Send failed ({e.Message}) — will reconnect.");
            lock (_lock)
            {
                _connected = false;
                _stream = null;
                _client?.Close();
                _client = null;
            }
        }
    }

    void OnDestroy()
    {
        _running = false;
        lock (_lock)
        {
            _stream?.Close();
            _client?.Close();
        }
    }

    void OnApplicationQuit() => OnDestroy();
}
