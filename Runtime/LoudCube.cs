using System;
using System.Reflection;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using TMPro;
using UnityEditor;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor.Events;
using UnityEngine.Events;
#endif

using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class LoudCube : UdonSharpBehaviour
{
    [SerializeField] private StagePermissionManager stagePermissionManager;

    [Tooltip("Unique name for this LoudCube - used to auto-assign objects with matching markers")]
    [SerializeField] public string loudCubeName;

    [UdonSynced] public bool isEnabled;
    public float defaultGain = 15;
    public float defaultDistanceNear;
    public float defaultDistanceFar = 25;
    public float defaultVolumetricRadius;
    public bool defaultVoiceLowpassFilter = true;
    [Space(5)] [SerializeField] public float gain = 20;
    [SerializeField] public float distanceNear;
    [SerializeField] public float distanceFar = 50;
    [SerializeField] public float volumetricRadius;
    [SerializeField] public bool voiceLowpassFilter;
    
    [Space(5)] 
    [SerializeField] Toggle isEnabledToggle;
    [SerializeField] GameObject[] toggleOnWhenActive;

    [SerializeField] private TextMeshProUGUI boostedPlayersText;

    private bool _isAuthorized;
    private bool _permsManagerInitialized;
    private const int MaxPlayers = 120;
    private int _boostedCount;
    private VRCPlayerApi[] _boostedPlayers = new VRCPlayerApi[MaxPlayers];

    private Collider _colliderCache;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
    private void OnValidate()
    {
        if (loudCubeName != null)
        {
            loudCubeName = loudCubeName.Trim();
        }
    }
#endif
    
    // Editor only public properties for auto-assigning StagePermissionManager
    #if UNITY_EDITOR && !COMPILER_UDONSHARP
    public StagePermissionManager StagePermissionManager
    {
        get => stagePermissionManager;
        set => stagePermissionManager = value;
    }
    
    public Toggle IsEnabledToggle
    {
        get => isEnabledToggle;
        set => isEnabledToggle = value;
    }
    #endif
    
    private void Start()
    {
        // Disable the collider until the permission manager is initialized
        gameObject.GetComponent<Collider>().enabled = false;
        
        stagePermissionManager.SubscribePermissionUpdates(this, nameof(OnPermissionUpdated));
        Debug.Log("[LoudCube] Start: Collider disabled, waiting for permission manager initialization.", this);
    }
    
    public void OnPermissionUpdated()
    {
        if (stagePermissionManager == null) return;

        if (!_permsManagerInitialized) {
            // isAuthorized = stagePermissionManager.IsLocalPlayerAuthorized;
            _permsManagerInitialized = true;

            // Now that we know we are initialized, set the collider which will trigger the collider callbacks
            gameObject.GetComponent<Collider>().enabled = _permsManagerInitialized;
        }
        
        // Show/Hide the toggle and boosted players text based on whether the local player is authorized
        var localPlayerAuthorized = stagePermissionManager._IsPlayerAuthorized(Networking.LocalPlayer);
        if (isEnabledToggle != null)
            isEnabledToggle.gameObject.SetActive(localPlayerAuthorized);
        if (boostedPlayersText != null)
            boostedPlayersText.gameObject.SetActive(localPlayerAuthorized);

        // When permissions change, iterate through boosted players and remove any that are no longer authorized.
        for (var i = _boostedCount - 1; i >= 0; i--)
        {
            var player = _boostedPlayers[i];
            if (player != null && player.IsValid() && !stagePermissionManager._IsPlayerAuthorized(player))
            {
                Debug.Log(
                    $"[LoudCube] OnPermissionUpdated: Player {player.displayName} is no longer authorized. Removing.",
                    this);
                RemovePlayerFromArray(player, ref _boostedPlayers);
                SetPlayersAudioSettings(player, defaultGain, defaultDistanceNear, defaultDistanceFar,
                    defaultVolumetricRadius, defaultVoiceLowpassFilter);
            }
        }
        
        // log the current authorization state
        Debug.Log($"[LoudCube] OnPermissionUpdated: isAuthorized={_isAuthorized}, Collider enabled={_isAuthorized}",
            this);
        
        UpdateBoostedPlayersText();
    }

    public void SetStageToggle()
    {
        Debug.Log("[LoudCube] SetStageToggle: Setting stage toggle");
        if (!stagePermissionManager._IsPlayerAuthorized(Networking.LocalPlayer)) return;
        Debug.Log($"[LoudCube] setStageToggle: Toggling isEnabled from {isEnabled} to {!isEnabled}", this);
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        isEnabled = !isEnabled;
        RequestSerialization();
        OnDeserialization();
    }

    public override void OnDeserialization()
    {
        Debug.Log($"[LoudCube] OnDeserialization: isEnabled={isEnabled}", this);
        isEnabledToggle.SetIsOnWithoutNotify(isEnabled);

        // Enable/disable all objects in toggleOnWhenActive based on isEnabled
        if (toggleOnWhenActive != null)
        {
            foreach (var obj in toggleOnWhenActive)
            {
                if (obj != null)
                    obj.SetActive(isEnabled);
            }
        }

        if (isEnabled)
        {
            // Enable the collider to allow player triggers
            gameObject.GetComponent<Collider>().enabled = true;
            Debug.Log("[LoudCube] OnDeserialization: Collider enabled.", this);
        }
        else
        {
            // Disable the collider to stop player triggers
            gameObject.GetComponent<Collider>().enabled = false;
            Debug.Log("[LoudCube] OnDeserialization: Collider disabled, resetting all players' voice settings.", this);
            // Reset all players' voice settings
            for (var i = 0; i < _boostedCount; i++)
            {
                if (_boostedPlayers[i] != null && _boostedPlayers[i].IsValid())
                {
                    _boostedPlayers[i].SetVoiceGain(defaultGain);
                    _boostedPlayers[i].SetVoiceDistanceNear(defaultDistanceNear);
                    _boostedPlayers[i].SetVoiceDistanceFar(defaultDistanceFar);
                    _boostedPlayers[i].SetVoiceVolumetricRadius(defaultVolumetricRadius);
                    _boostedPlayers[i].SetVoiceLowpass(defaultVoiceLowpassFilter);
                    Debug.Log($"[LoudCube] OnDeserialization: Reset voice settings for player {_boostedPlayers[i].displayName}", this);
                }
            }
            _boostedCount = 0;
        }
        UpdateBoostedPlayersText();
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        Debug.Log($"[LoudCube] OnPlayerTriggerEnter: Player {player.displayName} entered. Perms: {stagePermissionManager._IsPlayerAuthorized(player)}. Boost state: {isEnabled}", this);
        if (!isEnabled) return;
        if (!stagePermissionManager._IsPlayerAuthorized(player)) return;
        Debug.Log($"[LoudCube] OnPlayerTriggerEnter: Player {player.displayName} is authorized. Boosting audio settings.", this);
        AddPlayerToArray(player, ref _boostedPlayers);
        SetPlayersAudioSettings(player, gain, distanceNear, distanceFar, volumetricRadius, voiceLowpassFilter);
    }

    public override void OnPlayerTriggerStay(VRCPlayerApi player)
    {
        if (player == null || !isEnabled || stagePermissionManager == null) return;

        // If the player is not authorized, ignore them.
        if (!stagePermissionManager._IsPlayerAuthorized(player)) return;

        // If the player is already in the boosted list, refresh audio settings and return.
        for (var i = 0; i < _boostedCount; i++)
        {
            if (_boostedPlayers[i] != null && _boostedPlayers[i].IsValid() && _boostedPlayers[i].playerId == player.playerId)
            {
                SetPlayersAudioSettings(player, gain, distanceNear, distanceFar, volumetricRadius, voiceLowpassFilter);
                return;
            }
        }

        // If the player is not already stored, add them and apply settings. This ensures players
        // who were already inside the collider when it became enabled are boosted immediately.
        AddPlayerToArray(player, ref _boostedPlayers);
        SetPlayersAudioSettings(player, gain, distanceNear, distanceFar, volumetricRadius, voiceLowpassFilter);
    }


    private void AddPlayerToArray(VRCPlayerApi player, ref VRCPlayerApi[] array)
    {
        if (player == null || !player.IsValid()) return;

        // Check if player is already in the array
        for (var i = 0; i < _boostedCount; i++)
            if (array[i] != null && array[i].IsValid() && array[i].playerId == player.playerId)
                return;

        if (_boostedCount >= MaxPlayers) return; // Prevent overflow

        array[_boostedCount] = player;
        _boostedCount++;
        UpdateBoostedPlayersText();
    }

    private void RemovePlayerFromArray(VRCPlayerApi player, ref VRCPlayerApi[] array)
    {
        if (player == null || !player.IsValid()) return;

        for (var i = 0; i < _boostedCount; i++)
            if (array[i] != null && array[i].IsValid() && array[i].playerId == player.playerId)
            {
                // Shift elements left
                for (var j = i; j < _boostedCount - 1; j++)
                    array[j] = array[j + 1];

                array[_boostedCount - 1] = null;
                _boostedCount--;
                UpdateBoostedPlayersText();
                return;
            }
    }
    

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return;
        Debug.Log($"[LoudCube] OnPlayerTriggerExit: Player {player.displayName} exited.", this);
        RemovePlayerFromArray(player, ref _boostedPlayers);
        SetPlayersAudioSettings(player, defaultGain, defaultDistanceNear, defaultDistanceFar, defaultVolumetricRadius, defaultVoiceLowpassFilter);
    }

    private void OnDisable()
    {
        Debug.Log("[LoudCube] OnDisable: Resetting all players.", this);
        // Reset all players when the script is disabled
        for (var i = 0; i < _boostedCount; i++)
        {
            if (_boostedPlayers[i] != null && _boostedPlayers[i].IsValid())
            {
                _boostedPlayers[i].SetVoiceGain(defaultGain);
                _boostedPlayers[i].SetVoiceDistanceNear(defaultDistanceNear);
                _boostedPlayers[i].SetVoiceDistanceFar(defaultDistanceFar);
                _boostedPlayers[i].SetVoiceVolumetricRadius(defaultVolumetricRadius);
                Debug.Log($"[LoudCube] OnDisable: Reset voice settings for player {_boostedPlayers[i].displayName}", this);
            }
        }
        _boostedCount = 0;
        UpdateBoostedPlayersText();
    }
    
    private void SetPlayersAudioSettings(VRCPlayerApi player, float newGain, float newDistanceNear, float newDistanceFar, float newVolumetricRadius, bool newLowpassFilter)
    {
        if (player == null || !player.IsValid()) return;

        player.SetVoiceGain(newGain);
        player.SetVoiceDistanceNear(newDistanceNear);
        player.SetVoiceDistanceFar(newDistanceFar);
        player.SetVoiceVolumetricRadius(newVolumetricRadius);
        player.SetVoiceLowpass(newLowpassFilter);
    }
    
    private void UpdateBoostedPlayersText()
    {
        if (boostedPlayersText == null) return;

        var text = "Boosted Players:\n";
        for (var i = 0; i < _boostedCount; i++)
        {
            if (_boostedPlayers[i] != null && _boostedPlayers[i].IsValid())
            {
                text += _boostedPlayers[i].displayName + "\n";
            }
        }
        boostedPlayersText.text = text;
    }

    public VRCPlayerApi[] GetLoudPlayers()
    {
        if (_boostedPlayers == null) return new VRCPlayerApi[0];
        return _boostedPlayers;
    }
    

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, volumetricRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, distanceNear);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, distanceFar);
    }
#endif
}

#if UNITY_EDITOR && !COMPILER_UDONSHARP
[CustomEditor(typeof(LoudCube))]
public class LoudCubeEditor : Editor
{
    SerializedProperty loudCubeNameProp;

    private void OnEnable()
    {
        loudCubeNameProp = serializedObject.FindProperty("loudCubeName");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        LoudCube script = (LoudCube)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Auto-Assignment", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(loudCubeNameProp, new GUIContent("LoudCube Name"));

        if (GUILayout.Button("Auto-Assign All Targets by Name"))
        {
            SetupForInstance(script);
            EditorUtility.SetDirty(target);
        }

        if (GUILayout.Button("Setup All LoudCubes in Scene"))
        {
            SetupAllInScene();
        }

        EditorGUILayout.HelpBox("Place LoudCubeMarker components on GameObjects with matching 'LoudCube Name' to auto-assign them.", MessageType.Info);
        EditorGUILayout.Space();

        DrawPropertiesExcluding(serializedObject, "m_Script", "loudCubeName");

        if (script.StagePermissionManager == null)
        {
            // Try to find a StagePermissionManager in the currently open scenes / prefab stage.
            var found = UnityEngine.Object.FindObjectOfType<StagePermissionManager>();
            if (found != null)
            {
                script.StagePermissionManager = found;
                EditorUtility.SetDirty(script);
                Debug.Log($"[LoudCube] Auto-assigned StagePermissionManager: {found.name}", script);
            }
        }
            
        // Removed the standalone button for setup toggle, as it's now handled by auto-assignment logic
        // if (GUILayout.Button("Setup the button)")) ...
        
        if (loudCubeNameProp != null && loudCubeNameProp.stringValue != null)
        {
            loudCubeNameProp.stringValue = loudCubeNameProp.stringValue.Trim();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void SetupAllInScene()
    {
        var sceneInstances = FindObjectsOfType<LoudCube>(true);
        foreach (var inst in sceneInstances)
        {
            SetupForInstance(inst);
            EditorUtility.SetDirty(inst);
        }
        Debug.Log("[LoudCube] Setup complete for all instances!");
    }

    private static void SetupForInstance(LoudCube script)
    {
        string loudCubeName = NormalizeKey(script.loudCubeName);
        if (string.IsNullOrEmpty(loudCubeName))
        {
            Debug.LogWarning($"[LoudCube] Cannot auto-assign: loudCubeName is empty on {script.gameObject.name}", script);
            return;
        }

        // Find all markers in the scene
        LoudCubeMarker[] markers = FindObjectsOfType<LoudCubeMarker>(true);
        UdonBehaviour thisBehaviour = script.GetComponent<UdonBehaviour>();

        // Temporary collections
        var toggleOnWhenActiveList = new System.Collections.Generic.List<GameObject>();
        Toggle assignedToggle = null;
        TextMeshProUGUI assignedText = null;

        int foundCount = 0;
        foreach (var marker in markers)
        {
            if (NormalizeKey(marker.loudCubeName) != loudCubeName) continue;
            foundCount++;

            switch (marker.targetType)
            {
                case LoudCubeTargetType.UiToggle:
                    var markerToggle = marker.targetUiToggle;
                    if (markerToggle == null) markerToggle = marker.GetComponent<Toggle>();

                    if (markerToggle != null)
                    {
                        assignedToggle = markerToggle;
                        SetupToggleButton(thisBehaviour, markerToggle, nameof(LoudCube.SetStageToggle));
                    }
                    else
                    {
                        Debug.LogWarning($"[LoudCube] Marker on {marker.name} expects UI Toggle but none found/assigned.", marker);
                    }
                    break;

                case LoudCubeTargetType.ToggleWhenActive:
                    var targetGO = marker.targetGameObject;
                    if (targetGO == null) targetGO = marker.gameObject;
                    
                    toggleOnWhenActiveList.Add(targetGO);
                    break;

                case LoudCubeTargetType.BoostedPlayersText:
                    var markerText = marker.targetText;
                    if (markerText == null) markerText = marker.GetComponent<TextMeshProUGUI>();

                    if (markerText != null)
                    {
                        assignedText = markerText;
                    }
                    else
                    {
                        Debug.LogWarning($"[LoudCube] Marker on {marker.name} expects TextMeshProUGUI but none found/assigned.", marker);
                    }
                    break;
            }
        }

        // Apply changes
        SerializedObject so = new SerializedObject(script);
        so.Update();

        SerializedProperty toggleProp = so.FindProperty("isEnabledToggle");
        if (toggleProp != null) toggleProp.objectReferenceValue = assignedToggle;

        SerializedProperty textProp = so.FindProperty("boostedPlayersText");
        if (textProp != null) textProp.objectReferenceValue = assignedText;

        SerializedProperty listProp = so.FindProperty("toggleOnWhenActive");
        if (listProp != null)
        {
            listProp.ClearArray();
            listProp.arraySize = toggleOnWhenActiveList.Count;
            for (int i = 0; i < toggleOnWhenActiveList.Count; i++)
            {
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = toggleOnWhenActiveList[i];
            }
        }

        so.ApplyModifiedProperties();
        Debug.Log($"[LoudCube] Setup '{loudCubeName}' complete. Found {foundCount} markers.");
    }

    private static string NormalizeKey(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static void SetupToggleButton(UdonBehaviour toCall, Toggle button, string methodName)
    {
        // Clear all listeners
        var listeners = button.onValueChanged.GetPersistentEventCount();
        for (var i = 0; i < listeners; i++) UnityEventTools.RemovePersistentListener(button.onValueChanged, 0);

        var method = toCall.GetType().GetMethod("SendCustomEvent",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method != null)
        {
            var action = Delegate.CreateDelegate(typeof(UnityAction<string>), toCall, method) as UnityAction<string>;
            UnityEventTools.AddStringPersistentListener(button.onValueChanged, action, methodName);
        }

        EditorUtility.SetDirty(button);
    }
}
#endif
