using UnityEngine;
using VRC.SDKBase;
using UnityEngine.UI;
using TMPro;

public enum LoudCubeTargetType
{
    UiToggle,
    ToggleWhenActive,
    BoostedPlayersText
}

/// <summary>
/// Editor-only marker component that identifies objects for auto-assignment to LoudCube instances.
/// Place this on any GameObject and set the loudCubeName to match your LoudCube's name.
/// </summary>
public class LoudCubeMarker : MonoBehaviour, IEditorOnly
{
    [Tooltip("The name of the LoudCube this element should connect to (matches the key/name in LoudCubeEditor)")]
    public string loudCubeName;

    [Tooltip("What kind of connection this is")]
    public LoudCubeTargetType targetType = LoudCubeTargetType.ToggleWhenActive;

    // Specific Targets (optional overrides - if null, defaults to GetComponent on self)
    public Toggle targetUiToggle;
    public GameObject targetGameObject;
    public TextMeshProUGUI targetText;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (loudCubeName != null)
        {
            loudCubeName = loudCubeName.Trim();
        }
    }
#endif
}
