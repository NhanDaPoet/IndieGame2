using System.Collections;
using UnityEngine;

public class SlamWarningEffect : MonoBehaviour
{
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private Color warningColor = Color.red;
    private Renderer warningRenderer;
    private float originalAlpha;

    private void Start()
    {
        warningRenderer = GetComponent<Renderer>();
        if (warningRenderer != null)
        {
            originalAlpha = warningRenderer.material.color.a;
            StartCoroutine(PulseWarning());
        }
    }

    private IEnumerator PulseWarning()
    {
        while (true)
        {
            float alpha = Mathf.PingPong(Time.time * pulseSpeed, originalAlpha);
            Color newColor = warningColor;
            newColor.a = alpha;
            warningRenderer.material.color = newColor;
            yield return null;
        }
    }
}
