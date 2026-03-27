#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(global::StaffObjectManager))]
public class StaffObjectManagerEditor : Editor
    {
        private SerializedProperty _stagePermissionManagerProp;
        private SerializedProperty _managedObjectsProp;
        private SerializedProperty _permissionTargetsProp;
        private SerializedProperty _reactiveFlagsProp;

        // UI groups properties
        private SerializedProperty _managedUIGroupsProp;
        private SerializedProperty _uiPermissionTargetsProp;
        private SerializedProperty _uiReactiveFlagsProp;
        private SerializedProperty _uiAnimationCurveProp;
        private SerializedProperty _uiAnimationDurationProp;
        private SerializedProperty _uiControlInteractableProp;
        private SerializedProperty _uiControlBlockRaycastsProp;
        private SerializedProperty _uiControlIgnoreParentGroupsProp;

        private void OnEnable()
        {
            _stagePermissionManagerProp = serializedObject.FindProperty("stagePermissionManager");
            _managedObjectsProp = serializedObject.FindProperty("managedObjects");
            _permissionTargetsProp = serializedObject.FindProperty("objectPermissionTargets");
            _reactiveFlagsProp = serializedObject.FindProperty("objectReactiveToColliders");

            // UI groups
            _managedUIGroupsProp = serializedObject.FindProperty("managedUIGroups");
            _uiPermissionTargetsProp = serializedObject.FindProperty("uiPermissionTargets");
            _uiReactiveFlagsProp = serializedObject.FindProperty("uiReactiveToColliders");
            _uiAnimationCurveProp = serializedObject.FindProperty("uiAnimationCurve");
            _uiAnimationDurationProp = serializedObject.FindProperty("uiAnimationDuration");
            _uiControlInteractableProp = serializedObject.FindProperty("uiControlInteractable");
            _uiControlBlockRaycastsProp = serializedObject.FindProperty("uiControlBlockRaycasts");
            _uiControlIgnoreParentGroupsProp = serializedObject.FindProperty("uiControlIgnoreParentGroups");

            SyncArraySizes();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_stagePermissionManagerProp);
            if (_stagePermissionManagerProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign a StagePermissionManager to receive updates.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            DrawManagedObjectsSection();

            EditorGUILayout.Space();
            DrawUIGroupsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawManagedObjectsSection()
        {
            SyncArraySizes();

            EditorGUILayout.LabelField("Managed Objects", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Permission: Who can see this object (StaffOnly / NonStaffOnly / Both)\n" +
                "Reactive: If checked, object shows ONLY when local player is inside the collider AND has permission. " +
                "If unchecked, object shows whenever permission allows (collider is ignored).",
                MessageType.Info);

            using (new EditorGUI.IndentLevelScope())
            {
                int currentSize = _managedObjectsProp.arraySize;
                int newSize = Mathf.Max(0, EditorGUILayout.DelayedIntField("Count", currentSize));
                if (newSize != currentSize)
                {
                    SetArraySize(_managedObjectsProp, newSize);
                    SetArraySize(_permissionTargetsProp, newSize);
                    SetArraySize(_reactiveFlagsProp, newSize);
                }

                if (_managedObjectsProp.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("Add at least one managed object entry.", MessageType.Info);
                }
                else
                {
                    DrawManagedObjectTable();
                }

                if (GUILayout.Button("Add Managed Object", EditorStyles.miniButton))
                {
                    int index = _managedObjectsProp.arraySize;
                    SetArraySize(_managedObjectsProp, index + 1);
                    SetArraySize(_permissionTargetsProp, index + 1);
                    SetArraySize(_reactiveFlagsProp, index + 1);

                    SerializedProperty targetProp = _permissionTargetsProp.GetArrayElementAtIndex(index);
                    SerializedProperty reactiveProp = _reactiveFlagsProp.GetArrayElementAtIndex(index);
                    targetProp.enumValueIndex = (int)global::PermissionTarget.Both;
                    reactiveProp.boolValue = false;
                }
            }
        }

        private void DrawUIGroupsSection()
        {
            SyncUIArraySizes();

            EditorGUILayout.LabelField("UI Groups (Optional)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "UI groups fade in/out using the animation curve. When shown/hidden, checked properties are automatically set to true/false.\n" +
                "By default: Interactable and BlockRaycasts are controlled.",
                MessageType.Info);

            using (new EditorGUI.IndentLevelScope())
            {
                // Animation settings
                EditorGUILayout.PropertyField(_uiAnimationCurveProp, new GUIContent("Animation Curve"));
                EditorGUILayout.PropertyField(_uiAnimationDurationProp, new GUIContent("Duration (s)"));

                EditorGUILayout.Space();

                int currentSize = _managedUIGroupsProp.arraySize;
                int newSize = Mathf.Max(0, EditorGUILayout.DelayedIntField("Count", currentSize));
                if (newSize != currentSize)
                {
                    SetArraySize(_managedUIGroupsProp, newSize);
                    SetArraySize(_uiPermissionTargetsProp, newSize);
                    SetArraySize(_uiReactiveFlagsProp, newSize);
                    SetArraySize(_uiControlInteractableProp, newSize);
                    SetArraySize(_uiControlBlockRaycastsProp, newSize);
                    SetArraySize(_uiControlIgnoreParentGroupsProp, newSize);
                }

                if (_managedUIGroupsProp.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("No UI groups. Add entries to manage CanvasGroups.", MessageType.Info);
                }
                else
                {
                    DrawUIGroupsTable();
                }

                if (GUILayout.Button("Add UI Group", EditorStyles.miniButton))
                {
                    int index = _managedUIGroupsProp.arraySize;
                    SetArraySize(_managedUIGroupsProp, index + 1);
                    SetArraySize(_uiPermissionTargetsProp, index + 1);
                    SetArraySize(_uiReactiveFlagsProp, index + 1);
                    SetArraySize(_uiControlInteractableProp, index + 1);
                    SetArraySize(_uiControlBlockRaycastsProp, index + 1);
                    SetArraySize(_uiControlIgnoreParentGroupsProp, index + 1);

                    SerializedProperty targetProp = _uiPermissionTargetsProp.GetArrayElementAtIndex(index);
                    SerializedProperty reactiveProp = _uiReactiveFlagsProp.GetArrayElementAtIndex(index);
                    SerializedProperty interactableProp = _uiControlInteractableProp.GetArrayElementAtIndex(index);
                    SerializedProperty blockRaysProp = _uiControlBlockRaycastsProp.GetArrayElementAtIndex(index);
                    targetProp.enumValueIndex = (int)global::PermissionTarget.Both;
                    reactiveProp.boolValue = false;
                    interactableProp.boolValue = true;  // Default: control Interactable
                    blockRaysProp.boolValue = true;     // Default: control BlockRaycasts
                }
            }
        }

        private void DrawManagedObjectTable()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Object", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Permission", EditorStyles.miniBoldLabel, GUILayout.Width(120f));
                EditorGUILayout.LabelField("Reactive", EditorStyles.miniBoldLabel, GUILayout.Width(65f));
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();

                int removeIndex = -1;

                for (int i = 0; i < _managedObjectsProp.arraySize; i++)
                {
                    SerializedProperty objectProp = _managedObjectsProp.GetArrayElementAtIndex(i);
                    SerializedProperty targetProp = _permissionTargetsProp.GetArrayElementAtIndex(i);
                    SerializedProperty reactiveProp = _reactiveFlagsProp.GetArrayElementAtIndex(i);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(objectProp, GUIContent.none);
                    EditorGUILayout.PropertyField(targetProp, GUIContent.none, GUILayout.Width(120f));
                    EditorGUILayout.PropertyField(reactiveProp, GUIContent.none, GUILayout.Width(65f));

                    if (GUILayout.Button("-", EditorStyles.miniButton, GUILayout.Width(20f)))
                    {
                        removeIndex = i;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (removeIndex >= 0)
                {
                    RemoveEntryAt(removeIndex);
                }
            }
        }

        private void DrawUIGroupsTable()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("CanvasGroup", EditorStyles.miniBoldLabel, GUILayout.Width(120f));
                EditorGUILayout.LabelField("Permission", EditorStyles.miniBoldLabel, GUILayout.Width(100f));
                EditorGUILayout.LabelField("Reactive", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
                EditorGUILayout.LabelField("Interactable", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                EditorGUILayout.LabelField("BlockRays", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
                EditorGUILayout.LabelField("IgnoreParent", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();

                int removeIndex = -1;

                for (int i = 0; i < _managedUIGroupsProp.arraySize; i++)
                {
                    SerializedProperty groupProp = _managedUIGroupsProp.GetArrayElementAtIndex(i);
                    SerializedProperty targetProp = _uiPermissionTargetsProp.GetArrayElementAtIndex(i);
                    SerializedProperty reactiveProp = _uiReactiveFlagsProp.GetArrayElementAtIndex(i);
                    SerializedProperty interactableProp = _uiControlInteractableProp.GetArrayElementAtIndex(i);
                    SerializedProperty blockRaysProp = _uiControlBlockRaycastsProp.GetArrayElementAtIndex(i);
                    SerializedProperty ignoreParentProp = _uiControlIgnoreParentGroupsProp.GetArrayElementAtIndex(i);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(groupProp, GUIContent.none, GUILayout.Width(120f));
                    EditorGUILayout.PropertyField(targetProp, GUIContent.none, GUILayout.Width(100f));
                    EditorGUILayout.PropertyField(reactiveProp, GUIContent.none, GUILayout.Width(60f));
                    EditorGUILayout.PropertyField(interactableProp, GUIContent.none, GUILayout.Width(80f));
                    EditorGUILayout.PropertyField(blockRaysProp, GUIContent.none, GUILayout.Width(70f));
                    EditorGUILayout.PropertyField(ignoreParentProp, GUIContent.none, GUILayout.Width(80f));

                    if (GUILayout.Button("-", EditorStyles.miniButton, GUILayout.Width(20f)))
                    {
                        removeIndex = i;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (removeIndex >= 0)
                {
                    RemoveUIEntryAt(removeIndex);
                }
            }
        }

        private void RemoveUIEntryAt(int index)
        {
            RemoveArrayElement(_managedUIGroupsProp, index);
            RemoveArrayElement(_uiPermissionTargetsProp, index);
            RemoveArrayElement(_uiReactiveFlagsProp, index);
            RemoveArrayElement(_uiControlInteractableProp, index);
            RemoveArrayElement(_uiControlBlockRaycastsProp, index);
            RemoveArrayElement(_uiControlIgnoreParentGroupsProp, index);
        }

        private void SyncUIArraySizes()
        {
            if (_managedUIGroupsProp == null || _uiPermissionTargetsProp == null || _uiReactiveFlagsProp == null ||
                _uiControlInteractableProp == null || _uiControlBlockRaycastsProp == null || _uiControlIgnoreParentGroupsProp == null)
            {
                return;
            }

            int max = Mathf.Max(
                Mathf.Max(_managedUIGroupsProp.arraySize, _uiPermissionTargetsProp.arraySize),
                Mathf.Max(Mathf.Max(_uiReactiveFlagsProp.arraySize, _uiControlInteractableProp.arraySize),
                Mathf.Max(_uiControlBlockRaycastsProp.arraySize, _uiControlIgnoreParentGroupsProp.arraySize))
            );
            SetArraySize(_managedUIGroupsProp, max);
            SetArraySize(_uiPermissionTargetsProp, max);
            SetArraySize(_uiReactiveFlagsProp, max);
            SetArraySize(_uiControlInteractableProp, max);
            SetArraySize(_uiControlBlockRaycastsProp, max);
            SetArraySize(_uiControlIgnoreParentGroupsProp, max);
        }

        private void SyncArraySizes()
        {
            if (_managedObjectsProp == null || _permissionTargetsProp == null || _reactiveFlagsProp == null)
            {
                return;
            }

            int max = Mathf.Max(Mathf.Max(_managedObjectsProp.arraySize, _permissionTargetsProp.arraySize), _reactiveFlagsProp.arraySize);
            SetArraySize(_managedObjectsProp, max);
            SetArraySize(_permissionTargetsProp, max);
            SetArraySize(_reactiveFlagsProp, max);
        }

        private void SetArraySize(SerializedProperty property, int size)
        {
            if (property == null)
            {
                return;
            }

            while (property.arraySize < size)
            {
                property.InsertArrayElementAtIndex(property.arraySize);
                SerializedProperty newElement = property.GetArrayElementAtIndex(property.arraySize - 1);
                ResetElement(property, newElement);
            }

            while (property.arraySize > size)
            {
                property.DeleteArrayElementAtIndex(property.arraySize - 1);
            }
        }

        private void RemoveEntryAt(int index)
        {
            RemoveArrayElement(_managedObjectsProp, index);
            RemoveArrayElement(_permissionTargetsProp, index);
            RemoveArrayElement(_reactiveFlagsProp, index);
        }

        private void ResetElement(SerializedProperty property, SerializedProperty element)
        {
            if (property == _managedObjectsProp)
            {
                element.objectReferenceValue = null;
            }
            else if (property == _permissionTargetsProp)
            {
                element.enumValueIndex = (int)global::PermissionTarget.Both;
            }
            else if (property == _reactiveFlagsProp)
            {
                element.boolValue = false;
            }
            else if (property == _managedUIGroupsProp)
            {
                element.objectReferenceValue = null;
            }
            else if (property == _uiPermissionTargetsProp)
            {
                element.enumValueIndex = (int)global::PermissionTarget.Both;
            }
            else if (property == _uiReactiveFlagsProp)
            {
                element.boolValue = false;
            }
            else if (property == _uiControlInteractableProp)
            {
                element.boolValue = true;  // Default: control Interactable
            }
            else if (property == _uiControlBlockRaycastsProp)
            {
                element.boolValue = true;  // Default: control BlockRaycasts
            }
            else if (property == _uiControlIgnoreParentGroupsProp)
            {
                element.boolValue = false;
            }
        }

        private void RemoveArrayElement(SerializedProperty property, int index)
        {
            if (property == null || index < 0 || index >= property.arraySize)
            {
                return;
            }

            property.DeleteArrayElementAtIndex(index);

            if (property == _managedObjectsProp && index < property.arraySize)
            {
                property.DeleteArrayElementAtIndex(index);
            }
        }
    }
#endif
