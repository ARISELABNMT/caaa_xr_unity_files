using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using static PanelBuilderUtils;

public static class BuildConfirmationPopup
{
    [MenuItem("XR Panels/Build Confirmation Popup")]
    public static void Build()
    {
        Transform xrRoot = FindOrCreate("XRPanelSystem", null).transform;

        if (!ConfirmAndClearExisting(xrRoot, "ConfirmationPopup")) return;

        GameObject panel = CreateCanvasPanel("ConfirmationPopup", xrRoot, 460, 260,
            new Vector3(0f, 1.1f, 1.68f), new Vector3(45f, 0f, 0f));

        GameObject background = CreateUIObject("Background", panel.transform);
        AddImage(background, new Color(0.10f, 0.10f, 0.18f, 0.95f));
        StretchFull(background.GetComponent<RectTransform>());
        AddVerticalLayout(background, 12, 16, forceExpandHeight: true);

        TMP_Text stepText = AddTMPText(background, "StepText", "Waiting for robot task...", 24, Color.white, FontStyles.Normal, TextAlignmentOptions.Center);
        stepText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

        GameObject buttonRow = CreateUIObject("ButtonRow", background.transform);
        buttonRow.AddComponent<LayoutElement>().preferredHeight = 70;
        AddHorizontalLayout(buttonRow, 12, 0, forceExpandWidth: true, forceExpandHeight: true);
        Button confirmButton = CreateButton(buttonRow.transform, "ConfirmButton", "Confirm", AccentGreen, 22);
        Button rejectButton = CreateButton(buttonRow.transform, "RejectButton", "Reject", EStopRed, 22);

        ConfirmationPopupUI ui = panel.AddComponent<ConfirmationPopupUI>();
        // root is the Background child, not the panel itself: Hide()/Show() must only toggle the
        // visual content. Deactivating the panel GameObject would also disable RosConfirmDialogHandler
        // (which lives on this same object) via OnDisable(), permanently unsubscribing it from
        // rosBridge.OnRequestConfirm the very first time the popup hid itself.
        ui.root = background;
        ui.stepText = stepText;
        ui.confirmButton = confirmButton;
        ui.rejectButton = rejectButton;

        RosXRBridge rosBridge = Object.FindObjectOfType<RosXRBridge>();
        RosConfirmDialogHandler dialogHandler = panel.AddComponent<RosConfirmDialogHandler>();
        dialogHandler.popup = ui;
        dialogHandler.rosBridge = rosBridge;
        if (rosBridge == null)
            Debug.LogWarning("Confirmation Popup: no RosXRBridge found in scene yet — build it first (or use Build All Panels) so /xr/request_confirm can reach this popup.");

        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(panel.scene);
        Debug.Log("Confirmation Popup built under XRPanelSystem/ConfirmationPopup.");
    }
}
