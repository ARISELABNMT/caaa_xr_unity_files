using UnityEngine;

public class FindTFSubscriber : MonoBehaviour
{
    void Start()
    {
        var allComponents = FindObjectsOfType<MonoBehaviour>();
        foreach (var c in allComponents)
            Debug.Log(c.gameObject.name + " --> " + c.GetType().Name);
    }
}
