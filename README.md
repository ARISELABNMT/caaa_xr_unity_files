# MetaTrack — XR Human-Robot Collaborative Kitting System

Unity/Meta Quest 3 client for a mixed-reality, human-robot collaborative **kitting** cell built around a
real **UFactory xArm Lite6**, controlled and supervised over **ROS 2**. The headset renders a live digital
twin of the robot, tracks the human operator's body and risk exposure in real time (ISO/TS 15066), streams
a cognitive-load estimate from a separate ML pipeline, and feeds both into a rule-based **Decision Engine**
that continuously selects one of four autonomy modes — **Human-Led / Shared / Robot-Led / Safety** — for a
research project on context-adaptive autonomy allocation (CAAA).

This repository contains the **Unity/XR side only**. The ROS 2 stack (MoveIt2 planning, the mode manager
node, the xArm driver) lives in a separate workspace — see [ROS 2 side](#ros-2-side) below.

> Research code from an active lab project — interfaces and tuning constants may change as the accompanying
> paper's results are finalized.

## Table of Contents

- [Overview](#overview)
- [System Architecture](#system-architecture)
- [Repository Layout](#repository-layout)
- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Running It](#running-it)
- [Core Components](#core-components)
- [ROS 2 Interface](#ros-2-interface)
- [ROS 2 Side](#ros-2-side)
- [Results & Data Analysis](#results--data-analysis)
- [Known Limitations / Legacy Scripts](#known-limitations--legacy-scripts)
- [Troubleshooting](#troubleshooting)

## Overview

An operator wears a Meta Quest 3 and sees the real robot cell through passthrough, with a virtual ("ghost")
xArm Lite6 overlaid and aligned to the physical robot's base. As the operator and robot work through a
kitting task (picking parts from bins A–D into a kitting box), the system continuously computes:

- **R(t)** — a real-time collision risk score (ISO/TS 15066 speed-and-separation style: probability × impact
  × severity), from true surface-to-surface distance between the robot's collision meshes and the operator's
  tracked body capsules.
- **C(t)** — a cognitive-load score, produced by an external classifier (originally trained on HoloLens 2
  sensor data) fed live head/hand tracking over TCP and streamed back as a Low/Medium/High prediction.

A **Decision Engine** discretizes R(t)/C(t) into a fixed Low/Medium/High × Low/Medium/High lookup table each
decision epoch (default 200 ms), debounces the result (fast to revoke autonomy, slow to grant it), and
commits one of four modes — publishing both the mode and a continuous authority-blending parameter λ(t) to
ROS 2, where an Adaptive Autonomy Controller on the robot side consumes them. **Safety** mode pre-empts
everything else instantly, both automatically (High risk + High cognitive load) and via a manual E-Stop
latch on the in-headset Control Panel.

Everything the operator needs is presented as three in-headset UI panels — **Task**, **System Status**, and
**Control** — plus a Confirmation popup for step-by-step approvals in Human-Led/Shared modes.

## System Architecture

```
┌─────────────────────────────── Meta Quest 3 (this repo) ───────────────────────────────┐
│                                                                                          │
│  DigitalHumanTracker ──▶ GhostSurfaceRiskTest ──▶ DecisionEngine ──▶ SystemStatusUI      │
│  (OVRBody skeleton)      R(t) = P·I·S (ISO/TS        │  (Table IV lookup      (Task /    │
│                           15066, mesh-vs-capsule)     │   + debounce + λ(t))   Status /   │
│                                                        │                       Control    │
│  CognitiveLoadDataStreamer ───────────────────────────┘                       panels)    │
│  (plain TCP, external ML model) ── C(t)                                                  │
│                                                                                          │
│  RosXRBridge  ◀──────────────────────────────────────────────────────────────────────┐  │
│  (ROS-TCP-Connector)                                                                  │  │
│  RobotTaskManager (kitting loop) ─── ResultsLogger (per-run CSV) ── analysis/*.py ──▶ paper│
│  PassthroughQRAligner (MRUK QR tracking → aligns ghost robot to real robot base)         │
└──────────────────────────────────────┬───────────────────────────────────────────────┘
                                        │ rosbridge / ROS-TCP-Connector (TCP, port 10000)
                                        ▼
┌─────────────────────────────── ROS 2 (separate workspace) ─────────────────────────────┐
│  xr_mode_manager.py  ──  Adaptive Autonomy Controller  ──  MoveIt2  ──  xArm Lite6 driver│
└──────────────────────────────────────────────────────────────────────────────────────┘
```

A second, independent TCP link (plain sockets, not ROS) connects `CognitiveLoadDataStreamer` to a Python
process running the cognitive-load classifier — this is deliberately decoupled from the ROS bridge so it can
be added/removed without touching any robot-related code.

## Repository Layout

```
Assets/
├── RosXRBridge.cs               # Central Unity <-> ROS 2 bridge (all /xr/* topics)
├── RobotTaskManager.cs          # Kitting task loop: bin selection, task requests, results
├── DecisionEngine.cs            # R(t)/C(t) -> autonomy mode (Human-Led/Shared/Robot-Led/Safety)
├── GhostSurfaceRiskTest.cs      # R(t): mesh-surface-to-human-capsule risk computation
├── DigitalHumanTracker.cs       # Body-tracking -> capsule segments for risk + visualization
├── HumanCapsuleRosPublisher.cs  # Publishes human capsules to RViz as a MarkerArray
├── CognitiveLoadDataStreamer.cs # TCP link to the external cognitive-load ML model
├── PassthroughQRAligner.cs      # Aligns the virtual robot to the real one via QR tracking
├── ControlPanelUI.cs / SystemStatusUI.cs / TaskPanelUI.cs   # The three in-headset panels
├── ConfirmationPopupUI.cs / RosConfirmDialogHandler.cs      # Step-by-step confirmation flow
├── ResultsLogger.cs             # Per-kitting-run CSV logging for the paper's Results section
├── HandGestureScreenshot.cs     # Pinch-and-hold in-headset screenshot capture
├── JointStateSubscriber.cs / AndroidGhostMover.cs / AndroidRobotMover.cs / GhostRobotController.cs
│                                 # Drive the real/ghost robot's ArticulationBody joints from ROS
├── Editor/
│   ├── Build*.cs                # Editor menu items that build each UI panel's hierarchy from code
│   ├── ConfigureMRUKSceneSupport.cs
│   └── DecisionEngineLogicCheck.cs  # Editor-only exhaustive check of DecisionEngine vs. Table IV
├── URDF/xarm_description/       # xArm Lite6 URDF + meshes (imported via URDF-Importer)
├── Scenes/RobotSafetyTest.unity # Main scene
└── ...                          # Interaction (gaze/finger-poke), Meta SDK samples, plugins

analysis/                        # Python: turns ResultsLogger's CSVs into the paper's plots/tables
Screenshots/                     # In-headset screenshots captured via HandGestureScreenshot.cs
```

## Prerequisites

**Unity / XR side (this repo):**
- Unity **2022.3.62f3** (see `ProjectSettings/ProjectVersion.txt`) with **Android Build Support** (+ OpenJDK, Android SDK & NDK Tools) installed via Unity Hub.
- A **Meta Quest 3**, Developer Mode enabled, connected via `adb` (USB or Meta Quest Link over Wi-Fi/cable).
- [Meta Quest Developer Hub](https://developer.oculus.com/meta-quest-developer-hub/) (recommended for deploying builds and pulling logs off-device).
- A Wi-Fi network the Quest **and** the ROS 2 machine can both reach (this project talks to ROS over TCP, not USB).

**Unity packages** (already declared in [`Packages/manifest.json`](Packages/manifest.json), Unity resolves these automatically on first open):

| Package | Purpose |
|---|---|
| `com.meta.xr.sdk.core`, `com.meta.xr.sdk.interaction`, `com.meta.xr.sdk.avatars` | Meta Quest / OVR runtime, hand & body tracking, interaction SDK |
| `com.meta.xr.mrutilitykit` | Mixed Reality Utility Kit — native QR code trackable detection used for robot alignment |
| `com.unity.robotics.ros-tcp-connector` | ROS 2 connectivity (Unity ↔ `rosbridge`/ROS-TCP endpoint) |
| `com.unity.robotics.urdf-importer` | Imports the xArm Lite6 URDF as an `ArticulationBody` rig |
| `com.unity.xr.openxr`, `com.unity.xr.meta-openxr`, `com.unity.xr.arfoundation` | XR runtime / OpenXR backend |
| `com.unity.textmeshpro` | UI text rendering for all panels |

**ROS 2 side (separate machine/workspace, not in this repo):** ROS 2 Jazzy, MoveIt2, the `ROS-TCP-Endpoint`
package, and this project's `xr_mode_manager.py` node + xArm Lite6 driver.

## Setup

1. **Clone the repo:**
   ```bash
   git clone https://github.com/ARISELABNMT/caaa_xr_unity_files.git
   cd caaa_xr_unity_files
   ```
2. **Open in Unity Hub** using Unity **2022.3.62f3**. Let the Editor resolve packages on first open (may take
   several minutes — it's pulling the Meta XR packages from their scoped registry and the two Git-hosted
   Unity Robotics packages).
3. **Switch platform to Android:** `File > Build Settings > Android > Switch Platform`.
4. **Point the ROS connection at your ROS machine:** select the `ROSConnectionPrefab`
   (`Assets/Resources/ROSConnectionPrefab.prefab`) or the `ROSConnection` component in the scene, and set
   **ROS IP Address** / **ROS Port** to your ROS 2 machine's TCP endpoint (matches whatever `ROS-TCP-Endpoint`
   is bound to, e.g. port `10000`).
5. **Point the cognitive-load streamer at your Python listener:** on the `CognitiveLoadDataStreamer`
   component, set `targetIP`/`targetPort` (default `192.168.1.100:5555`) to wherever that classifier process
   is running. This is a **separate** connection from the ROS bridge above.
6. **Enable MRUK Scene Support** (required for QR-based robot alignment): `XR Panels > Configure MRUK Scene
   Support` menu item (`Assets/Editor/ConfigureMRUKSceneSupport.cs`), which sets Scene Support = Required and
   Anchor Support = Enabled on the project's MRUK settings.
7. **Position the bin/place markers** in the scene to match your physical robot cell — `RosXRBridge`'s
   `binATransform..binDTransform`/`placeTransform` fields, and `robotBaseFrame`, which must correspond to
   ROS's `link_base` frame.
8. Open `Assets/Scenes/RobotSafetyTest.unity`.

## Running It

1. Start your ROS 2 stack (`xr_mode_manager.py`, MoveIt2, the xArm Lite6 driver, `ROS-TCP-Endpoint`) on the
   ROS machine, and (optionally) the cognitive-load classifier's Python TCP listener.
2. Connect the Quest 3 (`adb devices` should list it), and in Unity: `File > Build Settings > Build And Run`
   (or use Meta Quest Developer Hub to push a build).
3. Grant the camera / scene / body-tracking permissions when prompted on-device
   (`Assets/CameraPermission.cs` requests these automatically at launch).
4. On the **Control Panel**, tap **Start Aligning** and look at the printed QR marker fixed to the real
   robot's base, then **Save Position** once the ghost robot lines up with the physical one
   (`PassthroughQRAligner.cs`).
5. Select a kit (A/B/C) and press **Start** to begin the kitting run. The **Task** panel tracks
   robot/human item progress; the **System Status** panel shows the live R(t)/C(t) scores, current autonomy
   mode, and nearest-body-part/distance readout; **E-Stop** on the Control Panel forces Safety mode
   immediately.
6. Pinch-and-hold with either hand for an in-headset screenshot (`HandGestureScreenshot.cs`).

Per-run CSV logs and screenshots are written to the app's external files directory on-device; pull them with:
```bash
adb pull /sdcard/Android/data/com.AsrafulApu.MetaTrack/files/ResultsLogs/ .
adb pull /sdcard/Android/data/com.AsrafulApu.MetaTrack/files/Screenshots/ .
```

## Core Components

- **`RosXRBridge`** — the single point of contact with ROS 2. Registers every `/xr/*` publisher/subscriber,
  exposes them as plain C# methods/events, and republishes the four bin poses + place pose in `link_base`
  frame on startup. All other scripts go through this rather than touching `ROSConnection` directly.
- **`DecisionEngine`** — implements the paper's Table IV autonomy-mode lookup over discretized R(t)/C(t),
  with asymmetric debouncing (revoke autonomy fast, grant it slowly) and a hard Safety pre-empt. Also
  computes the continuous authority-blend λ(t) consumed by the ROS-side controller while in Shared mode.
  Verified against all 9 (R, C) bin combinations by the Editor menu item **XR Panels > Verify Decision Engine
  Logic (Table IV)** (`Assets/Editor/DecisionEngineLogicCheck.cs`).
- **`GhostSurfaceRiskTest`** — computes R(t) = P(t)·I(t)·S from true collision-mesh-surface to human-capsule
  distance (ISO/TS 15066), using the xArm's own URDF collision meshes and `DigitalHumanTracker`'s body
  capsules. Drives the risk score shown on the System Status panel.
- **`DigitalHumanTracker`** — reads Meta Movement SDK's `OVRSkeleton`/`OVRBody` tracking and exposes
  ISO/TS 15066 Table IV body-region-tagged joint points and limb capsules (no avatar mesh involved).
- **`CognitiveLoadDataStreamer`** — streams head/hand tracking to an external classifier over plain TCP and
  converts its returned class probabilities into a continuous C(t) score.
- **`RobotTaskManager`** — drives the pick-and-place loop per selected kit (A/B/C), delegating all
  mode-specific behavior (step confirmation, shared approval, full autonomy, safety halt) to the ROS-side
  `xr_mode_manager.py`; Unity's job is bin selection, task triggering, and waiting on `/xr/result`.
- **`PassthroughQRAligner`** — aligns the virtual robot to the physical one using Meta MR Utility Kit's
  native QR-trackable detection (replaced an earlier ZXing.Net + Passthrough Camera Access approach that
  needed manual marker-size calibration and drifted at oblique angles).
- **`ResultsLogger`** — writes one CSV per kitting run (bound to `RobotTaskManager`'s start/complete events)
  plus a latency-summary file, feeding `analysis/plot_results.py`.
- **UI panels** (`ControlPanelUI`, `SystemStatusUI`, `TaskPanelUI`, `ConfirmationPopupUI`) — hands-free
  operable via gaze-dwell (`GazeInteractor`/`GazeClickable`) or finger-poke (`FingerPokeInteractor`), so an
  operator with hands full of parts can still drive the panels.

## ROS 2 Interface

All topics are under the `/xr/` namespace, published/subscribed by `RosXRBridge` and handled ROS-side by
`xr_mode_manager.py`. Robot base frame is `link_base`.

| Topic | Direction | Type | Purpose |
|---|---|---|---|
| `/xr/bin_A_pose` … `/xr/bin_D_pose`, `/xr/place_pose` | Unity → ROS | `geometry_msgs/PoseStamped` | Physical bin/place locations, published once at startup |
| `/xr/mode` | Unity → ROS | `std_msgs/String` | Commanded autonomy mode (`human_led`/`shared`/`robot_led`/`safety`) |
| `/xr/selected_bin` | Unity → ROS | `std_msgs/String` | Bin ID (`A`–`D`) for the next pick |
| `/xr/task_request` | Unity → ROS | `std_msgs/Bool` | Requests a pick-and-place for the selected bin |
| `/xr/confirm` | Unity → ROS | `std_msgs/Bool` | Operator's response to a `/xr/request_confirm` prompt |
| `/xr/reset` | Unity → ROS | `std_msgs/Bool` | Resets the ROS-side task/mode state |
| `/xr/authority_lambda` | Unity → ROS | `std_msgs/Float32` | Continuous λ(t) authority-blend value (Shared mode) |
| `/xr/human_capsules` | Unity → ROS | `visualization_msgs/MarkerArray` | Live human-capsule visualization for RViz |
| `/xr/status`, `/xr/mode_status` | ROS → Unity | `std_msgs/String` | Free-text status updates |
| `/xr/result` | ROS → Unity | `std_msgs/Bool` | Pick-and-place outcome |
| `/xr/request_confirm` | ROS → Unity | `std_msgs/String` | Prompt text for a confirmation popup |
| `/xr/current_mode` | ROS → Unity | `std_msgs/String` | ROS-side's committed mode (informational) |
| `/xr/holding_state` | ROS → Unity | `std_msgs/Bool` | Whether the robot is currently holding an item |
| `/joint_states` | ROS → Unity | `sensor_msgs/JointState` | Drives the real/ghost robot's `ArticulationBody` joints |
| `/unity_planned_trajectory` | ROS → Unity | `std_msgs/String` (JSON) | Planned trajectory played back on the ghost robot |

## ROS 2 Side

This repo is the XR client only. The companion ROS 2 workspace (ROS 2 Jazzy + MoveIt2, running
`xr_mode_manager.py`, the Adaptive Autonomy Controller, and the xArm Lite6 driver) is maintained separately
— point Unity's `ROSConnectionPrefab` at that machine's `ROS-TCP-Endpoint` IP/port as described in
[Setup](#setup). Ask a project maintainer for access to that workspace if you need it.

## Results & Data Analysis

`analysis/plot_results.py` turns `ResultsLogger`'s per-run CSVs into the plots/tables used in the paper's
Results section:

```bash
pip install -r analysis/requirements.txt
python analysis/plot_results.py <results_logs_folder> [--out <output_folder>]
```

Given a folder of `kit_*.csv` / `latency_*.csv` files pulled from the headset (see [Running
It](#running-it)), it produces, per run, an R(t)/C(t)-over-time figure with a mode-colored strip and a risk
breakdown figure (distance vs. protective separation, P/I/S components, Safety-trigger markers), plus
aggregated latency, mode-time, and completion-time tables across all runs found.

## Known Limitations / Legacy Scripts

A few scripts in `Assets/` predate the current pipeline and are kept for reference rather than active use:

- `RobotAligner.cs`, `RobotSpatialAnchor.cs` — earlier robot-alignment approaches (manual locomotion-based
  alignment, Meta spatial anchors) superseded by `PassthroughQRAligner.cs`.
- `ProximityRiskDetector.cs`, `PredictedRiskDetector.cs`, `QuestProximityRisk.cs`, `QuestPredictiveRisk.cs`,
  `RiskModeManager.cs`, `TrajectoryCloud.cs` — earlier risk-estimation experiments (link-origin-to-point
  distance, swept-volume particle clouds) superseded by `GhostSurfaceRiskTest.cs`'s mesh-surface-based R(t).

`DecisionEngine`'s R(t) bin boundaries currently mirror C(t)'s published Low/Medium/High cut points (0.40 /
0.70) as a placeholder, since the paper's own R(t) zone table wasn't available at implementation time — see
the doc comment at the top of `DecisionEngine.cs` before relying on its exact thresholds.

## Troubleshooting

- **ROS messages silently no-op / "Unknown message class ''" in logcat:** check `adb logcat -s Unity` —
  every publish/subscribe in `RosXRBridge` logs on send/receive specifically so this is diagnosable on-device.
- **QR alignment doesn't track:** confirm `com.oculus.permission.USE_SCENE` was granted, MRUK Scene Support
  is `Required` (`XR Panels > Configure MRUK Scene Support`), and that `MRUK.Instance.QRCodeTrackingSupported`
  is true for your OS build (logged as a warning if not).
- **Cognitive-load score never updates:** it's a separate plain-TCP connection from the ROS bridge — verify
  `CognitiveLoadDataStreamer.targetIP`/`targetPort` matches your classifier process, independent of the ROS
  connection settings.
- **Packages fail to resolve on first open:** the Meta XR packages come from a scoped registry
  (`npm.developer.oculus.com`) and the two ROS packages are pulled directly from GitHub — make sure the
  Editor machine has outbound network access to both.
