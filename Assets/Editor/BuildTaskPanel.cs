using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using static PanelBuilderUtils;

public static class BuildTaskPanel
{
    [MenuItem("XR Panels/Build Task Panel")]
    public static void Build()
    {
        Transform xrRoot = FindOrCreate("XRPanelSystem", null).transform;
        Transform backPanels = FindOrCreate("BackPanels", xrRoot).transform;

        if (!ConfirmAndClearExisting(backPanels, "TaskPanel")) return;

        GameObject panel = CreateCanvasPanel("TaskPanel", backPanels, 500, 800,
            new Vector3(-0.4f, 1.5f, 3f), Vector3.zero, 0.0013f);

        // --- Background ---
        GameObject background = CreateUIObject("Background", panel.transform);
        AddImage(background, BgDark);
        StretchFull(background.GetComponent<RectTransform>());
        AddVerticalLayout(background, 8, 10);

        // --- Header ---
        GameObject header = CreateSection("Header", background.transform, BgHeader, 60);
        AddTMPText(header, "HeaderText", "TASK PANEL", 28, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

        // --- Current Kit Section ---
        GameObject currentKit = CreateSection("CurrentKitSection", background.transform, BgSection, 80);
        AddVerticalLayout(currentKit, 4, 8);
        AddTMPText(currentKit, "KitLabel", "CURRENT KIT — KIT A", 22, Color.white, FontStyles.Bold, TextAlignmentOptions.Left);
        AddTMPText(currentKit, "AutonomyBadge", "ROBOT AUTONOMY-CONTROLLED", 18, AccentBlue, FontStyles.Normal, TextAlignmentOptions.Left);

        // --- Robot Tasks Section ---
        GameObject robotTasks = CreateSection("RobotTasksSection", background.transform, BgSection, 120);
        AddVerticalLayout(robotTasks, 6, 8);
        AddTMPText(robotTasks, "SectionHeader", "ROBOT", 18, AccentBlue, FontStyles.Bold, TextAlignmentOptions.Left);
        CreateTaskRow(robotTasks.transform, "BoltRow", "Bolt", "0/2", out TMP_Text boltName, out TMP_Text boltCount);
        CreateTaskRow(robotTasks.transform, "NutRow", "Nut", "0/1", out TMP_Text nutName, out TMP_Text nutCount);

        // --- Human Tasks Section ---
        GameObject humanTasks = CreateSection("HumanTasksSection", background.transform, BgSection, 180);
        AddVerticalLayout(humanTasks, 6, 8);
        AddTMPText(humanTasks, "SectionHeader", "HUMAN", 18, AccentOrange, FontStyles.Bold, TextAlignmentOptions.Left);
        CreateHumanTaskRow(humanTasks.transform, "WasherRow", "Washer  0/1", "Bin C → Kitting Box",
            out Toggle washerToggle, out TMP_Text washerText, out TMP_Text washerRoute, out GameObject washerRow);
        CreateHumanTaskRow(humanTasks.transform, "SpacerRow", "Spacer  0/1", "Bin D → Kitting Box",
            out Toggle spacerToggle, out TMP_Text spacerText, out TMP_Text spacerRoute, out GameObject spacerRow);

        // --- Progress Section ---
        GameObject progress = CreateSection("ProgressSection", background.transform, BgSection, 70);
        AddVerticalLayout(progress, 4, 8);
        TMP_Text progressText = AddTMPText(progress, "ProgressLabel", "KIT PROGRESS 0%", 20, Color.white, FontStyles.Normal, TextAlignmentOptions.Left);
        Slider progressSlider = CreateSlider(progress.transform, "ProgressBar", SliderBg, AccentGreen);

        // --- Next Item Section ---
        GameObject nextItem = CreateSection("NextItemSection", background.transform, BgHeader, 100);
        AddVerticalLayout(nextItem, 4, 8);
        AddTMPText(nextItem, "NextItemLabel", "ROBOT NEXT ITEM", 18, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left);
        TMP_Text nextItemName = AddTMPText(nextItem, "NextItemName", "Bolt", 26, Color.white, FontStyles.Bold, TextAlignmentOptions.Left);
        TMP_Text nextItemRoute = AddTMPText(nextItem, "NextItemRoute", "Bin A → Kitting Box", 18, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left);

        // --- Kit Complete Status ---
        GameObject kitCompleteSection = CreateSection("KitCompleteSection", background.transform, BgSection, 50);
        TMP_Text kitCompleteText = AddTMPText(kitCompleteSection, "KitCompleteText", "IN PROGRESS", 22, MutedGrey, FontStyles.Bold, TextAlignmentOptions.Center);

        // --- Wire up TaskPanelUI ---
        TaskPanelUI ui = panel.AddComponent<TaskPanelUI>();
        ui.currentKitText = FindTMP(currentKit.transform, "KitLabel");
        ui.kitProgressText = progressText;
        ui.kitProgressBar = progressSlider;
        ui.robotTask1Text = boltName;
        ui.robotTask1Count = boltCount;
        ui.robotTask2Text = nutName;
        ui.robotTask2Count = nutCount;
        ui.humanTask1Toggle = washerToggle;
        ui.humanTask1Text = washerText;
        ui.humanTask1RouteText = washerRoute;
        ui.humanTask2Toggle = spacerToggle;
        ui.humanTask2Text = spacerText;
        ui.humanTask2RouteText = spacerRoute;
        ui.humanTask2Row = spacerRow;
        ui.nextItemText = nextItemName;
        ui.nextItemRoute = nextItemRoute;
        ui.kitCompleteText = kitCompleteText;

        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(panel.scene);
        Debug.Log("Task Panel built successfully under XRPanelSystem/BackPanels/TaskPanel.");
    }

    private static void CreateTaskRow(Transform parent, string rowName, string taskName, string countText,
        out TMP_Text nameText, out TMP_Text countTextOut)
    {
        GameObject row = CreateUIObject(rowName, parent);
        row.AddComponent<LayoutElement>().preferredHeight = 35;
        AddHorizontalLayout(row, 0, 0, forceExpandWidth: false, forceExpandHeight: true);

        nameText = AddTMPText(row, "TaskName", taskName, 22, Color.white, FontStyles.Normal, TextAlignmentOptions.Left);
        nameText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        countTextOut = AddTMPText(row, "TaskCount", countText, 22, AccentBlue, FontStyles.Normal, TextAlignmentOptions.Right);
        countTextOut.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
    }

    private static void CreateHumanTaskRow(Transform parent, string rowName, string labelText, string routeText,
        out Toggle toggle, out TMP_Text label, out TMP_Text route, out GameObject rowContainer)
    {
        rowContainer = CreateUIObject(rowName, parent);
        rowContainer.AddComponent<LayoutElement>().preferredHeight = 60;
        AddVerticalLayout(rowContainer, 2, 0);

        GameObject topRow = CreateUIObject("TopRow", rowContainer.transform);
        topRow.AddComponent<LayoutElement>().preferredHeight = 36;
        AddHorizontalLayout(topRow, 6, 0, forceExpandWidth: false, forceExpandHeight: true);

        toggle = CreateToggle(topRow.transform, "Toggle", AccentGreen);

        label = AddTMPText(topRow, "TaskName", labelText, 20, Color.white, FontStyles.Normal, TextAlignmentOptions.Left);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        route = AddTMPText(rowContainer, "RouteText", routeText, 14, MutedGrey, FontStyles.Normal, TextAlignmentOptions.Left);
    }
}
