using UnityEngine;

/// <summary>
/// Bridges RosXRBridge's /xr/request_confirm event to the ConfirmationPopupUI, and the popup's
/// Confirm/Reject buttons back to /xr/confirm. Fully decoupled from RobotTaskManager — the ROS side
/// (xr_mode_manager.py) decides when and what to ask, independent of Unity's task loop.
/// </summary>
public class RosConfirmDialogHandler : MonoBehaviour
{
    public RosXRBridge rosBridge;
    public ConfirmationPopupUI popup;

    void OnEnable()
    {
        if (rosBridge != null)
            rosBridge.OnRequestConfirm += HandleRequestConfirm;
    }

    void OnDisable()
    {
        if (rosBridge != null)
            rosBridge.OnRequestConfirm -= HandleRequestConfirm;
    }

    void HandleRequestConfirm(string prompt)
    {
        if (popup == null || rosBridge == null) return;

        popup.Show(prompt,
            onConfirm: () => rosBridge.PublishConfirm(true),
            onReject: () => rosBridge.PublishConfirm(false));
    }
}
