using System.Linq;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    [CustomEditor(typeof(TutorialActionClip))]
    public sealed class TutorialActionClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var clip = (TutorialActionClip)target;
            if (!clip.Action) { EditorGUILayout.HelpBox("Assign an action document.", MessageType.Error); DrawDefaultInspector(); return; }
            EditorGUILayout.HelpBox("Move the clip to change its time. Resize a sequence to change its duration. Coordinates are original Unity metres. Changes support Undo.", MessageType.Info);
            var token = clip.Action.ToToken();
            EditorGUI.BeginChangeCheck();
            Draw(token, "Action");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(clip.Action, "Edit tutorial cinematic action");
                clip.Action.Import(token.ToString(Newtonsoft.Json.Formatting.None));
                EditorUtility.SetDirty(clip.Action);
            }
        }

        static void Draw(JToken value, string label)
        {
            if (value is JObject obj)
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                foreach (var field in obj.Properties().ToArray()) Draw(field.Value, ObjectNames.NicifyVariableName(field.Name));
                EditorGUI.indentLevel--;
            }
            else if (value is JArray array)
            {
                if (array.Count == 3 && array.All(v => v.Type == JTokenType.Float || v.Type == JTokenType.Integer))
                {
                    var point = EditorGUILayout.Vector3Field(label, new Vector3((float)array[0], (float)array[1], (float)array[2]));
                    array[0] = point.x; array[1] = point.y; array[2] = point.z;
                }
                else { for (int i = 0; i < array.Count; i++) Draw(array[i], label + " " + i); }
            }
            else if (value.Type != JTokenType.Null)
            {
                object replacement = value.Type switch {
                    JTokenType.Boolean => EditorGUILayout.Toggle(label, (bool)value),
                    JTokenType.Integer => EditorGUILayout.LongField(label, (long)value),
                    JTokenType.Float => EditorGUILayout.DoubleField(label, (double)value),
                    _ => EditorGUILayout.TextField(label, (string)value)
                };
                ((JValue)value).Value = replacement;
            }
            else EditorGUILayout.LabelField(label, "None");
        }
    }
}
