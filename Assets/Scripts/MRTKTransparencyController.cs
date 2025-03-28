using UnityEngine;
using Microsoft.MixedReality.Toolkit.UI;

public class MRTKTransparencyController : MonoBehaviour
{
    public PinchSlider transparencySlider; // Reference to the MRTK Slider
    public GameObject targetObject;        // Reference to the GameObject to control

    private Material targetMaterial;

    void Start()
    {
        // Get the material of the target object
        if (targetObject != null)
        {
            Renderer renderer = targetObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                targetMaterial = renderer.material;
            }
        }

        // Add listener to the slider
        if (transparencySlider != null)
        {
            transparencySlider.OnValueUpdated.AddListener(SetTransparency);
        }
    }

    void SetTransparency(SliderEventData data)
    {
        if (targetMaterial != null)
        {
            Color color = targetMaterial.color;
            color.a = data.NewValue; // Set the alpha value
            targetMaterial.color = color;
        }
    }

    void OnDestroy()
    {
        // Remove listener to avoid memory leaks
        if (transparencySlider != null)
        {
            transparencySlider.OnValueUpdated.RemoveListener(SetTransparency);
        }
    }
}
