using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using static PanelBuilderUtils;

public static class BuildControlPanel
{
    [MenuItem("XR Panels/Build Control Panel")]
    public static void Build()
    {
        Transform xrRoot = FindOrCreate("XRPanelSystem", null).transform;

        if (!ConfirmAndClearExisting(xrRoot, "ControlPanel")) return;

        GameObject panel = CreateCanvasPanel("ControlPanel", xrRoot, 500, 790,
            new Vector3(0.65f, 1.5f, 1.25f), new Vector3(0f, 90f, 0f));

        GameObject background = CreateUIObject("Background", panel.transform);
        AddImage(background, BgDark);
        StretchFull(background.GetComponent<RectTransform>());
        AddVerticalLayout(background, 8, 10);

        // --- Header ---
        GameObject header = CreateSection("Header", background.transform, BgHeader, 60);
        AddTMPText(header, "HeaderText", "CONTROL PANEL", 26, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

        // --- Robot Alignment Section ---
        // Section height must comfortably fit label + row (squishing below the content's natural size
        // shrinks the button's layout-driven RectTransform, which shrinks GazeClickable's collider along
        // with it — a button can render fine but become nearly unclickable this way).
        GameObject alignSection = CreateSection("AlignmentSection", background.transform, BgSection, 100);
        AddVerticalLayout(alignSection, 6, 8);
        AddTMPText(alignSection, "AlignSectionLabel", "ROBOT ALIGNMENT", 16, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left);

        GameObject alignButtonRow = CreateUIObject("AlignButtonRow", alignSection.transform);
        alignButtonRow.AddComponent<LayoutElement>().preferredHeight = 44;
        AddHorizontalLayout(alignButtonRow, 0, 0, forceExpandWidth: false, forceExpandHeight: true);
        Button alignToggleButton = CreateButton(alignButtonRow.transform, "AlignToggleButton", "Start Aligning", AccentOrange, 20);
        LayoutElement alignBtnLe = alignToggleButton.gameObject.AddComponent<LayoutElement>();
        alignBtnLe.preferredWidth = 220;
        alignBtnLe.preferredHeight = 44;

        // --- Kit Selection Section ---
        GameObject kitSection = CreateSection("KitSelectionSection", background.transform, BgSection, 130);
        AddVerticalLayout(kitSection, 6, 8);
        AddTMPText(kitSection, "KitSectionLabel", "SELECT KIT", 16, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left);

        GameObject kitButtonsRow = CreateUIObject("KitButtonsRow", kitSection.transform);
        kitButtonsRow.AddComponent<LayoutElement>().preferredHeight = 55;
        AddHorizontalLayout(kitButtonsRow, 8, 0);
        Button kitAButton = CreateButton(kitButtonsRow.transform, "KitAButton", "Kit A", AccentCyan, 20);
        Button kitBButton = CreateButton(kitButtonsRow.transform, "KitBButton", "Kit B", NeutralGrey, 20);
        Button kitCButton = CreateButton(kitButtonsRow.transform, "KitCButton", "Kit C", NeutralGrey, 20);

        TMP_Text selectedKitText = AddTMPText(kitSection, "SelectedKitText", "Selected: Kit A", 18, Color.white, FontStyles.Normal, TextAlignmentOptions.Left);

        // --- Human Tasks Section (Mark Done buttons) ---
        GameObject humanTasksSection = CreateSection("HumanTasksSection", background.transform, BgSection, 140);
        AddVerticalLayout(humanTasksSection, 6, 8);
        AddTMPText(humanTasksSection, "SectionHeader", "HUMAN TASKS", 16, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left);
        Button markHumanTask1DoneButton = CreateMarkDoneRow(humanTasksSection.transform, "HumanTask1Row", "Washer",
            out TMP_Text humanTask1Label, out _);
        Button markHumanTask2DoneButton = CreateMarkDoneRow(humanTasksSection.transform, "HumanTask2Row", "Spacer",
            out TMP_Text humanTask2Label, out GameObject humanTask2Row);

        // --- Status Section ---
        GameObject statusSection = CreateSection("StatusSection", background.transform, BgSection, 60);
        AddVerticalLayout(statusSection, 0, 8);
        TMP_Text statusText = AddTMPText(statusSection, "StatusText", "Ready", 22, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

        // --- Actions Section (2x2 button grid) ---
        GameObject actionsSection = CreateUIObject("ActionsSection", background.transform);
        actionsSection.AddComponent<LayoutElement>().preferredHeight = 150;
        AddVerticalLayout(actionsSection, 8, 0, forceExpandHeight: true);

        GameObject row1 = CreateUIObject("ActionsRow1", actionsSection.transform);
        AddHorizontalLayout(row1, 8, 0);
        Button startButton = CreateButton(row1.transform, "StartButton", "Start", AccentGreen, 22);
        Button pauseButton = CreateButton(row1.transform, "PauseButton", "Pause", PauseBlue, 22);

        GameObject row2 = CreateUIObject("ActionsRow2", actionsSection.transform);
        AddHorizontalLayout(row2, 8, 0);
        Button resetButton = CreateButton(row2.transform, "ResetButton", "Reset", NeutralGrey, 22);
        Button eStopButton = CreateButton(row2.transform, "EStopButton", "E-Stop", EStopRed, 22);

        // --- Wire up ControlPanelUI ---
        ControlPanelUI ui = panel.AddComponent<ControlPanelUI>();
        ui.kitAButton = kitAButton;
        ui.kitBButton = kitBButton;
        ui.kitCButton = kitCButton;
        ui.selectedKitText = selectedKitText;
        ui.pauseButton = pauseButton;
        ui.eStopButton = eStopButton;
        ui.resetButton = resetButton;
        ui.startButton = startButton;
        ui.statusText = statusText;
        ui.humanTask1Label = humanTask1Label;
        ui.markHumanTask1DoneButton = markHumanTask1DoneButton;
        ui.humanTask2Row = humanTask2Row;
        ui.humanTask2Label = humanTask2Label;
        ui.markHumanTask2DoneButton = markHumanTask2DoneButton;

        TaskPanelUI taskPanelUI = Object.FindObjectOfType<TaskPanelUI>();
        ui.taskPanel = taskPanelUI;
        if (taskPanelUI == null)
            Debug.LogWarning("Control Panel: no TaskPanelUI found in scene yet — build the Task Panel first (or use Build All Panels) so the Mark Done buttons can be wired.");

        RobotTaskManager taskManager = Object.FindObjectOfType<RobotTaskManager>();
        ui.robotTaskManager = taskManager;
        if (taskManager == null)
            Debug.LogWarning("Control Panel: no RobotTaskManager found in scene yet — build it first (or use Build All Panels) so the Start button can drive the kitting operation.");

        ui.rosBridge = Object.FindObjectOfType<RosXRBridge>();
        if (ui.rosBridge == null)
            Debug.LogWarning("Control Panel: no RosXRBridge found in scene yet — build it first (or use Build All Panels) so E-Stop/Reset reach /xr/mode and /xr/reset.");

        PassthroughQRAligner qrAligner = Object.FindObjectOfType<PassthroughQRAligner>();
        if (qrAligner != null)
            qrAligner.alignToggleButton = alignToggleButton;
        else
            Debug.LogWarning("Control Panel: no PassthroughQRAligner found in scene yet — build it first (XR Panels > Build Passthrough QR Aligner), then rebuild Control Panel so the Align button gets wired.");

        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(panel.scene);
        Debug.Log("Control Panel built successfully under XRPanelSystem/ControlPanel.");
    }

    private static Button CreateMarkDoneRow(Transform parent, string rowName, string taskLabel,
        out TMP_Text label, out GameObject row)
    {
        row = CreateUIObject(rowName, parent);
        row.AddComponent<LayoutElement>().preferredHeight = 45;
        AddHorizontalLayout(row, 8, 0, forceExpandWidth: false, forceExpandHeight: true);

        label = AddTMPText(row, "Label", taskLabel, 20, Color.white, FontStyles.Normal, TextAlignmentOptions.Left);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        Button markDone = CreateButton(row.transform, "MarkDoneButton", "Mark Done", AccentGreen, 18);
        LayoutElement btnLe = markDone.gameObject.AddComponent<LayoutElement>();
        btnLe.preferredWidth = 120;
        btnLe.preferredHeight = 40;

        return markDone;
    }
}
