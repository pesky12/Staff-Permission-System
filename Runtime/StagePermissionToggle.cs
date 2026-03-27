using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class StagePermissionToggle : UdonSharpBehaviour
{
    [SerializeField] private StagePermissionManager manager;
    [SerializeField] private VRCPlayerApi player;
    private bool allowed = false;

    [SerializeField] private TextMeshProUGUI buttonText;
    [SerializeField] private GameObject allowedImage;
    [SerializeField] private GameObject deniedImage;

    public void Init(StagePermissionManager manager, VRCPlayerApi player, bool isAllowed)
    {
        this.manager = manager;
        this.player = player;
        if (buttonText == null)
        {
            Debug.LogError("[StagePermissionToggle] Init: buttonText is not assigned on " + gameObject.name);
        }
        else
        {
            buttonText.text = player != null ? player.displayName : "<unknown>";
        }
        _SetButtonState(isAllowed);
    }

    public void _SetButtonState(bool state)
    {
        allowed = state;
        if (allowedImage == null || deniedImage == null)
        {
            Debug.LogError("[StagePermissionToggle] _SetButtonState: allowedImage or deniedImage not assigned on " + gameObject.name);
        }
        else
        {
            allowedImage.SetActive(state);
            deniedImage.SetActive(!state);
        }
    }

    public void OnButtonPressed()
    {
        if (manager == null)
        {
            Debug.LogError("[StagePermissionToggle] OnButtonPressed: manager reference missing on " + gameObject.name);
            return;
        }
        allowed = !allowed;
        manager._SetAuthorized(player, allowed);
    }
}
