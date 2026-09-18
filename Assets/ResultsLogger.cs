using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes one CSV per kitting run for the paper's Results section — file lifecycle is bound to
/// RobotTaskManager.onKittingStarted/onKittingComplete, so each file's own time range IS exactly one
/// complete kitting task's duration (Fig. 19's "representative episode", but the whole real run instead of
/// an arbitrary clock window). Also flushes a small separate latency-summary file per run (Table XV) —
/// kept out of the main per-row CSV since GhostSurfaceRiskTest's latency stats are sampled every frame,
/// not at this logger's decision-epoch-rate sampling interval.
///
/// Set conditionLabel before pressing Start (e.g. "Low"/"Medium"/"High" induced-distraction condition) to
/// tag the run for later Table XVI aggregation across multiple files.
///
/// Files land in Application.persistentDataPath/ResultsLogs/ — pull with:
///   adb pull /sdcard/Android/data/&lt;package&gt;/files/ResultsLogs/ .
/// </summary>
public class ResultsLogger : MonoBehaviour
{
    [Header("Sources (auto-found if left empty)")]
    public GhostSurfaceRiskTest riskTest;
    public CognitiveLoadDataStreamer cognitiveSource;
    public DecisionEngine decisionEngine;
    public RobotTaskManager taskManager;
    public DigitalHumanTracker humanTracker;

    [Header("Trial tagging")]
    [Tooltip("Set this before pressing Start — e.g. \"Low\"/\"Medium\"/\"High\" induced-distraction " +
        "condition (Table XVI), or any label useful for grouping runs later.")]
    public string conditionLabel = "";

    [Header("Sampling")]
    [Tooltip("Matches DecisionEngine's default decision epoch — not wired to it directly, keep in sync " +
        "manually if you change either.")]
    public float sampleIntervalSeconds = 0.2f;

    string _saveDir;
    StreamWriter _writer;
    float _sampleTimer;
    float _tSec;
    string _runKitName;
    bool _logging;

    void Start()
    {
        if (!riskTest) riskTest = FindObjectOfType<GhostSurfaceRiskTest>();
        if (!cognitiveSource) cognitiveSource = FindObjectOfType<CognitiveLoadDataStreamer>();
        if (!decisionEngine) decisionEngine = FindObjectOfType<DecisionEngine>();
        if (!taskManager) taskManager = FindObjectOfType<RobotTaskManager>();
        if (!humanTracker) humanTracker = FindObjectOfType<DigitalHumanTracker>();

        _saveDir = Path.Combine(Application.persistentDataPath, "ResultsLogs");
        Directory.CreateDirectory(_saveDir);

        if (taskManager == null)
        {
            Debug.LogWarning("[ResultsLogger] No RobotTaskManager found — logging will never start.");
            return;
        }

        taskManager.onKittingStarted += HandleKittingStarted;
        taskManager.onKittingComplete += HandleKittingComplete;
        Debug.Log($"[ResultsLogger] Ready. Will log each kitting run to {_saveDir}");
    }

    void OnDestroy()
    {
        if (taskManager != null)
        {
            taskManager.onKittingStarted -= HandleKittingStarted;
            taskManager.onKittingComplete -= HandleKittingComplete;
        }
        CloseWriter();
    }

    void HandleKittingStarted()
    {
        if (_logging) CloseWriter(); // shouldn't happen, but don't leak a handle if it does

        _tSec = 0f;
        _sampleTimer = 0f;
        _runKitName = taskManager.CurrentKitName;

        string fileName = $"kit_{Sanitize(conditionLabel, "unlabeled")}_{Sanitize(_runKitName, "kit")}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(_saveDir, fileName);

        _writer = new StreamWriter(path, false, Encoding.UTF8);
        _writer.WriteLine("t_sec,risk_R,prob_P,impact_I,severity_S,sprot_m,cognitive_C,mode,lambda,min_distance_m,nearest_body_part,human_tracking,kit_name,task_item,condition_label");

        riskTest?.ResetLatencyStats();

        _logging = true;
        Debug.Log($"[ResultsLogger] Started logging to {path}");
    }

    void HandleKittingComplete(bool success)
    {
        if (!_logging) return;

        WriteLatencySummary(success);
        CloseWriter();
        Debug.Log($"[ResultsLogger] Finished logging kit run (success={success}).");
    }

    void CloseWriter()
    {
        _logging = false;
        _writer?.Flush();
        _writer?.Close();
        _writer = null;
    }

    void WriteLatencySummary(bool success)
    {
        if (!riskTest) return;
        var (meanMs, maxMs, stdMs, count) = riskTest.GetLatencyStatsMs();

        string fileName = $"latency_{Sanitize(conditionLabel, "unlabeled")}_{Sanitize(_runKitName, "kit")}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(_saveDir, fileName);

        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("mean_ms,max_ms,std_ms,sample_count,kit_name,condition_label,kit_success");
        w.WriteLine($"{meanMs:F4},{maxMs:F4},{stdMs:F4},{count},{_runKitName},{conditionLabel},{success}");
    }

    void Update()
    {
        if (!_logging || _writer == null) return;

        _sampleTimer += Time.deltaTime;
        _tSec += Time.deltaTime;
        if (_sampleTimer < sampleIntervalSeconds) return;
        _sampleTimer = 0f;

        WriteRow();
    }

    void WriteRow()
    {
        float R = riskTest ? riskTest.Risk01 : 0f;
        float P = riskTest ? riskTest.Probability01 : 0f;
        float I = riskTest ? riskTest.Impact01 : 0f;
        float S = riskTest ? riskTest.Severity01 : 0f;
        float sprot = riskTest ? riskTest.ProtectiveSeparationMeters : 0f;
        float C = cognitiveSource ? cognitiveSource.CognitiveScore01 : 0f;
        string mode = decisionEngine ? decisionEngine.CurrentMode.ToString() : "";
        float lambda = decisionEngine ? decisionEngine.AuthorityLambda01 : 0f;
        float dist = riskTest ? riskTest.MinDistance : -1f;
        string bodyPart = riskTest ? riskTest.NearestSegmentName : "";
        bool tracking = humanTracker && humanTracker.IsTracking;
        string taskItem = taskManager ? taskManager.CurrentTaskItemName : "";

        _writer.WriteLine($"{_tSec:F3},{R:F4},{P:F4},{I:F4},{S:F2},{sprot:F4},{C:F4},{mode},{lambda:F4},{dist:F4},{bodyPart},{tracking},{_runKitName},{taskItem},{conditionLabel}");
    }

    static string Sanitize(string s, string fallback) =>
        string.IsNullOrEmpty(s) ? fallback : s.Replace(" ", "").Replace("/", "-");
}
