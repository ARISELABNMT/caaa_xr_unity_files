using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SystemStatusUI : MonoBehaviour
{
    [Header("Mode")]
    public TMP_Text robotModeText;
    public Button humanLedButton;
    public Button sharedButton;
    public Button robotLedButton;
    public TMP_Text modeDescriptionText;

    [Header("Risk")]
    public TMP_Text riskScoreText;
    public Slider riskBar;

    [Header("Cognitive Load")]
    public TMP_Text cogLoadText;
    public Slider cogLoadBar;

    [Header("Status")]
    public TMP_Text robotStatusText;
    public TMP_Text humanStatusText;
    public TMP_Text humanDistanceText;

    [Header("ROS")]
    public RosXRBridge rosBridge;

    private string _currentMode = "Robot-Led";
    private float _riskValue;
    private float _cogValue;

    private readonly string[] _modeDescriptions = {
        "Human drives all tasks.\nRobot assists on request.",
        "Human and robot share tasks\ncollaboratively.",
        "Robot runs tasks autonomously.\nHuman tasks run in parallel."
    };

    void Start()
    {
        if (humanLedButton) humanLedButton.onClick.AddListener(() => SetMode("Human-Led", 0));
        if (sharedButton) sharedButton.onClick.AddListener(() => SetMode("Shared", 1));
        if (robotLedButton) robotLedButton.onClick.AddListener(() => SetMode("Robot-Led", 2));

        SetPlaceholderData();
    }

    void SetPlaceholderData()
    {
        // Risk/Cognitive Load are fixed placeholders for now — later these will come from live sensing.
        SetRisk(0.59f);
        SetCognitiveLoad(0.78f);
        EvaluateAndApplyMode();
        SetRobotStatus("Halted");
        SetHumanStatus("Idle");
        SetHumanDistance(3.8f);
    }

    /// <summary>Derives the autonomy mode from the current Risk/Cognitive Load scores. High risk or
    /// cognitive load favors more human oversight; low scores allow full robot autonomy.</summary>
    public void EvaluateAndApplyMode()
    {
        if (_riskValue >= 0.5f || _cogValue >= 0.7f)
            SetMode("Human-Led", 0);
        else if (_riskValue >= 0.25f || _cogValue >= 0.4f)
            SetMode("Shared", 1);
        else
            SetMode("Robot-Led", 2);
    }

    public void SetMode(string mode, int descriptionIndex)
    {
        _currentMode = mode;
        if (robotModeText) robotModeText.text = mode;
        if (modeDescriptionText) modeDescriptionText.text = _modeDescriptions[descriptionIndex];

        SetModeButtonHighlight(humanLedButton, mode == "Human-Led");
        SetModeButtonHighlight(sharedButton, mode == "Shared");
        SetModeButtonHighlight(robotLedButton, mode == "Robot-Led");

        rosBridge?.PublishMode(ToRosMode(mode));
    }

    /// <summary>Converts Unity's display mode string ("Human-Led") to the lowercase snake_case value
    /// xr_mode_manager.py expects on /xr/mode ("human_led").</summary>
    static string ToRosMode(string mode) => mode switch
    {
        "Human-Led" => "human_led",
        "Shared" => "shared",
        "Robot-Led" => "robot_led",
        _ => mode.ToLowerInvariant().Replace('-', '_'),
    };

    void SetModeButtonHighlight(Button button, bool active)
    {
        if (!button) return;
        var img = button.GetComponent<Image>();
        if (img) img.color = active ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.5f, 0.5f, 0.5f);
    }

    public string GetCurrentMode() => _currentMode;

    public void SetRisk(float value)
    {
        _riskValue = value;
        if (riskScoreText) riskScoreText.text = value.ToString("F2");
        if (riskBar) riskBar.value = value;
        if (riskBar)
        {
            var fill = riskBar.fillRect.GetComponent<Image>();
            if (fill) fill.color = Color.Lerp(Color.green, Color.red, value);
        }
    }

    public void SetCognitiveLoad(float value)
    {
        _cogValue = value;
        if (cogLoadText) cogLoadText.text = value.ToString("F2");
        if (cogLoadBar) cogLoadBar.value = value;
    }

    public void SetRobotStatus(string status)
    {
        if (robotStatusText) robotStatusText.text = status;
    }

    public void SetHumanStatus(string status)
    {
        if (humanStatusText) humanStatusText.text = status;
    }

    public void SetHumanDistance(float meters)
    {
        if (humanDistanceText) humanDistanceText.text = $"{meters:F1} m";
    }
}
