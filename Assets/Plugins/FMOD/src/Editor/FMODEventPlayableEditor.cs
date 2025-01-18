#if UNITY_TIMELINE_EXIST

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;
using System;
using System.Linq;
using System.Reflection;

namespace FMODUnity
{
    [CustomEditor(typeof(FMODEventPlayable))]
    public class FMODEventPlayableEditor : Editor
    {
        private FMODEventPlayable eventPlayable;
        private EditorEventRef editorEventRef;
        private List<EditorParamRef> missingInitialParameterValues = new List<EditorParamRef>();
        private List<EditorParamRef> missingParameterAutomations = new List<EditorParamRef>();

        private SerializedProperty parametersProperty;
        private SerializedProperty parameterLinksProperty;
        private SerializedProperty parameterAutomationProperty;

        private ListView parameterLinksView;
        private ListView initialParameterValuesView;

        private string eventPath;

        public void OnEnable()
        {
            eventPlayable = target as FMODEventPlayable;

            parametersProperty = serializedObject.FindProperty("Parameters");
            parameterLinksProperty = serializedObject.FindProperty("Template.ParameterLinks");
            parameterAutomationProperty = serializedObject.FindProperty("Template.ParameterAutomation");

            parameterLinksView = new ListView(parameterLinksProperty);
            parameterLinksView.drawElementWithLabelCallback = DrawParameterLink;
            parameterLinksView.onCanAddCallback = (list) => missingParameterAutomations.Count > 0;
            parameterLinksView.onAddDropdownCallback = DoAddParameterLinkMenu;
            parameterLinksView.onRemoveCallback = (list) => DeleteParameterAutomation(list.index);

            initialParameterValuesView = new ListView(parametersProperty);
            initialParameterValuesView.drawElementWithLabelCallback = DrawInitialParameterValue;
            initialParameterValuesView.onCanAddCallback = (list) => missingInitialParameterValues.Count > 0;
            initialParameterValuesView.onAddDropdownCallback = DoAddInitialParameterValueMenu;
            initialParameterValuesView.onRemoveCallback = (list) => DeleteInitialParameterValue(list.index);

            RefreshEventRef();

            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public void OnDestroy()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            RefreshMissingParameterLists();

            // This is in case the undo/redo modified any curves on the Playable's clip
            RefreshTimelineEditor();
        }

        private void RefreshEventRef()
        {
            if (eventPath != eventPlayable.EventReference.Path)
            {
                eventPath = eventPlayable.EventReference.Path;

                if (!string.IsNullOrEmpty(eventPath))
                {
                    editorEventRef = EventManager.EventFromPath(eventPath);
                }
                else
                {
                    editorEventRef = null;
                }

                if (editorEventRef != null)
                {
                    eventPlayable.UpdateEventDuration(
                        editorEventRef.IsOneShot ? editorEventRef.Length : float.PositiveInfinity);
                }

                ValidateParameterSettings();
                RefreshMissingParameterLists();
            }
        }

        private void ValidateParameterSettings()
        {
            if (editorEventRef != null)
            {
                var namesToDelete = new List<string>();

                for (var i = 0; i < parametersProperty.arraySize; ++i)
                {
                    var current = parametersProperty.GetArrayElementAtIndex(i);
                    var name = current.FindPropertyRelative("Name");

                    var paramRef = editorEventRef.LocalParameters.FirstOrDefault(p => p.Name == name.stringValue);

                    if (paramRef != null)
                    {
                        var value = current.FindPropertyRelative("Value");
                        value.floatValue = Mathf.Clamp(value.floatValue, paramRef.Min, paramRef.Max);
                    }
                    else
                    {
                        namesToDelete.Add(name.stringValue);
                    }
                }

                foreach(var name in namesToDelete)
                {
                    DeleteInitialParameterValue(name);
                }

                namesToDelete.Clear();

                for (var i = 0; i < parameterLinksProperty.arraySize; ++i)
                {
                    var current = parameterLinksProperty.GetArrayElementAtIndex(i);
                    var name = current.FindPropertyRelative("Name");

                    if (!editorEventRef.LocalParameters.Any(p => p.Name == name.stringValue))
                    {
                        namesToDelete.Add(name.stringValue);
                    }
                }

                foreach(var name in namesToDelete)
                {
                    DeleteParameterAutomation(name);
                }
            }
        }

        private void RefreshMissingParameterLists()
        {
            if (editorEventRef != null)
            {
                serializedObject.Update();

                missingInitialParameterValues =
                    editorEventRef.LocalParameters.Where(p => !InitialParameterValueExists(p.Name)).ToList();
                missingParameterAutomations =
                    editorEventRef.LocalParameters.Where(p => !ParameterLinkExists(p.Name)).ToList();
            }
            else
            {
                missingInitialParameterValues.Clear();
                missingParameterAutomations.Clear();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            RefreshEventRef();

            var eventReference = serializedObject.FindProperty("EventReference");
            var stopType = serializedObject.FindProperty("StopType");

            const string EventReferenceLabel = "Event";

            EditorUtils.DrawLegacyEvent(serializedObject.FindProperty("eventName"), EventReferenceLabel);

            EditorGUILayout.PropertyField(eventReference, new GUIContent(EventReferenceLabel));
            EditorGUILayout.PropertyField(stopType, new GUIContent("Stop Mode"));

            DrawInitialParameterValues();
            DrawParameterAutomations();

            eventPlayable.OnValidate();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInitialParameterValues()
        {
            if (editorEventRef != null)
            {
                parametersProperty.isExpanded =
                    EditorGUILayout.Foldout(parametersProperty.isExpanded, "Initial Parameter Values", true);

                if (parametersProperty.isExpanded)
                {
                    initialParameterValuesView.DrawLayout();
                }
            }
        }

        private void DoAddInitialParameterValueMenu(Rect rect, UnityEditorInternal.ReorderableList list)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("All"), false, () =>
                {
                    foreach (var parameter in missingInitialParameterValues)
                    {
                        AddInitialParameterValue(parameter);
                    }
                });

            menu.AddSeparator(string.Empty);

            foreach (var parameter in missingInitialParameterValues)
            {
                var text = parameter.Name;

                if (ParameterLinkExists(parameter.Name))
                {
                    text += " (automated)";
                }

                menu.AddItem(new GUIContent(text), false,
                    (userData) =>
                    {
                        AddInitialParameterValue(userData as EditorParamRef);
                    },
                    parameter);
            }

            menu.DropDown(rect);
        }

        private void DrawInitialParameterValue(Rect rect, float labelRight, int index, bool active, bool focused)
        {
            if (editorEventRef == null)
            {
                return;
            }

            var property = parametersProperty.GetArrayElementAtIndex(index);

            var name = property.FindPropertyRelative("Name").stringValue;

            var paramRef = editorEventRef.LocalParameters.FirstOrDefault(p => p.Name == name);

            if (paramRef == null)
            {
                return;
            }

            var nameLabelRect = rect;
            nameLabelRect.xMax = labelRight;

            var sliderRect = rect;
            sliderRect.xMin = nameLabelRect.xMax;

            var valueProperty = property.FindPropertyRelative("Value");

            GUI.Label(nameLabelRect, name);

            using (new NoIndentScope())
            {
                valueProperty.floatValue =
                    EditorGUI.Slider(sliderRect, valueProperty.floatValue, paramRef.Min, paramRef.Max);
            }
        }

        private void DrawParameterAutomations()
        {
            if (editorEventRef != null)
            {
                parameterLinksProperty.isExpanded =
                    EditorGUILayout.Foldout(parameterLinksProperty.isExpanded, "Parameter Automations", true);

                if (parameterLinksProperty.isExpanded)
                {
                    parameterLinksView.DrawLayout();
                }
            }
        }

        private void DoAddParameterLinkMenu(Rect rect, UnityEditorInternal.ReorderableList list)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("All"), false, () =>
                {
                    foreach (var parameter in missingParameterAutomations)
                    {
                        AddParameterAutomation(parameter.Name);
                    }
                });

            menu.AddSeparator(string.Empty);

            foreach (var parameter in missingParameterAutomations)
            {
                var text = parameter.Name;

                if (InitialParameterValueExists(parameter.Name))
                {
                    text += " (has initial value)";
                }

                menu.AddItem(new GUIContent(text), false,
                    (userData) =>
                    {
                        AddParameterAutomation(userData as string);
                    },
                    parameter.Name);
            }

            menu.DropDown(rect);
        }

        private void DrawParameterLink(Rect rect, float labelRight, int index, bool active, bool focused)
        {
            if (editorEventRef == null)
            {
                return;
            }

            var linkProperty = parameterLinksProperty.GetArrayElementAtIndex(index);

            var name = linkProperty.FindPropertyRelative("Name").stringValue;

            var paramRef = editorEventRef.LocalParameters.FirstOrDefault(p => p.Name == name);

            if (paramRef == null)
            {
                return;
            }

            var slot = linkProperty.FindPropertyRelative("Slot").intValue;

            var slotName = string.Format("Slot{0:D2}", slot);
            var valueProperty = parameterAutomationProperty.FindPropertyRelative(slotName);

            var slotStyle = GUI.skin.label;

            var slotRect = rect;
            slotRect.width = slotStyle.CalcSize(new GUIContent("slot 00:")).x;

            var nameRect = rect;
            nameRect.xMin = slotRect.xMax;
            nameRect.xMax = labelRight;

            var valueRect = rect;
            valueRect.xMin = nameRect.xMax;

            using (new EditorGUI.PropertyScope(rect, GUIContent.none, valueProperty))
            {
                GUI.Label(slotRect, string.Format("slot {0:D2}:", slot), slotStyle);
                GUI.Label(nameRect, name);

                using (new NoIndentScope())
                {
                    valueProperty.floatValue =
                        EditorGUI.Slider(valueRect, valueProperty.floatValue, paramRef.Min, paramRef.Max);
                }
            }
        }

        private bool InitialParameterValueExists(string name)
        {
            return parametersProperty.ArrayContains("Name", p => p.stringValue == name);
        }

        private bool ParameterLinkExists(string name)
        {
            return parameterLinksProperty.ArrayContains("Name", p => p.stringValue == name);
        }

        private void AddInitialParameterValue(EditorParamRef editorParamRef)
        {
            serializedObject.Update();

            if (!InitialParameterValueExists(editorParamRef.Name))
            {
                DeleteParameterAutomation(editorParamRef.Name);

                parametersProperty.ArrayAdd(p => {
                    p.FindPropertyRelative("Name").stringValue = editorParamRef.Name;
                    p.FindPropertyRelative("Value").floatValue = editorParamRef.Default;
                });

                serializedObject.ApplyModifiedProperties();

                RefreshMissingParameterLists();
            }
        }

        private void DeleteInitialParameterValue(string name)
        {
            serializedObject.Update();

            var index = parametersProperty.FindArrayIndex("Name", p => p.stringValue == name);

            if (index >= 0)
            {
                DeleteInitialParameterValue(index);
            }
        }

        private void DeleteInitialParameterValue(int index)
        {
            serializedObject.Update();

            parametersProperty.DeleteArrayElementAtIndex(index);

            serializedObject.ApplyModifiedProperties();
            RefreshMissingParameterLists();
        }

        private void AddParameterAutomation(string name)
        {
            serializedObject.Update();

            if (!ParameterLinkExists(name))
            {
                var slot = -1;

                for (var i = 0; i < AutomatableSlots.Count; ++i)
                {
                    if (!parameterLinksProperty.ArrayContains("Slot", p => p.intValue == i))
                    {
                        slot = i;
                        break;
                    }
                }

                if (slot >= 0)
                {
                    DeleteInitialParameterValue(name);

                    parameterLinksProperty.ArrayAdd(p => {
                        p.FindPropertyRelative("Name").stringValue = name;
                        p.FindPropertyRelative("Slot").intValue = slot;
                    });

                    serializedObject.ApplyModifiedProperties();

                    RefreshMissingParameterLists();
                    RefreshTimelineEditor();
                }
            }
        }

        private void DeleteParameterAutomation(string name)
        {
            serializedObject.Update();

            var index = parameterLinksProperty.FindArrayIndex("Name", p => p.stringValue == name);

            if (index >= 0)
            {
                DeleteParameterAutomation(index);
            }
        }

        private void DeleteParameterAutomation(int index)
        {
            serializedObject.Update();

            if (eventPlayable.OwningClip.hasCurves)
            {
                var linkProperty = parameterLinksProperty.GetArrayElementAtIndex(index);
                var slotProperty = linkProperty.FindPropertyRelative("Slot");

                var curvesClip = eventPlayable.OwningClip.curves;

                Undo.RecordObject(curvesClip, string.Empty);
                AnimationUtility.SetEditorCurve(curvesClip, GetParameterCurveBinding(slotProperty.intValue), null);
            }

            parameterLinksProperty.DeleteArrayElementAtIndex(index);

            serializedObject.ApplyModifiedProperties();

            RefreshMissingParameterLists();

            RefreshTimelineEditor();
        }

        private static EditorCurveBinding GetParameterCurveBinding(int index)
        {
            var result = new EditorCurveBinding() {
                path = string.Empty,
                type = typeof(FMODEventPlayable),
                propertyName = string.Format("parameterAutomation.slot{0:D2}", index),
            };

            return result;
        }

        private static void RefreshTimelineEditor()
        {
            TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
        }
    }
}
#endif
