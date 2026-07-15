
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class QuestProximityRisk : MonoBehaviour
{
    public List<Transform> robotLinks = new List<Transform>();
    public float safeDistance = 1.0f;
    public float dangerDistance = 0.3f;
    public TMP_Text riskText;
    public UnityEngine.UI.Image riskPanel;

    private List<Transform> bodyParts = new List<Transform>();
    private bool bodyPartsFound = false;

    void Start() { FindBodyParts(); }

    void FindBodyParts()
    {
        bodyParts.Clear();
        string[] anchors = { "CenterEyeAnchor", "LeftHandAnchor", "RightHandAnchor",
                              "LeftHandAnchorDetached", "RightHandAnchorDetached" };
        foreach (string n in anchors)
        {
            GameObject go = GameObject.Find(n);
            if (go != null) bodyParts.Add(go.transform);
        }
        bodyPartsFound = bodyParts.Count > 0;
    }

    void Update()
    {
        if (!bodyPartsFound) { FindBodyParts(); return; }
        float risk = CalculateRisk();
        UpdateUI(risk);
    }

    float CalculateRisk()
    {
        float minDist = float.MaxValue;
        foreach (Transform link in robotLinks)
            foreach (Transform part in bodyParts)
            {
                float d = Vector3.Distance(link.position, part.position);
                if (d < minDist) minDist = d;
            }
        if (minDist == float.MaxValue) return 0f;
        if (minDist <= dangerDistance) return 100f;
        if (minDist >= safeDistance) return 0f;
        return (1f - (minDist - dangerDistance) / (safeDistance - dangerDistance)) * 100f;
    }

    void UpdateUI(float risk)
    {
        if (riskText) riskText.text = "Actual Risk: " + Mathf.RoundToInt(risk) + "%";
        if (riskPanel)
        {
            riskPanel.color = Color.Lerp(Color.green, Color.red, risk / 100f);
        }
    }
}
