
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[System.Serializable]
public enum PermissionTarget
{
    StaffOnly,
    NonStaffOnly,
    Both
}

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class StaffObjectManager : UdonSharpBehaviour
{

    [SerializeField] private StagePermissionManager stagePermissionManager;
    [SerializeField] private GameObject[] managedObjects; // Objects managed by permissions
    [SerializeField] private PermissionTarget[] objectPermissionTargets; // Per-object permission targets
    [SerializeField] private bool[] objectReactiveToColliders; // Per-object: true = show only when (inside collider AND permission allows); false = show whenever permission allows (ignores collider)

    // Optional UI groups (CanvasGoup) that will be faded in/out using an AnimationCurve
    [SerializeField] private CanvasGroup[] managedUIGroups;
    [SerializeField] private PermissionTarget[] uiPermissionTargets;
    [SerializeField] private bool[] uiReactiveToColliders;
    [SerializeField] private AnimationCurve uiAnimationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private float uiAnimationDuration = 0.25f;

    // UI group property control: which CanvasGroup properties to auto-sync with visibility
    // When a UI group shows/hides, these properties are set to true/false accordingly
    [SerializeField] private bool[] uiControlInteractable;
    [SerializeField] private bool[] uiControlBlockRaycasts;
    [SerializeField] private bool[] uiControlIgnoreParentGroups;

    // Cached state to avoid re-accessing permissions frequently
    private bool _isInCollider = false;
    private bool _hasCollider;

    // Animation state for UI groups
    private float[] _uiAnimProgress;
    private float[] _uiAnimStartAlpha;
    private float[] _uiAnimTargetAlpha;
    private bool[] _uiAnimPlaying;

    private void Start()
    {
        if (stagePermissionManager == null)
        {
            Debug.LogError("StagePermissionManager is not assigned.");
            return;
        }

        // Check for collider
        _hasCollider = GetComponent<Collider>() != null;

        // Initialize managed objects based on permissions and per-object reactivity
        bool isStaff = stagePermissionManager.IsLocalPlayerAuthorized;
        for (int i = 0; i < managedObjects.Length; i++)
        {
            bool reactive = objectReactiveToColliders.Length > i ? objectReactiveToColliders[i] : false;
            PermissionTarget target = objectPermissionTargets.Length > i ? objectPermissionTargets[i] : PermissionTarget.Both;

            // Reactive objects: show only when (inside collider AND permission allows)
            // Non-reactive objects: show whenever permission allows (ignore collider)
            bool shouldBeActive;
            if (reactive && _hasCollider)
            {
                // Must be inside collider AND pass permission check
                shouldBeActive = _isInCollider && PermissionMatches(target, isStaff);
            }
            else
            {
                // Just check permission
                shouldBeActive = PermissionMatches(target, isStaff);
            }

            if (managedObjects[i] != null) managedObjects[i].SetActive(shouldBeActive);
        }

        // Initialize UI groups animation state and set initial visibility
        if (managedUIGroups != null && managedUIGroups.Length > 0)
        {
            int uiCount = managedUIGroups.Length;
            _uiAnimProgress = new float[uiCount];
            _uiAnimStartAlpha = new float[uiCount];
            _uiAnimTargetAlpha = new float[uiCount];
            _uiAnimPlaying = new bool[uiCount];

            for (int i = 0; i < uiCount; i++)
            {
                var group = managedUIGroups[i];
                if (group == null) continue;

                bool reactive = uiReactiveToColliders.Length > i ? uiReactiveToColliders[i] : false;
                PermissionTarget target = uiPermissionTargets.Length > i ? uiPermissionTargets[i] : PermissionTarget.Both;

                bool shouldBeVisible;
                if (reactive && _hasCollider)
                {
                    shouldBeVisible = _isInCollider && PermissionMatches(target, isStaff);
                }
                else
                {
                    shouldBeVisible = PermissionMatches(target, isStaff);
                }

                float alpha = shouldBeVisible ? 1f : 0f;
                group.alpha = alpha;
                
                // Only set interactable/blocksRaycasts if control flags are enabled
                if (uiControlInteractable != null && i < uiControlInteractable.Length && uiControlInteractable[i])
                {
                    group.interactable = shouldBeVisible;
                }
                if (uiControlBlockRaycasts != null && i < uiControlBlockRaycasts.Length && uiControlBlockRaycasts[i])
                {
                    group.blocksRaycasts = shouldBeVisible;
                }
                if (uiControlIgnoreParentGroups != null && i < uiControlIgnoreParentGroups.Length && uiControlIgnoreParentGroups[i])
                {
                    group.ignoreParentGroups = shouldBeVisible;
                }

                _uiAnimProgress[i] = 0f;
                _uiAnimStartAlpha[i] = alpha;
                _uiAnimTargetAlpha[i] = alpha;
                _uiAnimPlaying[i] = false;
            }
        }

        stagePermissionManager.SubscribePermissionUpdates(this, nameof(OnPermissionUpdated));

        // Disable Update by default, will be enabled when an animation starts
        enabled = false;

        #if UNITY_EDITOR
        Debug.Log("[StaffObjectManager Debug] Initialized. hasCollider: " + _hasCollider);
        #endif
    }

    private bool PermissionMatches(PermissionTarget target, bool isStaff)
    {
        switch (target)
        {
            case PermissionTarget.StaffOnly: return isStaff;
            case PermissionTarget.NonStaffOnly: return !isStaff;
            case PermissionTarget.Both: return true;
            default: return false;
        }
    }    public void OnPermissionUpdated()
    {
        bool isStaff = stagePermissionManager.IsLocalPlayerAuthorized;

        #if UNITY_EDITOR
        Debug.Log("[StaffObjectManager Debug] OnPermissionUpdated called. IsStaff: " + isStaff + ", _isInCollider: " + _isInCollider);
        #endif

        for (int i = 0; i < managedObjects.Length; i++)
        {
            if (managedObjects[i] == null) continue;

            bool reactive = objectReactiveToColliders.Length > i ? objectReactiveToColliders[i] : false;
            PermissionTarget target = objectPermissionTargets.Length > i ? objectPermissionTargets[i] : PermissionTarget.Both;

            bool shouldBeActive;
            if (reactive && _hasCollider)
            {
                // Must be inside collider AND pass permission check
                shouldBeActive = _isInCollider && PermissionMatches(target, isStaff);
            }
            else
            {
                // Just check permission
                shouldBeActive = PermissionMatches(target, isStaff);
            }

            managedObjects[i].SetActive(shouldBeActive);

            #if UNITY_EDITOR
            Debug.Log($"[StaffObjectManager Debug] Updated object {i} to {shouldBeActive} (reactive: {reactive})");
            #endif
        }

        // Update UI groups with smooth animation
        if (managedUIGroups != null)
        {
            for (int i = 0; i < managedUIGroups.Length; i++)
            {
                var group = managedUIGroups[i];
                if (group == null) continue;

                bool reactive = uiReactiveToColliders.Length > i ? uiReactiveToColliders[i] : false;
                PermissionTarget target = uiPermissionTargets.Length > i ? uiPermissionTargets[i] : PermissionTarget.Both;

                bool shouldBeVisible;
                if (reactive && _hasCollider)
                {
                    shouldBeVisible = _isInCollider && PermissionMatches(target, isStaff);
                }
                else
                {
                    shouldBeVisible = PermissionMatches(target, isStaff);
                }

                float targetAlpha = shouldBeVisible ? 1f : 0f;
                StartUIAnimation(i, targetAlpha);
            }
        }
    }
    
    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        #if UNITY_EDITOR
        Debug.Log("[StaffObjectManager Debug] OnPlayerTriggerEnter called for player: " + (player != null ? player.displayName : "null") + ", hasCollider: " + _hasCollider);
        #endif

        if (!Utilities.IsValid(player) || !player.isLocal)
        {
            #if UNITY_EDITOR
            Debug.Log("[StaffObjectManager Debug] Early return: Invalid player or not local");
            #endif
            return;
        }

        _isInCollider = true;
        bool isStaff = stagePermissionManager.IsLocalPlayerAuthorized;

        #if UNITY_EDITOR
        Debug.Log("[StaffObjectManager Debug] Player entered collider. IsStaff: " + isStaff);
        #endif

        // Update reactive objects: now inside collider, evaluate permission
        for (int i = 0; i < managedObjects.Length; i++)
        {
            bool reactive = objectReactiveToColliders.Length > i ? objectReactiveToColliders[i] : false;
            if (managedObjects[i] == null || !reactive) continue;

            PermissionTarget target = objectPermissionTargets.Length > i ? objectPermissionTargets[i] : PermissionTarget.Both;
            bool shouldBeActive = PermissionMatches(target, isStaff);

            managedObjects[i].SetActive(shouldBeActive);

            #if UNITY_EDITOR
            Debug.Log("[StaffObjectManager Debug] Activated reactive object " + i + " on enter: " + shouldBeActive);
            #endif
        }

        // UI groups: entering collider can reveal reactive UI groups
        if (managedUIGroups != null)
        {
            for (int i = 0; i < managedUIGroups.Length; i++)
            {
                var group = managedUIGroups[i];
                if (group == null) continue;

                bool reactive = uiReactiveToColliders.Length > i ? uiReactiveToColliders[i] : false;
                if (!reactive) continue;

                PermissionTarget target = uiPermissionTargets.Length > i ? uiPermissionTargets[i] : PermissionTarget.Both;
                bool shouldBeVisible = PermissionMatches(target, isStaff);
                StartUIAnimation(i, shouldBeVisible ? 1f : 0f);
            }
        }
    }    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
    #if UNITY_EDITOR
    Debug.Log("[StaffObjectManager Debug] OnPlayerTriggerExit called for player: " + (player != null ? player.displayName : "null") + ", hasCollider: " + _hasCollider);
    #endif

        if (!Utilities.IsValid(player) || !player.isLocal)
        {
            #if UNITY_EDITOR
            Debug.Log("[StaffObjectManager Debug] Early return: Invalid player or not local");
            #endif
            return;
        }

        _isInCollider = false;
        bool isStaff = stagePermissionManager.IsLocalPlayerAuthorized;

        #if UNITY_EDITOR
        Debug.Log("[StaffObjectManager Debug] Player exited collider. IsStaff: " + isStaff);
        #endif

        // Hide reactive objects (since we're outside collider now)
        for (int i = 0; i < managedObjects.Length; i++)
        {
            bool reactive = objectReactiveToColliders.Length > i ? objectReactiveToColliders[i] : false;
            if (managedObjects[i] == null || !reactive) continue;

            managedObjects[i].SetActive(false);

            #if UNITY_EDITOR
            Debug.Log("[StaffObjectManager Debug] Deactivated reactive object " + i + " on exit");
            #endif
        }

        // UI groups: exiting collider hides reactive UI groups
        if (managedUIGroups != null)
        {
            for (int i = 0; i < managedUIGroups.Length; i++)
            {
                var group = managedUIGroups[i];
                if (group == null) continue;

                bool reactive = uiReactiveToColliders.Length > i ? uiReactiveToColliders[i] : false;
                if (!reactive) continue;

                StartUIAnimation(i, 0f);
            }
        }
    }

    private void StartUIAnimation(int index, float targetAlpha)
    {
        if (managedUIGroups == null) return;
        if (index < 0 || index >= managedUIGroups.Length) return;
        var group = managedUIGroups[index];
        if (group == null) return;

        if (_uiAnimProgress == null || _uiAnimProgress.Length <= index)
        {
            // ensure arrays are allocated (defensive)
            int len = managedUIGroups.Length;
            _uiAnimProgress = _uiAnimProgress ?? new float[len];
            _uiAnimStartAlpha = _uiAnimStartAlpha ?? new float[len];
            _uiAnimTargetAlpha = _uiAnimTargetAlpha ?? new float[len];
            _uiAnimPlaying = _uiAnimPlaying ?? new bool[len];
        }

        _uiAnimStartAlpha[index] = group.alpha;
        _uiAnimTargetAlpha[index] = Mathf.Clamp01(targetAlpha);
        _uiAnimProgress[index] = 0f;
        
        bool willPlay = !Mathf.Approximately(_uiAnimStartAlpha[index], _uiAnimTargetAlpha[index]);
        _uiAnimPlaying[index] = willPlay;

        if (willPlay)
        {
            enabled = true;
        }
        else
        {
            // If immediately finished, apply control properties now
            bool visible = _uiAnimTargetAlpha[index] > 0.5f;
            ApplyUIControlProperties(index, visible);
        }
    }

    private void Update()
    {
        if (managedUIGroups == null || _uiAnimPlaying == null) 
        {
            enabled = false;
            return;
        }

        bool anyPlaying = false;
        for (int i = 0; i < managedUIGroups.Length; i++)
        {
            if (_uiAnimPlaying == null || i >= _uiAnimPlaying.Length) break;
            if (!_uiAnimPlaying[i]) continue;

            anyPlaying = true;
            _uiAnimProgress[i] += (uiAnimationDuration > 0f) ? (Time.deltaTime / uiAnimationDuration) : 1f;
            float t = Mathf.Clamp01(_uiAnimProgress[i]);
            float curveT = uiAnimationCurve != null ? uiAnimationCurve.Evaluate(t) : t;
            float alpha = Mathf.Lerp(_uiAnimStartAlpha[i], _uiAnimTargetAlpha[i], curveT);

            var group = managedUIGroups[i];
            if (group == null)
            {
                _uiAnimPlaying[i] = false;
                continue;
            }

            group.alpha = alpha;

            if (t >= 1f - 1e-6f)
            {
                _uiAnimPlaying[i] = false;
                bool visible = _uiAnimTargetAlpha[i] > 0.5f;
                ApplyUIControlProperties(i, visible);
            }
        }

        if (!anyPlaying)
        {
            enabled = false;
        }
    }

    // Public method to be wired to UI Buttons: apply control properties based on visibility
    public void OnUIGroupClicked(int index)
    {
        if (managedUIGroups == null || index < 0 || index >= managedUIGroups.Length) return;
        var group = managedUIGroups[index];
        if (group == null) return;

        // Apply control properties based on current alpha (as a proxy for visibility)
        bool isVisible = group.alpha > 0.5f;
        ApplyUIControlProperties(index, isVisible);
    }

    // Apply control properties (Interactable, BlockRaycasts, IgnoreParentGroups) based on visibility state
    private void ApplyUIControlProperties(int index, bool isVisible)
    {
        if (managedUIGroups == null || index < 0 || index >= managedUIGroups.Length) return;
        var group = managedUIGroups[index];
        if (group == null) return;

        if (uiControlInteractable != null && index < uiControlInteractable.Length && uiControlInteractable[index])
        {
            group.interactable = isVisible;
        }

        if (uiControlBlockRaycasts != null && index < uiControlBlockRaycasts.Length && uiControlBlockRaycasts[index])
        {
            group.blocksRaycasts = isVisible;
        }

        if (uiControlIgnoreParentGroups != null && index < uiControlIgnoreParentGroups.Length && uiControlIgnoreParentGroups[index])
        {
            group.ignoreParentGroups = isVisible;
        }
    }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Editor-only: autopopulate the StagePermissionManager when the component is edited in the inspector
    private void OnValidate()
    {
        // If any managed object is marked reactive, warn when there's no collider or it's not a trigger.
        bool anyReactive = false;
        for (int i = 0; i < objectReactiveToColliders.Length; i++)
        {
            if (objectReactiveToColliders[i])
            {
                anyReactive = true;
                break;
            }
        }

        if (anyReactive)
        {
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                if (!collider.isTrigger)
                {
                    Debug.LogWarning("StaffObjectManager: Collider on this object must be set as a trigger for collider-based features to work properly.");
                }
            }
            else
            {
                Debug.LogWarning("StaffObjectManager: One or more managed objects are reactive but no Collider component found on this object.");
            }
        }

        // Debug logs for configuration
        #if UNITY_EDITOR
        Debug.Log($"[StaffObjectManager Debug] hasCollider: {_hasCollider}");
        Debug.Log($"[StaffObjectManager Debug] StagePermissionManager assigned: {stagePermissionManager != null}");
        #endif

        // Auto-assign manager if not set
        if (stagePermissionManager != null) return;

        // Try to find a StagePermissionManager in the currently open scenes / prefab stage.
        StagePermissionManager found = UnityEngine.Object.FindObjectOfType<StagePermissionManager>();
        if (found != null)
        {
            stagePermissionManager = found;
            // Mark the object dirty so the serialized reference is saved to the scene/prefab
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif
}
