using UnityEngine;

[DisallowMultipleComponent]
public class PandaEggSwitchController : MonoBehaviour
{
    public int selectedChildIndex;
    public float fps;
    public bool playOnStart = true;

    private float _timer;

    private void Start()
    {
        ApplySelection();
    }

    private void Update()
    {
        if (!playOnStart || fps <= 0.0f || transform.childCount == 0) return;

        _timer += Time.deltaTime;
        float frameTime = 1.0f / fps;
        while (_timer >= frameTime)
        {
            _timer -= frameTime;
            selectedChildIndex = (selectedChildIndex + 1) % transform.childCount;
            ApplySelection();
        }
    }

    public void ApplySelection()
    {
        int childCount = transform.childCount;
        if (childCount == 0) return;

        selectedChildIndex = Mathf.Clamp(selectedChildIndex, 0, childCount - 1);
        for (int i = 0; i < childCount; i++)
        {
            transform.GetChild(i).gameObject.SetActive(i == selectedChildIndex);
        }
    }
}
