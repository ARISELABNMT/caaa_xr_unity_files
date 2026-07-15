using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using TMPro;

public class TrajectoryCloud : MonoBehaviour
{
    [Header("Ghost Robot Links to Sample")]
    public List<Transform> ghostLinks;

    [Header("Visualization")]
    public ParticleSystem cloudParticles;
    public Color cloudColor = new Color(1f, 0.5f, 0f, 0.4f);
    public float particleSize = 0.04f;
    public int sampleEveryNFrames = 3;

    [Header("Risk")]
    public TMP_Text riskText;
    public UnityEngine.UI.Image riskPanel;
    public float safeDistance = 1.0f;
    public float dangerDistance = 0.3f;

    private List<Vector3> cloudPoints = new List<Vector3>();
    private List<Transform> bodyParts = new List<Transform>();
    private bool isRecording = false;
    private float recordDuration = 0f;
    private float recordTimer = 0f;
    private int frameCounter = 0;
    private float riskCheckTimer = 0f;

    private readonly Color safeColor    = new Color(0f,  0.8f, 0f,  0.7f);
    private readonly Color warningColor = new Color(1f,  0.9f, 0f,  0.7f);
    private readonly Color dangerColor  = new Color(1f,  0f,   0f,  0.7f);

    void Start()
    {
        FindBodyParts();
        StartCoroutine(Subscribe());
    }

    void FindBodyParts()
    {
        string[] names = { "CenterEyeAnchor", "LeftHandAnchor", "RightHandAnchor",
                           "LeftHandAnchorDetached", "RightHandAnchorDetached" };
        foreach (string n in names)
        {
            var go = GameObject.Find(n);
            if (go != null) bodyParts.Add(go.transform);
        }
    }

    IEnumerator Subscribe()
    {
        yield return new WaitForSeconds(3.5f);
        ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>("/unity_planned_trajectory", OnTrajectory);
    }

    void OnTrajectory(StringMsg msg)
    {
        var data = JsonUtility.FromJson<TrajectoryData>(msg.data);
        if (data?.points == null || data.points.Length == 0) return;

        var last = data.points[data.points.Length - 1];
        recordDuration = last.time_sec + last.time_nanosec * 1e-9f + 0.5f;

        cloudPoints.Clear();
        isRecording = true;
        recordTimer = 0f;
        frameCounter = 0;
    }

    void LateUpdate()
    {
        if (!isRecording) return;

        recordTimer += Time.deltaTime;
        frameCounter++;

        if (frameCounter % sampleEveryNFrames == 0)
            foreach (var link in ghostLinks)
                if (link != null) cloudPoints.Add(link.position);

        if (recordTimer >= recordDuration)
        {
            isRecording = false;
            RebuildParticles();
        }
    }

    void RebuildParticles()
    {
        if (cloudParticles == null || cloudPoints.Count == 0) return;
        var particles = new ParticleSystem.Particle[cloudPoints.Count];
        for (int i = 0; i < cloudPoints.Count; i++)
        {
            particles[i].position    = cloudPoints[i];
            particles[i].startSize   = particleSize;
            particles[i].startColor  = cloudColor;
            particles[i].remainingLifetime = float.MaxValue;
        }
        cloudParticles.SetParticles(particles, particles.Length);
    }

    void Update()
    {
        if (cloudPoints.Count == 0) return;

        riskCheckTimer += Time.deltaTime;
        if (riskCheckTimer < 0.1f) return;
        riskCheckTimer = 0f;

        float minDist = float.MaxValue;
        foreach (var bp in bodyParts)
        {
            if (bp == null) continue;
            foreach (var pt in cloudPoints)
            {
                float d = Vector3.Distance(bp.position, pt);
                if (d < minDist) minDist = d;
            }
        }

        float risk = 0f;
        if (minDist <= dangerDistance) risk = 100f;
        else if (minDist < safeDistance)
            risk = (1f - (minDist - dangerDistance) / (safeDistance - dangerDistance)) * 100f;

        UpdateUI(risk, minDist);
    }

    void UpdateUI(float risk, float minDist)
    {
        if (riskText != null)
            riskText.text = string.Format("Swept Risk: {0:F0}%\nDist: {1:F2}m", risk, minDist);

        if (riskPanel != null)
        {
            if (risk >= 70f)      riskPanel.color = dangerColor;
            else if (risk >= 30f) riskPanel.color = warningColor;
            else                  riskPanel.color = safeColor;
        }
    }

    void OnDisable()
    {
        if (riskText != null)    riskText.text = "Swept Risk: --";
        if (cloudParticles != null) cloudParticles.Clear();
    }

    [System.Serializable] class TrajectoryData  { public string[] joint_names; public TrajectoryPoint[] points; }
    [System.Serializable] class TrajectoryPoint { public float[] positions; public int time_sec; public int time_nanosec; }
}

