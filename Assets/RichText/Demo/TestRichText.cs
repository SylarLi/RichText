using UnityEngine;
using UnityEngine.UI;

public class TestRichText : MonoBehaviour
{
    void Start()
    {
        var richText = GetComponent<RichText>();
        richText.AddListener((e, args) =>
        {
            Debug.Log("click: " + e + ", args: " + args);
        });
    }
}