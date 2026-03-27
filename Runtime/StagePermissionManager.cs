using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.UdonNetworkCalling;
using LocalPoliceDepartment.Utilities.AccountManager;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class StagePermissionManager : UdonSharpBehaviour
{
    #region Fields and Properties

    // Utility methods
    /// <summary>
    /// Returns true if the local player is staff according to the account manager.
    /// </summary>
    public bool isStaff => stagePermissionEditor[0].activeSelf;

    /// <summary>
    /// Indicates if the local player is the owner of this object.
    /// </summary>
    private bool IsOwner => Networking.IsOwner(gameObject);

    /// <summary>
    /// Indicates if the local player is authorized.
    /// </summary>
    public bool IsLocalPlayerAuthorized => _IsPlayerAuthorized(Networking.LocalPlayer);

    /// <summary>
    /// If true, only staff can toggle permissions.
    /// </summary>
    [Tooltip("If true, only staff can toggle permissions.")]
    public bool restrictToggleToStaff = true;

    // Authorization data
    [SerializeField] private OfficerAccountManager accountManager;
    private string staffRoleID = "Staff";

    // Scene References (for internal UI)
    [SerializeField] private StagePermissionToggle buttonTemplate;
    [SerializeField] private GameObject[] stagePermissionEditor;
    // Legacy single scroll view reference. If multiple scroll views are not set, this will be used.
    [SerializeField] private UnityEngine.UI.VerticalLayoutGroup permissionScrollView;
    // New: support multiple scroll views. If set, UI will populate all of them.
    [SerializeField] private UnityEngine.UI.VerticalLayoutGroup[] permissionScrollViews = new UnityEngine.UI.VerticalLayoutGroup[0];

    // Networked data for clients to use
    [UdonSynced(UdonSyncMode.None)] private string[] allowedNames = new string[0];

    // Subscription system fields
    private UdonSharpBehaviour[] permissionSubscribers = new UdonSharpBehaviour[0];
    private string[] subscriberCallbackMethodNames = new string[0];

    // VRC Player data
    private VRCPlayerApi[] players;
    private int lastPlayerRefreshFrame = -1;

    private VRCPlayerApi[] allPlayers
    {
        get
        {
            RefreshPlayerList();
            return players;
        }
    }

    #endregion

    #region Unity Lifecycle Methods

    private void Start()
    {
        // Hide the staff editor UI by default
        if (stagePermissionEditor != null)
        {
            foreach (var VARIABLE in stagePermissionEditor)
            {
                VARIABLE.SetActive(false);
            }
        }
        else
        {
            Debug.LogError("[StagePermissionManager] StagePermissionEditor is not set!");
        }

        if (accountManager == null)
        {
            Debug.LogError("[StagePermissionManager] OfficerAccountManager is not set!");
            return;
        }
        accountManager.NotifyWhenInitialized(this, nameof(LateStart));
    }

    public void LateStart()
    {
        bool localPlayerIsStaff = accountManager._GetBool(Networking.LocalPlayer.displayName, staffRoleID);
        if (stagePermissionEditor != null)
        {
            foreach (var VARIABLE in stagePermissionEditor)
            {
                VARIABLE.SetActive(localPlayerIsStaff);
            }
        }

        if (IsOwner)
        {
            // Owner authorizes self if they are staff by default
            if (localPlayerIsStaff && !_IsPlayerDisplayNameInAllowedList(Networking.LocalPlayer.displayName))
            {
                Debug.Log($"[StagePermissionManager] Owner ({Networking.LocalPlayer.displayName}) is staff, authorizing self.");
                _OwnerDirectAuthorizePlayer(Networking.LocalPlayer.displayName); // Uses display name
            }
        }

        // Initial UI update and notification for subscribers based on current (possibly empty) allowedNames
        _UpdateUI(); // For the manager's own UI
        _NotifySubscribers(); // For external scripts
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return;

        if (IsOwner && !player.isLocal)
        {
            if (accountManager != null && accountManager._GetBool(player.displayName, staffRoleID))
            {
                if (!_IsPlayerDisplayNameInAllowedList(player.displayName))
                {
                     Debug.Log($"[StagePermissionManager] New player ({player.displayName}) is staff, owner authorizing.");
                    _OwnerDirectAuthorizePlayer(player.displayName);
                }
            }
        }
        _UpdateUI(); // Update manager's UI for all, especially if local player is staff
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return;

        if (IsOwner)
        {
            if (_IsPlayerDisplayNameInAllowedList(player.displayName))
            {
                Debug.Log($"[StagePermissionManager] Player ({player.displayName}) left, owner deauthorizing.");
                _OwnerDirectDeauthorizePlayer(player.displayName);
            }
        }
        _UpdateUI(); // Update manager's UI
    }

    public override void OnDeserialization()
    {
        Debug.Log("[StagePermissionManager] OnDeserialization called. Updating UI and notifying subscribers.");
        _UpdateUI(); // For the manager's own UI
        _NotifySubscribers();
    }

    #endregion

    #region Subscription System

    /// <summary>
    /// Subscribes a script to permission updates.
    /// </summary>
    public void SubscribePermissionUpdates(UdonSharpBehaviour subscriber, string callbackMethodName)
    {
        if (subscriber == null || string.IsNullOrEmpty(callbackMethodName)) return;
        // Avoid duplicate subscriptions
        for (int i = 0; i < permissionSubscribers.Length; i++)
        {
            if (permissionSubscribers[i] == subscriber && subscriberCallbackMethodNames[i] == callbackMethodName) return;
        }

        UdonSharpBehaviour[] newSubscribers = new UdonSharpBehaviour[permissionSubscribers.Length + 1];
        string[] newCallbacks = new string[subscriberCallbackMethodNames.Length + 1];

        for (int i = 0; i < permissionSubscribers.Length; i++)
        {
            newSubscribers[i] = permissionSubscribers[i];
            newCallbacks[i] = subscriberCallbackMethodNames[i];
        }
        newSubscribers[permissionSubscribers.Length] = subscriber;
        newCallbacks[subscriberCallbackMethodNames.Length] = callbackMethodName;

        permissionSubscribers = newSubscribers;
        subscriberCallbackMethodNames = newCallbacks;
        Debug.Log($"<color=green>[StagePermissionManager]</color> " +
                  $"<color=white>{subscriber.gameObject.name}</color> " +
                  $"<color=yellow>subscribed</color> " +
                  $"<color=white>with method {callbackMethodName}</color>");
    }

    /// <summary>
    /// Unsubscribes a script from permission updates.
    /// </summary>
    public void UnsubscribePermissionUpdates(UdonSharpBehaviour subscriber)
    {
        if (subscriber == null || permissionSubscribers.Length == 0) return;

        int foundIndex = -1;
        for (int i = 0; i < permissionSubscribers.Length; i++)
        {
            if (permissionSubscribers[i] == subscriber)
            {
                foundIndex = i;
                break;
            }
        }

        if (foundIndex == -1) return;

        UdonSharpBehaviour[] newSubscribers = new UdonSharpBehaviour[permissionSubscribers.Length - 1];
        string[] newCallbacks = new string[subscriberCallbackMethodNames.Length - 1];
        int currentNewIndex = 0;
        for (int i = 0; i < permissionSubscribers.Length; i++)
        {
            if (i == foundIndex) continue;
            newSubscribers[currentNewIndex] = permissionSubscribers[i];
            newCallbacks[currentNewIndex] = subscriberCallbackMethodNames[i];
            currentNewIndex++;
        }
        permissionSubscribers = newSubscribers;
        subscriberCallbackMethodNames = newCallbacks;
        Debug.Log($"[StagePermissionManager] {subscriber.gameObject.name} unsubscribed.");
    }

    private void _NotifySubscribers()
    {
        Debug.Log("[StagePermissionManager] Notifying subscribers of permission update.");
        foreach (UdonSharpBehaviour subscriber in permissionSubscribers)
        {
            if (subscriber != null)
            {
                // Find the callback name for this subscriber
                string callbackName = "";
                for(int i = 0; i < permissionSubscribers.Length; ++i)
                {
                    if(permissionSubscribers[i] == subscriber)
                    {
                        callbackName = subscriberCallbackMethodNames[i];
                        break;
                    }
                }
                if(!string.IsNullOrEmpty(callbackName))
                {
                    Debug.Log($"<color=green>[StagePermissionManager]</color> " +
                              $"<color=white>{subscriber.gameObject.name}</color> " +
                              $"<color=yellow>notifying</color> " +
                              $"<color=white>{callbackName}</color>");
                    subscriber.SendCustomEvent(callbackName);
                }
            }
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Checks if the local player is authorized.
    /// </summary>
    public bool GetLocalPlayerAuthorized()
    {
        return _IsPlayerAuthorized(Networking.LocalPlayer);
    }

    /// <summary>
    /// Sends a client request to change a player's authorization.
    /// </summary>
    public void ClientRequestChangeAuthorization(VRCPlayerApi playerToModify, bool newAuthorizationState)
    {
        if (playerToModify == null || !playerToModify.IsValid())
        {
            Debug.LogError("[StagePermissionManager] ClientRequestChangeAuthorization: Invalid playerToModify.");
            return;
        }
        // if (!Networking.LocalPlayer.IsUserInVR()) // Basic check, can be expanded
        // {
        //      // Potentially add a staff check here if non-staff shouldn't even be able to send requests
        //     if (accountManager == null || !accountManager._GetBool(Networking.LocalPlayer.displayName, staffRoleID))
        //     {
        //         Debug.LogWarning("[StagePermissionManager] Non-staff attempting to send auth change request. Ignoring.");
        //         return;
        //     }
        // }


        Debug.Log($"[StagePermissionManager] Client requesting to set {playerToModify.displayName} authorization to {newAuthorizationState}. Sending to owner.");
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(MasterRequestSetPlayerAuthorization), playerToModify.displayName, newAuthorizationState);
    }

    #endregion

    #region Network Callable Methods

    /// <summary>
    /// Processes a request to set a player's authorization (executed by the owner).
    /// </summary>
    [NetworkCallable]
    public void MasterRequestSetPlayerAuthorization(string targetPlayerDisplayName, bool shouldBeAuthorized)
    {
        if (!IsOwner)
        {
            Debug.LogWarning("[StagePermissionManager] MasterRequestSetPlayerAuthorization called on non-owner. Ignoring.");
            return;
        }

        VRCPlayerApi requester = NetworkCalling.CallingPlayer;
        if (requester == null || !requester.IsValid())
        {
            Debug.LogError("[StagePermissionManager] MasterRequestSetPlayerAuthorization: Invalid requester. Ignoring.");
            return;
        }

        // if (accountManager == null || !accountManager._GetBool(requester.displayName, staffRoleID))
        // {
        //     Debug.LogWarning($"[StagePermissionManager] MasterRequestSetPlayerAuthorization: Requester {requester.displayName} is not staff. Ignoring.");
        //     return;
        // }

        Debug.Log($"[StagePermissionManager] Master processing request from {requester.displayName} to set {targetPlayerDisplayName} authorization to {shouldBeAuthorized}.");

        if (shouldBeAuthorized)
        {
            if (!_IsPlayerDisplayNameInAllowedList(targetPlayerDisplayName))
            {
                allowedNames = _Append(allowedNames, targetPlayerDisplayName);
                RequestSerialization();
                OnDeserialization(); // Update master's state/UI immediately and notify its subscribers
            }
        }
        else
        {
            if (_IsPlayerDisplayNameInAllowedList(targetPlayerDisplayName))
            {
                allowedNames = _Remove(allowedNames, targetPlayerDisplayName);
                RequestSerialization();
                OnDeserialization(); // Update master's state/UI immediately and notify its subscribers
            }
        }
    }

    #endregion

    #region Owner Direct Actions

    private void _OwnerDirectAuthorizePlayer(string playerDisplayName)
    {
        if (!IsOwner) return;
        if (!_IsPlayerDisplayNameInAllowedList(playerDisplayName))
        {
            Debug.Log($"[StagePermissionManager] Owner directly authorizing {playerDisplayName}");
            allowedNames = _Append(allowedNames, playerDisplayName);
            RequestSerialization();
            OnDeserialization(); // Update master's state/UI immediately and notify its subscribers
        }
    }

    private void _OwnerDirectDeauthorizePlayer(string playerDisplayName)
    {
        if (!IsOwner) return;
        if (_IsPlayerDisplayNameInAllowedList(playerDisplayName))
        {
            Debug.Log($"[StagePermissionManager] Owner directly deauthorizing {playerDisplayName}");
            allowedNames = _Remove(allowedNames, playerDisplayName);
            RequestSerialization();
            OnDeserialization(); // Update master's state/UI immediately and notify its subscribers
        }
    }

    #endregion

    #region Internal Helpers

    public bool _IsPlayerAuthorized(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return false;
        return _IsPlayerDisplayNameInAllowedList(player.displayName);
    }

    private bool _IsPlayerDisplayNameInAllowedList(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return false;
        foreach (string name in allowedNames)
        {
            if (name == displayName) return true;
        }
        return false;
    }
    
    public void _SetAuthorized(VRCPlayerApi player, bool state)
    {
        ClientRequestChangeAuthorization(player, state);
    }

    private void _UpdateUI()
    {
        // Update editor UI visibility based on local authorization
        bool shouldBeVisible = IsLocalPlayerAuthorized;
        if (stagePermissionEditor != null)
        {
            foreach (var editor in stagePermissionEditor)
            {
                if (editor.activeSelf != shouldBeVisible)
                {
                    Debug.Log($"[StagePermissionManager] Toggling editor UI active state to {shouldBeVisible}");
                    editor.SetActive(shouldBeVisible);
                }
            }
        }

        // Only rebuild toggles if local player is authorized (use the state variable, not activeSelf)
        if (stagePermissionEditor != null && shouldBeVisible)
        {
            Debug.Log("[StagePermissionManager] Updating UI: Clearing old toggles.");

            // Determine target scroll views (multiple supported, falling back to legacy single reference)
            UnityEngine.UI.VerticalLayoutGroup[] targets;
            if (permissionScrollViews != null && permissionScrollViews.Length > 0)
            {
                targets = permissionScrollViews;
            }
            else if (permissionScrollView != null)
            {
                targets = new UnityEngine.UI.VerticalLayoutGroup[] { permissionScrollView };
            }
            else
            {
                Debug.LogWarning("[StagePermissionManager] No permission scroll views assigned. Skipping UI rebuild.");
                return;
            }

            // Clear existing children in all targets
            for (int i = 0; i < targets.Length; i++)
            {
                var group = targets[i];
                if (group == null) continue;
                foreach (Transform child in group.transform)
                {
                    Destroy(child.gameObject);
                }
            }

            // Rebuild lists for each target
            Debug.Log($"[StagePermissionManager] Rebuilding UI for {allPlayers.Length} players across {targets.Length} scroll views.");
            foreach (VRCPlayerApi player in allPlayers)
            {
                bool isAuthorized = _IsPlayerAuthorized(player);
                Debug.Log($"[StagePermissionManager] Creating toggles for player: {player.displayName}, authorized: {isAuthorized}");
                for (int i = 0; i < targets.Length; i++)
                {
                    var group = targets[i];
                    if (group == null) continue;
                    StagePermissionToggle toggle = Instantiate(buttonTemplate.gameObject, group.transform).GetComponent<StagePermissionToggle>();
                    toggle.Init(this, player, isAuthorized);
                    toggle.gameObject.SetActive(true);
                }
            }
        }
        else
        {
            Debug.Log("[StagePermissionManager] UI update skipped: stagePermissionEditor not active.");
        }
    }

    private static string[] _Append(string[] array, string value)
    {
        string[] newArray = new string[array.Length + 1];
        for (int i = 0; i < array.Length; i++)
        {
            newArray[i] = array[i];
        }
        newArray[array.Length] = value;
        return newArray;
    }

    private static string[] _Remove(string[] array, string value)
    {
        //Figure out which elements to keep
        bool[] toKeep = new bool[array.Length];
        int numToKeep = 0;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] == value)
            {
                toKeep[i] = false;
            }
            else
            {
                toKeep[i] = true;
                numToKeep++;
            }
        }

        //Skip the rest if the value wasn't found
        if (numToKeep == array.Length) return array;

        //Create the new array
        string[] newArray = new string[numToKeep];
        int j = 0;
        for (int i = 0; i < array.Length; i++)
        {
            if (toKeep[i])
            {
                newArray[j] = array[i];
                j++;
            }
        }
        return newArray; //Value found, return the new array
    }

    #endregion

    #region Player List Management

    private void RefreshPlayerList()
    {
        if (lastPlayerRefreshFrame == Time.frameCount) return;
        lastPlayerRefreshFrame = Time.frameCount;

        //Make sure the array is the right size
        if (players == null || players.Length != VRCPlayerApi.GetPlayerCount())
        {
            players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
        }

        //Fill the array
        players = VRCPlayerApi.GetPlayers(players);
    }

    #endregion

    #region Editor Utilities

    #if !COMPILER_UDONSHARP && UNITY_EDITOR
    private void OnValidate()
    {
        if (accountManager == null) accountManager = FindObjectOfType<OfficerAccountManager>();
        if (accountManager == null) Debug.LogError("[StagePermissionManager] OfficerAccountManager not found in scene! Please assign it in the inspector.");
    }
    #endif

    #endregion
}

