"""
Turns ResultsLogger's per-kit CSVs (Assets/ResultsLogger.cs) into the plots and tables for the CAAA paper's
Results section.

Input: a folder containing kit_*.csv (one row per ~0.2s decision epoch, spanning exactly one full kitting
run) and latency_*.csv (one row per run, Table XV stats) files pulled from the headset via:
    adb pull /sdcard/Android/data/<package>/files/ResultsLogs/ .

Usage:
    python plot_results.py <results_logs_folder> [--out <output_folder>]

Produces, per kit_*.csv:
    <name>_fig19.pdf/.png        - R(t)/C(t) with a mode-colored strip beneath (paper's Fig. 19 style)
    <name>_risk_breakdown.pdf/.png - distance+S_prot / P,I,S / R(t)+bands, mode-colored strip, Safety-trigger
                                      marker, nearest-body-part change labels

And once, aggregated across every kit_*.csv found:
    table_xv.tex   - R(t) computation latency (mean/max/std), from latency_*.csv
    table_xvi.tex  - mode-time % by condition_label
    table_xvii.tex - completion time by dominant mode, min distance, Safety activation appropriate/false-positive
"""

import argparse
import glob
import os

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

plt.rcParams.update({
    "font.family": "serif",
    "mathtext.fontset": "cm",
    "axes.grid": True,
    "grid.alpha": 0.3,
    "figure.dpi": 150,
})

MODE_COLORS = {
    "RobotLed": "#1f77b4",  # blue
    "Shared": "#2ca02c",    # green
    "HumanLed": "#ff7f0e",  # orange
    "Safety": "#d62728",    # red
}
MODE_LABELS = {
    "RobotLed": "Robot-Led",
    "Shared": "Shared",
    "HumanLed": "Human-Led",
    "Safety": "Safety",
}
# Mirrors DecisionEngine's riskLowMax/riskHighMin defaults — update here if those are retuned.
RISK_LOW_MAX = 0.40
RISK_HIGH_MIN = 0.70


def load_kit_csv(path):
    df = pd.read_csv(path)
    df["mode"] = df["mode"].fillna("")
    return df


def mode_segments(df):
    """Collapses the per-row mode column into contiguous (start_t, duration, mode) runs for broken_barh."""
    segments = []
    if df.empty:
        return segments
    start_t = df["t_sec"].iloc[0]
    current_mode = df["mode"].iloc[0]
    for i in range(1, len(df)):
        if df["mode"].iloc[i] != current_mode:
            segments.append((start_t, df["t_sec"].iloc[i] - start_t, current_mode))
            start_t = df["t_sec"].iloc[i]
            current_mode = df["mode"].iloc[i]
    segments.append((start_t, df["t_sec"].iloc[-1] - start_t + 0.2, current_mode))
    return segments


def draw_mode_strip(ax, df, height=1.0):
    segs = mode_segments(df)
    bars = [(s, d) for s, d, _ in segs]
    colors = [MODE_COLORS.get(m, "#999999") for _, _, m in segs]
    ax.broken_barh(bars, (0, height), facecolors=colors)
    ax.set_ylim(0, height)
    ax.set_yticks([])
    ax.set_xlim(df["t_sec"].iloc[0], df["t_sec"].iloc[-1])


FRIENDLY_BODY_PART = {
    "L_UpperArm": "Upper Arm", "R_UpperArm": "Upper Arm",
    "L_Forearm": "Forearm", "R_Forearm": "Forearm",
    "L_Hand": "Hand", "R_Hand": "Hand",
    "Neck_Head": "Head", "Chest": "Chest",
}


def body_part_change_labels(df, min_segment_seconds=2.0):
    """Timestamps + labels where the *friendly* nearest-body-part category changes (mirrors
    GhostSurfaceRiskTest.FriendlyBodyPartName) — collapses L_Hand/R_Hand etc. into one "Hand" category so
    normal left/right hand alternation doesn't spam a label change every time it switches sides. Also skips
    segments shorter than min_segment_seconds so brief tracking noise doesn't clutter the plot."""
    labels = []
    if df.empty or "nearest_body_part" not in df.columns:
        return labels
    friendly = df["nearest_body_part"].map(lambda p: FRIENDLY_BODY_PART.get(p, p))
    prev = None
    start_t = df["t_sec"].iloc[0]
    for i in range(len(df)):
        part = friendly.iloc[i]
        if part != prev:
            if prev is not None and df["t_sec"].iloc[i] - start_t >= min_segment_seconds:
                labels.append((start_t, prev))
            start_t = df["t_sec"].iloc[i]
            prev = part
    if prev is not None and df["t_sec"].iloc[-1] - start_t >= min_segment_seconds:
        labels.append((start_t, prev))
    return labels


def savefig(fig, out_prefix):
    fig.savefig(out_prefix + ".pdf", bbox_inches="tight")
    fig.savefig(out_prefix + ".png", bbox_inches="tight")
    plt.close(fig)


def plot_fig19(df, out_prefix):
    fig, (ax_lines, ax_strip) = plt.subplots(
        2, 1, figsize=(9, 4.5), sharex=True, gridspec_kw={"height_ratios": [4, 1], "hspace": 0.08}
    )

    ax_lines.plot(df["t_sec"], df["risk_R"], color="black", lw=1.6, label=r"$R(t)$")
    ax_lines.plot(df["t_sec"], df["cognitive_C"], color="crimson", lw=1.4, ls="--", label=r"$C(t)$")
    ax_lines.set_ylim(0, 1.05)
    ax_lines.set_ylabel("Score")
    ax_lines.legend(loc="upper right", frameon=False)

    draw_mode_strip(ax_strip, df)
    ax_strip.set_xlabel("Time (s)")

    handles = [plt.Rectangle((0, 0), 1, 1, color=c) for c in MODE_COLORS.values()]
    ax_strip.legend(handles, MODE_LABELS.values(), loc="upper center",
                     bbox_to_anchor=(0.5, -0.9), ncol=4, frameon=False, fontsize=8)

    fig.suptitle(r"$R(t)$, $C(t)$, and resulting mode over one full kitting task")
    savefig(fig, out_prefix)


def plot_risk_breakdown(df, out_prefix):
    fig, (ax_dist, ax_factors, ax_risk) = plt.subplots(
        3, 1, figsize=(9, 8), sharex=True, gridspec_kw={"hspace": 0.12}
    )

    # Panel 1: distance + S_prot + mode strip beneath
    ax_dist.plot(df["t_sec"], df["min_distance_m"], color="blue", lw=1.6, label=r"$d_H(t)$")
    if "sprot_m" in df.columns:
        ax_dist.plot(df["t_sec"], df["sprot_m"], color="gray", lw=1.2, ls="--", label=r"$S_{prot}$")
    ax_dist.set_ylabel(r"$d_H(t)$ (m)")
    ax_dist.legend(loc="upper right", frameon=False)

    for t, part in body_part_change_labels(df):
        ax_dist.annotate(part, xy=(t, ax_dist.get_ylim()[1]), xytext=(t, ax_dist.get_ylim()[1] * 1.05),
                          fontsize=6, rotation=45, ha="left", va="bottom", annotation_clip=False)

    strip_ax = ax_dist.inset_axes([0, -0.18, 1, 0.12], transform=ax_dist.transAxes)
    draw_mode_strip(strip_ax, df)
    strip_ax.set_xlim(ax_dist.get_xlim())

    # Panel 2: P(t), I(t), S(t)
    ax_factors.plot(df["t_sec"], df["prob_P"], color="teal", lw=1.6, label=r"$P(t)$")
    ax_factors.plot(df["t_sec"], df["impact_I"], color="deeppink", lw=1.4, ls="--", label=r"$I(t)$")
    ax_factors.plot(df["t_sec"], df["severity_S"], color="saddlebrown", lw=1.2, ls=":", label=r"$S$")
    ax_factors.set_ylim(-0.05, 1.05)
    ax_factors.set_ylabel("Factor value")
    ax_factors.legend(loc="upper right", frameon=False, ncol=3)

    # Panel 3: R(t) with risk-zone bands + Safety-trigger marker
    ax_risk.axhspan(0, RISK_LOW_MAX, color="lightgreen", alpha=0.3)
    ax_risk.axhspan(RISK_LOW_MAX, RISK_HIGH_MIN, color="khaki", alpha=0.3)
    ax_risk.axhspan(RISK_HIGH_MIN, 1.0, color="lightcoral", alpha=0.3)
    ax_risk.plot(df["t_sec"], df["risk_R"], color="black", lw=1.6, label=r"$R(t)$")

    safety_triggers = df[(df["mode"] == "Safety") & (df["mode"].shift(1) != "Safety")]
    if not safety_triggers.empty:
        ax_risk.scatter(safety_triggers["t_sec"], safety_triggers["risk_R"], color="red", zorder=5, s=30)
        for _, row in safety_triggers.iterrows():
            ax_risk.annotate("Safety triggers", xy=(row["t_sec"], row["risk_R"]),
                              xytext=(row["t_sec"], min(row["risk_R"] + 0.08, 1.0)),
                              fontsize=7, ha="center", color="red")

    ax_risk.set_ylim(0, 1.05)
    ax_risk.set_ylabel(r"Risk score $R(t)$")
    ax_risk.set_xlabel("Time (s)")

    fig.suptitle("Risk score breakdown over one full kitting task")
    savefig(fig, out_prefix)


def compute_mode_time_pct(df):
    """Approximates time-in-mode as sample-count proportion — valid since sampling is ~uniform (0.2s)."""
    counts = df["mode"].value_counts(normalize=True) * 100
    return {m: counts.get(m, 0.0) for m in MODE_COLORS}


def dominant_mode(df):
    counts = df["mode"].value_counts()
    return counts.idxmax() if not counts.empty else ""


def safety_activation_counts(df):
    triggers = df[(df["mode"] == "Safety") & (df["mode"].shift(1) != "Safety")]
    if triggers.empty or "human_tracking" not in df.columns:
        return 0, 0
    appropriate = int((triggers["human_tracking"] == True).sum())  # noqa: E712
    false_positive_candidate = int((triggers["human_tracking"] == False).sum())  # noqa: E712
    return appropriate, false_positive_candidate


def table_xv_latex(latency_df):
    if latency_df.empty:
        return "% No latency_*.csv files found — Table XV skipped.\n"
    mean_ms = latency_df["mean_ms"].mean()
    max_ms = latency_df["max_ms"].max()
    # Eq. 1-4 alone (sProt/P/I/S/R — a handful of arithmetic ops) is sub-millisecond on-device, so ms with
    # 1 decimal rounds it straight to "0.0" — report in microseconds instead, matching the actual scale.
    mean_us = mean_ms * 1000
    max_us = max_ms * 1000
    return (
        "\\begin{table}[t]\n"
        "\\caption{Digital Twin \\& Risk Assessor Computational Performance}\n"
        "\\label{tab:xv}\n"
        "\\centering\n"
        "\\begin{tabular}{lc}\n"
        "\\toprule\n"
        "Measure & Value \\\\\n"
        "\\midrule\n"
        f"Mean $R(t)$ computation latency & {mean_us:.1f} $\\mu$s \\\\\n"
        f"Maximum observed latency & {max_us:.1f} $\\mu$s \\\\\n"
        "Decision-epoch budget & 100 ms \\\\\n"
        "\\bottomrule\n"
        "\\end{tabular}\n"
        "\\end{table}\n"
    )


def table_xvi_latex(kit_dfs):
    rows = []
    for name, df in kit_dfs:
        cond = df["condition_label"].iloc[0] if "condition_label" in df.columns and not df.empty else None
        # A blank Inspector field round-trips through CSV as an empty cell, which pandas reads as NaN, not
        # "" — and groupby() silently drops NaN keys by default, so an unlabeled run would vanish from the
        # table entirely instead of falling back to "unlabeled".
        if pd.isna(cond) or cond == "":
            cond = "unlabeled"
        pct = compute_mode_time_pct(df)
        rows.append({"condition_label": cond, **pct})
    agg = pd.DataFrame(rows).groupby("condition_label").mean(numeric_only=True)

    lines = [
        "\\begin{table}[t]",
        "\\caption{Autonomy Mode Time Distribution by Induced Condition}",
        "\\label{tab:xvi}",
        "\\centering",
        "\\begin{tabular}{lcccc}",
        "\\toprule",
        "Condition & Robot-Led & Shared & Human-Led & Safety \\\\",
        "\\midrule",
    ]
    for cond, row in agg.iterrows():
        lines.append(f"{cond} & {row['RobotLed']:.0f}\\% & {row['Shared']:.0f}\\% & "
                      f"{row['HumanLed']:.0f}\\% & {row['Safety']:.0f}\\% \\\\")
    lines += ["\\bottomrule", "\\end{tabular}", "\\end{table}\n"]
    return "\n".join(lines)


def table_xvii_latex(kit_dfs):
    per_kit = []
    total_appropriate, total_false_positive = 0, 0
    min_dist_overall = np.inf
    for name, df in kit_dfs:
        duration = df["t_sec"].iloc[-1] if not df.empty else 0.0
        dom = dominant_mode(df)
        per_kit.append({"file": name, "duration_s": duration, "dominant_mode": dom})
        appropriate, false_positive = safety_activation_counts(df)
        total_appropriate += appropriate
        total_false_positive += false_positive
        if "min_distance_m" in df.columns and not df.empty:
            min_dist_overall = min(min_dist_overall, df["min_distance_m"].min())

    per_kit_df = pd.DataFrame(per_kit)
    mean_by_mode = per_kit_df.groupby("dominant_mode")["duration_s"].mean() if not per_kit_df.empty else pd.Series(dtype=float)

    lines = [
        "\\begin{table}[t]",
        "\\caption{Efficiency and Safety Summary}",
        "\\label{tab:xvii}",
        "\\centering",
        "\\begin{tabular}{lc}",
        "\\toprule",
        "Measure & Value \\\\",
        "\\midrule",
    ]
    for mode_key, label in MODE_LABELS.items():
        if mode_key in mean_by_mode.index:
            lines.append(f"Mean completion time: {label} & {mean_by_mode[mode_key]:.0f} s/kit \\\\")
    lines.append("Collisions / contact events & N/A --- requires manual/video review \\\\")
    lines.append(f"Safety activations (appropriate / false-positive) & {total_appropriate} / {total_false_positive} \\\\")
    if np.isfinite(min_dist_overall):
        lines.append(f"Minimum recorded $d_H(t)$ & {min_dist_overall:.2f} m \\\\")
    lines += ["\\bottomrule", "\\end{tabular}", "\\end{table}\n"]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("results_dir", help="Folder containing kit_*.csv and latency_*.csv (pulled via adb)")
    parser.add_argument("--out", default=None, help="Output folder (default: <results_dir>/plots)")
    args = parser.parse_args()

    out_dir = args.out or os.path.join(args.results_dir, "plots")
    os.makedirs(out_dir, exist_ok=True)

    kit_paths = sorted(glob.glob(os.path.join(args.results_dir, "kit_*.csv")))
    latency_paths = sorted(glob.glob(os.path.join(args.results_dir, "latency_*.csv")))

    if not kit_paths:
        print(f"No kit_*.csv files found in {args.results_dir}")
        return

    kit_dfs = []
    for path in kit_paths:
        name = os.path.splitext(os.path.basename(path))[0]
        df = load_kit_csv(path)
        kit_dfs.append((name, df))

        plot_fig19(df, os.path.join(out_dir, f"{name}_fig19"))
        plot_risk_breakdown(df, os.path.join(out_dir, f"{name}_risk_breakdown"))
        print(f"Plotted {name}")

    latency_df = pd.concat([pd.read_csv(p) for p in latency_paths], ignore_index=True) if latency_paths else pd.DataFrame()

    with open(os.path.join(out_dir, "table_xv.tex"), "w") as f:
        f.write(table_xv_latex(latency_df))
    with open(os.path.join(out_dir, "table_xvi.tex"), "w") as f:
        f.write(table_xvi_latex(kit_dfs))
    with open(os.path.join(out_dir, "table_xvii.tex"), "w") as f:
        f.write(table_xvii_latex(kit_dfs))

    print(f"\nDone. Plots and tables written to {out_dir}")


if __name__ == "__main__":
    main()
