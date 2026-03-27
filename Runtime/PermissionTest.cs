
using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class PermissionTest : UdonSharpBehaviour
{
    [SerializeField] private StagePermissionManager manager;
    [SerializeField] private Text permissionText;

    private void Start()
    {
        // Subscribe to the event
        manager.SubscribePermissionUpdates(this, nameof(OnPermissionUpdated));
    }
    
    public void OnPermissionUpdated()
    {
        // Handle the permission update
        Debug.Log($"Permission updated for: {(manager.IsLocalPlayerAuthorized ? "Allowed" : "Denied")}");
        permissionText.text = manager.IsLocalPlayerAuthorized ? "Allowed" : "Denied";
    }
}
