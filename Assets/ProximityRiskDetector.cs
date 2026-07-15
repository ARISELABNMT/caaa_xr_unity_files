using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class ProximityRiskDetector : MonoBehaviour
{
    [Header("Robot Links")]
    public List<Transform> robotLinks = new List<Transform>();

    [Header("Risk Settings")]
    public float safeDistance = 1.0f;
    public float dangerDistance = 0.3f;

    [Header("UI")]
    public TMP_Text riskText;
    public UnityEngine.UI.Image riskPanel;

    private List<Transform> bodyParts = new List<Transform>();
    private bool bodyPartsFound = false;

    void Update()
    {
        if (!bodyPartsFound)
            FindBodyParts();

        if (bodyParts.Count == 0 || robotLinks.Count == 0)
            return;

        float risk = CalculateRisk();
        UpdateUI(risk);
    }

    void FindBodyParts()
    {
        bodyParts.Clear();

        string[] bodyPartNames = new string[]
        {
            "Joint Head",
            "Joint Chest",
            "Joint LeftHandWrist",
            "Joint RightHandWrist"
        };

        foreach (string partName in bodyPartNames)
        {
            GameObject obj = GameObject.Find(partName);
            if (obj != null)
            {
                bodyParts.Add(obj.transform);
                Debug.Log("Found body part: " + partName);
            }
        }

        if (bodyParts.Count > 0)
        {
            bodyPartsFound = true;
            Debug.Log("All body parts found: " + bodyParts.Count);
        }
    }

    float CalculateRisk()
    {
        float minDistance = float.MaxValue;

        foreach (Transform link in robotLinks)
        {
            if (link == null) continue;
            foreach (Transform body in bodyParts)
            {
                if (body == null) continue;
                float dist = Vector3.Distance(
                    link.position, body.position);
                if (dist < minDistance)
                    minDistance = dist;
            }
        }

        if (minDistance == float.MaxValue) return 0f;
        if (minDistance >= safeDistance) return 0f;
        if (minDistance <= dangerDistance) return 1f;

        return 1f - ((minDistance - dangerDistance) /
                     (safeDistance - dangerDistance));
    }

    void UpdateUI(float risk)
    {
        if (riskText != null)
            riskText.text = "Actual Risk: " + (risk * 100f).ToString("F0") + "%";

        if (riskPanel != null)
            riskPanel.color = Color.Lerp(Color.green, Color.red, risk);
    }
}