using UnityEngine;
using RosMessageTypes.Std;

/// <summary>
/// CAAA Decision Engine (paper Section III-E) — the single authority that turns the continuous risk score
/// R(t) (GhostSurfaceRiskTest.Risk01) and cognitive score C(t) (CognitiveLoadDataStreamer.CognitiveScore01)
/// into one of four discrete autonomy modes (Human-Led / Shared / Robot-Led / Safety) via a fixed,
/// deterministic Low/Medium/High x Low/Medium/High lookup (Table IV), plus the continuous authority-blending
/// parameter lambda(t) (Eq. 5) consumed by the ROS-side Adaptive Autonomy Controller while in Shared mode.
///
/// Bin boundaries: R(t)'s own zone table (Table II) wasn't available at implementation time, so riskLowMax/
/// riskHighMin currently mirror C(t)'s published Table IX cut points (0.40 / 0.70) since both scores are
/// normalized to the same [0,1] range — the closest available reference. Update riskLowMax/riskHighMin here
/// if Table II turns out to specify different boundaries.
///
/// Table IV itself is taken from the paper's prose walkthrough of the logic, not the table graphic — the two
/// disagreed on two cells (Low-risk/High-cognitive and Medium-risk/High-cognitive) and the prose is the
/// unambiguous source. Worth a manual cross-check against the actual paper figure.
///
/// Mode transitions are debounced asymmetrically per the paper's "revoke autonomy quickly, grant it
/// conservatively" trust policy: downward transitions (more robot autonomy -> less) commit almost
/// immediately (debounceDownSeconds), upward transitions (less -> more) must stay strictly better than the
/// currently committed mode for debounceUpSeconds before committing — not necessarily the same exact target
/// the whole time, since R(t)/C(t) naturally flicker across bin boundaries (e.g. Robot-Led <-> Shared as
/// risk hovers near a cut point); only a dip back to the current mode or worse resets the sustain timer.
/// Evaluated once per decisionEpochSeconds, not every frame, matching the paper's "fixed decision epoch"
/// framing.
///
/// Safety pre-empts everything else instantly and unconditionally, exactly as specified — both when the
/// matrix itself lands on (High risk, High cognitive load), and via a manual latch (ForceSafety/
/// ClearSafetyLatch) so ControlPanelUI's E-Stop button can hold the system in Safety until an explicit Reset
/// clears it. Note: the paper explicitly distinguishes this software Safety mode (only auto-triggered by the
/// joint High/High condition) from a "hardware-level emergency stop," which it treats as outside/above this
/// logic entirely — wiring the E-Stop into this latch is a pragmatic choice to keep the panel's displayed
/// mode consistent with what the hardware E-Stop already does, not something the paper itself specifies.
///
/// Non-Safety commits additionally wait for taskManager (RobotTaskManager) to be between pick-and-place
/// cycles (IsCycleActive == false) before applying — the debounce timers keep counting underneath while a
/// cycle is in flight, so a held transition applies the instant the cycle ends rather than needing a fresh
/// debounce window afterward. This is a stricter reading than the paper's own "deferred ... where risk
/// permits" wording (which allows skipping the wait when risk is severe) — here every non-Safety transition
/// waits, full stop; Safety itself is the only escape hatch during an active cycle.
/// </summary>
public class DecisionEngine : MonoBehaviour
{
    public enum Mode { HumanLed = 0, Shared = 1, RobotLed = 2, Safety = 3 }
    enum Bin { Low, Medium, High }

    [Header("Inputs (auto-found in scene if left empty)")]
    public GhostSurfaceRiskTest riskSource;
    public CognitiveLoadDataStreamer cognitiveSource;
    public SystemStatusUI statusUI;
    public RosXRBridge rosBridge;

    [Header("Task cycle awareness (auto-found if left empty)")]
    [Tooltip("While taskManager.IsCycleActive is true, no non-Safety mode commit happens — the debounce " +
        "timer keeps running underneath, so the held transition applies the instant the current pick-and-" +
        "place cycle ends, without waiting for a fresh debounce window.")]
    public RobotTaskManager taskManager;

    [Header("R(t) bins — Table II not available; mirrors C(t)'s Table IX bins (both scores are [0,1])")]
    public float riskLowMax = 0.40f;
    public float riskHighMin = 0.70f;

    [Header("C(t) bins (Table IX)")]
    public float cogLowMax = 0.40f;
    public float cogHighMin = 0.70f;

    [Header("Decision epoch")]
    public float decisionEpochSeconds = 0.2f;

    [Header("Transition stability")]
    [Tooltip("Seconds a higher-autonomy target (e.g. Shared -> Robot-Led) must be sustained before committing.")]
    public float debounceUpSeconds = 2.0f;
    [Tooltip("Seconds a lower-autonomy target must be sustained before committing — short, just rejects single-epoch noise.")]
    public float debounceDownSeconds = 0.2f;

    [Header("Authority blending lambda(t) = betaR*R(t) + betaC*C(t) (Eq. 5), Shared mode only")]
    [Tooltip("Weight on risk R(t) — kept higher than betaC per the paper's safety-over-cognition priority.")]
    public float betaR = 0.7f;
    public float betaC = 0.3f;

    public Mode CurrentMode { get; private set; } = Mode.RobotLed;
    public float AuthorityLambda01 { get; private set; }

    // Row = risk bin, column = cognitive bin. See class doc comment re: Table IV vs. prose discrepancy.
    static readonly Mode[,] ModeMatrix =
    {
        { Mode.RobotLed, Mode.Shared,   Mode.HumanLed }, // risk Low
        { Mode.RobotLed, Mode.Shared,   Mode.Shared   }, // risk Medium
        { Mode.Shared,   Mode.HumanLed, Mode.Safety   }, // risk High
    };

    bool _safetyLatched;
    Mode _candidateMode;
    float _upTimer;
    float _downTimer;
    float _epochTimer;
    bool _ready;

    void Start()
    {
        if (!riskSource) riskSource = FindObjectOfType<GhostSurfaceRiskTest>();
        if (!cognitiveSource) cognitiveSource = FindObjectOfType<CognitiveLoadDataStreamer>();
        if (!statusUI) statusUI = FindObjectOfType<SystemStatusUI>();
        if (!rosBridge) rosBridge = FindObjectOfType<RosXRBridge>();
        if (!taskManager) taskManager = FindObjectOfType<RobotTaskManager>();

        _candidateMode = CurrentMode;

        _ready = riskSource != null && cognitiveSource != null;
        if (!_ready)
            Debug.LogWarning("[DecisionEngine] Missing GhostSurfaceRiskTest and/or CognitiveLoadDataStreamer in scene — mode will not update live.");
    }

    void Update()
    {
        if (!_ready) return;

        _epochTimer += Time.deltaTime;
        if (_epochTimer < decisionEpochSeconds) return;
        _epochTimer = 0f;

        RunDecisionEpoch();
    }

    void RunDecisionEpoch()
    {
        Mode rawMode = _safetyLatched ? Mode.Safety : EvaluateMatrix(riskSource.Risk01, cognitiveSource.CognitiveScore01);

        if (rawMode == Mode.Safety)
        {
            // Preempts instantly and unconditionally — no debounce, no exceptions.
            Commit(Mode.Safety);
        }
        else
        {
            int rawRank = Rank(rawMode);
            int curRank = Rank(CurrentMode);

            // Tracks "is the raw signal still an improvement/degradation over the committed mode", not
            // "is it this exact target" — R(t)/C(t) are continuous and naturally cross bin boundaries
            // (e.g. flickering Robot-Led <-> Shared as risk hovers near the Medium/High cut point), so
            // resetting the sustain timer on every such flicker meant a low-autonomy mode could hold
            // forever even once conditions genuinely improved, since a full unbroken window at one exact
            // target was rarely reached. Any raw mode that's still on the same side (still better / still
            // worse) keeps the timer running; the mode actually committed is whichever one is current when
            // the threshold is crossed.
            if (rawRank > curRank)
            {
                _upTimer += decisionEpochSeconds;
                _downTimer = 0f;
                _candidateMode = rawMode;
                if (_upTimer >= debounceUpSeconds && CanCommitNow())
                    Commit(_candidateMode);
            }
            else if (rawRank < curRank)
            {
                _downTimer += decisionEpochSeconds;
                _upTimer = 0f;
                _candidateMode = rawMode;
                if (_downTimer >= debounceDownSeconds && CanCommitNow())
                    Commit(_candidateMode);
            }
            else
            {
                _upTimer = 0f;
                _downTimer = 0f;
            }
        }

        UpdateAuthorityLambda();
    }

    /// <summary>Non-Safety commits wait for taskManager to be between pick-and-place cycles — Safety
    /// bypasses this check entirely (see RunDecisionEpoch), matching the paper's "cannot be delayed,
    /// skipped, or reasoned around" language for that mode specifically.</summary>
    bool CanCommitNow() => taskManager == null || !taskManager.IsCycleActive;

    static int Rank(Mode m) => m switch
    {
        Mode.Safety => -1,
        Mode.HumanLed => 0,
        Mode.Shared => 1,
        Mode.RobotLed => 2,
        _ => 0,
    };

    Mode EvaluateMatrix(float risk, float cognitive)
    {
        Bin r = Discretize(risk, riskLowMax, riskHighMin);
        Bin c = Discretize(cognitive, cogLowMax, cogHighMin);
        return ModeMatrix[(int)r, (int)c];
    }

    static Bin Discretize(float value, float lowMax, float highMin)
    {
        if (value < lowMax) return Bin.Low;
        if (value < highMin) return Bin.Medium;
        return Bin.High;
    }

    void Commit(Mode mode)
    {
        if (CurrentMode == mode) return;
        CurrentMode = mode;
        _candidateMode = mode;
        _upTimer = 0f;
        _downTimer = 0f;
        if (statusUI) statusUI.SetMode(ModeDisplayName(mode), (int)mode);
        Debug.Log($"[DecisionEngine] Mode -> {mode}");
    }

    void UpdateAuthorityLambda()
    {
        float lambda = CurrentMode switch
        {
            Mode.HumanLed => 0f,
            Mode.RobotLed => 1f,
            Mode.Safety => 0f,
            Mode.Shared => Mathf.Clamp01(betaR * riskSource.Risk01 + betaC * cognitiveSource.CognitiveScore01),
            _ => 0f,
        };
        AuthorityLambda01 = lambda;
        rosBridge?.PublishAuthorityLambda(lambda);
    }

    static string ModeDisplayName(Mode m) => m switch
    {
        Mode.HumanLed => "Human-Led",
        Mode.Shared => "Shared",
        Mode.RobotLed => "Robot-Led",
        Mode.Safety => "Safety",
        _ => "Shared",
    };

    /// <summary>Forces Safety regardless of live R(t)/C(t) — call from a manual E-Stop. Held until
    /// ClearSafetyLatch(); leaving Safety afterwards still goes through the normal upward debounce, since
    /// Safety ranks below every other mode.</summary>
    public void ForceSafety() => _safetyLatched = true;
    public void ClearSafetyLatch() => _safetyLatched = false;
}
