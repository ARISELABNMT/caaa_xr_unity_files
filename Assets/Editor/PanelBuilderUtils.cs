using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class PanelBuilderUtils
{
    public static readonly Color BgDark       = new Color32(0x1A, 0x1A, 0x2E, 0xFF);
    public static readonly Color BgSection    = new Color32(0x16, 0x21, 0x3E, 0xFF);
    public static readonly Color BgHeader     = new Color32(0x1B, 0x4F, 0x72, 0xFF);
    public static readonly Color AccentBlue   = new Color32(0x34, 0x98, 0xDB, 0xFF);
    public static readonly Color AccentOrange = new Color32(0xE6, 0x7E, 0x22, 0xFF);
    public static readonly Color AccentGreen  = new Color32(0x27, 0xAE, 0x60, 0xFF);
    public static readonly Color AccentCyan   = new Color32(0x00, 0xBC, 0xD4, 0xFF);
    public static readonly Color SliderBg     = new Color32(0x2C, 0x3E, 0x50, 0xFF);
    public static readonly Color MutedGrey    = new Color32(0xBD, 0xC3, 0xC7, 0xFF);
    public static readonly Color PauseBlue    = new Color32(0x29, 0x80, 0xB9, 0xFF);
    public static readonly Color EStopRed     = new Color32(0xE7, 0x4C, 0x3C, 0xFF);
    public static readonly Color NeutralGrey  = new Color32(0x7F, 0x8C, 0x8D, 0xFF);

    public static GameObject FindOrCreate(string name, Transform parent)
    {
        Transform found = parent != null ? parent.Find(name) : GameObject.Find("/" + name)?.transform;
        if (found != null) return found.gameObject;

        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Panel");
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>Deletes any existing child with this name under parent, after a confirmation dialog. Returns false if the user cancelled.</summary>
    public static bool ConfirmAndClearExisting(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing == null) return true;

        if (!EditorUtility.DisplayDialog($"Rebuild {childName}",
            $"A {childName} already exists under {parent.name}. Delete and rebuild it from scratch?",
            "Rebuild", "Cancel"))
            return false;

        Undo.DestroyObjectImmediate(existing.gameObject);
        return true;
    }

    public static GameObject CreateCanvasPanel(string name, Transform parent, float width, float height,
        Vector3 localPosition, Vector3 localEulerAngles = default, float scale = 0.001f)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(panel, "Build Panel");
        panel.transform.SetParent(parent, false);

        Canvas canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        panel.AddComponent<CanvasScaler>();
        panel.AddComponent<GraphicRaycaster>();

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(width, height);
        panel.transform.localScale = new Vector3(scale, scale, scale);
        panel.transform.localPosition = localPosition;
        panel.transform.localEulerAngles = localEulerAngles;

        return panel;
    }

    public static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    public static Image AddImage(GameObject go, Color color)
    {
        Image img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    public static GameObject CreateSection(string name, Transform parent, Color color, float height)
    {
        GameObject section = CreateUIObject(name, parent);
        AddImage(section, color);
        LayoutElement le = section.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        return section;
    }

    public static VerticalLayoutGroup AddVerticalLayout(GameObject go, float spacing, float padding,
        bool forceExpandHeight = false)
    {
        VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = spacing;
        vlg.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = forceExpandHeight;
        return vlg;
    }

    public static HorizontalLayoutGroup AddHorizontalLayout(GameObject go, float spacing, float padding,
        bool forceExpandWidth = true, bool forceExpandHeight = true)
    {
        HorizontalLayoutGroup hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = forceExpandWidth;
        hlg.childForceExpandHeight = forceExpandHeight;
        return hlg;
    }

    public static TMP_Text AddTMPText(GameObject parent, string name, string text, float fontSize,
        Color color, FontStyles style, TextAlignmentOptions align)
    {
        GameObject textGo = CreateUIObject(name, parent.transform);
        StretchFull(textGo.GetComponent<RectTransform>());
        TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = align;
        return tmp;
    }

    public static TMP_Text FindTMP(Transform parent, string childName)
    {
        Transform t = parent.Find(childName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    public static Button CreateButton(Transform parent, string name, string label, Color bgColor, float fontSize)
    {
        GameObject go = CreateUIObject(name, parent);
        AddImage(go, bgColor);
        Button btn = go.AddComponent<Button>();
        go.AddComponent<GazeClickable>();
        AddTMPText(go, "Text", label, fontSize, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        return btn;
    }

    public static Slider CreateSlider(Transform parent, string name, Color bgColor, Color fillColor,
        float preferredHeight = 20)
    {
        GameObject sliderGo = CreateUIObject(name, parent);
        LayoutElement sliderLe = sliderGo.AddComponent<LayoutElement>();
        sliderLe.preferredHeight = preferredHeight;
        AddImage(sliderGo, bgColor);
        Slider slider = sliderGo.AddComponent<Slider>();

        GameObject fillArea = CreateUIObject("Fill Area", sliderGo.transform);
        RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0, 0);
        fillAreaRt.anchorMax = new Vector2(1, 1);
        fillAreaRt.offsetMin = new Vector2(4, 4);
        fillAreaRt.offsetMax = new Vector2(-4, -4);

        GameObject fill = CreateUIObject("Fill", fillArea.transform);
        Image fillImg = AddImage(fill, fillColor);
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0, 0);
        fillRt.anchorMax = new Vector2(0, 1);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        slider.fillRect = fillRt;
        slider.targetGraphic = null;
        slider.handleRect = null;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 0;

        return slider;
    }

    public static Toggle CreateToggle(Transform parent, string name, Color checkColor, float width = 30)
    {
        GameObject toggleGo = CreateUIObject(name, parent);
        LayoutElement toggleLe = toggleGo.AddComponent<LayoutElement>();
        toggleLe.preferredWidth = width;
        Toggle toggle = toggleGo.AddComponent<Toggle>();

        GameObject toggleBg = CreateUIObject("Background", toggleGo.transform);
        Image toggleBgImg = AddImage(toggleBg, Color.white);
        StretchFull(toggleBg.GetComponent<RectTransform>());

        GameObject checkmark = CreateUIObject("Checkmark", toggleBg.transform);
        Image checkmarkImg = AddImage(checkmark, checkColor);
        StretchFull(checkmark.GetComponent<RectTransform>());

        toggle.targetGraphic = toggleBgImg;
        toggle.graphic = checkmarkImg;
        toggle.isOn = false;

        return toggle;
    }
}
