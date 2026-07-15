using System;
using System.Collections;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;

/// <summary>
/// Central Unity &lt;-&gt; ROS2 bridge for the /xr/* topic set handled by xr_mode_manager.py on the
/// ROS side. Publishes the 4 bin poses + place pose once ready, forwards mode / bin-selection /
/// task-request / confirm / reset commands, and raises C# events for the status/result/
/// request_confirm/mode messages ROS sends back. Other scripts (RobotTaskManager, SystemStatusUI,
/// ControlPanelUI, RosConfirmDialogHandler) call into this rather than touching ROSConnection directly.
///
/// Every publish/receive logs via Debug.Log (tag "[RosXRBridge]") so `adb logcat -s Unity` can confirm
/// what actually left/arrived at the device, independent of whether ROS received it.
/// </summary>
public class RosXRBridge : MonoBehaviour
{
    [Header("Robot base frame (must match ROS frame_id 'link_base')")]
    public Transform robotBaseFrame;

    [Header("Bin / place markers — position these to match the physical robot cell")]
    public Transform binATransform;
    public Transform binBTransform;
    public Transform binCTransform;
    public Transform binDTransform;
    public Transform placeTransform;

    public event Action<string> OnStatus;
    public event Action<bool> OnResult;
    public event Action<string> OnRequestConfirm;
    public event Action<string> OnCurrentMode;
    public event Action<string> OnModeStatus;
    public event Action<bool> OnHoldingState;

    private bool _isReady;

    // Registration must happen in Start(), not Awake(): each ROS message class (StringMsg, BoolMsg,
    // PoseStampedMsg, ...) registers itself into MessageRegistry via a bare
    // [RuntimeInitializeOnLoadMethod] attribute, which defaults to AfterSceneLoad timing. Unity's actual
    // order is: scene Awake()/OnEnable() -> RuntimeInitializeOnLoadMethod(AfterSceneLoad) -> scene
    // Start(). Registering in Awake() ran before MessageRegistry had these types populated at all,
    // which silently resolved every RegisterPublisher/Subscribe call to an empty ROS message name
    // (visible on device as "SysCommand.subscribe/publish - Unknown message class ''" in logcat).
    void Start()
    {
        ROSConnection ros = ROSConnection.GetOrCreateInstance();

        ros.RegisterPublisher<StringMsg>("/xr/mode");
        ros.RegisterPublisher<PoseStampedMsg>("/xr/bin_A_pose");
        ros.RegisterPublisher<PoseStampedMsg>("/xr/bin_B_pose");
        ros.RegisterPublisher<PoseStampedMsg>("/xr/bin_C_pose");
        ros.RegisterPublisher<PoseStampedMsg>("/xr/bin_D_pose");
        ros.RegisterPublisher<PoseStampedMsg>("/xr/place_pose");
        ros.RegisterPublisher<StringMsg>("/xr/selected_bin");
        ros.RegisterPublisher<BoolMsg>("/xr/task_request");
        ros.RegisterPublisher<BoolMsg>("/xr/confirm");
        ros.RegisterPublisher<BoolMsg>("/xr/reset");

        ros.Subscribe<StringMsg>("/xr/status", msg => { Debug.Log($"[RosXRBridge] RECV /xr/status = {msg.data}"); OnStatus?.Invoke(msg.data); });
        ros.Subscribe<BoolMsg>("/xr/result", msg => { Debug.Log($"[RosXRBridge] RECV /xr/result = {msg.data}"); OnResult?.Invoke(msg.data); });
        ros.Subscribe<StringMsg>("/xr/request_confirm", msg => { Debug.Log($"[RosXRBridge] RECV /xr/request_confirm = {msg.data}"); OnRequestConfirm?.Invoke(msg.data); });
        ros.Subscribe<StringMsg>("/xr/current_mode", msg => { Debug.Log($"[RosXRBridge] RECV /xr/current_mode = {msg.data}"); OnCurrentMode?.Invoke(msg.data); });
        ros.Subscribe<StringMsg>("/xr/mode_status", msg => { Debug.Log($"[RosXRBridge] RECV /xr/mode_status = {msg.data}"); OnModeStatus?.Invoke(msg.data); });
        ros.Subscribe<BoolMsg>("/xr/holding_state", msg => { Debug.Log($"[RosXRBridge] RECV /xr/holding_state = {msg.data}"); OnHoldingState?.Invoke(msg.data); });

        Debug.Log($"[RosXRBridge] Start: registered all /xr/* publishers+subscribers. Target ROS endpoint: {ros.RosIPAddress}:{ros.RosPort}");

        _isReady = true;
        PublishAllBinPoses();
    }

    public void PublishAllBinPoses()
    {
        PublishPose("/xr/bin_A_pose", binATransform);
        PublishPose("/xr/bin_B_pose", binBTransform);
        PublishPose("/xr/bin_C_pose", binCTransform);
        PublishPose("/xr/bin_D_pose", binDTransform);
        PublishPose("/xr/place_pose", placeTransform);
    }

    void PublishPose(string topic, Transform marker)
    {
        if (marker == null)
        {
            Debug.LogWarning($"[RosXRBridge] no marker assigned for {topic} — skipped. Position a Transform in the scene and assign it in the Inspector.");
            return;
        }

        Vector3 pos;
        Quaternion rot;
        if (robotBaseFrame != null)
        {
            pos = robotBaseFrame.InverseTransformPoint(marker.position);
            rot = Quaternion.Inverse(robotBaseFrame.rotation) * marker.rotation;
        }
        else
        {
            Debug.LogWarning("[RosXRBridge] robotBaseFrame not assigned — publishing marker in world space, which will be wrong unless Unity's world origin already matches link_base.");
            pos = marker.position;
            rot = marker.rotation;
        }

        PoseStampedMsg msg = new PoseStampedMsg
        {
            header = new HeaderMsg { frame_id = "link_base" },
            pose = new PoseMsg
            {
                position = pos.To<FLU>(),
                orientation = rot.To<FLU>()
            }
        };

        SendOrQueue(topic, msg, $"pos=({pos.x:F3},{pos.y:F3},{pos.z:F3})");
    }

    public void PublishMode(string mode) => SendOrQueue("/xr/mode", new StringMsg(mode), mode);
    public void PublishSelectedBin(string binId) => SendOrQueue("/xr/selected_bin", new StringMsg(binId), binId);
    public void PublishTaskRequest(bool value) => SendOrQueue("/xr/task_request", new BoolMsg(value), value.ToString());
    public void PublishConfirm(bool value) => SendOrQueue("/xr/confirm", new BoolMsg(value), value.ToString());
    public void PublishReset(bool value) => SendOrQueue("/xr/reset", new BoolMsg(value), value.ToString());

    /// <summary>Publishes immediately if registration has completed; otherwise waits for Start() to
    /// finish first. This protects against other scripts (SystemStatusUI, ControlPanelUI) publishing
    /// from their own Start() before RosXRBridge's Start() has run — Unity doesn't guarantee Start()
    /// order between different components.</summary>
    void SendOrQueue(string topic, Message msg, string valueForLog)
    {
        if (_isReady)
            DoPublish(topic, msg, valueForLog);
        else
            StartCoroutine(PublishWhenReady(topic, msg, valueForLog));
    }

    IEnumerator PublishWhenReady(string topic, Message msg, string valueForLog)
    {
        yield return new WaitUntil(() => _isReady);
        DoPublish(topic, msg, valueForLog);
    }

    void DoPublish(string topic, Message msg, string valueForLog)
    {
        try
        {
            ROSConnection.GetOrCreateInstance().Publish(topic, msg);
            Debug.Log($"[RosXRBridge] SENT {topic} = {valueForLog}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RosXRBridge] FAILED to publish {topic}: {e.Message}");
        }
    }
}
