
using System;
using System.Reflection;
using UdonSharp;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Events;
#endif
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.UdonNetworkCalling;
using TMPro;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// A secure PIN code panel that integrates with StagePermissionManager.
/// The PIN is hashed with a salt in the editor for security.
/// Only the owner of the StagePermissionManager can validate PIN attempts.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PINCodePanel : UdonSharpBehaviour
{
    #region Inspector Fields
    
    [Header("References")]
    [Tooltip("The StagePermissionManager to register authorized users with")]
    [SerializeField] private StagePermissionManager permissionManager;
    
    [Header("UI Elements")]
    [Tooltip("TextMeshPro text to display the entered PIN (will show as asterisks)")]
    [SerializeField] private TextMeshProUGUI displayText;
    
    [Tooltip("Optional status text to show feedback to the user")]
    [SerializeField] private TextMeshProUGUI statusText;
    
    [Header("Security Settings")]
    [Tooltip("EDITOR ONLY: Set your PIN here (4-8 digits). This will be hashed automatically.")]
    [SerializeField] private string editorPIN = "1234";
    
    [Tooltip("Hashed PIN (automatically generated in editor, do not modify)")]
    [SerializeField] private string hashedPIN = "";
    
    [Tooltip("Salt for hashing (automatically generated in editor, do not modify)")]
    [SerializeField] private string salt = "";
    
    [Header("Settings")]
    [Tooltip("Maximum PIN length")]
    [SerializeField] private int maxPINLength = 8;
    
    [Tooltip("Minimum PIN length")]
    [SerializeField] private int minPINLength = 4;
    
    [Tooltip("Time in seconds to show status messages")]
    [SerializeField] private float statusDisplayTime = 3f;
    
    [Tooltip("If true, the owner will receive a notification when someone attempts to enter the PIN")]
    [SerializeField] private bool notifyOwnerOnAttempt = true;
    
    [Tooltip("Maximum failed attempts before temporary lockout")]
    [SerializeField] private int maxFailedAttempts = 3;
    
    [Tooltip("Lockout duration in seconds after max failed attempts")]
    [SerializeField] private float lockoutDuration = 30f;
    
    #endregion
    
    #region Private Fields
    
    // Local state (not synced)
    private string currentPIN = "";
    private int failedAttempts = 0;
    private float lockoutEndTime = 0f;
    private bool isLockedOut = false;
    
    // UI update tracking
    private bool isShowingStatus = false;
    private float statusEndTime = 0f;
    
    // Owner notification tracking
    [UdonSynced] private string lastAttemptPlayerName = "";
    [UdonSynced] private bool pendingOwnerApproval = false;
    
    #endregion
    
    #region Unity Lifecycle
    
    void Start()
    {
        // Initialize UI
        _UpdateDisplay();
        _ClearStatus();
        
        // Validate setup
        if (permissionManager == null)
        {
            Debug.LogError("[PINCodePanel] StagePermissionManager reference is not set!");
            _ShowStatus("ERROR: Not configured", true);
            return;
        }
        
        if (string.IsNullOrEmpty(hashedPIN) || string.IsNullOrEmpty(salt))
        {
            Debug.LogWarning("[PINCodePanel] PIN has not been hashed. Please set the PIN in the editor.");
        }
        
        // Ensure ownership matches the StagePermissionManager's owner
        _LoopSyncOwnership();
    }
    
    public void _LoopSyncOwnership()
    {
        _SyncOwnership();
        SendCustomEventDelayedSeconds(nameof(_LoopSyncOwnership), 5f);
    }
    
    public override void OnDeserialization()
    {
        // If we're the owner and there's a pending approval, show notification
        if (Networking.IsOwner(gameObject) && pendingOwnerApproval)
        {
            _NotifyOwner(lastAttemptPlayerName);
            pendingOwnerApproval = false;
            RequestSerialization();
        }
    }
    
    #endregion
    
    #region Public Button Callback Methods
    
    /// <summary>
    /// Called when a number button (0-9) is pressed
    /// </summary>
    public void OnNumberPressed(int number)
    {
        if (isLockedOut)
        {
            if (Time.time >= lockoutEndTime)
            {
                isLockedOut = false;
                failedAttempts = 0;
                _ClearStatus();
            }
            else
            {
                _ShowStatus($"Locked out. Try again in {Mathf.CeilToInt(lockoutEndTime - Time.time)}s", true);
                return;
            }
        }
        
        if (currentPIN.Length < maxPINLength)
        {
            currentPIN += number.ToString();
            _UpdateDisplay();
        }
    }
    
    // Individual button methods for Udon compatibility
    public void OnButton0() { OnNumberPressed(0); }
    public void OnButton1() { OnNumberPressed(1); }
    public void OnButton2() { OnNumberPressed(2); }
    public void OnButton3() { OnNumberPressed(3); }
    public void OnButton4() { OnNumberPressed(4); }
    public void OnButton5() { OnNumberPressed(5); }
    public void OnButton6() { OnNumberPressed(6); }
    public void OnButton7() { OnNumberPressed(7); }
    public void OnButton8() { OnNumberPressed(8); }
    public void OnButton9() { OnNumberPressed(9); }
    
    /// <summary>
    /// Called when the Clear button is pressed
    /// </summary>
    public void OnClearPressed()
    {
        currentPIN = "";
        _UpdateDisplay();
        _ClearStatus();
    }
    
    /// <summary>
    /// Called when the Enter button is pressed
    /// </summary>
    public void OnEnterPressed()
    {
        if (isLockedOut)
        {
            if (Time.time >= lockoutEndTime)
            {
                isLockedOut = false;
                failedAttempts = 0;
                _ClearStatus();
            }
            else
            {
                _ShowStatus($"Locked out. Try again in {Mathf.CeilToInt(lockoutEndTime - Time.time)}s", true);
                return;
            }
        }
        
        if (currentPIN.Length < minPINLength)
        {
            _ShowStatus($"PIN must be at least {minPINLength} digits", true);
            return;
        }
        
        // Hash the entered PIN and send to owner for validation
        string enteredHash = _HashPIN(currentPIN, salt);
        _SendPINToOwner(enteredHash, Networking.LocalPlayer.displayName);
        
        // Clear the entered PIN for security
        currentPIN = "";
        _UpdateDisplay();
        _ShowStatus("Verifying...", false);
    }
    
    #endregion
    
    #region Network Methods
    
    /// <summary>
    /// Sends the hashed PIN to the owner for validation
    /// </summary>
    private void _SendPINToOwner(string pinHash, string playerName)
    {
        // Send to owner for validation
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(OwnerValidatePIN), pinHash, playerName);
    }
    
    /// <summary>
    /// Owner validates the PIN hash and authorizes the player if correct
    /// </summary>
    [NetworkCallable]
    public void OwnerValidatePIN(string submittedHash, string playerDisplayName)
    {
        if (!Networking.IsOwner(gameObject))
        {
            Debug.LogWarning("[PINCodePanel] OwnerValidatePIN called on non-owner, ignoring.");
            return;
        }
        
        Debug.Log($"[PINCodePanel] Owner validating PIN for player: {playerDisplayName}");
        
        bool isCorrect = submittedHash == hashedPIN;
        
        if (isCorrect)
        {
            Debug.Log($"[PINCodePanel] PIN correct for {playerDisplayName}, authorizing...");
            // Find the player and authorize them
            VRCPlayerApi player = _GetPlayerByDisplayName(playerDisplayName);
            if (player != null && player.IsValid())
            {
                permissionManager._SetAuthorized(player, true);
                // Notify the player
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ClientReceivePINResult), playerDisplayName, true);
            }
            else
            {
                Debug.LogWarning($"[PINCodePanel] Could not find player {playerDisplayName} to authorize.");
            }
        }
        else
        {
            Debug.Log($"[PINCodePanel] PIN incorrect for {playerDisplayName}");
            // Notify the player of failure
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ClientReceivePINResult), playerDisplayName, false);
            
            // Optionally notify owner
            if (notifyOwnerOnAttempt)
            {
                lastAttemptPlayerName = playerDisplayName;
                pendingOwnerApproval = true;
                RequestSerialization();
            }
        }
    }
    
    /// <summary>
    /// Client receives the result of their PIN attempt
    /// </summary>
    [NetworkCallable]
    public void ClientReceivePINResult(string playerDisplayName, bool success)
    {
        // Only process if this is for the local player
        if (playerDisplayName != Networking.LocalPlayer.displayName)
            return;
        
        if (success)
        {
            _ShowStatus("Access Granted!", false);
            failedAttempts = 0;
            Debug.Log("[PINCodePanel] Access granted!");
        }
        else
        {
            failedAttempts++;
            
            if (failedAttempts >= maxFailedAttempts)
            {
                isLockedOut = true;
                lockoutEndTime = Time.time + lockoutDuration;
                _ShowStatus($"Too many attempts! Locked for {lockoutDuration}s", true);
                Debug.LogWarning($"[PINCodePanel] Locked out due to {failedAttempts} failed attempts.");
            }
            else
            {
                int remainingAttempts = maxFailedAttempts - failedAttempts;
                _ShowStatus($"Incorrect PIN. {remainingAttempts} attempts remaining", true);
            }
        }
    }
    
    /// <summary>
    /// Owner can manually approve a player (fallback if PIN is forgotten)
    /// </summary>
    public void OwnerManualApprove(string playerDisplayName)
    {
        if (!Networking.IsOwner(gameObject))
        {
            Debug.LogWarning("[PINCodePanel] OwnerManualApprove can only be called by owner.");
            return;
        }
        
        VRCPlayerApi player = _GetPlayerByDisplayName(playerDisplayName);
        if (player != null && player.IsValid())
        {
            permissionManager._SetAuthorized(player, true);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ClientReceivePINResult), playerDisplayName, true);
            Debug.Log($"[PINCodePanel] Owner manually approved {playerDisplayName}");
        }
    }
    
    #endregion
    
    #region UI Methods
    
    private void _UpdateDisplay()
    {
        if (displayText == null) return;
        
        // Show asterisks for security
        string maskedPIN = new string('*', currentPIN.Length);
        displayText.text = maskedPIN;
    }
    
    private void _ShowStatus(string message, bool isError)
    {
        if (statusText == null) return;
        
        statusText.text = message;
        statusText.color = isError ? Color.red : Color.green;
        isShowingStatus = true;
        statusEndTime = Time.time + statusDisplayTime;
        
        // Schedule status clearing
        SendCustomEventDelayedSeconds(nameof(_ClearStatus), statusDisplayTime + 0.1f);
    }
    
    public void _ClearStatus()
    {
        if (statusText == null || !isShowingStatus) return;
        if (Time.time < statusEndTime) return; // Still showing a newer message
        
        statusText.text = "";
        isShowingStatus = false;
    }
    
    private void _NotifyOwner(string playerName)
    {
        if (statusText == null) return;
        
        // Only show to owner
        if (Networking.IsOwner(gameObject))
        {
            _ShowStatus($"{playerName} attempted PIN entry", false);
        }
    }
    
    #endregion
    
    #region Utility Methods
    
    /// <summary>
    /// Ensures this panel is owned by the same player who owns the StagePermissionManager
    /// </summary>
    private void _SyncOwnership()
    {
        if (permissionManager == null) return;
        
        // Get the owner of the permission manager
        VRCPlayerApi permissionOwner = Networking.GetOwner(permissionManager.gameObject);
        VRCPlayerApi currentOwner = Networking.GetOwner(gameObject);
        
        if (permissionOwner != null && permissionOwner.IsValid() && 
            currentOwner != null && currentOwner.IsValid() &&
            permissionOwner.playerId != currentOwner.playerId)
        {
            // Transfer ownership to match
            Networking.SetOwner(permissionOwner, gameObject);
            Debug.Log($"[PINCodePanel] Ownership synced to {permissionOwner.displayName}");
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
    
    /// <summary>
    /// Simple hash function for PIN validation
    /// This is not cryptographically secure but sufficient for VRChat world access control
    /// </summary>
    private string _HashPIN(string pin, string saltValue)
    {
        if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(saltValue))
            return "";
        
        string combined = pin + saltValue;
        int hash = 0;
        
        for (int i = 0; i < combined.Length; i++)
        {
            hash = ((hash << 5) - hash) + combined[i];
            hash = hash & hash; // Convert to 32bit integer
        }
        
        // Convert to positive hex string by treating as unsigned via bitwise AND
        // This avoids the uint cast exception in Udon
        long unsignedHash = hash & 0xFFFFFFFFL; // Mask to get unsigned 32-bit value
        return unsignedHash.ToString("X8");
    }
    
    #endregion
    
    #region Editor Utilities
    
    #if !COMPILER_UDONSHARP && UNITY_EDITOR
    
    private void OnValidate()
    {
        // Auto-generate salt if empty
        if (string.IsNullOrEmpty(salt))
        {
            salt = _GenerateRandomSalt();
            Debug.Log("[PINCodePanel] Generated new salt.");
        }
        
        // Auto-hash PIN when changed in editor
        if (!string.IsNullOrEmpty(editorPIN) && _HashPIN(editorPIN, salt) != hashedPIN)
        {
            hashedPIN = _HashPIN(editorPIN, salt);
            Debug.Log($"[PINCodePanel] PIN hashed. Hash: {hashedPIN}");
        }
        
        // Validate min/max lengths
        if (minPINLength < 1) minPINLength = 1;
        if (maxPINLength < minPINLength) maxPINLength = minPINLength;
        
        // Auto-find permission manager if not set
        if (permissionManager == null)
        {
            permissionManager = GameObject.FindObjectOfType<StagePermissionManager>();
            if (permissionManager != null)
            {
                Debug.Log("[PINCodePanel] Auto-found StagePermissionManager.");
            }
        }
    }
    
    /// <summary>
    /// Generates a random salt for PIN hashing
    /// </summary>
    private string _GenerateRandomSalt()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        char[] saltChars = new char[16];
        
        for (int i = 0; i < saltChars.Length; i++)
        {
            saltChars[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
        }
        
        return new string(saltChars);
    }
    
    /// <summary>
    /// Public method to manually regenerate salt (will invalidate current PIN)
    /// </summary>
    [ContextMenu("Regenerate Salt and Rehash PIN")]
    private void RegenerateSalt()
    {
        salt = _GenerateRandomSalt();
        if (!string.IsNullOrEmpty(editorPIN))
        {
            hashedPIN = _HashPIN(editorPIN, salt);
            Debug.Log($"[PINCodePanel] Salt regenerated and PIN rehashed. New hash: {hashedPIN}");
        }
        else
        {
            Debug.LogWarning("[PINCodePanel] No PIN set. Please set editorPIN first.");
        }
    }
    
    #endif
    
    #endregion
}

#if UNITY_EDITOR && !COMPILER_UDONSHARP

/// <summary>
/// Custom editor for PINCodePanel with automatic marker-based setup
/// </summary>
[CustomEditor(typeof(PINCodePanel))]
public class PINCodePanelAutoSetupEditor : Editor
{
    private SerializedProperty permissionManagerProp;
    private SerializedProperty displayTextProp;
    private SerializedProperty statusTextProp;
    private SerializedProperty editorPINProp;
    private SerializedProperty hashedPINProp;
    private SerializedProperty saltProp;
    private SerializedProperty maxPINLengthProp;
    private SerializedProperty minPINLengthProp;
    private SerializedProperty statusDisplayTimeProp;
    private SerializedProperty notifyOwnerOnAttemptProp;
    private SerializedProperty maxFailedAttemptsProp;
    private SerializedProperty lockoutDurationProp;
    
    private bool showSecuritySettings = true;
    private bool showUISettings = true;
    private bool showBehaviorSettings = true;
    private bool showAdvancedSettings = false;
    
    private GUIStyle headerStyle;
    private GUIStyle warningBoxStyle;
    private GUIStyle successBoxStyle;
    private bool stylesInitialized = false;
    
    private void OnEnable()
    {
        permissionManagerProp = serializedObject.FindProperty("permissionManager");
        displayTextProp = serializedObject.FindProperty("displayText");
        statusTextProp = serializedObject.FindProperty("statusText");
        editorPINProp = serializedObject.FindProperty("editorPIN");
        hashedPINProp = serializedObject.FindProperty("hashedPIN");
        saltProp = serializedObject.FindProperty("salt");
        maxPINLengthProp = serializedObject.FindProperty("maxPINLength");
        minPINLengthProp = serializedObject.FindProperty("minPINLength");
        statusDisplayTimeProp = serializedObject.FindProperty("statusDisplayTime");
        notifyOwnerOnAttemptProp = serializedObject.FindProperty("notifyOwnerOnAttempt");
        maxFailedAttemptsProp = serializedObject.FindProperty("maxFailedAttempts");
        lockoutDurationProp = serializedObject.FindProperty("lockoutDuration");
    }
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        InitializeStyles();
        
        // Header
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("PIN Code Panel", headerStyle);
        EditorGUILayout.LabelField("Secure authentication system for StagePermissionManager", EditorStyles.miniLabel);
        EditorGUILayout.Space(5);
        
        // AUTO-SETUP BUTTONS (Main Feature!)
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("🚀 Quick Setup (Using Markers)", EditorStyles.boldLabel);
        
        EditorGUILayout.HelpBox(
            "Add PINPanelMarker components to your UI elements (buttons, text), " +
            "then click 'Auto-Setup from Markers' to automatically configure everything!",
            MessageType.Info);
        
        if (GUILayout.Button("🔧 Auto-Setup from Markers", GUILayout.Height(40)))
        {
            SetupFromMarkers((PINCodePanel)target);
            EditorUtility.SetDirty(target);
        }
        
        EditorGUILayout.Space(5);
        
        if (GUILayout.Button("🔧 Setup All PIN Panels in Scene", GUILayout.Height(30)))
        {
            var allPanels = FindObjectsOfType<PINCodePanel>(true);
            foreach (var panel in allPanels)
            {
                SetupFromMarkers(panel);
                EditorUtility.SetDirty(panel);
            }
            
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Success", 
                $"Set up {allPanels.Length} PIN Panel(s) from markers!", "OK");
        }
        
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(10);
        
        // Setup status
        DrawSetupStatus();
        
        EditorGUILayout.Space(10);
        
        // References Section
        DrawReferencesSection();
        
        EditorGUILayout.Space(10);
        
        // Security Section
        DrawSecuritySection();
        
        EditorGUILayout.Space(10);
        
        // UI Settings Section
        DrawUISection();
        
        EditorGUILayout.Space(10);
        
        // Behavior Settings Section
        DrawBehaviorSection();
        
        EditorGUILayout.Space(10);
        
        // Advanced Section
        DrawAdvancedSection();
        
        serializedObject.ApplyModifiedProperties();
    }
    
    /// <summary>
    /// Main auto-setup logic using PINPanelMarker components
    /// </summary>
    private static void SetupFromMarkers(PINCodePanel panel)
    {
        if (panel == null) return;
        
        // Find all markers in the scene
        PINPanelMarker[] markers = FindObjectsOfType<PINPanelMarker>(true);
        UdonBehaviour panelUdon = panel.GetComponent<UdonBehaviour>();
        
        if (panelUdon == null)
        {
            Debug.LogError("[PINCodePanel] No UdonBehaviour found on PINCodePanel!");
            return;
        }
        
        int buttonsSetup = 0;
        int textsSetup = 0;
        
        foreach (var marker in markers)
        {
            // Check if this marker is for this specific panel
            if (marker.targetPINPanel != null && marker.targetPINPanel != panel)
                continue; // This marker is for a different panel
            
            // If no target specified, auto-assign to nearest panel
            if (marker.targetPINPanel == null)
            {
                marker.targetPINPanel = panel;
                EditorUtility.SetDirty(marker);
            }
            
            switch (marker.elementType)
            {
                // Number buttons
                case PINPanelMarkerType.Button0:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton0));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button1:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton1));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button2:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton2));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button3:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton3));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button4:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton4));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button5:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton5));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button6:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton6));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button7:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton7));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button8:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton8));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.Button9:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnButton9));
                    buttonsSetup++;
                    break;
                
                // Control buttons
                case PINPanelMarkerType.ButtonClear:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnClearPressed));
                    buttonsSetup++;
                    break;
                case PINPanelMarkerType.ButtonEnter:
                    SetupButton(panelUdon, marker, nameof(PINCodePanel.OnEnterPressed));
                    buttonsSetup++;
                    break;
                
                // Text elements
                case PINPanelMarkerType.DisplayText:
                    var displayTMP = marker.GetComponent<TextMeshProUGUI>();
                    if (displayTMP != null)
                    {
                        var so = new SerializedObject(panel);
                        so.FindProperty("displayText").objectReferenceValue = displayTMP;
                        so.ApplyModifiedProperties();
                        textsSetup++;
                    }
                    break;
                
                case PINPanelMarkerType.StatusText:
                    var statusTMP = marker.GetComponent<TextMeshProUGUI>();
                    if (statusTMP != null)
                    {
                        var so = new SerializedObject(panel);
                        so.FindProperty("statusText").objectReferenceValue = statusTMP;
                        so.ApplyModifiedProperties();
                        textsSetup++;
                    }
                    break;
            }
        }
        
        Debug.Log($"<color=green>[PINCodePanel]</color> Auto-setup complete! " +
                  $"Configured {buttonsSetup} buttons and {textsSetup} text elements.");
    }
    
    /// <summary>
    /// Sets up a button to call an Udon method
    /// </summary>
    private static void SetupButton(UdonBehaviour targetUdon, PINPanelMarker marker, string methodName)
    {
        var button = marker.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"[PINCodePanel] No Button component found on {marker.gameObject.name}");
            return;
        }
        
        // Clear existing listeners
        int listenerCount = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < listenerCount; i++)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, 0);
        }
        
        // Add new listener
        var method = targetUdon.GetType().GetMethod("SendCustomEvent",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var action = Delegate.CreateDelegate(typeof(UnityAction<string>), targetUdon, method) as UnityAction<string>;
        
        UnityEventTools.AddStringPersistentListener(button.onClick, action, methodName);
        
        EditorUtility.SetDirty(button);
        
        Debug.Log($"<color=cyan>[PINCodePanel]</color> Button '{marker.gameObject.name}' → {methodName}()");
    }
    
    private void InitializeStyles()
    {
        if (stylesInitialized) return;
        
        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft
        };
        
        warningBoxStyle = new GUIStyle(EditorStyles.helpBox)
        {
            normal = { textColor = new Color(1f, 0.6f, 0f) }
        };
        
        successBoxStyle = new GUIStyle(EditorStyles.helpBox)
        {
            normal = { textColor = new Color(0f, 0.8f, 0f) }
        };
        
        stylesInitialized = true;
    }
    
    private void DrawSetupStatus()
    {
        bool hasPermissionManager = permissionManagerProp.objectReferenceValue != null;
        bool hasDisplayText = displayTextProp.objectReferenceValue != null;
        bool hasPIN = !string.IsNullOrEmpty(editorPINProp.stringValue);
        bool hasHash = !string.IsNullOrEmpty(hashedPINProp.stringValue);
        bool hasSalt = !string.IsNullOrEmpty(saltProp.stringValue);
        
        bool isFullySetup = hasPermissionManager && hasDisplayText && hasPIN && hasHash && hasSalt;
        
        if (isFullySetup)
        {
            EditorGUILayout.HelpBox("✓ PIN Panel is fully configured and ready to use!", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("⚠ Setup incomplete. Please configure the following:", MessageType.Warning);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (!hasPermissionManager) EditorGUILayout.LabelField("• Assign StagePermissionManager reference", EditorStyles.miniLabel);
            if (!hasDisplayText) EditorGUILayout.LabelField("• Assign Display Text (or use markers)", EditorStyles.miniLabel);
            if (!hasPIN) EditorGUILayout.LabelField("• Set a PIN code", EditorStyles.miniLabel);
            if (!hasHash || !hasSalt) EditorGUILayout.LabelField("• PIN will be auto-hashed when set", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
    }
    
    private void DrawReferencesSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
        
        EditorGUILayout.PropertyField(permissionManagerProp, new GUIContent("Permission Manager", 
            "The StagePermissionManager that will register authorized users"));
        
        if (permissionManagerProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("Permission Manager is required!", MessageType.Error);
            
            if (GUILayout.Button("Find StagePermissionManager in Scene"))
            {
                var found = GameObject.FindObjectOfType<StagePermissionManager>();
                if (found != null)
                {
                    permissionManagerProp.objectReferenceValue = found;
                    Debug.Log("[PINCodePanelEditor] Found and assigned StagePermissionManager");
                }
                else
                {
                    EditorUtility.DisplayDialog("Not Found", 
                        "Could not find a StagePermissionManager in the scene.", "OK");
                }
            }
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawSecuritySection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        showSecuritySettings = EditorGUILayout.Foldout(showSecuritySettings, "Security Settings", true, EditorStyles.foldoutHeader);
        
        if (showSecuritySettings)
        {
            EditorGUI.indentLevel++;
            
            EditorGUILayout.HelpBox("The PIN is hashed with a salt for security. Never share the salt or hash!", MessageType.Info);
            
            EditorGUILayout.Space(5);
            
            // PIN Entry
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(editorPINProp, new GUIContent("PIN Code", 
                "Enter your desired PIN (4-8 digits). Will be auto-hashed."));
            
            if (GUILayout.Button("Generate Random", GUILayout.Width(120)))
            {
                editorPINProp.stringValue = GenerateRandomPIN(6);
                serializedObject.ApplyModifiedProperties();
                ((PINCodePanel)target).SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);
            }
            EditorGUILayout.EndHorizontal();
            
            // Validate PIN
            string pin = editorPINProp.stringValue;
            if (!string.IsNullOrEmpty(pin))
            {
                bool isNumeric = System.Text.RegularExpressions.Regex.IsMatch(pin, @"^\d+$");
                bool isValidLength = pin.Length >= minPINLengthProp.intValue && pin.Length <= maxPINLengthProp.intValue;
                
                if (!isNumeric)
                {
                    EditorGUILayout.HelpBox("⚠ PIN should only contain numbers (0-9)", MessageType.Warning);
                }
                else if (!isValidLength)
                {
                    EditorGUILayout.HelpBox($"⚠ PIN must be {minPINLengthProp.intValue}-{maxPINLengthProp.intValue} digits", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox($"✓ PIN is valid ({pin.Length} digits)", MessageType.Info);
                }
            }
            
            EditorGUILayout.Space(5);
            
            // Read-only hash and salt display
            GUI.enabled = false;
            EditorGUILayout.PropertyField(hashedPINProp, new GUIContent("Hashed PIN (Read Only)", 
                "Automatically generated hash of your PIN"));
            EditorGUILayout.PropertyField(saltProp, new GUIContent("Salt (Read Only)", 
                "Random salt for hashing"));
            GUI.enabled = true;
            
            EditorGUILayout.Space(5);
            
            // Regenerate button
            if (GUILayout.Button("Regenerate Salt & Rehash PIN", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("Regenerate Security", 
                    "This will invalidate all current access. Users will need to re-enter the PIN.\n\nContinue?", 
                    "Yes", "Cancel"))
                {
                    ((PINCodePanel)target).SendMessage("RegenerateSalt", SendMessageOptions.DontRequireReceiver);
                    EditorUtility.DisplayDialog("Success", "Salt regenerated and PIN rehashed!", "OK");
                }
            }
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawUISection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        showUISettings = EditorGUILayout.Foldout(showUISettings, "UI Elements", true, EditorStyles.foldoutHeader);
        
        if (showUISettings)
        {
            EditorGUI.indentLevel++;
            
            EditorGUILayout.HelpBox("Use PINPanelMarker components and click 'Auto-Setup from Markers' above, " +
                                   "or manually assign references here.", MessageType.Info);
            
            EditorGUILayout.PropertyField(displayTextProp, new GUIContent("Display Text", 
                "TextMeshProUGUI to show entered PIN as asterisks"));
            
            if (displayTextProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Display Text is required to show the PIN!", MessageType.Warning);
            }
            
            EditorGUILayout.PropertyField(statusTextProp, new GUIContent("Status Text", 
                "TextMeshProUGUI to show feedback messages (optional but recommended)"));
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawBehaviorSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        showBehaviorSettings = EditorGUILayout.Foldout(showBehaviorSettings, "Behavior Settings", true, EditorStyles.foldoutHeader);
        
        if (showBehaviorSettings)
        {
            EditorGUI.indentLevel++;
            
            EditorGUILayout.PropertyField(minPINLengthProp, new GUIContent("Min PIN Length", 
                "Minimum digits required for a valid PIN"));
            EditorGUILayout.PropertyField(maxPINLengthProp, new GUIContent("Max PIN Length", 
                "Maximum digits allowed for a PIN"));
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.PropertyField(maxFailedAttemptsProp, new GUIContent("Max Failed Attempts", 
                "Number of failed attempts before lockout"));
            EditorGUILayout.PropertyField(lockoutDurationProp, new GUIContent("Lockout Duration (seconds)", 
                "How long to lock the panel after max failed attempts"));
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.PropertyField(statusDisplayTimeProp, new GUIContent("Status Display Time", 
                "How long to show status messages"));
            EditorGUILayout.PropertyField(notifyOwnerOnAttemptProp, new GUIContent("Notify Owner on Attempt", 
                "Alert the owner when someone attempts to enter the PIN"));
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawAdvancedSection()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced / Help", true, EditorStyles.foldoutHeader);
        
        if (showAdvancedSettings)
        {
            EditorGUI.indentLevel++;
            
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Open Documentation"))
            {
                string path = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour((PINCodePanel)target));
                string dir = System.IO.Path.GetDirectoryName(path);
                string docPath = System.IO.Path.Combine(dir, "PINCodePanel_README.md");
                
                if (System.IO.File.Exists(docPath))
                {
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(docPath, 1);
                }
                else
                {
                    EditorUtility.DisplayDialog("Documentation Not Found", 
                        "Could not find PINCodePanel_README.md", "OK");
                }
            }
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField("Debug Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Hash Length: {hashedPINProp.stringValue?.Length ?? 0}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Salt Length: {saltProp.stringValue?.Length ?? 0}", EditorStyles.miniLabel);
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private string GenerateRandomPIN(int length)
    {
        string pin = "";
        for (int i = 0; i < length; i++)
        {
            pin += UnityEngine.Random.Range(0, 10).ToString();
        }
        return pin;
    }
}

#endif
