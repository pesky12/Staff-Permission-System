
using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDK3.Persistence;
using LocalPoliceDepartment.Utilities.AccountManager;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Allows staff members (verified via AccountManager) to temporarily opt in/out of staff permissions.
/// Staff can toggle their status for the current session and save their preferred default state.
/// Only shows UI to users who are in the online database (verified staff).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class StaffTemporaryOptOut : UdonSharpBehaviour
{
    #region Inspector Fields
    
    [Header("References")]
    [Tooltip("The StagePermissionManager to integrate with")]
    [SerializeField] private StagePermissionManager permissionManager;
    
    [Tooltip("The AccountManager to verify staff status")]
    [SerializeField] private OfficerAccountManager accountManager;
    
    [Header("UI Elements")]
    [Tooltip("UI panel to show/hide for verified staff only")]
    [SerializeField] private GameObject staffOptOutPanel;
    
    [Tooltip("Toggle for current session opt in/out")]
    [SerializeField] private Toggle sessionToggle;
    
    [Tooltip("Toggle for saving preferred default state")]
    [SerializeField] private Toggle saveDefaultToggle;
    
    [Tooltip("Text showing current status")]
    [SerializeField] private TextMeshProUGUI statusText;
    
    [Tooltip("Optional: Text showing saved preference")]
    [SerializeField] private TextMeshProUGUI savedPreferenceText;
    
    [Header("Settings")]
    [Tooltip("Role ID to check in AccountManager (e.g., 'Staff')")]
    [SerializeField] private string staffRoleID = "Staff";
    
    [Tooltip("PlayerData key for saving opt-in preference")]
    [SerializeField] private string playerDataKey = "StaffOptIn_Default";
    
    [Tooltip("Default staff status for first-time join (before PlayerData is saved)")]
    [SerializeField] private bool firstTimeJoinDefaultStaff = true;
    
    #endregion
    
    #region Private Fields
    
    // Local state
    private bool isVerifiedStaff = false;
    private bool isCurrentlyOptedIn = true; // Default: opted in
    private bool savedDefaultOptIn = true; // Default preference: opted in
    private bool isInitialized = false;
    
    // Permission manager owner tracking
    [UdonSynced] private int permissionManagerOwnerID = -1;
    
    // Delayed initialization
    private bool needsDelayedApply = false;
    
    #endregion
    
    #region Unity Lifecycle
    
    void Start()
    {
        // Hide UI by default
        if (staffOptOutPanel != null)
        {
            staffOptOutPanel.SetActive(false);
        }
        
        // Validate references
        if (permissionManager == null)
        {
            Debug.LogError("[StaffTemporaryOptOut] StagePermissionManager reference is not set!");
            return;
        }
        
        if (accountManager == null)
        {
            Debug.LogError("[StaffTemporaryOptOut] OfficerAccountManager reference is not set!");
            return;
        }
        
        // Wait for AccountManager to initialize
        accountManager.NotifyWhenInitialized(this, nameof(_OnAccountManagerReady));
        
        // Subscribe to permission updates
        permissionManager.SubscribePermissionUpdates(this, nameof(_OnPermissionUpdate));
        
        // Note: UI listeners will be set up via Unity Inspector OnValueChanged events
        // or we'll use public methods called by the toggles
        
        // Sync ownership with permission manager
        _LoopSyncOwnership();
    }
    
    public void _LoopSyncOwnership()
    {
        _SyncOwnership();
        SendCustomEventDelayedSeconds(nameof(_LoopSyncOwnership), 5f);
    }
    
    public override void OnDeserialization()
    {
        // When ownership changes, ensure we're synced
        _SyncOwnership();
    }
    
    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (Networking.IsOwner(gameObject))
        {
            // Update synced owner ID when players join
            _UpdateOwnerID();
        }
    }
    
    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            Debug.Log("[StaffTemporaryOptOut] OnPlayerRestored - PlayerData is now safe to access");
            // PlayerData is now loaded and safe to access
            // The actual loading will happen in _OnAccountManagerReady
        }
    }
    
    #endregion
    
    #region Initialization
    
    /// <summary>
    /// Called when AccountManager finishes initializing
    /// </summary>
    public void _OnAccountManagerReady()
    {
        Debug.Log("[StaffTemporaryOptOut] AccountManager ready, checking staff status...");
        
        // Check if local player is verified staff
        isVerifiedStaff = accountManager._GetBool(Networking.LocalPlayer.displayName, staffRoleID);
        
        if (isVerifiedStaff)
        {
            Debug.Log($"[StaffTemporaryOptOut] {Networking.LocalPlayer.displayName} is verified staff!");
            
            // Load saved preference
            _LoadSavedPreference();
            
            // Apply saved preference to current session
            isCurrentlyOptedIn = savedDefaultOptIn;
            
            // Show UI
            if (staffOptOutPanel != null)
            {
                staffOptOutPanel.SetActive(true);
            }
            
            // Initialize UI state
            _UpdateUI();
            
            isInitialized = true;
            
            // Delay applying opt-in state to give StagePermissionManager time to initialize
            // Use SendCustomEventDelayedSeconds as fallback (no Update needed!)
            Debug.Log($"[StaffTemporaryOptOut] Scheduling delayed apply of opt-in state: {isCurrentlyOptedIn}");
            needsDelayedApply = true;
            SendCustomEventDelayedSeconds(nameof(_ApplyOptInStateFallback), 2f);
        }
        else
        {
            Debug.Log($"[StaffTemporaryOptOut] {Networking.LocalPlayer.displayName} is NOT verified staff. UI will remain hidden.");
            
            // Ensure UI is hidden for non-staff
            if (staffOptOutPanel != null)
            {
                staffOptOutPanel.SetActive(false);
            }
        }
    }
    
    #endregion
    
    #region UI Callbacks
    
    /// <summary>
    /// Public method: Toggle session opt-in state
    /// Call this from UI buttons or toggle OnValueChanged
    /// </summary>
    public void OnSessionToggleChanged()
    {
        if (!isVerifiedStaff || !isInitialized) return;
        
        // Toggle the state
        isCurrentlyOptedIn = !isCurrentlyOptedIn;
        
        Debug.Log($"[StaffTemporaryOptOut] Session toggled to: {isCurrentlyOptedIn}");
        
        // Update toggle UI
        if (sessionToggle != null)
        {
            sessionToggle.SetIsOnWithoutNotify(isCurrentlyOptedIn);
        }
        
        _ApplyOptInState();
        _UpdateUI();
    }
    
    /// <summary>
    /// Public method: Toggle save default state
    /// Call this from UI buttons or toggle OnValueChanged
    /// </summary>
    public void OnSaveDefaultToggleChanged()
    {
        if (!isVerifiedStaff || !isInitialized) return;
        
        // Toggle the saved default state
        savedDefaultOptIn = !savedDefaultOptIn;
        
        Debug.Log($"[StaffTemporaryOptOut] Save default toggled to: {savedDefaultOptIn}");
        
        // Update toggle UI
        if (saveDefaultToggle != null)
        {
            saveDefaultToggle.SetIsOnWithoutNotify(savedDefaultOptIn);
        }
        
        _SavePreference();
        _UpdateUI();
    }
    
    /// <summary>
    /// Public method for button: Opt In
    /// </summary>
    public void OnOptInButtonPressed()
    {
        if (!isVerifiedStaff || !isInitialized) return;
        
        Debug.Log("[StaffTemporaryOptOut] Opt In button pressed");
        
        isCurrentlyOptedIn = true;
        if (sessionToggle != null)
        {
            sessionToggle.SetIsOnWithoutNotify(true);
        }
        _ApplyOptInState();
        _UpdateUI();
    }
    
    /// <summary>
    /// Public method for button: Opt Out
    /// </summary>
    public void OnOptOutButtonPressed()
    {
        if (!isVerifiedStaff || !isInitialized) return;
        
        Debug.Log("[StaffTemporaryOptOut] Opt Out button pressed");
        
        isCurrentlyOptedIn = false;
        if (sessionToggle != null)
        {
            sessionToggle.SetIsOnWithoutNotify(false);
        }
        _ApplyOptInState();
        _UpdateUI();
    }
    
    #endregion
    
    #region Permission Management
    
    /// <summary>
    /// Applies the current opt-in state to the permission manager
    /// </summary>
    private void _ApplyOptInState()
    {
        if (permissionManager == null || !isVerifiedStaff) return;
        
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        
        if (isCurrentlyOptedIn)
        {
            // Opt in: Request authorization via owner
            Debug.Log($"[StaffTemporaryOptOut] Opting IN - Requesting staff permissions for {localPlayer.displayName}");
            _RequestOwnerApproval(localPlayer.displayName, true);
        }
        else
        {
            // Opt out: Request deauthorization via owner
            Debug.Log($"[StaffTemporaryOptOut] Opting OUT - Removing staff permissions for {localPlayer.displayName}");
            _RequestOwnerApproval(localPlayer.displayName, false);
        }
    }
    
    /// <summary>
    /// Requests owner to approve/deny staff permissions
    /// </summary>
    private void _RequestOwnerApproval(string playerDisplayName, bool shouldAuthorize)
    {
        // Send request to owner for verification
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_OwnerVerifyAndApprove), playerDisplayName, shouldAuthorize);
    }
    
    /// <summary>
    /// Owner verifies the player is in the database and approves/denies
    /// </summary>
    [NetworkCallable]
    public void _OwnerVerifyAndApprove(string playerDisplayName, bool shouldAuthorize)
    {
        if (!Networking.IsOwner(gameObject))
        {
            Debug.LogWarning("[StaffTemporaryOptOut] _OwnerVerifyAndApprove called on non-owner. Ignoring.");
            return;
        }
        
        // Verify the player is actually in the online database
        bool isInDatabase = accountManager._GetBool(playerDisplayName, staffRoleID);
        
        if (!isInDatabase)
        {
            Debug.LogWarning($"[StaffTemporaryOptOut] Owner denying request for {playerDisplayName} - NOT in database!");
            return;
        }
        
        Debug.Log($"[StaffTemporaryOptOut] Owner verified {playerDisplayName} is in database. Setting authorization to: {shouldAuthorize}");
        
        // Find the player
        VRCPlayerApi targetPlayer = _GetPlayerByDisplayName(playerDisplayName);
        if (targetPlayer != null && targetPlayer.IsValid())
        {
            // Use permission manager to set authorization
            permissionManager.ClientRequestChangeAuthorization(targetPlayer, shouldAuthorize);
        }
        else
        {
            Debug.LogWarning($"[StaffTemporaryOptOut] Could not find player {playerDisplayName} to authorize.");
        }
    }
    
    /// <summary>
    /// Called when permission manager updates
    /// </summary>
    public void _OnPermissionUpdate()
    {
        if (!isVerifiedStaff) return;
        
        Debug.Log("[StaffTemporaryOptOut] Permission update received from StagePermissionManager");
        
        // If we have a pending delayed apply and permission manager is now ready, apply immediately
        if (needsDelayedApply)
        {
            needsDelayedApply = false;
            Debug.Log($"[StaffTemporaryOptOut] Permission manager ready! Applying opt-in state immediately: {isCurrentlyOptedIn}");
            _ApplyOptInState();
        }
        
        // Update UI to reflect current permission state
        _UpdateUI();
    }
    
    /// <summary>
    /// Fallback method called after delay if permission manager didn't notify us
    /// Called via SendCustomEventDelayedSeconds
    /// </summary>
    public void _ApplyOptInStateFallback()
    {
        // Only apply if still pending (might have been applied via callback already)
        if (needsDelayedApply)
        {
            needsDelayedApply = false;
            Debug.Log($"[StaffTemporaryOptOut] Fallback timeout reached. Applying opt-in state: {isCurrentlyOptedIn}");
            _ApplyOptInState();
        }
        else
        {
            Debug.Log("[StaffTemporaryOptOut] Fallback called but state already applied via callback (no action needed)");
        }
    }
    
    #endregion
    
    #region UI Updates
    
    private void _UpdateUI()
    {
        if (!isVerifiedStaff) return;
        
        // Update session toggle
        if (sessionToggle != null)
        {
            sessionToggle.SetIsOnWithoutNotify(isCurrentlyOptedIn);
        }
        
        // Update save default toggle
        if (saveDefaultToggle != null)
        {
            saveDefaultToggle.SetIsOnWithoutNotify(savedDefaultOptIn);
        }
        
        // Update status text
        if (statusText != null)
        {
            bool isActuallyAuthorized = permissionManager.IsLocalPlayerAuthorized;
            
            string status = isCurrentlyOptedIn ? 
                "<color=green>OPTED IN</color>" : 
                "<color=orange>OPTED OUT</color>";
            
            string actual = isActuallyAuthorized ? 
                "<color=green>Active</color>" : 
                "<color=red>Inactive</color>";
            
            statusText.text = $"Staff Permissions: {actual}";
        }
        
        // Update saved preference text
        if (savedPreferenceText != null)
        {
            string prefText = savedDefaultOptIn ? 
                "<color=green>On join: Become staff</color>" : 
                "<color=orange>On join: Not staff</color>";
            savedPreferenceText.text = prefText;
        }
    }
    
    #endregion
    
    #region Save/Load Preferences
    
    private void _SavePreference()
    {
        PlayerData.SetBool(playerDataKey, savedDefaultOptIn);
        Debug.Log($"[StaffTemporaryOptOut] Saved preference: {savedDefaultOptIn}");
    }
    
    private void _LoadSavedPreference()
    {
        if (PlayerData.HasKey(Networking.LocalPlayer, playerDataKey))
        {
            savedDefaultOptIn = PlayerData.GetBool(Networking.LocalPlayer, playerDataKey);
            Debug.Log($"[StaffTemporaryOptOut] Loaded saved preference: {savedDefaultOptIn}");
        }
        else
        {
            savedDefaultOptIn = firstTimeJoinDefaultStaff; // Use configurable first-time default
            Debug.Log($"[StaffTemporaryOptOut] No saved preference found. Using first-time join default: {firstTimeJoinDefaultStaff}");
        }
    }
    
    #endregion
    
    #region Utility Methods
    
    /// <summary>
    /// Ensures this script is owned by the same player who owns the StagePermissionManager
    /// </summary>
    private void _SyncOwnership()
    {
        if (permissionManager == null) return;
        
        VRCPlayerApi permissionOwner = Networking.GetOwner(permissionManager.gameObject);
        VRCPlayerApi currentOwner = Networking.GetOwner(gameObject);
        
        if (permissionOwner != null && permissionOwner.IsValid() && 
            currentOwner != null && currentOwner.IsValid() &&
            permissionOwner.playerId != currentOwner.playerId)
        {
            Networking.SetOwner(permissionOwner, gameObject);
            Debug.Log($"[StaffTemporaryOptOut] Ownership synced to {permissionOwner.displayName}");
            
            if (Networking.IsOwner(gameObject))
            {
                _UpdateOwnerID();
            }
        }
    }
    
    private void _UpdateOwnerID()
    {
        if (!Networking.IsOwner(gameObject)) return;
        
        VRCPlayerApi owner = Networking.GetOwner(gameObject);
        if (owner != null && owner.IsValid())
        {
            permissionManagerOwnerID = owner.playerId;
            RequestSerialization();
        }
    }
    
    /// <summary>
    /// Gets a player by their display name
    /// </summary>
    private VRCPlayerApi _GetPlayerByDisplayName(string displayName)
    {
        VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
        VRCPlayerApi.GetPlayers(players);
        
        foreach (VRCPlayerApi player in players)
        {
            if (player != null && player.IsValid() && player.displayName == displayName)
            {
                return player;
            }
        }
        
        return null;
    }
    
    #endregion
    
    #region Editor Utilities
    
    #if !COMPILER_UDONSHARP && UNITY_EDITOR
    
    private void OnValidate()
    {
        // Auto-find permission manager if not set
        if (permissionManager == null)
        {
            permissionManager = GameObject.FindObjectOfType<StagePermissionManager>();
            if (permissionManager != null)
            {
                Debug.Log("[StaffTemporaryOptOut] Auto-found StagePermissionManager.");
            }
        }
        
        // Auto-find account manager if not set
        if (accountManager == null)
        {
            accountManager = GameObject.FindObjectOfType<OfficerAccountManager>();
            if (accountManager != null)
            {
                Debug.Log("[StaffTemporaryOptOut] Auto-found OfficerAccountManager.");
            }
        }
    }
    
    #endif
    
    #endregion
}
