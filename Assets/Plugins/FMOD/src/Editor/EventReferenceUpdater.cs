using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FMODUnity
{
    public class EventReferenceUpdater : EditorWindow
    {
        public const string MenuPath = "FMOD/Update Event References";

        private const string SearchButtonText = "Scan";

        private const int EventReferenceTransitionVersion = 0x00020200;

        private const BindingFlags DefaultBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly string HelpText =
            string.Format("Click {0} to search your project for obsolete event references.", SearchButtonText);

        private readonly string[] SearchFolders = {
            "Assets",
        };

        private SceneSetup[] sceneSetup;

        private IEnumerator<string> processingState;

        private SearchProgress prefabProgress;
        private SearchProgress sceneProgress;
        private SearchProgress scriptableObjectProgress;

        [SerializeField]
        private List<Asset> assets = new List<Asset>();

        [SerializeField]
        private List<Component> components = new List<Component>();

        [SerializeField]
        private List<Task> tasks = new List<Task>();

        private int executableTaskCount = 0;

        private TreeViewState taskViewState = new TreeViewState();

        private TaskView taskView;

        [NonSerialized]
        private GUIContent status = GUIContent.none;

        [NonSerialized]
        private Task selectedTask;

        [NonSerialized]
        private Vector2 manualDescriptionScrollPosition;

        [NonSerialized]
        private static GUIContent AssetContent = new GUIContent("Asset");
        private static GUIContent ComponentTypeContent = new GUIContent("Component Type");
        private static GUIContent GameObjectContent = new GUIContent("Game Object");

        private string ExecuteButtonText()
        {
            return string.Format("Execute {0} Selected Tasks", executableTaskCount);
        }

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var updater = GetWindow<EventReferenceUpdater>("FMOD Event Reference Updater");
            updater.minSize = new Vector2(800, 600);

            updater.SetStatus(HelpText);

            updater.Show();
        }

        public static bool IsUpToDate()
        {
            return Settings.Instance.LastEventReferenceScanVersion >= EventReferenceTransitionVersion;
        }

        private void BeginSearching()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                tasks.Clear();
                executableTaskCount = 0;
                taskView.SetSelection(new List<int>(), TreeViewSelectionOptions.FireSelectionChanged);
                taskView.Reload();

                processingState = SearchProject();
            }
        }

        private void StopProcessing(bool isComplete)
        {
            processingState = null;

            if (isComplete)
            {
                if (tasks.Count == 0)
                {
                    SetStatus("No required tasks found. Event references are up to date.");
                    Settings.Instance.LastEventReferenceScanVersion = FMOD.VERSION.number;
                    EditorUtility.SetDirty(Settings.Instance);

                    SetupWizardWindow.SetUpdateTaskComplete(SetupWizardWindow.UpdateTaskType.UpdateEventReferences);
                }
                else if (tasks.All(x => x.HasExecuted))
                {
                    SetStatus("Finished executing tasks. New tasks may now be required. Please re-scan your project.");
                }
                else
                {
                    SetStatus("Finished scanning. Please execute the tasks above.");
                }
            }
            else
            {
                SetStatus("Cancelled.");
            }
        }

        private void BeginExecuting()
        {
            var enabledTasks = tasks.Where(t => t.CanExecute()).ToArray();

            if (enabledTasks.Length == 0)
            {
                return;
            }

            var affectedAssets = enabledTasks.Select(t => assets[t.AssetIndex]).Distinct().ToArray();

            var prefabCount = affectedAssets.Count(a => IsPrefab(a.Type));
            var sceneCount = affectedAssets.Count(a => a.Type == AssetType.Scene);

            var warningText = string.Format(
                "Executing these {0} tasks will change {1} prefabs and {2} scenes on disk.\n\n" +
                "Please ensure you have committed any outstanding changes to source control before continuing!",
                enabledTasks.Length, prefabCount, sceneCount);

            if (!EditorUtility.DisplayDialog("Confirm Bulk Changes", warningText, ExecuteButtonText(), "Cancel"))
            {
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                processingState = ExecuteTasks(enabledTasks);
            }
        }

        private void Cancel()
        {
            if (IsProcessing)
            {
                StopProcessing(false);
            }
            else
            {
                Close();
            }
        }

        private bool IsProcessing { get { return processingState != null; } }

        private struct SearchProgress
        {
            private int maximum;
            private int current;

            public float Fraction()
            {
                return (maximum > 0) ? (current / (float)maximum) : 1;
            }

            public void Increment()
            {
                if (current < maximum)
                {
                    ++current;
                }
            }

            public SearchProgress(int total)
            {
                this.maximum = total;
                this.current = 0;
            }
        }

        private IEnumerator<string> SearchProject()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:GameObject", SearchFolders);
            var sceneGuids = AssetDatabase.FindAssets("t:Scene", SearchFolders);
            var scriptableObjectGuids =
                AssetDatabase.FindAssets("t:ScriptableObject", SearchFolders).Distinct().ToArray();

            prefabProgress = new SearchProgress(prefabGuids.Length);
            sceneProgress = new SearchProgress(sceneGuids.Length);
            scriptableObjectProgress = new SearchProgress(scriptableObjectGuids.Length);

            return SearchPrefabs(prefabGuids)
                .Concat(SearchScriptableObjects(scriptableObjectGuids))
                .Concat(SearchScenes(sceneGuids))
                .GetEnumerator();
        }

        private IEnumerable<string> SearchPrefabs(string[] guids)
        {
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                yield return string.Format("Searching {0}", path);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                var assetIndex = -1;

                foreach (var task in SearchGameObject(prefab, prefab))
                {
                    if (assetIndex < 0)
                    {
                        assetIndex = AddAsset(GetAssetType(prefab), path);
                    }

                    task.AssetIndex = assetIndex;

                    AddTask(task);
                }

                prefabProgress.Increment();
            }
        }

        private IEnumerable<string> SearchScriptableObjects(string[] guids)
        {
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                yield return string.Format("Searching {0}", path);

                var scriptableObjects =
                    AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>();

                var assetIndex = -1;

                foreach (var scriptableObject in scriptableObjects)
                {
                    var componentIndex = -1;

                    foreach (var task in GetUpdateTasks(scriptableObject))
                    {
                        if (assetIndex < 0)
                        {
                            assetIndex = AddAsset(AssetType.ScriptableObject, path);
                        }

                        if (componentIndex < 0)
                        {
                            componentIndex = AddComponent(scriptableObject);
                        }

                        task.AssetIndex = assetIndex;
                        task.ComponentIndex = componentIndex;

                        AddTask(task);
                    }
                }

                scriptableObjectProgress.Increment();
            }
        }

        private IEnumerable<string> SearchScenes(string[] guids)
        {
            sceneSetup = EditorSceneManager.GetSceneManagerSetup();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                yield return string.Format("Searching {0}", path);

                var scene = SceneManager.GetSceneByPath(path);

                if (!scene.IsValid())
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                }

                var assetIndex = -1;

                foreach (var gameObject in scene.GetRootGameObjects())
                {
                    foreach (var task in SearchGameObject(gameObject, null))
                    {
                        if (assetIndex < 0)
                        {
                            assetIndex = AddAsset(AssetType.Scene,  path);
                        }

                        task.AssetIndex = assetIndex;

                        AddTask(task);
                    }
                }

                sceneProgress.Increment();
            }

            if (sceneSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
            }
        }

        private IEnumerable<Task> SearchGameObject(GameObject gameObject, GameObject root)
        {
            var behaviours = gameObject.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var behaviour in behaviours)
            {
                var componentIndex = -1;

                foreach (var task in GetUpdateTasks(behaviour))
                {
                    if (componentIndex < 0)
                    {
                        componentIndex = AddComponent(behaviour, root);
                    }

                    task.ComponentIndex = componentIndex;

                    yield return task;
                }
            }
        }

        private static IEnumerable<Task> GetUpdateTasks(UnityEngine.Object target)
        {
            if (target == null)
            {
                return Enumerable.Empty<Task>();
            }
            else if (target is StudioEventEmitter)
            {
                return GetEmitterUpdateTasks(target as StudioEventEmitter);
            }
#if UNITY_TIMELINE_EXIST
            else if (target is FMODEventPlayable)
            {
                return GetPlayableUpdateTasks(target as FMODEventPlayable);
            }
#endif
            else
            {
                return GetGenericUpdateTasks(target);
            }
        }

        private static IEnumerable<Task> GetEmitterUpdateTasks(StudioEventEmitter emitter)
        {
            var hasOwnEvent = true;
            var hasOwnEventReference = true;

            if (PrefabUtility.IsPartOfPrefabInstance(emitter))
            {
                var sourceEmitter = PrefabUtility.GetCorrespondingObjectFromSource(emitter);
                var modifications = PrefabUtility.GetPropertyModifications(emitter);

                if (modifications != null) // GetPropertyModifications returns null if the prefab instance is disconnected
                {
                    hasOwnEvent = modifications.Any(
                        m => m.target == sourceEmitter && m.propertyPath == "Event");

                    hasOwnEventReference = modifications.Any(
                        m => m.target == sourceEmitter && m.propertyPath.StartsWith("EventReference"));
                }
            }

            if (hasOwnEventReference)
            {
                var updateTask = GetUpdateEventReferenceTask(emitter.EventReference, "EventReference");
                if (updateTask != null)
                {
                    yield return updateTask;
                }

                if (hasOwnEvent)
                {
#pragma warning disable 0618 // Suppress warnings about using the obsolete StudioEventEmitter.Event field
                    if (!string.IsNullOrEmpty(emitter.Event))
#pragma warning restore 0618
                    {
                        if (emitter.EventReference.IsNull)
                        {
                            yield return Task.MoveEventToEventReference(emitter);
                        }
                        else
                        {
                            yield return Task.ClearEvent(emitter);
                        }
                    }
                }
            }
            else if (hasOwnEvent)
            {
                yield return Task.MoveEventOverrideToEventReference(emitter);
            }
        }

        private static Task GetUpdateEventReferenceTask(EventReference eventReference, string fieldName,
            string subObjectPath = null)
        {
            if (eventReference.IsNull)
            {
                return null;
            }

            if (Settings.Instance.EventLinkage == EventLinkage.GUID)
            {
                var editorEventRef = EventManager.EventFromGUID(eventReference.Guid);

                if (editorEventRef == null)
                {
                    return null;
                }

                if (eventReference.Path != editorEventRef.Path)
                {
                    return Task.UpdateEventReferencePath(subObjectPath, fieldName, eventReference.Path,
                        editorEventRef.Path, eventReference.Guid);
                }
            }
            else if (Settings.Instance.EventLinkage == EventLinkage.Path)
            {
                var editorEventRef = EventManager.EventFromPath(eventReference.Path);

                if (editorEventRef != null)
                {
                    if (eventReference.Guid != editorEventRef.Guid)
                    {
                        return Task.UpdateEventReferenceGuid(subObjectPath, fieldName, eventReference.Guid,
                            editorEventRef.Guid, eventReference.Path);
                    }
                }
                else if (!eventReference.Guid.IsNull)
                {
                    editorEventRef = EventManager.EventFromGUID(eventReference.Guid);

                    if (editorEventRef != null)
                    {
                        return Task.UpdateEventReferencePath(subObjectPath, fieldName, eventReference.Path,
                            editorEventRef.Path, eventReference.Guid);
                    }
                }
            }
            else
            {
                throw new NotSupportedException("Unrecognized EventLinkage: " + Settings.Instance.EventLinkage);
            }

            return null;
        }

#if UNITY_TIMELINE_EXIST
        private static IEnumerable<Task> GetPlayableUpdateTasks(FMODEventPlayable playable)
        {
            var updateTask = GetUpdateEventReferenceTask(playable.EventReference, "EventReference");
            if (updateTask != null)
            {
                yield return updateTask;
            }

#pragma warning disable 0618 // Suppress warnings about using the obsolete FMODEventPlayable.eventName field
            if (!string.IsNullOrEmpty(playable.eventName))
#pragma warning restore 0618
            {
                if (playable.EventReference.IsNull)
                {
                    yield return Task.MoveEventNameToEventReference(playable);
                }
                else
                {
                    yield return Task.ClearEventName(playable);
                }
            }
        }
#endif

#pragma warning disable 0618 // Suppress a warning about using the obsolete EventRefAttribute class
        private static bool IsEventRef(FieldInfo field)
        {
            return field.FieldType == typeof(string) && EditorUtils.HasAttribute<EventRefAttribute>(field);
        }
#pragma warning restore 0618

        private static T GetCustomAttribute<T>(FieldInfo field)
            where T : Attribute
        {
            return Attribute.GetCustomAttribute(field, typeof(T)) as T;
        }

        private static readonly Assembly SystemAssembly = typeof(object).Assembly;

        private static IEnumerable<Task> GetGenericUpdateTasks(object target, string subObjectPath = null, IEnumerable<object> parents = null)
        {
            var targetType = target.GetType();
            var fields = targetType.GetFields(DefaultBindingFlags);

            var oldFields = new List<FieldInfo>();
            var newFields = new List<FieldInfo>();
            var subObjectFields = new List<FieldInfo>();

            foreach (var f in fields)
            {
                if (IsEventRef(f))
                {
                    oldFields.Add(f);
                }
                else if (f.FieldType == typeof(EventReference))
                {
                    newFields.Add(f);
                }
                else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(f.FieldType))
                {
                    subObjectFields.Add(f);
                }
                else if (f.FieldType.Assembly != SystemAssembly && !f.FieldType.IsEnum)
                {
                    subObjectFields.Add(f);
                }
            }

            var initialOldFieldCount = oldFields.Count;

            // Remove empty [EventRef] fields
            for (var i = 0; i < oldFields.Count; )
            {
                var oldField = oldFields[i];

                if (string.IsNullOrEmpty(oldField.GetValue(target) as string))
                {
                    oldFields.RemoveAt(i);

                    yield return Task.RemoveEmptyEventRefField(subObjectPath, oldField.Name, targetType.Name);
                }
                else
                {
                    ++i;
                }
            }

            // Handle conflicts where multiple [EventRef] fields have the same migration target
#pragma warning disable 0618 // Suppress a warning about using the obsolete EventRefAttribute class
            var conflictingGroups = oldFields
                .GroupBy(f => GetCustomAttribute<EventRefAttribute>(f).MigrateTo)
                .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
                .ToArray();
#pragma warning restore 0618

            foreach (var group in conflictingGroups)
            {
                foreach (var field in group)
                {
                    oldFields.Remove(field);
                }

                yield return Task.FixMigrationTargetConflict(subObjectPath, targetType.Name, group.Select(f => f.Name));
            }

            // Handle [EventRef] fields with MigrateTo set
#pragma warning disable 0618 // Suppress a warning about using the obsolete EventRefAttribute class
            for (var i = 0; i < oldFields.Count; )
            {
                var oldField = oldFields[i];

                var attribute = GetCustomAttribute<EventRefAttribute>(oldField);

                if (!string.IsNullOrEmpty(attribute.MigrateTo))
                {
                    oldFields.RemoveAt(i);

                    var oldValue = oldField.GetValue(target) as string;

                    var newField = newFields.FirstOrDefault(f => f.Name == attribute.MigrateTo);

                    if (newField != null)
                    {
                        var newValue = (EventReference)newField.GetValue(target);

                        if (newValue.IsNull)
                        {
                            yield return Task.MoveEventRefFieldToEventReferenceField(subObjectPath, oldValue,
                                oldField.Name, newField.Name);
                        }
                        else
                        {
                            yield return Task.RemoveEventRefField(subObjectPath, oldValue, oldField.Name, targetType.Name);
                        }
                    }
                    else
                    {
                        yield return Task.AddMigrationTarget(subObjectPath, oldValue, oldField.Name, targetType.Name,
                            attribute.MigrateTo);
                    }
                }
                else
                {
                    ++i;
                }
            }
#pragma warning restore 0618

            // Auto-migrate if there is a single old field that hasn't been handled already,
            // and there is a single new field
            if (initialOldFieldCount == 1 && oldFields.Count == 1 && newFields.Count == 1)
            {
                var oldField = oldFields[0];

                var oldValue = oldField.GetValue(target) as string;

                var newField = newFields[0];

                var newValue = (EventReference)newField.GetValue(target);

                if (newValue.IsNull)
                {
                    yield return Task.MoveEventRefFieldToEventReferenceField(subObjectPath, oldValue,
                        oldField.Name, newField.Name);
                }
                else
                {
                    yield return Task.RemoveEventRefField(subObjectPath, oldValue, oldField.Name, targetType.Name);
                }

                oldFields.RemoveAt(0);
            }

            // Handle old fields with no migration target
            foreach (var oldField in oldFields)
            {
                yield return Task.AddMigrationTarget(subObjectPath, oldField.GetValue(target) as string, oldField.Name,
                    targetType.Name);
            }

            // Check new fields for GUID/path mismatches
            foreach (var newField in newFields)
            {
                var eventReference = (EventReference)newField.GetValue(target);

                var updateTask = GetUpdateEventReferenceTask(eventReference, newField.Name, subObjectPath);
                if (updateTask != null)
                {
                    yield return updateTask;
                }
            }

            // Check sub-object fields
            if (subObjectFields.Any())
            {
                if (parents == null)
                {
                    parents = Enumerable.Empty<object>();
                }

                parents = parents.Append(target);

                foreach (var subObjectField in subObjectFields)
                {
                    var value = subObjectField.GetValue(target);

                    if (value != null && !parents.Contains(value))
                    {
                        if (value is System.Collections.IEnumerable)
                        {
                            var index = 0;
                            foreach (var item in value as System.Collections.IEnumerable)
                            {
                                foreach (var t in GetGenericUpdateTasks(item, FieldPath(subObjectPath, subObjectField.Name, index), parents))
                                {
                                    yield return t;
                                }
                                index++;
                            }
                        }
                        else
                        {
                            foreach (var t in GetGenericUpdateTasks(value, FieldPath(subObjectPath, subObjectField.Name), parents))
                            {
                                yield return t;
                            }
                        }
                    }
                }
            }
        }

        private IEnumerator<string> ExecuteTasks(Task[] tasks)
        {
            sceneSetup = EditorSceneManager.GetSceneManagerSetup();

            foreach (var task in tasks)
            {
                yield return string.Format("Executing: {0}", task);

                ExecuteTask(task, SavePolicy.AutoSave);
            }

            EditorSceneManager.SaveOpenScenes();
            UpdateExecutableTaskCount();

            if (sceneSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
            }
        }

        private enum AssetType
        {
            Scene,
            Prefab,
            PrefabModel,
            PrefabVariant,
            ScriptableObject,
        }

        private static bool IsPrefab(AssetType type)
        {
            return type == AssetType.Prefab
                || type == AssetType.PrefabModel
                || type == AssetType.PrefabVariant;
        }

        private static AssetType GetAssetType(GameObject gameObject)
        {
            var prefabType = PrefabUtility.GetPrefabAssetType(gameObject);

            if (prefabType == PrefabAssetType.Model)
            {
                return AssetType.PrefabModel;
            }
            else if (prefabType == PrefabAssetType.Variant)
            {
                return AssetType.PrefabVariant;
            }
            else
            {
                return AssetType.Prefab;
            }
        }

        private enum EnableState
        {
            Enabled,
            Disabled,
            Mixed,
        }

        [Serializable]
        private class Asset
        {
            public AssetType Type;
            public string Path;
            public EnableState EnableState;
        }

        [Serializable]
        private class Component
        {
            public GlobalObjectId GameObjectID;
            public string Type;
            public string Path;
            public string ScriptPath;
        }

        [Serializable]
        private class Task
        {
            public bool Enabled = true;
            public int AssetIndex; // index into the assets list
            public int ComponentIndex; // index into the components list

            private Type type;
            private string[] Data;

            private const string EmitterEventField = "Event";
            private const string EmitterEventReferenceField = "EventReference";
            private const string PlayableEventNameField = "eventName";
            private const string PlayableEventReferenceField = "eventReference";

            private delegate string DescriptionDelegate(string[] data);
            private delegate string ManualInstructionsDelegate(string[] data, Component component);
            private delegate bool IsValidDelegate(string[] data, UnityEngine.Object target);
            private delegate void ExecuteDelegate(string[] data, UnityEngine.Object target);

            private static readonly Delegates[] Implementations;

            private enum Type
            {
                EmitterClearEvent,
                EmitterMoveEventToEventReference,
                EmitterMoveEventOverrideToEventReference,
                PlayableClearEventName,
                PlayableMoveEventNameToEventReference,
                GenericRemoveEventRefField,
                GenericRemoveEmptyEventRefField,
                GenericMoveEventRefFieldToEventReferenceField,
                GenericAddMigrationTarget,
                GenericUpdateEventReferencePath,
                GenericUpdateEventReferenceGuid,
                GenericFixMigrationTargetConflict,

                Count
            }

            public bool HasExecuted { get; private set; }

            // Suppress warnings about using the obsolete StudioEventEmitter.Event and FMODEventPlayable.eventName fields
#pragma warning disable 0618
            public static Task ClearEvent(StudioEventEmitter emitter)
            {
                return new Task()
                {
                    type = Type.EmitterClearEvent,
                    Data = new string[] { emitter.Event },
                };
            }

#if UNITY_TIMELINE_EXIST
            public static Task ClearEventName(FMODEventPlayable playable)
            {
                return new Task()
                {
                    type = Type.PlayableClearEventName,
                    Data = new string[] { playable.eventName },
                };
            }
#endif

            public static Task MoveEventToEventReference(StudioEventEmitter emitter)
            {
                return new Task()
                {
                    type = Type.EmitterMoveEventToEventReference,
                    Data = new string[] { emitter.Event },
                };
            }


#if UNITY_TIMELINE_EXIST
            public static Task MoveEventNameToEventReference(FMODEventPlayable playable)
            {
                return new Task()
                {
                    type = Type.PlayableMoveEventNameToEventReference,
                    Data = new string[] { playable.eventName },
                };
            }
#endif

            public static Task MoveEventOverrideToEventReference(StudioEventEmitter emitter)
            {
                return new Task()
                {
                    type = Type.EmitterMoveEventOverrideToEventReference,
                    Data = new string[] { emitter.Event },
                };
            }
#pragma warning restore 0618

            public static Task RemoveEventRefField(string subObjectPath, string value, string fieldName, string targetType)
            {
                return new Task()
                {
                    type = Type.GenericRemoveEventRefField,
                    Data = new string[] { subObjectPath, value, fieldName, targetType },
                };
            }

            public static Task RemoveEmptyEventRefField(string subObjectPath, string fieldName, string targetType)
            {
                return new Task()
                {
                    type = Type.GenericRemoveEmptyEventRefField,
                    Data = new string[] { subObjectPath, fieldName, targetType },
                };
            }

            public static Task MoveEventRefFieldToEventReferenceField(
                string subObjectPath, string value, string oldFieldName, string newFieldName)
            {
                return new Task()
                {
                    type = Type.GenericMoveEventRefFieldToEventReferenceField,
                    Data = new string[] { subObjectPath, value, oldFieldName, newFieldName },
                };
            }

            public static Task AddMigrationTarget(string subObjectPath, string value, string fieldName, string targetType,
                string targetName = null)
            {
                return new Task()
                {
                    type = Type.GenericAddMigrationTarget,
                    Data = new string[] { subObjectPath, value, fieldName, targetType, targetName },
                };
            }

            public static Task UpdateEventReferencePath(string subObjectPath, string fieldName,
                string oldPath, string newPath, FMOD.GUID guid)
            {
                return new Task()
                {
                    type = Type.GenericUpdateEventReferencePath,
                    Data = new string[] { subObjectPath, fieldName, oldPath, newPath, guid.ToString() },
                };
            }

            public static Task UpdateEventReferenceGuid(string subObjectPath, string fieldName,
                FMOD.GUID oldGuid, FMOD.GUID newGuid, string path)
            {
                return new Task()
                {
                    type = Type.GenericUpdateEventReferenceGuid,
                    Data = new string[] { subObjectPath, fieldName, oldGuid.ToString(), newGuid.ToString(), path },
                };
            }

            public static Task FixMigrationTargetConflict(string subObjectPath, string targetType,
                IEnumerable<string> fieldNames)
            {
                return new Task()
                {
                    type = Type.GenericFixMigrationTargetConflict,
                    Data = (new string[] { subObjectPath, targetType }).Concat(fieldNames).ToArray(),
                };
            }

            private struct Delegates
            {
                public DescriptionDelegate Description;
                public ManualInstructionsDelegate ManualInstructions;
                public IsValidDelegate IsValid;
                public ExecuteDelegate Execute;
            }

            private static void Implement(Type type,
                DescriptionDelegate Description,
                IsValidDelegate IsValid,
                ExecuteDelegate Execute,
                ManualInstructionsDelegate ManualInstructions = null)
            {
                Implementations[(int)type] = new Delegates() {
                    Description = Description,
                    IsValid = IsValid,
                    Execute = Execute,
                    ManualInstructions = ManualInstructions,
                };
            }

            private Delegates GetDelegates()
            {
                return Implementations[(int)type];
            }

            static Task()
            {
                Implementations = new Delegates[(int)Type.Count];

                // Suppress warnings about using the obsolete StudioEventEmitter.Event
                // and FMODEventPlayable.eventName fields
#pragma warning disable 0618

                Implement(Type.EmitterClearEvent,
                    Description: (data) => {
                        return string.Format("Clear <b>'{0}'</b> from the <b>{1}</b> field", data[0], EmitterEventField);
                    },
                    IsValid: (data, target) => {
                        var emitter = target as StudioEventEmitter;
                        return emitter != null && emitter.Event == data[0] && !emitter.EventReference.IsNull;
                    },
                    Execute: (data, target) => {
                        var emitter = target as StudioEventEmitter;

                        emitter.Event = string.Empty;
                        EditorUtility.SetDirty(emitter);
                    }
                );
                Implement(Type.EmitterMoveEventToEventReference,
                    Description: (data) => {
                        return string.Format("Move <b>'{0}'</b> from <b>{1}</b> to <b>{2}</b>",
                            data[0], EmitterEventField, EmitterEventReferenceField);
                    },
                    IsValid: (data, target) => {
                        var emitter = target as StudioEventEmitter;
                        return emitter != null && emitter.Event == data[0] && emitter.EventReference.IsNull;
                    },
                    Execute: (data, target) => {
                        var emitter = target as StudioEventEmitter;

                        emitter.EventReference.Path = emitter.Event;
                        emitter.Event = string.Empty;

                        var eventRef = EventManager.EventFromPath(emitter.EventReference.Path);

                        if (eventRef != null)
                        {
                            emitter.EventReference.Guid = eventRef.Guid;
                        }

                        EditorUtility.SetDirty(emitter);
                    }
                );
                Implement(Type.EmitterMoveEventOverrideToEventReference,
                    Description: (data) => {
                        return string.Format("Move prefab override <b>'{0}'</b> from <b>{1}</b> to <b>{2}</b>",
                            data[0], EmitterEventField, EmitterEventReferenceField);
                    },
                    IsValid: (data, target) => {
                        if (!PrefabUtility.IsPartOfPrefabInstance(target))
                        {
                            return false;
                        }

                        var emitter = target as StudioEventEmitter;

                        if (emitter == null)
                        {
                            return false;
                        }

                        var sourceEmitter = PrefabUtility.GetCorrespondingObjectFromSource(emitter);

                        if (sourceEmitter == null)
                        {
                            return false;
                        }

                        var modifications = PrefabUtility.GetPropertyModifications(emitter);
                        var eventOverride = modifications.FirstOrDefault(
                            m => m.target == sourceEmitter && m.propertyPath == "Event");

                        if (eventOverride == null || eventOverride.value != data[0])
                        {
                            return false;
                        }

                        var hasEventReferenceOverride = modifications.Any(
                            m => m.target == sourceEmitter && m.propertyPath.StartsWith("EventReference"));

                        if (hasEventReferenceOverride)
                        {
                            return false;
                        }

                        return true;
                    },
                    Execute: (data, target) => {
                        var emitter = target as StudioEventEmitter;

                        var path = emitter.Event;

                        // Clear the Event override
                        var sourceEmitter = PrefabUtility.GetCorrespondingObjectFromSource(emitter);
                        var modifications = PrefabUtility.GetPropertyModifications(emitter);

                        modifications = modifications
                            .Where(m => !(m.target == sourceEmitter && m.propertyPath == "Event"))
                            .ToArray();

                        PrefabUtility.SetPropertyModifications(emitter, modifications);

                        // Set the EventReference override
                        emitter.EventReference.Path = path;

                        var eventRef = EventManager.EventFromPath(path);

                        if (eventRef != null)
                        {
                            emitter.EventReference.Guid = eventRef.Guid;
                        }

                        EditorUtility.SetDirty(emitter);
                    }
                );

#if UNITY_TIMELINE_EXIST
                Implement(Type.PlayableClearEventName,
                    Description: (data) => {
                        return string.Format("Clear <b>'{0}'</b> from the <b>{1}</b> field", data[0], PlayableEventNameField);
                    },
                    IsValid: (data, target) => {
                        var playable = target as FMODEventPlayable;
                        return playable != null && playable.eventName == data[0] && !playable.EventReference.IsNull;
                    },
                    Execute: (data, target) => {
                        var playable = target as FMODEventPlayable;

                        playable.eventName = string.Empty;
                        EditorUtility.SetDirty(playable);
                    }
                );
                Implement(Type.PlayableMoveEventNameToEventReference,
                    Description: (data) => {
                        return string.Format("Move <b>'{0}'</b> from <b>{1}</b> to <b>{2}</b>",
                            data[0], PlayableEventNameField, PlayableEventReferenceField);
                    },
                    IsValid: (data, target) => {
                        var playable = target as FMODEventPlayable;
                        return playable != null && playable.eventName == data[0] && playable.EventReference.IsNull;
                    },
                    Execute: (data, target) => {
                        var playable = target as FMODEventPlayable;

                        playable.EventReference.Path = playable.eventName;
                        playable.eventName = string.Empty;

                        var eventRef = EventManager.EventFromPath(playable.EventReference.Path);

                        if (eventRef != null)
                        {
                            playable.EventReference.Guid = eventRef.Guid;
                        }

                        EditorUtility.SetDirty(playable);
                    }
                );
#endif
                Implement(Type.GenericRemoveEventRefField,
                    Description: (data) => {
                        return string.Format("Remove field <b>{0}</b>", FieldPath(data[0], data[2]));
                    },
                    ManualInstructions: (data, component) => {
                        var subObjectPath = data[0];
                        var value = data[1];
                        var fieldName = data[2];
                        var targetType = data[3];

                        var fieldPath = FieldPath(subObjectPath, fieldName);

                        return string.Format(
                            "The {0} field on component {1} has value '{2}', " +
                            "but the corresponding EventReference field already has a value.\n" +
                            "* Ensure no other instances of the {3} type are using the {4} field\n" +
                            "* Edit the definition of the {3} type and remove the {4} field",
                            fieldPath, component.Type, value, targetType, fieldName);
                    },
                    IsValid: (data, rootObject) => {
                        var target = FindSubObject(rootObject, data[0]);

                        var targetType = target.GetType();
                        var field = targetType.GetField(data[2]);

                        return field != null && IsEventRef(field) && (field.GetValue(target) as string) == data[1];
                    },
                    Execute: null
                );
                Implement(Type.GenericRemoveEmptyEventRefField,
                    Description: (data) => {
                        return string.Format("Remove empty field <b>{0}</b>", FieldPath(data[0], data[1]));
                    },
                    ManualInstructions: (data, component) => {
                        var subObjectPath = data[0];
                        var fieldName = data[1];
                        var targetType = data[2];

                        var fieldPath = FieldPath(subObjectPath, fieldName);

                        return string.Format(
                            "The {0} field on component {1} is empty.\n" +
                            "* Ensure no other instances of the {2} type are using the {3} field\n" +
                            "* Edit the definition of the {2} type and remove the {3} field",
                            fieldPath, component.Type, targetType, fieldName);
                    },
                    IsValid: (data, rootObject) => {
                        var target = FindSubObject(rootObject, data[0]);

                        var targetType = target.GetType();
                        var field = targetType.GetField(data[1]);

                        return field != null && IsEventRef(field)
                            && string.IsNullOrEmpty(field.GetValue(target) as string);
                    },
                    Execute: null
                );
                Implement(Type.GenericMoveEventRefFieldToEventReferenceField,
                    Description: (data) => {
                        var subObjectPath = data[0];
                        var value = data[1];
                        var oldFieldPath = FieldPath(subObjectPath, data[2]);
                        var newFieldPath = FieldPath(subObjectPath, data[3]);

                        return string.Format("Move <b>'{0}'</b> from <b>{1}</b> to <b>{2}</b>",
                            value, oldFieldPath, newFieldPath);
                    },
                    IsValid: (data, rootObject) => {
                        var subObjectPath = data[0];
                        var value = data[1];
                        var oldFieldName = data[2];
                        var newFieldName = data[3];

                        var target = FindSubObject(rootObject, subObjectPath);
                        var targetType = target.GetType();

                        var oldField = targetType.GetField(oldFieldName, DefaultBindingFlags);
                        var newField = targetType.GetField(newFieldName, DefaultBindingFlags);

                        if (oldField == null || newField == null
                            || !IsEventRef(oldField)
                            || newField.FieldType != typeof(EventReference))
                        {
                            return false;
                        }

                        var oldValue = oldField.GetValue(target) as string;
                        var newValue = (EventReference)newField.GetValue(target);

                        return oldValue == value && newValue.IsNull;
                    },
                    Execute: (data, rootObject) => {
                        var subObjectPath = data[0];
                        var path = data[1];
                        var oldFieldName = data[2];
                        var newFieldName = data[3];

                        var target = FindSubObject(rootObject, subObjectPath);
                        var type = target.GetType();

                        var oldField = type.GetField(oldFieldName, DefaultBindingFlags);
                        var newField = type.GetField(newFieldName, DefaultBindingFlags);

                        var eventReference = new EventReference() { Path = path };

                        var eventRef = EventManager.EventFromPath(path);

                        if (eventRef != null)
                        {
                            eventReference.Guid = eventRef.Guid;
                        }

                        oldField.SetValue(target, string.Empty);
                        newField.SetValue(target, eventReference);

                        EditorUtility.SetDirty(rootObject);
                    }
                );
                Implement(Type.GenericAddMigrationTarget,
                    Description: (data) => {
                        var value = data[1];
                        var fieldPath = FieldPath(data[0], data[2]);
                        var targetName = data[4];

                        if (!string.IsNullOrEmpty(targetName))
                        {
                            return string.Format(
                                "Add an <b>EventReference</b> field named <b>{0}</b> to hold <b>'{1}'</b> from <b>{2}</b>",
                                targetName, value, fieldPath);
                        }
                        else
                        {
                            return string.Format("Add an <b>EventReference</b> field to hold <b>'{0}'</b> from <b>{1}</b>",
                                value, fieldPath);
                        }
                    },
                    ManualInstructions: (data, component) => {
                        var fieldName = data[2];
                        var targetType = data[3];
                        var targetName = data[4];
                        var fieldPath = FieldPath(data[0], fieldName);

                        string script;

                        if (targetType != null)
                        {
                            script = string.Format("the definition of the {0} type", targetType);
                        }
                        else
                        {
                            script = component.ScriptPath;
                        }

                        if (!string.IsNullOrEmpty(targetName))
                        {
                            return string.Format(
                                "The {0} field on component {1} has an [EventRef(MigrateTo=\"{2}\")] " +
                                "attribute, but the {2} field doesn't exist.\n" +
                                "* Edit {3} and add an EventReference field named {2}:\n" +
                                "    public EventReference {2};\n" +
                                "* Re-scan your project",
                                fieldPath, component.Type, targetName, script);
                        }
                        else
                        {
                            return string.Format(
                                "The {0} field on component {1} has an [EventRef] " +
                                "attribute with no migration target specified.\n" +
                                "* Edit {2} and add an EventReference field:\n" +
                                "    public EventReference <fieldname>;\n" +
                                "* Change the [EventRef] attribute on the {3} field to:\n" +
                                "    [EventRef(MigrateTo=\"<fieldname>\")]\n" +
                                "* Re-scan your project.",
                                fieldPath, component.Type, script, fieldName);
                        }
                    },
                    IsValid: (data, rootObject) => {
                        var value = data[1];
                        var oldFieldName = data[2];

                        var target = FindSubObject(rootObject, data[0]);

                        var targetType = target.GetType();
                        var oldField = targetType.GetField(oldFieldName, DefaultBindingFlags);

                        return oldField != null && IsEventRef(oldField)
                            && (oldField.GetValue(target) as string) == value;
                    },
                    Execute: null
                );
                Implement(Type.GenericUpdateEventReferencePath,
                    Description: (data) => {
                        return string.Format(
                            "Change the path on field <b>{0}</b> " +
                            "from <b>'{1}'</b> to <b>'{2}'</b> (to match GUID <b>{3}</b>)",
                            FieldPath(data[0], data[1]), data[2], data[3], data[4]);
                    },
                    IsValid: (data, rootObject) => {
                        var target = FindSubObject(rootObject, data[0]);

                        var targetType = target.GetType();
                        var field = targetType.GetField(data[1], DefaultBindingFlags);

                        if (field == null || field.FieldType != typeof(EventReference))
                        {
                            return false;
                        }

                        var value = (EventReference)field.GetValue(target);

                        return value.Path == data[2] && value.Guid.ToString() == data[4];
                    },
                    Execute: (data, rootObject) => {
                        var target = FindSubObject(rootObject, data[0]);

                        var targetType = target.GetType();
                        var field = targetType.GetField(data[1], DefaultBindingFlags);

                        var value = (EventReference)field.GetValue(target);
                        value.Path = data[3];

                        field.SetValue(target, value);

                        EditorUtility.SetDirty(rootObject);
                    }
                );
                Implement(Type.GenericUpdateEventReferenceGuid,
                    Description: (data) => {
                        return string.Format(
                            "Change the GUID on field <b>{0}</b> " +
                            "from <b>{1}</b> to <b>{2}</b> (to match path <b>'{3}'</b>)",
                            FieldPath(data[0], data[1]), data[2], data[3], data[4]);
                    },
                    IsValid: (data, rootObject) => {
                        var target = FindSubObject(rootObject, data[0]);

                        var targetType = target.GetType();
                        var field = targetType.GetField(data[1], DefaultBindingFlags);

                        if (field == null || field.FieldType != typeof(EventReference))
                        {
                            return false;
                        }

                        var value = (EventReference)field.GetValue(target);

                        return value.Guid.ToString() == data[2] && value.Path == data[4];
                    },
                    Execute: (data, rootObject) => {
                        var target = FindSubObject(rootObject, data[0]);

                        var targetType = target.GetType();
                        var field = targetType.GetField(data[1], DefaultBindingFlags);

                        var value = (EventReference)field.GetValue(target);
                        value.Guid = FMOD.GUID.Parse(data[3]);

                        field.SetValue(target, value);

                        EditorUtility.SetDirty(rootObject);
                    }
                );
                Implement(Type.GenericFixMigrationTargetConflict,
                    Description: (data) => {
                        var subObjectPath = data[0];
                        var fieldPaths = data.Skip(2).Select(field => FieldPath(subObjectPath, field));

                        return string.Format("Fix conflicting migration targets on fields <b>{0}</b>",
                            EditorUtils.SeriesString("</b>, <b>", "</b> and <b>", fieldPaths));
                    },
                    ManualInstructions: (data, component) => {
                        return string.Format(
                            "Fields {0} on the {1} type have [EventRef] attributes with the same MigrateTo value.\n" +
                            "* Edit the definition of the {1} type and make sure all [EventRef] attributes have " +
                            "different MigrateTo values\n" +
                            "* Re-scan your project",
                            EditorUtils.SeriesString(", ", " and ", data.Skip(2)), data[1]);
                    },
                    IsValid: (data, target) => {
                        return true;
                    },
                    Execute: null
                );

#pragma warning restore 0618
            }

            public override string ToString()
            {
                return GetDelegates().Description(Data);
            }

            public string PlainDescription()
            {
                return Regex.Replace(ToString(), "</?b>", string.Empty);
            }

            public string ManualInstructions(Component component)
            {
                var delegates = GetDelegates();

                if (delegates.ManualInstructions != null)
                {
                    return delegates.ManualInstructions(Data, component);
                }
                else
                {
                    return null;
                }
            }

            public bool CanExecute()
            {
                return Enabled && !IsManual() && !HasExecuted;
            }

            public bool IsManual()
            {
                return GetDelegates().Execute == null;
            }

            public bool IsValid(UnityEngine.Object target)
            {
                return GetDelegates().IsValid(Data, target);
            }

            public bool Execute(UnityEngine.Object target)
            {
                if (IsValid(target))
                {
                    var delegates = GetDelegates();

                    if (delegates.Execute != null)
                    {
                        delegates.Execute(Data, target);
                        HasExecuted = true;
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        private static string FieldPath(string subObjectPath, string fieldName)
        {
            if (subObjectPath != null)
            {
                return string.Format("{0}.{1}", subObjectPath, fieldName);
            }
            else
            {
                return fieldName;
            }
        }

        private static string FieldPath(string subObjectPath, string fieldName, int index)
        {
            if (subObjectPath != null)
            {
                return string.Format("{0}.{1}[{2}]", subObjectPath, fieldName, index);
            }
            else
            {
                return string.Format("{0}[{1}]", fieldName, index);
            }
        }

        private static object FindSubObject(object o, string path)
        {
            if (path == null)
            {
                return o;
            }

            var result = o;

            foreach (var pathElement in path.Split('.'))
            {
                var type = result.GetType();

                var regex = new Regex(@"(\w+)\[(\d+)\]$");
                var match = regex.Match(pathElement);
                var index = -1;
                var fieldName = pathElement;

                if (match.Success)
                {
                    fieldName = match.Groups[1].Value;
                    index = int.Parse(match.Groups[2].Value);
                }

                var field = type.GetField(fieldName, DefaultBindingFlags);

                if (field == null)
                {
                    return null;
                }

                result = field.GetValue(result);

                if (index >= 0)
                {
                    var enumerable = result as System.Collections.IEnumerable;

                    result = null;

                    if (enumerable != null)
                    {
                        var i = 0;

                        foreach (var obj in enumerable)
                        {
                            if (index == i)
                            {
                                result = obj;
                                break;
                            }
                            i++;
                        }
                    }
                }

                if (result == null)
                {
                    return null;
                }
            }

            return result;
        }

        private void ExecuteTask(Task task, SavePolicy savePolicy)
        {
            var asset = assets[task.AssetIndex];

            if (asset.Type == AssetType.ScriptableObject)
            {
                ExecuteScriptableObjectTask(task, savePolicy);
            }
            else
            {
                ExecuteGameObjectTask(task, savePolicy);
            }
        }

        private void ExecuteScriptableObjectTask(Task task, SavePolicy savePolicy)
        {
            var asset = assets[task.AssetIndex];
            var component = components[task.ComponentIndex];

            var scriptableObjects =
                AssetDatabase.LoadAllAssetsAtPath(asset.Path).OfType<ScriptableObject>();

            foreach (var scriptableObject in scriptableObjects)
            {
                if (scriptableObject.GetType().Name == component.Type)
                {
                    if (task.Execute(scriptableObject))
                    {
                        break;
                    }
                }
            }
        }

        private void ExecuteGameObjectTask(Task task, SavePolicy savePolicy)
        {
            var gameObject = LoadTargetGameObject(task, savePolicy);

            if (gameObject == null)
            {
                return;
            }

            Selection.activeGameObject = gameObject;
            EditorGUIUtility.PingObject(gameObject);

            var component = components[task.ComponentIndex];

            foreach (var behaviour in gameObject.GetComponents<MonoBehaviour>())
            {
                if (behaviour.GetType().Name == component.Type)
                {
                    if (task.Execute(behaviour))
                    {
                        break;
                    }
                }
            }
        }

        private enum SavePolicy
        {
            AskToSave,
            AutoSave,
        }

        private GameObject LoadTargetGameObject(Task task, SavePolicy savePolicy)
        {
            var asset = assets[task.AssetIndex];
            var component = components[task.ComponentIndex];

            if (IsPrefab(asset.Type))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset.Path);

                if (prefab == null)
                {
                    return null;
                }

                if (!AssetDatabase.OpenAsset(prefab))
                {
                    return null;
                }

                return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(component.GameObjectID) as GameObject;
            }
            else if (asset.Type == AssetType.Scene)
            {
                var scene = SceneManager.GetSceneByPath(asset.Path);

                if (!scene.IsValid())
                {
                    if (savePolicy == SavePolicy.AskToSave)
                    {
                        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        {
                            return null;
                        }
                    }
                    else if (savePolicy == SavePolicy.AutoSave)
                    {
                        EditorSceneManager.SaveOpenScenes();
                    }
                    else
                    {
                        throw new ArgumentException("Unrecognized SavePolicy: " + savePolicy, "savePolicy");
                    }

                    scene = EditorSceneManager.OpenScene(asset.Path, OpenSceneMode.Single);

                    if (!scene.IsValid())
                    {
                        return null;
                    }
                }

                return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(component.GameObjectID) as GameObject;
            }
            else
            {
                return null;
            }
        }

        private int AddAsset(AssetType type, string path)
        {
            var asset = new Asset() {
                Type = type,
                Path = path,
            };

            assets.Add(asset);

            return assets.Count - 1;
        }

        private int AddComponent(MonoBehaviour behaviour, GameObject root)
        {
            var script = MonoScript.FromMonoBehaviour(behaviour);

            var component = new Component() {
                GameObjectID = GlobalObjectId.GetGlobalObjectIdSlow(behaviour.gameObject),
                Type = behaviour.GetType().Name,
                Path = EditorUtils.GameObjectPath(behaviour, root),
                ScriptPath = AssetDatabase.GetAssetPath(script),
            };

            components.Add(component);

            return components.Count - 1;
        }

        private int AddComponent(ScriptableObject scriptableObject)
        {
            var script = MonoScript.FromScriptableObject(scriptableObject);

            var component = new Component() {
                Type = scriptableObject.GetType().Name,
                ScriptPath = AssetDatabase.GetAssetPath(script),
            };

            components.Add(component);

            return components.Count - 1;
        }

        private void UpdateExecutableTaskCount()
        {
            executableTaskCount = tasks.Count(t => t.CanExecute());
        }

        private void AddTask(Task task)
        {
            tasks.Add(task);
            UpdateExecutableTaskCount();
            taskView.Reload();
            taskView.ExpandAll();
        }

        private void UpdateProcessing()
        {
            if (processingState != null)
            {
                if (processingState.MoveNext())
                {
                    SetStatus(processingState.Current);
                }
                else
                {
                    StopProcessing(true);
                }

                Repaint();
            }
        }

        private void OnEnable()
        {
            taskView = new TaskView(taskViewState, tasks, assets, components);
            taskView.Reload();
            taskView.taskSelected += OnTaskSelected;
            taskView.taskDoubleClicked += OnTaskDoubleClicked;
            taskView.taskEnableStateChanged += OnTaskEnableStateChanged;
            taskView.assetEnableStateChanged += ApplyAssetEnableStateToTasks;

            EditorApplication.update += UpdateProcessing;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateProcessing;
        }

        private void OnTaskSelected(Task task)
        {
            selectedTask = task;
        }

        private void OnTaskDoubleClicked(Task task)
        {
            var asset = assets[task.AssetIndex];

            if (asset.Type == AssetType.ScriptableObject)
            {
                var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(asset.Path);

                if (target == null)
                {
                    return;
                }

                if (!AssetDatabase.OpenAsset(target))
                {
                    return;
                }

                var component = components[task.ComponentIndex];

                var scriptableObjects =
                    AssetDatabase.LoadAllAssetsAtPath(asset.Path).OfType<ScriptableObject>();

                foreach (var scriptableObject in scriptableObjects)
                {
                    if (scriptableObject.GetType().Name == component.Type
                        && task.IsValid(scriptableObject))
                    {
                        Selection.activeObject = scriptableObject;
                    }
                }
            }
            else
            {
                var gameObject = LoadTargetGameObject(task, SavePolicy.AskToSave);

                if (gameObject == null)
                {
                    return;
                }

                Selection.activeGameObject = gameObject;
                EditorGUIUtility.PingObject(gameObject);
            }
        }

        private void OnTaskEnableStateChanged(Task task)
        {
            UpdateAssetEnableState(task.AssetIndex);
            UpdateExecutableTaskCount();
        }

        private void UpdateAssetEnableState(int assetIndex)
        {
            var asset = assets[assetIndex];

            asset.EnableState = tasks
                .Where(t => t.AssetIndex == assetIndex)
                .Select(t => t.Enabled ? EnableState.Enabled : EnableState.Disabled)
                .Aggregate((current, next) => (current == next) ? current : EnableState.Mixed);
        }

        private void ApplyAssetEnableStateToTasks(Asset asset)
        {
            var assetIndex = assets.IndexOf(asset);

            foreach (var task in tasks.Where(t => t.AssetIndex == assetIndex))
            {
                task.Enabled = (asset.EnableState == EnableState.Enabled);
            }

            UpdateExecutableTaskCount();
        }

        private class Styles
        {
            public static GUIStyle RichText;
            public static GUIStyle RichTextBox;
            public static GUIStyle TreeViewRichText;

            private static bool Initialized = false;

            public static void Affirm()
            {
                if (!Initialized)
                {
                    Initialized = true;

                    RichText = new GUIStyle(GUI.skin.label) { richText = true };
                    RichTextBox = new GUIStyle(EditorStyles.helpBox) { richText = true };
                    TreeViewRichText = new GUIStyle(TreeView.DefaultStyles.label) { richText = true };
                }
            }
        }

        private class Icons
        {
            public static Texture2D Scene;
            public static Texture2D Prefab;
            public static Texture2D PrefabModel;
            public static Texture2D PrefabVariant;
            public static Texture2D ScriptableObject;
            public static Texture2D GameObject;

            private static bool Initialized = false;

            public static void Affirm()
            {
                if (!Initialized)
                {
                    Initialized = true;

                    Scene = EditorGUIUtility.IconContent("SceneAsset Icon").image as Texture2D;
                    Prefab = EditorGUIUtility.IconContent("Prefab Icon").image as Texture2D;
                    PrefabModel = EditorGUIUtility.IconContent("PrefabModel Icon").image as Texture2D;
                    PrefabVariant = EditorGUIUtility.IconContent("PrefabVariant Icon").image as Texture2D;
                    ScriptableObject = EditorGUIUtility.IconContent("ScriptableObject Icon").image as Texture2D;
                    GameObject = EditorGUIUtility.IconContent("GameObject Icon").image as Texture2D;
                }
            }

            public static Texture2D GetAssetIcon(AssetType type)
            {
                Affirm();

                if (type == AssetType.Scene)
                {
                    return Scene;
                }
                else if (type == AssetType.Prefab)
                {
                    return Prefab;
                }
                else if (type == AssetType.PrefabModel)
                {
                    return PrefabModel;
                }
                else if (type == AssetType.PrefabVariant)
                {
                    return PrefabVariant;
                }
                else if (type == AssetType.ScriptableObject)
                {
                    return ScriptableObject;
                }
                else
                {
                    throw new ArgumentException("Unrecognized AssetType: " + type, "type");
                }
            }

            public static Texture2D GetComponentIcon(Component component)
            {
                return AssetDatabase.GetCachedIcon(component.ScriptPath) as Texture2D;
            }
        }

        private void SetStatus(string text)
        {
            status = new GUIContent(text, EditorGUIUtility.IconContent("console.infoicon.sml").image);
        }

        private void OnGUI()
        {
            Styles.Affirm();

            var buttonHeight = EditorGUIUtility.singleLineHeight * 2;

            // Task List
            using (var scope = new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                taskView.DrawLayout(scope.rect);
            }

            // Selected Task
            if (selectedTask != null)
            {
                var asset = assets[selectedTask.AssetIndex];
                var component = components[selectedTask.ComponentIndex];

                DrawSelectableLabel(selectedTask.PlainDescription(), EditorStyles.wordWrappedLabel);

                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField(AssetContent,
                        new GUIContent(asset.Path, Icons.GetAssetIcon(asset.Type)));
                    EditorGUILayout.LabelField(ComponentTypeContent,
                        new GUIContent(component.Type, Icons.GetComponentIcon(component)));

                    if (!string.IsNullOrEmpty(component.Path))
                    {
                        EditorGUILayout.LabelField(GameObjectContent, new GUIContent(component.Path, Icons.GameObject));
                    }

                    if (selectedTask.IsManual())
                    {
                        var buttonsRect = EditorGUILayout.GetControlRect(false, buttonHeight);
                        buttonsRect = EditorGUI.IndentedRect(buttonsRect);

                        var openScriptContent = new GUIContent("Open " + component.ScriptPath);

                        var openScriptRect = buttonsRect;
                        openScriptRect.width = GUI.skin.button.CalcSize(openScriptContent).x;

                        if (GUI.Button(openScriptRect, openScriptContent))
                        {
                            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(component.ScriptPath);
                            AssetDatabase.OpenAsset(script);
                        }

                        var viewDocumentationContent = new GUIContent("View Documentation");

                        var viewDocumentationRect = buttonsRect;
                        viewDocumentationRect.x = openScriptRect.xMax + GUI.skin.button.margin.left;
                        viewDocumentationRect.width = GUI.skin.button.CalcSize(viewDocumentationContent).x;

                        if (GUI.Button(viewDocumentationRect, viewDocumentationContent))
                        {
                            EditorUtils.OpenOnlineDocumentation("unity", "tools", "manual-tasks");
                        }

                        using (var scope = new EditorGUILayout.ScrollViewScope(manualDescriptionScrollPosition, GUILayout.Height(100)))
                        {
                            manualDescriptionScrollPosition = scope.scrollPosition;

                            DrawSelectableLabel(selectedTask.ManualInstructions(component), EditorStyles.wordWrappedLabel);
                        }
                    }
                    else
                    {
                        var buttonContent = new GUIContent("Execute");

                        var buttonRect = EditorGUILayout.GetControlRect(false, buttonHeight);
                        buttonRect.width = EditorGUIUtility.labelWidth;
                        buttonRect = EditorGUI.IndentedRect(buttonRect);

                        if (GUI.Button(buttonRect, buttonContent))
                        {
                            ExecuteTask(selectedTask, SavePolicy.AskToSave);
                        }
                    }
                }
            }

            // Status
            if (IsProcessing)
            {
                DrawProgressBar("Prefabs", prefabProgress);
                DrawProgressBar("ScriptableObjects", scriptableObjectProgress);
                DrawProgressBar("Scenes", sceneProgress);
            }

            GUILayout.Label(status, Styles.RichTextBox);

            // Buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(buttonHeight)))
                {
                    Cancel();
                }

                using (new EditorGUI.DisabledScope(IsProcessing))
                {
                    if (GUILayout.Button(SearchButtonText, GUILayout.Height(buttonHeight)))
                    {
                        BeginSearching();
                    }

                    using (new EditorGUI.DisabledScope(executableTaskCount == 0))
                    {
                        if (GUILayout.Button(ExecuteButtonText(), GUILayout.Height(buttonHeight)))
                        {
                            BeginExecuting();
                        }
                    }
                }
            }

            if (focusedWindow == this
                && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Escape)
            {
                Cancel();
                Event.current.Use();
            }
        }

        private static void DrawProgressBar(string label, SearchProgress progress)
        {
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.ProgressBar(rect, progress.Fraction(), label);
        }

        private static void DrawSelectableLabel(string text, GUIStyle style)
        {
            var height = style.CalcHeight(new GUIContent(text), EditorGUIUtility.currentViewWidth);

            EditorGUILayout.SelectableLabel(text, style, GUILayout.Height(height));
        }

        private class TaskView : TreeView
        {
            private List<Task> tasks;
            private List<Asset> assets;
            private List<Component> components;

            public delegate void TaskEventHandler(Task task);

            public event TaskEventHandler taskSelected;
            public event TaskEventHandler taskDoubleClicked;
            public event TaskEventHandler taskEnableStateChanged;

            public delegate void AssetEventHandler(Asset asset);

            public event AssetEventHandler assetEnableStateChanged;

            public TaskView(TreeViewState state, List<Task> tasks, List<Asset> assets, List<Component> components)
                : base(state, new MultiColumnHeader(CreateHeaderState()))
            {
                this.tasks = tasks;
                this.assets = assets;
                this.components = components;

                showAlternatingRowBackgrounds = true;
                showBorder = true;

                multiColumnHeader.ResizeToFit();
            }

            public static MultiColumnHeaderState CreateHeaderState()
            {
                var columns = new MultiColumnHeaderState.Column[] {
                    new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("Target"),
                        width = 225,
                        autoResize = false,
                        allowToggleVisibility = false,
                        canSort = false,
                    },
                    new MultiColumnHeaderState.Column() {
                        headerContent = new GUIContent("Task"),
                        autoResize = true,
                        allowToggleVisibility = false,
                        canSort = false,
                    },
                    new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("Status"),
                        width = 175,
                        autoResize = false,
                        allowToggleVisibility = false,
                        canSort = false,
                    }
                };

                return new MultiColumnHeaderState(columns);
            }

            public void DrawLayout(Rect rect)
            {
                extraSpaceBeforeIconAndLabel = ToggleWidth();

                OnGUI(rect);
            }

            public enum Column
            {
                Asset,
                Task,
                Status,
            }

            private class AssetItem : TreeViewItem
            {
                public Asset asset;
            }

            private class TaskItem : TreeViewItem
            {
                public Task task;
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem(-1, -1);

                if (tasks.Count > 0)
                {
                    var index = 0;

                    AssetItem assetItem = null;

                    foreach (var task in tasks)
                    {
                        var asset = assets[task.AssetIndex];

                        if (assetItem == null || assetItem.asset != asset)
                        {
                            assetItem = new AssetItem() {
                                id = index++,
                                asset = asset,
                                displayName = asset.Path,
                                icon = Icons.GetAssetIcon(asset.Type),
                            };

                            root.AddChild(assetItem);
                        }

                        TreeViewItem taskItem = new TaskItem() {
                            id = index++,
                            task = task,
                        };

                        assetItem.AddChild(taskItem);
                    }
                }
                else
                {
                    var item = new TreeViewItem(0);
                    item.displayName = "No tasks.";

                    root.AddChild(item);
                }

                SetupDepthsFromParentsAndChildren(root);

                return root;
            }

            protected override bool CanMultiSelect(TreeViewItem item)
            {
                return false;
            }

            protected override void SelectionChanged(IList<int> selectedIds)
            {
                base.SelectionChanged(selectedIds);

                if (taskSelected != null)
                {
                    if (selectedIds.Count > 0)
                    {
                        var item = FindItem(selectedIds[0], rootItem) as TaskItem;

                        if (item != null)
                        {
                            taskSelected(item.task);
                            return;
                        }
                    }

                    taskSelected(null);
                }
            }

            protected override void SingleClickedItem(int id)
            {
                var item = FindItem(id, rootItem);

                if (!(item is TaskItem))
                {
                    SetExpanded(id, !IsExpanded(id));
                }
                else
                {
                    base.SingleClickedItem(id);
                }
            }

            protected override void DoubleClickedItem(int id)
            {
                if (taskDoubleClicked != null)
                {
                    var item = FindItem(id, rootItem) as TaskItem;

                    if (item == null)
                    {
                        return;
                    }

                    taskDoubleClicked(item.task);
                }
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                var item = args.item;

                if (item is TaskItem)
                {
                    var task = (item as TaskItem).task;

                    var toggleRect = args.rowRect;
                    toggleRect.x = GetContentIndent(item);
                    toggleRect.width = ToggleWidth();

                    TaskToggle(toggleRect, task);

                    for (var i = 0; i < args.GetNumVisibleColumns(); ++i)
                    {
                        var rect = args.GetCellRect(i);

                        if (i == 0)
                        {
                            rect.xMin = toggleRect.xMax;
                        }

                        CellGUI(rect, task, args.GetColumn(i), args.selected, args.focused);
                    }
                }
                else if (item is AssetItem)
                {
                    base.RowGUI(args);

                    var rect = args.rowRect;
                    rect.x = GetContentIndent(item);
                    rect.width = ToggleWidth();

                    AssetToggle(rect, (item as AssetItem).asset);
                }
                else
                {
                    base.RowGUI(args);
                }
            }

            private static float ToggleWidth()
            {
                return GUI.skin.toggle.CalcSize(GUIContent.none).x;
            }

            private void AssetToggle(Rect rect, Asset asset)
            {
                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    EditorGUI.showMixedValue = (asset.EnableState == EnableState.Mixed);

                    var enabled = EditorGUI.Toggle(rect, asset.EnableState == EnableState.Enabled);

                    EditorGUI.showMixedValue = false;

                    if (scope.changed)
                    {
                        asset.EnableState = enabled ? EnableState.Enabled : EnableState.Disabled;

                        if (assetEnableStateChanged != null)
                        {
                            assetEnableStateChanged(asset);
                        }
                    }
                }
            }

            private void TaskToggle(Rect rect, Task task)
            {
                if (!task.IsManual())
                {
                    using (var scope = new EditorGUI.ChangeCheckScope())
                    {
                        task.Enabled = EditorGUI.Toggle(rect, task.Enabled);

                        if (scope.changed && taskEnableStateChanged != null)
                        {
                            taskEnableStateChanged(task);
                        }
                    }
                }
            }

            private void CellGUI(Rect rect, Task task, int columnIndex, bool selected, bool focused)
            {
                var component = components[task.ComponentIndex];

                switch ((Column)columnIndex)
                {
                    case Column.Asset:
                        if (Event.current.type == EventType.Repaint)
                        {
                            var typeIcon = Icons.GetComponentIcon(components[task.ComponentIndex]);

                            using (new GUI.GroupScope(rect))
                            {
                                var iconRect = new Rect(0, 0, rect.height, rect.height);

                                GUI.DrawTexture(iconRect, typeIcon, ScaleMode.ScaleToFit);

                                var type = new GUIContent(component.Type);

                                var hasGameObjectPath = !string.IsNullOrEmpty(component.Path);

                                if (hasGameObjectPath)
                                {
                                    type.text += " on";
                                }

                                var typeRect = new Rect(iconRect.xMax, 0,
                                    DefaultStyles.label.CalcSize(type).x, rect.height);

                                DefaultGUI.Label(typeRect, type.text, selected, focused);

                                if (hasGameObjectPath)
                                {
                                    iconRect.x = typeRect.xMax;

                                    GUI.DrawTexture(iconRect, Icons.GameObject, ScaleMode.ScaleToFit);

                                    var gameObject = new GUIContent(component.Path);

                                    var gameObjectRect = new Rect(iconRect.xMax, 0,
                                        DefaultStyles.label.CalcSize(gameObject).x, rect.height);

                                    DefaultGUI.Label(gameObjectRect, gameObject.text, selected, focused);
                                }
                            }
                        }

                        break;
                    case Column.Task:
                        if (Event.current.type == EventType.Repaint)
                        {
                            var text = task.ToString();

                            if (task.IsManual())
                            {
                                text = "Manual task: " + text;
                            }

                            Styles.TreeViewRichText.Draw(rect, text, false, false, selected, focused);
                        }
                        break;
                    case Column.Status:
                        if (Event.current.type == EventType.Repaint)
                        {
                            if (task.IsManual())
                            {
                                DefaultGUI.Label(rect, "Manual Changes Required", selected, focused);
                            }
                            else
                            {
                                DefaultGUI.Label(rect, task.HasExecuted ? "Complete" : "Pending", selected, focused);
                            }
                        }
                        break;
                }
            }
        }
    }
}
