using UnityEngine;

public class AvatarNameDebug : MonoBehaviour
{
    void Start()
    {
        foreach (Transform t in FindObjectsOfType<Transform>())
        {
            if (t.name.Contains("Joint") || t.name.Contains("joint") || 
                t.name.Contains("Head") || t.name.Contains("Chest") || 
                t.name.Contains("Hand"))
            {
                Debug.Log("Found: " + t.name + " | Path: " + GetPath(t));
            }
        }
    }

    string GetPath(Transform t)
    {
        return t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
    }
}
