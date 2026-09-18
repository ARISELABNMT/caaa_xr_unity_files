using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Section VI-C logic verification — exhaustively checks all 9 (R,C) bin combinations against Table IV,
/// matching the paper's "inject all 9 (R, C) bin combinations ... and confirm the selected mode against
/// Table IV" protocol, but as an instant Editor check instead of a runtime debug scene: no build, no
/// device, no human involved, and it exercises DecisionEngine's actual private EvaluateMatrix via
/// reflection rather than a reimplementation of the table, so a genuine logic bug would actually be caught.
///
/// Table IV here is DecisionEngine's own doc-commented version (paper's prose walkthrough, not the table
/// graphic, which disagreed with the prose on two cells — see DecisionEngine.cs class doc comment).
/// </summary>
public static class DecisionEngineLogicCheck
{
    [MenuItem("XR Panels/Verify Decision Engine Logic (Table IV)")]
    public static void Run()
    {
        var go = new GameObject("DecisionEngineLogicCheck_TEMP");
        DecisionEngine engine = go.AddComponent<DecisionEngine>();

        MethodInfo evaluateMatrix = typeof(DecisionEngine).GetMethod("EvaluateMatrix", BindingFlags.NonPublic | BindingFlags.Instance);
        if (evaluateMatrix == null)
        {
            Debug.LogError("[DecisionEngineLogicCheck] Could not find EvaluateMatrix via reflection — DecisionEngine's internals may have changed since this check was written.");
            Object.DestroyImmediate(go);
            return;
        }

        // One representative value per bin, computed from the engine's own configured thresholds so this
        // stays correct even if riskLowMax/riskHighMin/cogLowMax/cogHighMin are retuned later (e.g. once
        // Table II's real values replace the current placeholder).
        float lowR = engine.riskLowMax * 0.5f;
        float medR = (engine.riskLowMax + engine.riskHighMin) * 0.5f;
        float highR = Mathf.Min(engine.riskHighMin + 0.1f, 1f);
        float lowC = engine.cogLowMax * 0.5f;
        float medC = (engine.cogLowMax + engine.cogHighMin) * 0.5f;
        float highC = Mathf.Min(engine.cogHighMin + 0.1f, 1f);

        var cases = new (float risk, float cog, DecisionEngine.Mode expected, string label)[]
        {
            (lowR,  lowC,  DecisionEngine.Mode.RobotLed, "Risk Low, Cog Low"),
            (lowR,  medC,  DecisionEngine.Mode.Shared,   "Risk Low, Cog Medium"),
            (lowR,  highC, DecisionEngine.Mode.HumanLed, "Risk Low, Cog High"),
            (medR,  lowC,  DecisionEngine.Mode.RobotLed, "Risk Medium, Cog Low"),
            (medR,  medC,  DecisionEngine.Mode.Shared,   "Risk Medium, Cog Medium"),
            (medR,  highC, DecisionEngine.Mode.Shared,   "Risk Medium, Cog High"),
            (highR, lowC,  DecisionEngine.Mode.Shared,   "Risk High, Cog Low"),
            (highR, medC,  DecisionEngine.Mode.HumanLed, "Risk High, Cog Medium"),
            (highR, highC, DecisionEngine.Mode.Safety,   "Risk High, Cog High"),
        };

        int passCount = 0;
        var log = new StringBuilder();
        log.AppendLine("[DecisionEngineLogicCheck] Table IV verification:");
        foreach (var c in cases)
        {
            var actual = (DecisionEngine.Mode)evaluateMatrix.Invoke(engine, new object[] { c.risk, c.cog });
            bool pass = actual == c.expected;
            if (pass) passCount++;
            log.AppendLine($"  [{(pass ? "PASS" : "FAIL")}] {c.label} (R={c.risk:F2}, C={c.cog:F2}) -> expected {c.expected}, got {actual}");
        }
        log.AppendLine($"Result: {passCount}/{cases.Length} passed.");

        if (passCount == cases.Length) Debug.Log(log.ToString());
        else Debug.LogError(log.ToString());

        Object.DestroyImmediate(go);
    }
}
