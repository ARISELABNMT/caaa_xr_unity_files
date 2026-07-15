using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using static PanelBuilderUtils;

public static class BuildSystemStatusPanel
{
    [MenuItem("XR Panels/Build System Status Panel")]
    public static void Build()
    {
        Transform xrRoot = FindOrCreate("XRPanelSystem", null).transform;
        Transform backPanels = FindOrCreate("BackPanels", xrRoot).transform;

        RemoveObsoleteAutonomyPanel(xrRoot);

        if (!ConfirmAndClearExisting(backPanels, "SystemStatusPanel")) return;

        GameObject panel = CreateCanvasPanel("SystemStatusPanel", backPanels, 500, 820,
            new Vector3(0.4f, 1.5f, 3f), Vector3.zero, 0.0013f);

        GameObject background = CreateUIObject("Background", panel.transform);
        AddImage(background, BgDark);
        StretchFull(background.GetComponent<RectTransform>());
        AddVerticalLayout(background, 8, 10);

        // --- Header ---
        GameObject header = CreateSection("Header", background.transform, BgHeader, 60);
        AddTMPText(header, "HeaderText", "SYSTEM STATUS", 28, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

        // --- Mode Section (absorbs former Autonomy Panel) ---
        GameObject modeSection = CreateSection("ModeSection", background.transform, BgSection, 230);
        AddVerticalLayout(modeSection, 6, 8);
        AddTMPText(modeSection, "ModeLabel", "ROBOT MODE", 18, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left);
        TMP_Text robotModeText = AddTMPText(modeSection, "ModeValue", "Robot-Led", 24, AccentGreen, FontStyles.Bold, TextAlignmentOptions.Left);

        GameObject modeButtonsRow = CreateUIObject("ModeButtonsRow", modeSection.transform);
        modeButtonsRow.AddComponent<LayoutElement>().preferredHeight = 50;
        AddHorizontalLayout(modeButtonsRow, 6, 0);
        Button humanLedButton = CreateButton(modeButtonsRow.transform, "HumanLedButton", "Human-Led", NeutralGrey, 16);
        Button sharedButton = CreateButton(modeButtonsRow.transform, "SharedButton", "Shared", NeutralGrey, 16);
        Button robotLedButton = CreateButton(modeButtonsRow.transform, "RobotLedButton", "Robot-Led", NeutralGrey, 16);

        TMP_Text modeDescriptionText = AddTMPText(modeSection, "ModeDescription",
            "Robot runs tasks autonomously.\nHuman tasks run in parallel.",
            16, Color.white, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        // --- Risk Section ---
        GameObject riskSection = CreateSection("RiskSection", background.transform, BgSection, 100);
        AddVerticalLayout(riskSection, 4, 8);
        GameObject riskLabelRow = CreateUIObject("RiskLabelRow", riskSection.transform);
        riskLabelRow.AddComponent<LayoutElement>().preferredHeight = 26;
        AddHorizontalLayout(riskLabelRow, 0, 0);
        AddTMPText(riskLabelRow, "RiskLabel", "RISK SCORE", 18, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left)
            .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        TMP_Text riskScoreText = AddTMPText(riskLabelRow, "RiskValue", "0.59", 20, Color.white, FontStyles.Bold, TextAlignmentOptions.Right);
        riskScoreText.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
        Slider riskBar = CreateSlider(riskSection.transform, "RiskBar", SliderBg, EStopRed, 24);

        // --- Cognitive Load Section ---
        GameObject cogSection = CreateSection("CognitiveLoadSection", background.transform, BgSection, 100);
        AddVerticalLayout(cogSection, 4, 8);
        GameObject cogLabelRow = CreateUIObject("CogLabelRow", cogSection.transform);
        cogLabelRow.AddComponent<LayoutElement>().preferredHeight = 26;
        AddHorizontalLayout(cogLabelRow, 0, 0);
        AddTMPText(cogLabelRow, "CogLabel", "COGNITIVE LOAD", 18, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left)
            .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        TMP_Text cogLoadText = AddTMPText(cogLabelRow, "CogValue", "0.78", 20, Color.white, FontStyles.Bold, TextAlignmentOptions.Right);
        cogLoadText.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
        Slider cogLoadBar = CreateSlider(cogSection.transform, "CogLoadBar", SliderBg, AccentOrange, 24);

        // --- Status Rows Section ---
        GameObject statusSection = CreateSection("StatusSection", background.transform, BgSection, 140);
        AddVerticalLayout(statusSection, 6, 8);
        AddTMPText(statusSection, "SectionHeader", "STATUS", 18, AccentBlue, FontStyles.Bold, TextAlignmentOptions.Left);
        TMP_Text robotStatusText = CreateLabelValueRow(statusSection.transform, "RobotStatusRow", "Robot", "Halted");
        TMP_Text humanStatusText = CreateLabelValueRow(statusSection.transform, "HumanStatusRow", "Human", "Idle");
        TMP_Text humanDistanceText = CreateLabelValueRow(statusSection.transform, "HumanDistanceRow", "Distance", "3.8 m");

        // --- Wire up SystemStatusUI ---
        SystemStatusUI ui = panel.AddComponent<SystemStatusUI>();
        ui.robotModeText = robotModeText;
        ui.humanLedButton = humanLedButton;
        ui.sharedButton = sharedButton;
        ui.robotLedButton = robotLedButton;
        ui.modeDescriptionText = modeDescriptionText;
        ui.riskScoreText = riskScoreText;
        ui.riskBar = riskBar;
        ui.cogLoadText = cogLoadText;
        ui.cogLoadBar = cogLoadBar;
        ui.robotStatusText = robotStatusText;
        ui.humanStatusText = humanStatusText;
        ui.humanDistanceText = humanDistanceText;

        ui.rosBridge = Object.FindObjectOfType<RosXRBridge>();
        if (ui.rosBridge == null)
            Debug.LogWarning("System Status Panel: no RosXRBridge found in scene yet — build it first (or use Build All Panels) so mode changes reach /xr/mode.");

        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(panel.scene);
        Debug.Log("System Status Panel built successfully under XRPanelSystem/BackPanels/SystemStatusPanel.");
    }

    private static void RemoveObsoleteAutonomyPanel(Transform xrRoot)
    {
        Transform oldAutonomy = xrRoot.Find("AutonomyPanel");
        if (oldAutonomy == null) return;

        if (EditorUtility.DisplayDialog("Remove Autonomy Panel",
            "Autonomy Panel functionality has been merged into System Status Panel. Delete the old AutonomyPanel GameObject?",
            "Delete", "Keep it"))
        {
            Undo.DestroyObjectImmediate(oldAutonomy.gameObject);
        }
    }

    private static TMP_Text CreateLabelValueRow(Transform parent, string rowName, string label, string value)
    {
        GameObject row = CreateUIObject(rowName, parent);
        row.AddComponent<LayoutElement>().preferredHeight = 30;
        AddHorizontalLayout(row, 0, 0, forceExpandWidth: false, forceExpandHeight: true);

        AddTMPText(row, "Label", label, 20, Color.white, FontStyles.Normal, TextAlignmentOptions.Left)
            .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        TMP_Text valueText = AddTMPText(row, "Value", value, 20, AccentBlue, FontStyles.Bold, TextAlignmentOptions.Right);
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 120;
        return valueText;
    }
}
