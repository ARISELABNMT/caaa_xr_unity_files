using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Lets the user capture an in-headset screenshot with a pinch-and-hold gesture on either hand — no
/// controller, no external trigger. Renders from a dedicated non-stereo capture camera (not
/// ScreenCapture.CaptureScreenshot(), which targets the VR compositor's stereo output and isn't reliable
/// for a clean single-eye image on Quest) positioned at the headset each time a capture fires, copying the
/// main camera's culling mask/clip planes/FOV so it matches what the user was actually seeing.
///
/// Saved to Application.persistentDataPath/Screenshots/ (the app's own external files dir on Android — no
/// extra permissions needed). Pull them off with:
///   adb pull /sdcard/Android/data/&lt;package&gt;/files/Screenshots/ .
///
/// Gesture is index-finger pinch held for holdSeconds — deliberately a *hold*, not a tap, so it's unlikely
/// to fire from a normal brief pinch-select interaction elsewhere in the app. If it still triggers
/// accidentally during normal UI use, raise holdSeconds or switch the trigger to require both hands
/// pinching at once.
/// </summary>
public class HandGestureScreenshot : MonoBehaviour
{
    [Header("Hand sources (auto-found if left empty)")]
    public OVRHand rightHand;
    public OVRHand leftHand;

    [Header("Gesture")]
    [Tooltip("How long the pinch must be held before it fires.")]
    public float holdSeconds = 0.6f;
    [Tooltip("Minimum time between captures.")]
    public float cooldownSeconds = 1.5f;

    [Header("Capture")]
    [Tooltip("Auto-created at Start if left empty — a non-stereo camera used only for rendering the " +
        "screenshot, positioned at the headset and matched to its FOV/culling/clip planes at capture time.")]
    public Camera captureCamera;
    [Tooltip("Auto-found by name (\"CenterEyeAnchor\") if left empty.")]
    public Transform headTransform;
    public int width = 1920;
    public int height = 1080;

    [Header("Feedback (optional)")]
    public AudioSource shutterSound;

    public event Action<string> OnScreenshotSaved;

    float _rightHoldTimer;
    float _leftHoldTimer;
    float _cooldownTimer;
    string _saveDir;
    bool _ready;

    void Start()
    {
        if (!rightHand || !leftHand) FindHandSources();

        if (!headTransform)
        {
            var go = GameObject.Find("CenterEyeAnchor");
            if (go) headTransform = go.transform;
        }
        if (!headTransform)
        {
            Debug.LogError("[HandGestureScreenshot] CenterEyeAnchor not found — can't position the capture camera, not starting.");
            return;
        }

        if (!captureCamera) captureCamera = CreateCaptureCamera(headTransform.GetComponent<Camera>());

        _saveDir = Path.Combine(Application.persistentDataPath, "Screenshots");
        Directory.CreateDirectory(_saveDir);

        _ready = true;
        Debug.Log($"[HandGestureScreenshot] Ready. Saving to {_saveDir}");
    }

    void FindHandSources()
    {
        foreach (var hand in FindObjectsOfType<OVRHand>())
        {
            var skeleton = hand.GetComponent<OVRSkeleton>();
            if (!skeleton) continue;
            var type = skeleton.GetSkeletonType();
            if (!rightHand && (type == OVRSkeleton.SkeletonType.XRHandRight || type == OVRSkeleton.SkeletonType.HandRight))
                rightHand = hand;
            else if (!leftHand && (type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.HandLeft))
                leftHand = hand;
        }
    }

    Camera CreateCaptureCamera(Camera source)
    {
        var go = new GameObject("ScreenshotCaptureCamera");
        var cam = go.AddComponent<Camera>();
        cam.enabled = false; // rendered on-demand only, via cam.Render() — never runs every frame
        cam.stereoTargetEye = StereoTargetEyeMask.None; // never picked up by the VR compositor

        if (source)
        {
            cam.clearFlags = source.clearFlags;
            cam.backgroundColor = source.backgroundColor;
            cam.cullingMask = source.cullingMask;
            cam.nearClipPlane = source.nearClipPlane;
            cam.farClipPlane = source.farClipPlane;
            cam.fieldOfView = source.fieldOfView;
        }
        else
        {
            cam.fieldOfView = 90f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
        }
        return cam;
    }

    void Update()
    {
        if (!_ready) return;

        _cooldownTimer -= Time.deltaTime;
        _rightHoldTimer = UpdatePinchTimer(rightHand, _rightHoldTimer);
        _leftHoldTimer = UpdatePinchTimer(leftHand, _leftHoldTimer);

        if (_cooldownTimer <= 0f && (_rightHoldTimer >= holdSeconds || _leftHoldTimer >= holdSeconds))
        {
            _cooldownTimer = cooldownSeconds;
            _rightHoldTimer = 0f;
            _leftHoldTimer = 0f;
            Capture();
        }
    }

    static float UpdatePinchTimer(OVRHand hand, float timer)
    {
        bool pinching = hand && hand.IsTracked && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        return pinching ? timer + Time.deltaTime : 0f;
    }

    void Capture()
    {
        captureCamera.transform.SetPositionAndRotation(headTransform.position, headTransform.rotation);

        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        captureCamera.targetTexture = rt;
        captureCamera.Render();

        var previousActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = previousActive;

        captureCamera.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);

        byte[] png = tex.EncodeToPNG();
        Destroy(tex);

        string fileName = $"metatrack_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string path = Path.Combine(_saveDir, fileName);
        File.WriteAllBytes(path, png);

        if (shutterSound) shutterSound.Play();

        Debug.Log($"[HandGestureScreenshot] Saved {path}");
        OnScreenshotSaved?.Invoke(path);
    }
}
