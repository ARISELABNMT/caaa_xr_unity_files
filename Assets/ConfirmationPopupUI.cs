using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Generic Confirm/Reject popup. Every prompt now comes from the ROS side (/xr/request_confirm) via
/// RosConfirmDialogHandler — the step context (e.g. "Confirm pick from Bin A?") is entirely conveyed
/// by the message text, so the two buttons stay fixed as "Confirm"/"Reject".
/// </summary>
public class ConfirmationPopupUI : MonoBehaviour
{
    public GameObject root;
    public TMP_Text stepText;
    public Button confirmButton;
    public Button rejectButton;

    private Action _onConfirm;
    private Action _onReject;

    void Start()
    {
        confirmButton.onClick.AddListener(HandleConfirm);
        rejectButton.onClick.AddListener(HandleReject);
        Hide();
    }

    public void Show(string message, Action onConfirm, Action onReject)
    {
        if (root) root.SetActive(true);
        if (stepText) stepText.text = message;
        _onConfirm = onConfirm;
        _onReject = onReject;
    }

    public void Hide()
    {
        if (root) root.SetActive(false);
        _onConfirm = null;
        _onReject = null;
    }

    void HandleConfirm()
    {
        Action callback = _onConfirm;
        Hide();
        callback?.Invoke();
    }

    void HandleReject()
    {
        Action callback = _onReject;
        Hide();
        callback?.Invoke();
    }
}
