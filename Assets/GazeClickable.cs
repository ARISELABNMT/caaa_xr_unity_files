using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to a UI Button so it can be selected hands-free by looking at it (see GazeInteractor).
/// Adds a thin trigger BoxCollider sized to the button's RectTransform on a dedicated "GazeUI"
/// physics layer, kept isolated from scene raycasts used by other scripts (robot/proximity detectors).
/// </summary>
public class GazeClickable : MonoBehaviour
{
    private const string LayerName = "GazeUI";

    private Image _image;
    private Color _baseColor;

    void Start()
    {
        _image = GetComponent<Image>();
        if (_image) _baseColor = _image.color;

        int layer = LayerMask.NameToLayer(LayerName);
        if (layer >= 0) gameObject.layer = layer;

        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) col = gameObject.AddComponent<BoxCollider>();

        RectTransform rt = GetComponent<RectTransform>();
        Vector2 size = rt.rect.size;
        col.size = new Vector3(Mathf.Max(size.x, 1f), Mathf.Max(size.y, 1f), 50f);
        col.center = Vector3.zero;
        col.isTrigger = true;
    }

    public void SetGazeProgress(float t)
    {
        if (_image) _image.color = Color.Lerp(_baseColor, Color.white, Mathf.Clamp01(t) * 0.6f);
    }

    public void ResetGaze()
    {
        if (_image) _image.color = _baseColor;
    }
}
