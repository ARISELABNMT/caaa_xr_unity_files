using UnityEngine;
using System.Collections.Generic;

public class BodyTrackingMapper : MonoBehaviour
{
    private OVRSkeleton skeleton;
    private Animator animator;

    // Stores original bone rotations to reset when not tracking
    private Dictionary<HumanBodyBones, Quaternion> initialBoneRotations =
        new Dictionary<HumanBodyBones, Quaternion>();

    private bool wasTracking = false;

    private Dictionary<OVRSkeleton.BoneId, HumanBodyBones> boneMapping =
        new Dictionary<OVRSkeleton.BoneId, HumanBodyBones>()
    {
        // Spine
        { OVRSkeleton.BoneId.Body_Hips,                         HumanBodyBones.Hips },
        { OVRSkeleton.BoneId.Body_SpineLower,                   HumanBodyBones.Spine },
        { OVRSkeleton.BoneId.Body_SpineMiddle,                  HumanBodyBones.Chest },
        { OVRSkeleton.BoneId.Body_SpineUpper,                   HumanBodyBones.UpperChest },
        { OVRSkeleton.BoneId.Body_Neck,                         HumanBodyBones.Neck },
        { OVRSkeleton.BoneId.Body_Head,                         HumanBodyBones.Head },

        // Left Arm
        { OVRSkeleton.BoneId.Body_LeftShoulder,                 HumanBodyBones.LeftShoulder },
        { OVRSkeleton.BoneId.Body_LeftArmUpper,                 HumanBodyBones.LeftUpperArm },
        { OVRSkeleton.BoneId.Body_LeftArmLower,                 HumanBodyBones.LeftLowerArm },
        { OVRSkeleton.BoneId.Body_LeftHandWrist,                HumanBodyBones.LeftHand },

        // Right Arm
        { OVRSkeleton.BoneId.Body_RightShoulder,                HumanBodyBones.RightShoulder },
        { OVRSkeleton.BoneId.Body_RightArmUpper,                HumanBodyBones.RightUpperArm },
        { OVRSkeleton.BoneId.Body_RightArmLower,                HumanBodyBones.RightLowerArm },
        { OVRSkeleton.BoneId.Body_RightHandWrist,               HumanBodyBones.RightHand },

        // Left Fingers
        { OVRSkeleton.BoneId.Body_LeftHandThumbMetacarpal,      HumanBodyBones.LeftThumbProximal },
        { OVRSkeleton.BoneId.Body_LeftHandThumbProximal,        HumanBodyBones.LeftThumbIntermediate },
        { OVRSkeleton.BoneId.Body_LeftHandThumbDistal,          HumanBodyBones.LeftThumbDistal },
        { OVRSkeleton.BoneId.Body_LeftHandIndexProximal,        HumanBodyBones.LeftIndexProximal },
        { OVRSkeleton.BoneId.Body_LeftHandIndexIntermediate,    HumanBodyBones.LeftIndexIntermediate },
        { OVRSkeleton.BoneId.Body_LeftHandIndexDistal,          HumanBodyBones.LeftIndexDistal },
        { OVRSkeleton.BoneId.Body_LeftHandMiddleProximal,       HumanBodyBones.LeftMiddleProximal },
        { OVRSkeleton.BoneId.Body_LeftHandMiddleIntermediate,   HumanBodyBones.LeftMiddleIntermediate },
        { OVRSkeleton.BoneId.Body_LeftHandMiddleDistal,         HumanBodyBones.LeftMiddleDistal },
        { OVRSkeleton.BoneId.Body_LeftHandRingProximal,         HumanBodyBones.LeftRingProximal },
        { OVRSkeleton.BoneId.Body_LeftHandRingIntermediate,     HumanBodyBones.LeftRingIntermediate },
        { OVRSkeleton.BoneId.Body_LeftHandRingDistal,           HumanBodyBones.LeftRingDistal },
        { OVRSkeleton.BoneId.Body_LeftHandLittleProximal,       HumanBodyBones.LeftLittleProximal },
        { OVRSkeleton.BoneId.Body_LeftHandLittleIntermediate,   HumanBodyBones.LeftLittleIntermediate },
        { OVRSkeleton.BoneId.Body_LeftHandLittleDistal,         HumanBodyBones.LeftLittleDistal },

        // Right Fingers
        { OVRSkeleton.BoneId.Body_RightHandThumbMetacarpal,     HumanBodyBones.RightThumbProximal },
        { OVRSkeleton.BoneId.Body_RightHandThumbProximal,       HumanBodyBones.RightThumbIntermediate },
        { OVRSkeleton.BoneId.Body_RightHandThumbDistal,         HumanBodyBones.RightThumbDistal },
        { OVRSkeleton.BoneId.Body_RightHandIndexProximal,       HumanBodyBones.RightIndexProximal },
        { OVRSkeleton.BoneId.Body_RightHandIndexIntermediate,   HumanBodyBones.RightIndexIntermediate },
        { OVRSkeleton.BoneId.Body_RightHandIndexDistal,         HumanBodyBones.RightIndexDistal },
        { OVRSkeleton.BoneId.Body_RightHandMiddleProximal,      HumanBodyBones.RightMiddleProximal },
        { OVRSkeleton.BoneId.Body_RightHandMiddleIntermediate,  HumanBodyBones.RightMiddleIntermediate },
        { OVRSkeleton.BoneId.Body_RightHandMiddleDistal,        HumanBodyBones.RightMiddleDistal },
        { OVRSkeleton.BoneId.Body_RightHandRingProximal,        HumanBodyBones.RightRingProximal },
        { OVRSkeleton.BoneId.Body_RightHandRingIntermediate,    HumanBodyBones.RightRingIntermediate },
        { OVRSkeleton.BoneId.Body_RightHandRingDistal,          HumanBodyBones.RightRingDistal },
        { OVRSkeleton.BoneId.Body_RightHandLittleProximal,      HumanBodyBones.RightLittleProximal },
        { OVRSkeleton.BoneId.Body_RightHandLittleIntermediate,  HumanBodyBones.RightLittleIntermediate },
        { OVRSkeleton.BoneId.Body_RightHandLittleDistal,        HumanBodyBones.RightLittleDistal },
    };

    void Start()
    {
        skeleton = GetComponent<OVRSkeleton>();
        animator = GetComponent<Animator>();

        if (skeleton == null) Debug.LogError("OVRSkeleton not found!");
        if (animator == null) Debug.LogError("Animator not found!");

        // Save all initial bone rotations
        foreach (var pair in boneMapping)
        {
            Transform t = animator.GetBoneTransform(pair.Value);
            if (t != null)
                initialBoneRotations[pair.Value] = t.rotation;
        }
    }

    void LateUpdate()
    {
        if (skeleton == null || animator == null) return;

        // Not initialized or no bones — reset to default pose
        if (!skeleton.IsInitialized)
        {
            if (wasTracking)
            {
                ResetToInitialPose();
                wasTracking = false;
            }
            return;
        }

        var bones = skeleton.Bones;
        if (bones == null || bones.Count == 0)
        {
            if (wasTracking)
            {
                ResetToInitialPose();
                wasTracking = false;
            }
            return;
        }

        wasTracking = true;

        // Apply tracked rotations
        foreach (var bone in bones)
        {
            if (boneMapping.TryGetValue(
                (OVRSkeleton.BoneId)bone.Id, out HumanBodyBones humanBone))
            {
                Transform boneTransform = animator.GetBoneTransform(humanBone);
                if (boneTransform != null)
                    boneTransform.rotation = bone.Transform.rotation;
            }
        }

        // Move hips position
        foreach (var bone in bones)
        {
            if ((OVRSkeleton.BoneId)bone.Id == OVRSkeleton.BoneId.Body_Hips)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null)
                    hips.position = bone.Transform.position;
                break;
            }
        }
    }

    // Resets avatar to original T-pose when tracking lost
    void ResetToInitialPose()
    {
        foreach (var pair in initialBoneRotations)
        {
            Transform t = animator.GetBoneTransform(pair.Key);
            if (t != null)
                t.rotation = pair.Value;
        }
    }
}