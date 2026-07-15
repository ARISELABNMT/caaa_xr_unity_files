using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to a hand's OVRSkeleton. Lets the operator click a GazeClickable button by physically
/// touching it with their index fingertip (hand tracking), as an alternative to gaze-dwell for
/// whenever their hands are free.
/// </summary>
[RequireComponent(typeof(OVRSkeleton))]
public class FingerPokeInteractor : MonoBehaviour
{
    public float pokeRadius = 0.015f;
    public float cooldownSeconds = 0.5f;

    private OVRSkeleton _skeleton;
    private int _gazeMask = -1;
    private GazeClickable _lastPoked;
    private float _cooldownTimer;

    void Start()
    {
        _skeleton = GetComponent<OVRSkeleton>();

        int layer = LayerMask.NameToLayer("GazeUI");
        _gazeMask = layer >= 0 ? (1 << layer) : 0;
    }

    void Update()
    {
        if (_gazeMask == 0 || _skeleton == null || !_skeleton.IsInitialized) return;

        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= Time.deltaTime;
            return;
        }

        Transform fingertip = FindIndexTip();
        if (fingertip == null) return;

        Collider[] hits = Physics.OverlapSphere(fingertip.position, pokeRadius, _gazeMask);
        if (hits.Length == 0)
        {
            _lastPoked = null;
            return;
        }

        GazeClickable clickable = hits[0].GetComponentInParent<GazeClickable>();
        if (clickable == null || clickable == _lastPoked) return;

        Button button = clickable.GetComponent<Button>();
        if (button != null && button.interactable)
        {
            button.onClick.Invoke();
            _lastPoked = clickable;
            _cooldownTimer = cooldownSeconds;
        }
    }

    Transform FindIndexTip()
    {
        var bones = _skeleton.Bones;
        if (bones == null) return null;

        // Legacy hand skeletons expose Hand_IndexTip; OpenXR-based hand skeletons
        // (SkeletonType.XRHandLeft/XRHandRight) expose XRHand_IndexTip instead.
        foreach (var bone in bones)
            if (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip || bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip)
                return bone.Transform;

        return null;
    }
}
