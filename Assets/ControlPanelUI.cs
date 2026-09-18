using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ControlPanelUI : MonoBehaviour
{
    [Header("Kit Selection")]
    public Button kitAButton;
    public Button kitBButton;
    public Button kitCButton;
    public TMP_Text selectedKitText;

    [Header("Human Task Completion")]
    public TaskPanelUI taskPanel;
    public TMP_Text humanTask1Label;
    public Button markHumanTask1DoneButton;
    public GameObject humanTask2Row;
    public TMP_Text humanTask2Label;
    public Button markHumanTask2DoneButton;

    [Header("Robot Controls")]
    public Button pauseButton;
    public Button eStopButton;
    public Button resetButton;
    public Button startButton;

    [Header("Status")]
    public TMP_Text statusText;

    [Header("Kitting Operation")]
    public RobotTaskManager robotTaskManager;
    public RosXRBridge rosBridge;

    [Header("Decision Engine (auto-found if left empty)")]
    public DecisionEngine decisionEngine;

    private static readonly Color ReadyGreen = new Color(0.153f, 0.682f, 0.376f);

    private string _selectedKit = "Kit A";
    private bool _isPaused = false;

    void Start()
    {
        if (!decisionEngine) decisionEngine = FindObjectOfType<DecisionEngine>();

        kitAButton.onClick.AddListener(() => SelectKit("Kit A"));
        kitBButton.onClick.AddListener(() => SelectKit("Kit B"));
        kitCButton.onClick.AddListener(() => SelectKit("Kit C"));

        if (markHumanTask1DoneButton)
            markHumanTask1DoneButton.onClick.AddListener(() => MarkHumanTaskDone(markHumanTask1DoneButton, () => taskPanel?.MarkHumanTask1Done()));
        if (markHumanTask2DoneButton)
            markHumanTask2DoneButton.onClick.AddListener(() => MarkHumanTaskDone(markHumanTask2DoneButton, () => taskPanel?.MarkHumanTask2Done()));

        pauseButton.onClick.AddListener(OnPause);
        eStopButton.onClick.AddListener(OnEStop);
        resetButton.onClick.AddListener(OnReset);
        startButton.onClick.AddListener(OnStart);

        if (robotTaskManager != null)
            robotTaskManager.onKittingComplete += HandleKittingComplete;

        SelectKit("Kit A");
        SetStatus("Ready");
    }

    void SelectKit(string kit)
    {
        _selectedKit = kit;
        if (selectedKitText) selectedKitText.text = $"Selected: {kit}";

        kitAButton.GetComponent<Image>().color = kit == "Kit A" ? Color.cyan : Color.grey;
        kitBButton.GetComponent<Image>().color = kit == "Kit B" ? Color.cyan : Color.grey;
        kitCButton.GetComponent<Image>().color = kit == "Kit C" ? Color.cyan : Color.grey;

        if (robotTaskManager == null) return;

        robotTaskManager.SetKit(kit);
        ApplyKitToHumanTaskButtons(robotTaskManager.GetKit(kit));
    }

    void ApplyKitToHumanTaskButtons(RobotTaskManager.KitDefinition kit)
    {
        if (kit == null) return;

        if (humanTask1Label) humanTask1Label.text = kit.humanTask1.name;
        ResetMarkDoneButton(markHumanTask1DoneButton);

        bool hasTask2 = kit.HasHumanTask2;
        if (humanTask2Row) humanTask2Row.SetActive(hasTask2);
        if (hasTask2)
        {
            if (humanTask2Label) humanTask2Label.text = kit.humanTask2.name;
            ResetMarkDoneButton(markHumanTask2DoneButton);
        }
    }

    void ResetMarkDoneButton(Button button)
    {
        if (!button) return;

        button.interactable = true;
        var text = button.GetComponentInChildren<TMP_Text>();
        if (text) text.text = "Mark Done";
        var img = button.GetComponent<Image>();
        if (img) img.color = ReadyGreen;
    }

    void MarkHumanTaskDone(Button button, System.Action markAction)
    {
        markAction?.Invoke();

        var text = button.GetComponentInChildren<TMP_Text>();
        if (text) text.text = "Done";
        button.interactable = false;

        var img = button.GetComponent<Image>();
        if (img) img.color = Color.grey;
    }

    void OnPause()
    {
        _isPaused = !_isPaused;
        pauseButton.GetComponentInChildren<TMP_Text>().text = _isPaused ? "Resume" : "Pause";
        SetStatus(_isPaused ? "Paused" : "Running");
    }

    void OnEStop()
    {
        SetStatus("EMERGENCY STOP");
        statusText.color = Color.red;
        rosBridge?.PublishMode("safety");
        decisionEngine?.ForceSafety();
    }

    void OnReset()
    {
        _isPaused = false;
        pauseButton.GetComponentInChildren<TMP_Text>().text = "Pause";
        startButton.interactable = true;
        SetStatus("Ready");
        statusText.color = Color.white;
        rosBridge?.PublishReset(true);
        decisionEngine?.ClearSafetyLatch();
        // Stops any kitting run stuck mid pick-and-place (e.g. waiting on a /xr/result that will never
        // arrive because Safety halted the robot) — without this, BeginKitting() would silently no-op on
        // every future Start press since RobotTaskManager's _isRunning would stay true forever.
        robotTaskManager?.AbortKitting();
    }

    void OnStart()
    {
        SetStatus($"Running — {_selectedKit}");
        statusText.color = Color.green;
        startButton.interactable = false;
        robotTaskManager?.BeginKitting();
    }

    void HandleKittingComplete(bool success)
    {
        startButton.interactable = true;
        if (success)
        {
            SetStatus("Kit Complete");
            statusText.color = Color.green;
        }
        else
        {
            SetStatus("Task Failed");
            statusText.color = Color.red;
        }
    }

    void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
    }

    public string GetSelectedKit() => _selectedKit;
}
