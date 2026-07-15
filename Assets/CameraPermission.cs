using UnityEngine;
using UnityEngine.Android;

public class CameraPermission : MonoBehaviour
{
    void Awake()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            Permission.RequestUserPermission(Permission.Camera);

        Permission.RequestUserPermission("horizonos.permission.HEADSET_CAMERA");

        // Required for MRUK QR code trackable detection (PassthroughQRAligner).
        if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
    }
}
