using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Marker types for PIN Panel UI elements
/// </summary>
public enum PINPanelMarkerType
{
    Button0,
    Button1,
    Button2,
    Button3,
    Button4,
    Button5,
    Button6,
    Button7,
    Button8,
    Button9,
    ButtonClear,
    ButtonEnter,
    DisplayText,
    StatusText
}

/// <summary>
/// Marker component for PIN Panel UI elements.
/// Place this on buttons and TextMeshPro elements to automatically set them up.
/// This component will not be included in builds (IEditorOnly).
/// </summary>
public class PINPanelMarker : MonoBehaviour, IEditorOnly
{
    [Tooltip("The PINCodePanel this UI element belongs to (leave empty to auto-find)")]
    public PINCodePanel targetPINPanel;
    
    [Tooltip("What type of UI element this is")]
    public PINPanelMarkerType elementType;
    
    [Tooltip("Optional: Custom label for this element (shown in editor only)")]
    public string customLabel = "";
}


