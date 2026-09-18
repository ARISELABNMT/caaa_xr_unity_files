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
    public TMP_Text humanPartText;

    [Header("ROS")]
    public RosXRBridge rosBridge;

    private string _currentMode = "Robot-Led";

    private readonly string[] _modeDescriptions = {
        "Human drives all tasks.\nRobot assists on request.",
        "Human and robot share tasks\ncollaboratively.",
        "Robot runs tasks autonomously.\nHuman tasks run in parallel.",
        "PROTECTIVE STOP.\nRisk and cognitive load are both High."
    };

    private static readonly Color SafetyRed = new Color(0.85f, 0.15f, 0.15f);
    private static readonly Color NormalModeColor = Color.white;

    void Start()
    {
        // Mode is selected automatically by DecisionEngine from R(t)/C(t) — these buttons are read-only
        // status indicators now (SetModeButtonHighlight still colors whichever one is currently active).
        // Deliberately NOT setting button.interactable = false here: Selectable's disabled-state color
        // transition can overwrite SetModeButtonHighlight's manually-set Image color, which looked like the
        // active-mode highlight silently vanishing. No onClick listeners are registered below anymore, so
        // the buttons are already functionally inert — nothing is gained by touching interactable too.
        SetPlaceholderData();
    }

    void SetPlaceholderData()
    {
        // Risk/Cognitive Load are fixed placeholders for now — DecisionEngine takes over live mode
        // selection within its first decision epoch once it finds real R(t)/C(t) sources in the scene.
        SetRisk(0.59f);
        SetCognitiveLoad(0.78f);
        SetMode("Robot-Led", 2);
        SetRobotStatus("Halted");
        SetHumanStatus("Idle");
        SetHumanDistance(3.8f);
        SetNearestBodyPart("--");
    }

    public void SetMode(string mode, int descriptionIndex)
    {
        _currentMode = mode;
        if (robotModeText)
        {
            robotModeText.text = mode;
            robotModeText.color = mode == "Safety" ? SafetyRed : NormalModeColor;
        }
        if (modeDescriptionText) modeDescriptionText.text = _modeDescriptions[descriptionIndex];

        SetModeButtonHighlight(humanLedButton, mode == "Human-Led");
        SetModeButtonHighlight(sharedButton, mode == "Shared");
        SetModeButtonHighlight(robotLedButton, mode == "Robot-Led");

        rosBridge?.PublishMode(ToRosMode(mode));
    }

    /// <summary>Converts Unity's display mode string ("Human-Led") to the lowercase snake_case value
    /// xr_mode_manager.py expects on /xr/mode ("human_led"). "Safety" also matches the string
    /// ControlPanelUI's E-Stop already publishes directly, kept explicit here since it's safety-critical.</summary>
    static string ToRosMode(string mode) => mode switch
    {
        "Human-Led" => "human_led",
        "Shared" => "shared",
        "Robot-Led" => "robot_led",
        "Safety" => "safety",
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

    public void SetNearestBodyPart(string partName)
    {
        if (humanPartText) humanPartText.text = string.IsNullOrEmpty(partName) ? "--" : partName;
    }
}
