using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TaskPanelUI : MonoBehaviour
{
    [Header("Kit Info")]
    public TMP_Text currentKitText;
    public TMP_Text kitProgressText;
    public Slider kitProgressBar;

    [Header("Robot Tasks")]
    public TMP_Text robotTask1Text;
    public TMP_Text robotTask1Count;
    public TMP_Text robotTask2Text;
    public TMP_Text robotTask2Count;

    [Header("Human Tasks")]
    public Toggle humanTask1Toggle;
    public TMP_Text humanTask1Text;
    public TMP_Text humanTask1RouteText;
    public Toggle humanTask2Toggle;
    public TMP_Text humanTask2Text;
    public TMP_Text humanTask2RouteText;
    public GameObject humanTask2Row;

    [Header("Next Item")]
    public TMP_Text nextItemText;
    public TMP_Text nextItemRoute;

    [Header("Kit Completion")]
    public TMP_Text kitCompleteText;

    private int _robotTask1Done, _robotTask1Total = 2;
    private int _robotTask2Done, _robotTask2Total = 1;
    private bool _hasHumanTask2 = true;

    void Start()
    {
        if (humanTask1Toggle) humanTask1Toggle.onValueChanged.AddListener(_ => CheckKitComplete());
        if (humanTask2Toggle) humanTask2Toggle.onValueChanged.AddListener(_ => CheckKitComplete());

        SetPlaceholderData();
    }

    void SetPlaceholderData()
    {
        if (currentKitText) currentKitText.text = "CURRENT KIT — KIT A";
        if (kitProgressText) kitProgressText.text = "KIT PROGRESS 0%";
        if (kitProgressBar) kitProgressBar.value = 0f;
        if (robotTask1Text) robotTask1Text.text = "Bolt";
        if (robotTask1Count) robotTask1Count.text = "0/2";
        if (robotTask2Text) robotTask2Text.text = "Nut";
        if (robotTask2Count) robotTask2Count.text = "0/1";
        if (humanTask1Text) humanTask1Text.text = "Washer  0/1";
        if (humanTask1RouteText) humanTask1RouteText.text = "Bin C → Kitting Box";
        if (humanTask2Text) humanTask2Text.text = "Spacer  0/1";
        if (humanTask2RouteText) humanTask2RouteText.text = "Bin D → Kitting Box";
        if (nextItemText) nextItemText.text = "Bolt";
        if (nextItemRoute) nextItemRoute.text = "Bin A → Kitting Box";

        _robotTask1Done = 0; _robotTask1Total = 2;
        _robotTask2Done = 0; _robotTask2Total = 1;
        _hasHumanTask2 = true;

        CheckKitComplete();
    }

    /// <summary>Redisplays this panel for a newly selected kit — robot task names/quantities, human
    /// task names/quantities/routes, and whether the kit even has a second human task at all.</summary>
    public void ConfigureKit(RobotTaskManager.KitDefinition kit)
    {
        if (kit == null) return;

        if (currentKitText) currentKitText.text = $"CURRENT KIT — {kit.kitName.ToUpper()}";

        _robotTask1Done = 0;
        _robotTask1Total = kit.robotTask1.quantity;
        _robotTask2Done = 0;
        _robotTask2Total = kit.robotTask2.quantity;

        if (robotTask1Text) robotTask1Text.text = kit.robotTask1.name;
        if (robotTask1Count) robotTask1Count.text = $"0/{kit.robotTask1.quantity}";
        if (robotTask2Text) robotTask2Text.text = kit.robotTask2.name;
        if (robotTask2Count) robotTask2Count.text = $"0/{kit.robotTask2.quantity}";

        if (humanTask1Toggle) humanTask1Toggle.isOn = false;
        if (humanTask1Text) humanTask1Text.text = $"{kit.humanTask1.name}  0/{kit.humanTask1.quantity}";
        if (humanTask1RouteText) humanTask1RouteText.text = $"{kit.humanTask1.binLocation} → Kitting Box";

        _hasHumanTask2 = kit.HasHumanTask2;
        if (humanTask2Row) humanTask2Row.SetActive(_hasHumanTask2);
        if (_hasHumanTask2)
        {
            if (humanTask2Toggle) humanTask2Toggle.isOn = false;
            if (humanTask2Text) humanTask2Text.text = $"{kit.humanTask2.name}  0/{kit.humanTask2.quantity}";
            if (humanTask2RouteText) humanTask2RouteText.text = $"{kit.humanTask2.binLocation} → Kitting Box";
        }

        if (nextItemText) nextItemText.text = kit.robotTask1.name;
        if (nextItemRoute) nextItemRoute.text = $"{kit.robotTask1.binLocation} → Kitting Box";

        CheckKitComplete();
    }

    public void UpdateKit(string kitName, float progress)
    {
        if (currentKitText) currentKitText.text = $"CURRENT KIT — {kitName}";
        if (kitProgressText) kitProgressText.text = $"KIT PROGRESS {(int)(progress * 100)}%";
        if (kitProgressBar) kitProgressBar.value = progress;
    }

    public void UpdateRobotTasks(string t1, int t1done, int t1total,
                                  string t2, int t2done, int t2total)
    {
        if (robotTask1Text) robotTask1Text.text = t1;
        if (robotTask1Count) robotTask1Count.text = $"{t1done}/{t1total}";
        if (robotTask2Text) robotTask2Text.text = t2;
        if (robotTask2Count) robotTask2Count.text = $"{t2done}/{t2total}";

        _robotTask1Done = t1done; _robotTask1Total = t1total;
        _robotTask2Done = t2done; _robotTask2Total = t2total;

        CheckKitComplete();
    }

    public void UpdateNextItem(string item, string route)
    {
        if (nextItemText) nextItemText.text = item;
        if (nextItemRoute) nextItemRoute.text = route;
    }

    /// <summary>Called by Control Panel's "Mark Done" button for the kit's first human task.</summary>
    public void MarkHumanTask1Done()
    {
        if (humanTask1Toggle) humanTask1Toggle.isOn = true;
        CheckKitComplete();
    }

    /// <summary>Called by Control Panel's "Mark Done" button for the kit's second human task (if any).</summary>
    public void MarkHumanTask2Done()
    {
        if (humanTask2Toggle) humanTask2Toggle.isOn = true;
        CheckKitComplete();
    }

    void CheckKitComplete()
    {
        int humanTotal = 1 + (_hasHumanTask2 ? 1 : 0);
        int humanDoneCount = (humanTask1Toggle != null && humanTask1Toggle.isOn ? 1 : 0)
                            + (_hasHumanTask2 && humanTask2Toggle != null && humanTask2Toggle.isOn ? 1 : 0);

        int totalUnits = _robotTask1Total + _robotTask2Total + humanTotal;
        int doneUnits = _robotTask1Done + _robotTask2Done + humanDoneCount;

        float progress = totalUnits > 0 ? (float)doneUnits / totalUnits : 0f;
        if (kitProgressText) kitProgressText.text = $"KIT PROGRESS {(int)(progress * 100)}%";
        if (kitProgressBar) kitProgressBar.value = progress;

        if (!kitCompleteText) return;

        bool robotDone = _robotTask1Done >= _robotTask1Total && _robotTask2Done >= _robotTask2Total;
        bool humanDone = (humanTask1Toggle == null || humanTask1Toggle.isOn)
                       && (!_hasHumanTask2 || humanTask2Toggle == null || humanTask2Toggle.isOn);

        if (robotDone && humanDone)
        {
            kitCompleteText.text = "KIT COMPLETE";
            kitCompleteText.color = new Color(0.15f, 0.68f, 0.38f);
        }
        else
        {
            kitCompleteText.text = "IN PROGRESS";
            kitCompleteText.color = new Color(0.74f, 0.77f, 0.78f);
        }
    }
}
