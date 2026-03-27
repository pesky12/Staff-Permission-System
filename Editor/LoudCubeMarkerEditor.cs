using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[CustomEditor(typeof(LoudCubeMarker))]
public class LoudCubeMarkerEditor : Editor
{
    private SerializedProperty loudCubeNameProp;
    private SerializedProperty targetTypeProp;
    
    private SerializedProperty targetUiToggleProp;
    private SerializedProperty targetGameObjectProp;
    private SerializedProperty targetTextProp;

    private void OnEnable()
    {
        loudCubeNameProp = serializedObject.FindProperty("loudCubeName");
        targetTypeProp = serializedObject.FindProperty("targetType");
        
        targetUiToggleProp = serializedObject.FindProperty("targetUiToggle");
        targetGameObjectProp = serializedObject.FindProperty("targetGameObject");
        targetTextProp = serializedObject.FindProperty("targetText");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        LoudCubeMarker marker = (LoudCubeMarker)target;

        EditorGUILayout.PropertyField(loudCubeNameProp);
        EditorGUILayout.PropertyField(targetTypeProp);
        EditorGUILayout.Space();

        LoudCubeTargetType type = (LoudCubeTargetType)targetTypeProp.enumValueIndex;

        switch (type)
        {
            case LoudCubeTargetType.UiToggle:
                DrawTargetField(targetUiToggleProp, marker.gameObject, typeof(Toggle), "Target UI Toggle");
                EditorGUILayout.HelpBox("This marker will serve as the main toggle for the LoudCube.", MessageType.Info);
                break;

            case LoudCubeTargetType.ToggleWhenActive:
                // For GameObject, we usually target the one the marker is on, but allow override
                EditorGUILayout.HelpBox("When 'LoudCube' is enabled, this object will be active. Otherwise inactive.", MessageType.Info);
                EditorGUILayout.PropertyField(targetGameObjectProp, new GUIContent("Target GameObject (Override)"));
                if (targetGameObjectProp.objectReferenceValue == null)
                {
                    EditorGUILayout.LabelField("(Currently targeting SELF)", EditorStyles.miniLabel);
                }
                break;

            case LoudCubeTargetType.BoostedPlayersText:
                DrawTargetField(targetTextProp, marker.gameObject, typeof(TextMeshProUGUI), "Target Text");
                EditorGUILayout.HelpBox("This text element will display the list of boosted players.", MessageType.Info);
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawTargetField(SerializedProperty prop, GameObject owner, System.Type type, string label)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(prop, new GUIContent(label));
        
        // Auto-fix button if null but component exists on self
        if (prop.objectReferenceValue == null)
        {
            Component c = owner.GetComponent(type);
            if (c != null)
            {
                if (GUILayout.Button("Use Self", GUILayout.Width(60)))
                {
                    prop.objectReferenceValue = c;
                }
            }
        }
        EditorGUILayout.EndHorizontal();
    }
}
