using UnityEngine;
using UnityEditor;
using Player;
using System.Reflection;

namespace PlayerEditor
{
    /// <summary>
    /// Custom editor for SimpleAnimationPlayer
    /// Auto-fills animation clips and allows preview in Edit mode
    /// </summary>
    [CustomEditor(typeof(SimpleAnimationPlayer))]
    public class SimpleAnimationPlayerEditor : Editor
    {
        private SerializedProperty genderPrefixProp;
        private SerializedProperty idleClipProp;
        private SerializedProperty walkClipProp;
        private SerializedProperty runClipProp;
        private SerializedProperty walkBackClipProp;
        private SerializedProperty runBackClipProp;
        private SerializedProperty strafeLeftClipProp;
        private SerializedProperty strafeRightClipProp;
        private SerializedProperty runDiagonalLeftClipProp;
        private SerializedProperty runDiagonalRightClipProp;
        private SerializedProperty walkBackDiagonalLeftClipProp;
        private SerializedProperty walkBackDiagonalRightClipProp;
        private SerializedProperty turnLeftClipProp;
        private SerializedProperty turnRightClipProp;
        private SerializedProperty spinLeftClipProp;
        private SerializedProperty spinRightClipProp;
        private SerializedProperty jumpClipProp;
        private SerializedProperty swimWalkClipProp;

        private AnimationClip lastPreviewedClip;

        private void OnEnable()
        {
            // Get serialized properties
            genderPrefixProp = serializedObject.FindProperty("genderPrefix");
            idleClipProp = serializedObject.FindProperty("idleClip");
            walkClipProp = serializedObject.FindProperty("walkClip");
            runClipProp = serializedObject.FindProperty("runClip");
            walkBackClipProp = serializedObject.FindProperty("walkBackClip");
            runBackClipProp = serializedObject.FindProperty("runBackClip");
            strafeLeftClipProp = serializedObject.FindProperty("strafeLeftClip");
            strafeRightClipProp = serializedObject.FindProperty("strafeRightClip");
            runDiagonalLeftClipProp = serializedObject.FindProperty("runDiagonalLeftClip");
            runDiagonalRightClipProp = serializedObject.FindProperty("runDiagonalRightClip");
            walkBackDiagonalLeftClipProp = serializedObject.FindProperty("walkBackDiagonalLeftClip");
            walkBackDiagonalRightClipProp = serializedObject.FindProperty("walkBackDiagonalRightClip");
            turnLeftClipProp = serializedObject.FindProperty("turnLeftClip");
            turnRightClipProp = serializedObject.FindProperty("turnRightClip");
            spinLeftClipProp = serializedObject.FindProperty("spinLeftClip");
            spinRightClipProp = serializedObject.FindProperty("spinRightClip");
            jumpClipProp = serializedObject.FindProperty("jumpClip");
            swimWalkClipProp = serializedObject.FindProperty("swimWalkClip");

            // Note: Animation preview removed - not compatible with RuntimeAnimatorPlayer
            // Use Animation Window for previewing clips instead
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw default inspector first
            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            // Auto-fill button
            if (GUILayout.Button("Auto-Fill Animation Clips", GUILayout.Height(30)))
            {
                AutoFillAnimations();
            }

            EditorGUILayout.Space(5);

            // Note: Animation preview removed - not compatible with RuntimeAnimatorPlayer
            // Use Unity's Animation Window (Window > Animation > Animation) to preview clips
            EditorGUILayout.HelpBox("To preview animations, use Unity's Animation Window (Window > Animation > Animation)", MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        private void AutoFillAnimations()
        {
            string genderPrefix = genderPrefixProp.stringValue;

            if (string.IsNullOrEmpty(genderPrefix))
            {
                Debug.LogWarning("Gender prefix is empty. Using 'mp_' (male) as default.");
                genderPrefix = "mp_";
                genderPrefixProp.stringValue = genderPrefix;
            }

            Debug.Log($"🔍 Auto-filling animations with prefix: {genderPrefix}");

            string[] phases = { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };
            string[] searchPaths = { "char", "models/char" };

            // Auto-fill each animation
            idleClipProp.objectReferenceValue = FindClip(SimpleAnimationPlayer.ResolveDefaultIdleAnimationName(genderPrefix), genderPrefix, phases, searchPaths);
            walkClipProp.objectReferenceValue = FindClip("walk", genderPrefix, phases, searchPaths);
            runClipProp.objectReferenceValue = FindClip("run", genderPrefix, phases, searchPaths);

            walkBackClipProp.objectReferenceValue = FindClip("walk_back", genderPrefix, phases, searchPaths);
            if (walkBackClipProp.objectReferenceValue == null)
                walkBackClipProp.objectReferenceValue = FindClip("walk_backward", genderPrefix, phases, searchPaths);
            if (walkBackClipProp.objectReferenceValue == null)
                walkBackClipProp.objectReferenceValue = FindClip("walkback", genderPrefix, phases, searchPaths);

            runBackClipProp.objectReferenceValue = FindClip("run_back", genderPrefix, phases, searchPaths);
            if (runBackClipProp.objectReferenceValue == null)
                runBackClipProp.objectReferenceValue = FindClip("run_backward", genderPrefix, phases, searchPaths);
            if (runBackClipProp.objectReferenceValue == null)
                runBackClipProp.objectReferenceValue = FindClip("runback", genderPrefix, phases, searchPaths);

            strafeLeftClipProp.objectReferenceValue = FindClip("strafe_left", genderPrefix, phases, searchPaths);
            if (strafeLeftClipProp.objectReferenceValue == null)
                strafeLeftClipProp.objectReferenceValue = FindClip("walk_left", genderPrefix, phases, searchPaths);
            if (strafeLeftClipProp.objectReferenceValue == null)
                strafeLeftClipProp.objectReferenceValue = FindClip("strafeleft", genderPrefix, phases, searchPaths);

            strafeRightClipProp.objectReferenceValue = FindClip("strafe_right", genderPrefix, phases, searchPaths);
            if (strafeRightClipProp.objectReferenceValue == null)
                strafeRightClipProp.objectReferenceValue = FindClip("walk_right", genderPrefix, phases, searchPaths);
            if (strafeRightClipProp.objectReferenceValue == null)
                strafeRightClipProp.objectReferenceValue = FindClip("straferight", genderPrefix, phases, searchPaths);

            runDiagonalLeftClipProp.objectReferenceValue = FindClip("run_diagonal_left", genderPrefix, phases, searchPaths);
            runDiagonalRightClipProp.objectReferenceValue = FindClip("run_diagonal_right", genderPrefix, phases, searchPaths);
            walkBackDiagonalLeftClipProp.objectReferenceValue = FindClip("walk_back_diagonal_left", genderPrefix, phases, searchPaths);
            walkBackDiagonalRightClipProp.objectReferenceValue = FindClip("walk_back_diagonal_right", genderPrefix, phases, searchPaths);

            turnLeftClipProp.objectReferenceValue = FindClip("turn_left", genderPrefix, phases, searchPaths);
            turnRightClipProp.objectReferenceValue = FindClip("turn_right", genderPrefix, phases, searchPaths);
            spinLeftClipProp.objectReferenceValue = FindClip("spin_left", genderPrefix, phases, searchPaths);
            spinRightClipProp.objectReferenceValue = FindClip("spin_right", genderPrefix, phases, searchPaths);

            jumpClipProp.objectReferenceValue = FindClip("jump", genderPrefix, phases, searchPaths);
            swimWalkClipProp.objectReferenceValue = FindClip("swim", genderPrefix, phases, searchPaths);

            serializedObject.ApplyModifiedProperties();

            Debug.Log("✅ Auto-fill complete!");
        }

        private AnimationClip FindClip(string animName, string genderPrefix, string[] phases, string[] searchPaths)
        {
            // Try with gender prefix first
            string prefixedName = genderPrefix + animName;

            foreach (string phase in phases)
            {
                foreach (string path in searchPaths)
                {
                    string fullPath = $"{phase}/{path}/{prefixedName}";
                    AnimationClip clip = Resources.Load<AnimationClip>(fullPath);

                    if (clip != null)
                    {
                        return clip;
                    }
                }
            }

            // Try without prefix as fallback
            foreach (string phase in phases)
            {
                foreach (string path in searchPaths)
                {
                    string fullPath = $"{phase}/{path}/{animName}";
                    AnimationClip clip = Resources.Load<AnimationClip>(fullPath);

                    if (clip != null)
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        // Preview methods removed - not compatible with RuntimeAnimatorPlayer
        // Use Unity's Animation Window for previewing animations
    }
}
