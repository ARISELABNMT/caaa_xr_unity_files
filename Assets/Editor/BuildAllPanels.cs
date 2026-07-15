using UnityEditor;

public static class BuildAllPanels
{
    [MenuItem("XR Panels/Build All Panels")]
    public static void Build()
    {
        GazeUILayerSetup.EnsureLayerExists();

        BuildRosXRBridge.Build();
        BuildTaskPanel.Build();
        BuildSystemStatusPanel.Build();
        BuildConfirmationPopup.Build();
        BuildRobotTaskManager.Build();
        BuildControlPanel.Build();

        SetupGazeInteractor.Build();
        SetupFingerPokeInteractors.Build();
    }
}
