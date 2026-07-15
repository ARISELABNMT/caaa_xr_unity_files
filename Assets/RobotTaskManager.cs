using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the kitting operation once the operator presses Start. For each robot item unit, this
/// selects the physical bin and requests the task from the ROS2 side (xr_mode_manager.py), then waits
/// for the result. All mode-specific behavior (Human-Led step-by-step confirmation, Shared single
/// approval, Robot-Led fully automatic, Safety halt) is now owned by xr_mode_manager.py on the ROS
/// side — Unity's only job here is bin selection + task triggering + waiting for /xr/result via
/// RosXRBridge. Confirmation popups themselves are handled separately by RosConfirmDialogHandler,
/// reacting to /xr/request_confirm independently of this task loop.
///
/// If no RosXRBridge is assigned (e.g. testing the UI without ROS connected), each unit is instead
/// simulated with a fixed delay so the panels remain usable standalone.
/// </summary>
public class RobotTaskManager : MonoBehaviour
{
    [Serializable]
    public class RobotTaskItem
    {
        public string name;
        public string binId;       // "A" | "B" | "C" | "D" — physical bin, matches ROS /xr/selected_bin
        public string binLocation; // display text, e.g. "Bin A"
        public int quantity;
    }

    [Serializable]
    public class KitDefinition
    {
        public string kitName;
        public RobotTaskItem robotTask1;
        public RobotTaskItem robotTask2;
        public RobotTaskItem humanTask1;
        public RobotTaskItem humanTask2; // null (or quantity 0) if this kit only has one human task

        public bool HasHumanTask2 => humanTask2 != null && humanTask2.quantity > 0;
    }

    public TaskPanelUI taskPanel;
    public RosXRBridge rosBridge;

    // Physical bins are fixed per kit: robot items always use Bin A / Bin B; human items (if present)
    // always use Bin C / Bin D. What item occupies a bin changes per kit, not the bin itself.
    public List<KitDefinition> kitDefinitions = new List<KitDefinition>
    {
        new KitDefinition
        {
            kitName = "Kit A",
            robotTask1 = new RobotTaskItem { name = "Bolt", binId = "A", binLocation = "Bin A", quantity = 2 },
            robotTask2 = new RobotTaskItem { name = "Nut", binId = "B", binLocation = "Bin B", quantity = 1 },
            humanTask1 = new RobotTaskItem { name = "Washer", binId = "C", binLocation = "Bin C", quantity = 1 },
            humanTask2 = new RobotTaskItem { name = "Spacer", binId = "D", binLocation = "Bin D", quantity = 1 },
        },
        new KitDefinition
        {
            kitName = "Kit B",
            robotTask1 = new RobotTaskItem { name = "Bracket", binId = "A", binLocation = "Bin A", quantity = 1 },
            robotTask2 = new RobotTaskItem { name = "Screw", binId = "B", binLocation = "Bin B", quantity = 3 },
            humanTask1 = new RobotTaskItem { name = "Gear", binId = "C", binLocation = "Bin C", quantity = 2 },
            humanTask2 = null,
        },
        new KitDefinition
        {
            kitName = "Kit C",
            robotTask1 = new RobotTaskItem { name = "Bearing", binId = "A", binLocation = "Bin A", quantity = 2 },
            robotTask2 = new RobotTaskItem { name = "Shaft", binId = "B", binLocation = "Bin B", quantity = 1 },
            humanTask1 = new RobotTaskItem { name = "Clip", binId = "C", binLocation = "Bin C", quantity = 4 },
            humanTask2 = new RobotTaskItem { name = "Seal", binId = "D", binLocation = "Bin D", quantity = 1 },
        },
    };

    public List<RobotTaskItem> robotTasks;

    [Header("Standalone fallback (used only if rosBridge is unassigned)")]
    public float pickDuration = 1.5f;
    public float placeDuration = 1.5f;

    [Header("ROS result wait")]
    public float taskTimeoutSeconds = 310f; // slightly longer than xr_mode_manager.py's 300s TASK_TIMEOUT_SEC

    /// <summary>Fires when the kitting run ends, whether by finishing all units (true) or aborting
    /// due to a failed/timed-out pick-place (false). Callers must branch on this — treating every
    /// firing as success will misreport failures as "Kit Complete".</summary>
    public Action<bool> onKittingComplete;

    private int _task1Done;
    private int _task2Done;
    private bool _isRunning;
    private bool _lastStepSucceeded;

    void Awake()
    {
        if (robotTasks == null || robotTasks.Count == 0)
            robotTasks = new List<RobotTaskItem> { kitDefinitions[0].robotTask1, kitDefinitions[0].robotTask2 };
    }

    public KitDefinition GetKit(string kitName) => kitDefinitions.Find(k => k.kitName == kitName);

    /// <summary>Switches the active kit — updates the robot task queue used by BeginKitting() and
    /// tells TaskPanelUI to redisplay the new kit's robot/human task rows.</summary>
    public void SetKit(string kitName)
    {
        KitDefinition kit = GetKit(kitName);
        if (kit == null) return;

        robotTasks = new List<RobotTaskItem> { kit.robotTask1, kit.robotTask2 };

        if (taskPanel)
            taskPanel.ConfigureKit(kit);
    }

    public void BeginKitting()
    {
        if (_isRunning) return;
        _isRunning = true;
        _task1Done = 0;
        _task2Done = 0;
        StartCoroutine(RunKitting());
    }

    private IEnumerator RunKitting()
    {
        for (int taskIndex = 0; taskIndex < robotTasks.Count; taskIndex++)
        {
            RobotTaskItem item = robotTasks[taskIndex];

            for (int unit = 0; unit < item.quantity; unit++)
            {
                if (taskPanel)
                    taskPanel.UpdateNextItem(item.name, $"{item.binLocation} → Kitting Box");

                yield return RunRosPickPlace(item);

                if (!_lastStepSucceeded)
                {
                    Debug.LogWarning($"RobotTaskManager: stopping kitting run — {item.name} (Bin {item.binId}) did not complete successfully.");
                    _isRunning = false;
                    onKittingComplete?.Invoke(false);
                    yield break;
                }

                if (taskIndex == 0) _task1Done++;
                else _task2Done++;

                if (taskPanel)
                    taskPanel.UpdateRobotTasks(
                        robotTasks[0].name, _task1Done, robotTasks[0].quantity,
                        robotTasks[1].name, _task2Done, robotTasks[1].quantity);
            }
        }

        _isRunning = false;
        onKittingComplete?.Invoke(true);
    }

    private IEnumerator RunRosPickPlace(RobotTaskItem item)
    {
        if (rosBridge == null)
        {
            // No ROS connection configured — simulate so the UI stays testable standalone.
            yield return new WaitForSeconds(pickDuration + placeDuration);
            _lastStepSucceeded = true;
            yield break;
        }

        bool done = false;
        bool success = false;
        void OnResult(bool ok) { success = ok; done = true; }

        rosBridge.OnResult += OnResult;
        rosBridge.PublishSelectedBin(item.binId);
        rosBridge.PublishTaskRequest(true);

        float elapsed = 0f;
        while (!done && elapsed < taskTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        rosBridge.OnResult -= OnResult;

        if (!done)
        {
            Debug.LogWarning($"RobotTaskManager: timed out waiting for /xr/result — {item.name} (Bin {item.binId}). Robot may be in Safety halt or unreachable.");
            _lastStepSucceeded = false;
        }
        else if (!success)
        {
            Debug.LogWarning($"RobotTaskManager: pick-place reported failure — {item.name} (Bin {item.binId}).");
            _lastStepSucceeded = false;
        }
        else
        {
            _lastStepSucceeded = true;
        }
    }
}
