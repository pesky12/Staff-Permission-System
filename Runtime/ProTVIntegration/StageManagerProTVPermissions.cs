using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using ArchiTech.ProTV;

namespace ArchiTech.ProTV
{
    /// <summary>
    /// Bridges StagePermissionManager with ProTV authentication.
    /// Grants ProTV super user permissions to users who have staff permissions.
    /// 
    /// This component is only available when ProTV is installed.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StageManagerProTVPermissions : TVAuthPlugin
    {
        [Header("Permission Manager Integration")]
        [Tooltip("Reference to the StagePermissionManager that handles staff permissions.")]
        [SerializeField] private StagePermissionManager permissionManager;

        [Header("Settings")]
        [Tooltip("If true, all authorized users can control the TV. If false, only super users (staff) can control it.")]
        [SerializeField] private bool allowAllAuthorizedUsers = true;

        private bool hasPermissionManager = false;

        public override void Start()
        {
            hasPermissionManager = permissionManager != null;

            if (!hasPermissionManager)
            {
                Warn("No StagePermissionManager found! This plugin will not function properly.");
            }
            else
            {
                // Subscribe to permission updates from the manager
                permissionManager.SubscribePermissionUpdates(this, nameof(OnPermissionsUpdated));
            }

            // Call base class Start to initialize TV connection
            base.Start();
        }

        public override void _TvReady()
        {
            if (IsDebugEnabled)
            {
                Debug($"TV is ready. Permission manager active: {hasPermissionManager}");
            }
            
            // Initial update of TV authorization when TV becomes ready
            UpdateTVAuthorization();
        }

        /// <summary>
        /// Called by StagePermissionManager when permissions are updated.
        /// This is the callback method for the subscription system.
        /// </summary>
        public void OnPermissionsUpdated()
        {
            if (IsDebugEnabled)
            {
                Debug("Permissions updated, refreshing TV authorization.");
            }
            
            UpdateTVAuthorization();
        }

        /// <summary>
        /// Updates the TV's authorization state based on current permissions.
        /// Notifies the TV that authorization has changed.
        /// </summary>
        private void UpdateTVAuthorization()
        {
            if (!hasTV || !hasPermissionManager)
            {
                return;
            }

            // Notify the TV that authorization has changed
            // This will cause the TV to re-check permissions for all users
            tv._Reauthorize();
            
            if (IsDebugEnabled)
            {
                Debug("TV authorization refreshed.");
            }
        }

        /// <summary>
        /// Checks if a user is authorized to use the TV.
        /// If allowAllAuthorizedUsers is true, returns true for anyone authorized by the permission manager.
        /// If false, only super users (staff) are authorized.
        /// </summary>
        public override bool _IsAuthorizedUser(VRCPlayerApi who)
        {
            if (!hasPermissionManager || who == null || !who.IsValid())
            {
                return false;
            }

            // If we allow all authorized users, check the permission manager
            if (allowAllAuthorizedUsers)
            {
                return permissionManager._IsPlayerAuthorized(who);
            }

            // Otherwise, only super users are authorized
            return _IsSuperUser(who);
        }

        /// <summary>
        /// Checks if a user is a super user (staff member).
        /// Super users have full control over the TV.
        /// </summary>
        public override bool _IsSuperUser(VRCPlayerApi who)
        {
            if (!hasPermissionManager || who == null || !who.IsValid())
            {
                return false;
            }

            // Check if the player is authorized through the permission manager
            // The permission manager grants access to staff members
            return permissionManager._IsPlayerAuthorized(who);
        }
    }
}