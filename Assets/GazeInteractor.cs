using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the XR camera (CenterEyeAnchor). Casts the camera's forward ray each frame; looking at a
/// GazeClickable button continuously for dwellDuration seconds invokes its onClick, so the operator
/// can operate the Control Panel / confirmation popups with their hands full doing physical assembly.
/// </summary>
public class GazeInteractor : MonoBehaviour
{
    public float dwellDuration = 1.2f;
    public float maxDistance = 5f;

    private GazeClickable _current;
    private Button _currentButton;
    private float _gazeTimer;
    private int _gazeMask = -1;

    void Start()
    {
        int layer = LayerMask.NameToLayer("GazeUI");
        if (layer < 0)
        {
            Debug.LogError("GazeInteractor: layer 'GazeUI' not found. Run XR Panels > Setup Gaze Interactor once from the Editor menu to create it, then rebuild.");
            _gazeMask = 0;
        }
        else
        {
            _gazeMask = 1 << layer;
        }
    }

    void Update()
    {
        if (_gazeMask == 0) return;

        GazeClickable hit = RaycastForClickable();

        if (hit != _current)
        {
            _current?.ResetGaze();
            _current = hit;
            _currentButton = hit != null ? hit.GetComponent<Button>() : null;
            _gazeTimer = 0f;
        }

        if (_current != null && _currentButton != null && _currentButton.interactable)
        {
            _gazeTimer += Time.deltaTime;
            _current.SetGazeProgress(_gazeTimer / dwellDuration);

            if (_gazeTimer >= dwellDuration)
            {
                _currentButton.onClick.Invoke();
                _current.ResetGaze();
                _gazeTimer = 0f;
                _current = null;
                _currentButton = null;
            }
        }
    }

    GazeClickable RaycastForClickable()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hitInfo, maxDistance, _gazeMask))
            return hitInfo.collider.GetComponentInParent<GazeClickable>();
        return null;
    }
}
