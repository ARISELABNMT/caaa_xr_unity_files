using UnityEditor;
using UnityEngine;

/// <summary>
/// MRUK's own project-setup validator (OVRProjectSetupMRUK) requires Scene Support = Required and
/// Anchor Support = Enabled in the project config before QR code trackable detection will work. This is
/// a project-level setting (OVRProjectConfig), not a scene GameObject field — normally set via the
/// "Meta > Tools > Project Setup Tool" fix button; this menu item does the same thing directly.
/// </summary>
public static class ConfigureMRUKSceneSupport
{
    [MenuItem("XR Panels/Configure MRUK Scene Support")]
    public static void Configure()
    {
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        projectConfig.sceneSupport = OVRProjectConfig.FeatureSupport.Required;
        projectConfig.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
        OVRProjectConfig.CommitProjectConfig(projectConfig);
        Debug.Log("MRUK: Scene Support set to Required, Anchor Support set to Enabled in the project config.");
    }
}
