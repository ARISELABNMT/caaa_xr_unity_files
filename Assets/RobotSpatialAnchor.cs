using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;

public class RobotSpatialAnchor : MonoBehaviour
{
    [Header("Robot")]
    public Transform robotRoot;

    [Header("UI")]
    public TMP_Text statusText;

    private const string UUID_KEY = "RobotAnchorUUID";
    private OVRSpatialAnchor _anchor;
    private readonly List<ArticulationBody> _rootBodies = new();

    async void Start()
    {
        foreach (var body in FindObjectsOfType<ArticulationBody>())
            if (body.isRoot) _rootBodies.Add(body);

        string saved = PlayerPrefs.GetString(UUID_KEY, "");
        if (!string.IsNullOrEmpty(saved))
        {
            SetStatus("Loading saved robot position...");
            await LoadAnchor(new Guid(saved));
        }
        else
            SetStatus("No saved anchor.\nAlign robot manually then press B to save.");
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Two))
            _ = SaveAnchor();
    }

    async Task SaveAnchor()
    {
        SetStatus("Saving anchor...");

        if (_anchor != null) Destroy(_anchor.gameObject);

        var go = new GameObject("RobotPositionAnchor");
        go.transform.SetPositionAndRotation(robotRoot.position, robotRoot.rotation);
        _anchor = go.AddComponent<OVRSpatialAnchor>();

        while (!_anchor.Created)
            await Task.Delay(50);

        var result = await _anchor.SaveAnchorAsync();
        if (result.Success)
        {
            PlayerPrefs.SetString(UUID_KEY, _anchor.Uuid.ToString());
            PlayerPrefs.Save();
            SetStatus("Robot anchor saved!\nPosition will auto-restore next launch.");
            Debug.Log("Robot anchor saved: " + _anchor.Uuid);
        }
        else
        {
            SetStatus("Anchor save failed: " + result.Status);
            Debug.LogError("Anchor save failed: " + result.Status);
        }
    }

    async Task LoadAnchor(Guid uuid)
    {
        var options = new OVRSpatialAnchor.LoadOptions
        {
            StorageLocation = OVRSpace.StorageLocation.Local,
            Uuids = new Guid[] { uuid }
        };

        var anchors = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(options);

        if (anchors == null || anchors.Length == 0)
        {
            SetStatus("Saved anchor not found.\nAlign manually and press B.");
            Debug.LogWarning("Saved anchor not found.");
            return;
        }

        SetStatus("Anchor found. Localizing...");

        var go = new GameObject("RobotPositionAnchor");
        _anchor = go.AddComponent<OVRSpatialAnchor>();
        anchors[0].BindTo(_anchor);

        while (!_anchor.Localized)
            await Task.Delay(50);

        MoveRobotTo(go.transform);
        SetStatus("Robot position restored!");
        Debug.Log("Robot position restored from anchor!");
    }

    void MoveRobotTo(Transform t)
    {
        Vector3 delta = t.position - robotRoot.position;
        robotRoot.SetPositionAndRotation(t.position, t.rotation);
        foreach (var body in _rootBodies)
            body.TeleportRoot(body.transform.position + delta, body.transform.rotation);
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log("[RobotAnchor] " + message);
    }
}
