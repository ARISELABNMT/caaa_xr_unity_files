using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RiskModeManager : MonoBehaviour
{
    [Header("Risk Scripts")]
    public QuestPredictiveRisk plannedRiskDetector;
    public TrajectoryCloud sweptVolumeDetector;

    [Header("Buttons (visual indicator)")]
    public Button plannedButton;
    public Button sweptButton;

    [Header("Colors")]
    public Color activeColor   = new Color(0.2f, 0.8f, 0.2f, 1f);
    public Color inactiveColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private bool isPlannedMode = true;

    void Start()
    {
        if (plannedButton != null) plannedButton.onClick.AddListener(ActivatePlanned);
        if (sweptButton   != null) sweptButton.onClick.AddListener(ActivateSwepted);
        ActivatePlanned();
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            if (isPlannedMode) ActivateSwepted();
            else ActivatePlanned();
        }
    }

    public void ActivatePlanned()
    {
        isPlannedMode = true;
        if (plannedRiskDetector != null) plannedRiskDetector.enabled = true;
        if (sweptVolumeDetector != null) sweptVolumeDetector.enabled = false;
        UpdateButtonColors();
    }

    public void ActivateSwepted()
    {
        isPlannedMode = false;
        if (plannedRiskDetector != null) plannedRiskDetector.enabled = false;
        if (sweptVolumeDetector != null) sweptVolumeDetector.enabled = true;
        UpdateButtonColors();
    }

    void UpdateButtonColors()
    {
        if (plannedButton != null)
            plannedButton.GetComponent<Image>().color = isPlannedMode ? activeColor : inactiveColor;
        if (sweptButton != null)
            sweptButton.GetComponent<Image>().color = isPlannedMode ? inactiveColor : activeColor;
    }
}
