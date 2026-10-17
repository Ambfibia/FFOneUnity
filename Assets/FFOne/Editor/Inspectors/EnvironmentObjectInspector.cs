using ContentPaths = FFOne.Content.ContentPaths;
using FFOne.World;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    [CustomEditor(typeof(EnvironmentObject))]
    public sealed class EnvironmentObjectInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var instance = (EnvironmentObject)target;
            if (instance.OriginalStates.Length == 0) return;
            EditorGUILayout.LabelField("Исходные состояния", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || EditorUtility.IsPersistent(instance)))
                foreach (string state in instance.OriginalStates)
                    if (GUILayout.Button(state)) instance.PlayOriginalState(state);
            if (!Application.isPlaying) EditorGUILayout.HelpBox("Состояния можно проверить на экземпляре объекта в Play Mode.", MessageType.Info);
        }
    }
}
