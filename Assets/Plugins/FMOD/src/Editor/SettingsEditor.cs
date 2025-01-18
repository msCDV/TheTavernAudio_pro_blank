using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using System.IO;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace FMODUnity
{
    [CustomEditor(typeof(Settings))]
    public class SettingsEditor : Editor
    {
        private static readonly string[] ToggleDisplay = new string[] { "Disabled", "Enabled", "Development Build Only",  };

        private static readonly string[] OverlayLocations = new string[] { "TopLeft", "TopCenter", "TopRight", "BottomLeft", "BottomCenter", "BottomRight", "Center", "VR", };

        private static readonly string[] FrequencyDisplay = new string[] {
            "Platform Default",
            "22.05 kHz",
            "24 kHz",
            "32 kHz",
            "44.1 kHz",
            "48 kHz"
        };

        private static readonly int[] FrequencyValues = new int[] { 0, 22050, 24000, 32000, 44100, 48000 };

        private static readonly string[] SpeakerModeDisplay = new string[] {
            "Stereo",
            "Surround 5.1",
            "Surround 7.1",
            "Surround 7.1.4"
        };

        private static readonly FMOD.SPEAKERMODE[] SpeakerModeValues = new FMOD.SPEAKERMODE[] {
            FMOD.SPEAKERMODE.STEREO,
            FMOD.SPEAKERMODE._5POINT1,
            FMOD.SPEAKERMODE._7POINT1,
            FMOD.SPEAKERMODE._7POINT1POINT4
        };

        private bool hasBankSourceChanged = false;
        private bool hasBankTargetChanged = false;

        private bool expandThreadAffinity;
        private bool expandCodecChannels;
        private bool expandDynamicPlugins;
        private bool expandStaticPlugins;

        private static Section sExpandedSections;

        private SerializedProperty automaticEventLoading;
        private SerializedProperty automaticSampleLoading;
        private SerializedProperty bankLoadType;
        private SerializedProperty banksToLoad;
        private SerializedProperty enableMemoryTracking;
        private SerializedProperty encryptionKey;
        private SerializedProperty hasSourceProject;
        private SerializedProperty hasPlatforms;
        private SerializedProperty importType;
        private SerializedProperty loggingLevel;
        private SerializedProperty meterChannelOrdering;
        private SerializedProperty sourceBankPath;
        private SerializedProperty sourceProjectPath;
        private SerializedProperty stopEventsOutsideMaxDistance;
        private SerializedProperty enableErrorCallback;
        private SerializedProperty targetAssetPath;
        private SerializedProperty targetBankFolder;
        private SerializedProperty bankRefreshCooldown;
        private SerializedProperty showBankRefreshWindow;
        private SerializedProperty eventLinkage;

        [NonSerialized]
        private bool resourcesLoaded = false;

        private GUIStyle mainHeaderStyle;
        private GUIStyle sectionHeaderStyle;
        private GUIStyle platformHeaderStyle;
        private GUIStyle dropdownStyle;
        private GUIStyle inheritedPropertyLabelStyle;
        private GUIStyle overriddenPropertyLabelStyle;
        private GUIStyle inheritedPropertyFoldoutStyle;
        private GUIStyle overriddenPropertyFoldoutStyle;

        private GUIContent mainHeaderIcon;

        private Texture2D propertyOverrideIndicator;

        private const int THREAD_AFFINITY_CORES_PER_ROW = 8;

        private const string EditPlatformUndoMessage = "Edit FMOD Platform Properties";

        private PlatformPropertyStringListView staticPluginsView;
        private PlatformPropertyStringListView dynamicPluginsView;

        private static readonly int[] LoggingValues = new int[] {
            (int)FMOD.DEBUG_FLAGS.NONE,
            (int)FMOD.DEBUG_FLAGS.ERROR,
            (int)FMOD.DEBUG_FLAGS.WARNING,
            (int)FMOD.DEBUG_FLAGS.LOG,
        };

        private static readonly string[] LoggingDisplay = new string[] {
            "None",
            "Error",
            "Warning",
            "Log",
        };

        private ReorderableList banksToLoadView;

        private PlatformsView platformsView;
        private TreeViewState platformTreeViewState = new TreeViewState();

        private string lastSourceBankPath;

        private static readonly GUIContent BankRefreshLabel = new GUIContent("Refresh Banks");

        private static readonly GUIContent[] BankRefreshCooldownLabels = new GUIContent[] {
            new GUIContent("After 1 second"),
            new GUIContent("After 5 seconds"),
            new GUIContent("After 10 seconds"),
            new GUIContent("After 20 seconds"),
            new GUIContent("After 30 seconds"),
            new GUIContent("After 1 minute"),
            new GUIContent("Prompt Me"),
            new GUIContent("Manually"),
        };

        private static readonly int[] BankRefreshCooldownValues = new int[] {
            1,
            5,
            10,
            20,
            30,
            60,
            Settings.BankRefreshPrompt,
            Settings.BankRefreshManual,
        };

        internal enum SourceType : uint
        {
            FMODStudioProject = 0,
            SinglePlatformBuild,
            MultiplePlatformBuild
        }

        [Flags]
        private enum Section
        {
            BankImport = 1 << 0,
            Initialization = 1 << 1,
            Behavior = 1 << 2,
            UserInterface = 1 << 3,
            PlatformSpecific = 1 << 4,
        }

        private void OnEnable()
        {
            automaticEventLoading = serializedObject.FindProperty("AutomaticEventLoading");
            automaticSampleLoading = serializedObject.FindProperty("AutomaticSampleLoading");
            bankLoadType = serializedObject.FindProperty("BankLoadType");
            banksToLoad = serializedObject.FindProperty("BanksToLoad");
            enableMemoryTracking = serializedObject.FindProperty("EnableMemoryTracking");
            encryptionKey = serializedObject.FindProperty("EncryptionKey");
            hasSourceProject = serializedObject.FindProperty("HasSourceProject");
            hasPlatforms = serializedObject.FindProperty("HasPlatforms");
            importType = serializedObject.FindProperty("ImportType");
            loggingLevel = serializedObject.FindProperty("LoggingLevel");
            meterChannelOrdering = serializedObject.FindProperty("MeterChannelOrdering");
            sourceBankPath = serializedObject.FindProperty("sourceBankPath");
            sourceProjectPath = serializedObject.FindProperty("sourceProjectPath");
            stopEventsOutsideMaxDistance = serializedObject.FindProperty("StopEventsOutsideMaxDistance");
            enableErrorCallback = serializedObject.FindProperty("EnableErrorCallback");
            targetAssetPath = serializedObject.FindProperty("TargetAssetPath");
            targetBankFolder = serializedObject.FindProperty("TargetBankFolder");
            bankRefreshCooldown = serializedObject.FindProperty("BankRefreshCooldown");
            showBankRefreshWindow = serializedObject.FindProperty("ShowBankRefreshWindow");
            eventLinkage = serializedObject.FindProperty("EventLinkage");

            platformsView = new PlatformsView(target as Settings, platformTreeViewState);

            banksToLoadView = new ReorderableList(banksToLoad);
            banksToLoadView.onAddDropdownCallback = (rect, list) => {
                var menu = new GenericMenu();

                menu.AddItem(new GUIContent("Browse..."), false, BrowseForBankToLoad);
                menu.AddItem(new GUIContent("Add All"), false, AddAllBanksToLoad);

                menu.DropDown(rect);
            };

            staticPluginsView = new PlatformPropertyStringListView(Platform.PropertyAccessors.StaticPlugins);
            dynamicPluginsView = new PlatformPropertyStringListView(Platform.PropertyAccessors.Plugins);

            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDestroy()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            platformsView.ForceReload();

            RefreshBanks();

            Repaint();
        }

        private void AffirmResources()
        {
            if (!resourcesLoaded)
            {
                resourcesLoaded = true;

                mainHeaderStyle = new GUIStyle(EditorStyles.label) {
                    fontStyle = FontStyle.Bold,
                    fontSize = 18,
                };
                mainHeaderStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;

                sectionHeaderStyle = new GUIStyle(GUI.skin.FindStyle("Foldout")) {
                    fontStyle = FontStyle.Bold,
                };

                platformHeaderStyle = new GUIStyle(GUI.skin.label) {
                    richText = true,
                };

                dropdownStyle = new GUIStyle(GUI.skin.FindStyle("dropdownButton"));
                dropdownStyle.fixedHeight = 0;

                inheritedPropertyLabelStyle = GUI.skin.label;

                overriddenPropertyLabelStyle = new GUIStyle(inheritedPropertyLabelStyle) {
                    fontStyle = FontStyle.Bold,
                };

                inheritedPropertyFoldoutStyle = EditorStyles.foldout;

                overriddenPropertyFoldoutStyle = new GUIStyle(inheritedPropertyFoldoutStyle) {
                    fontStyle = FontStyle.Bold,
                };

                mainHeaderIcon = new GUIContent(EditorUtils.LoadImage("StudioIcon.png"));

                propertyOverrideIndicator = new Texture2D(2, 1);

                Color darkBlue;
                ColorUtility.TryParseHtmlString("#1974a5", out darkBlue);

                Color blue;
                ColorUtility.TryParseHtmlString("#0f81be", out blue);

                propertyOverrideIndicator.SetPixel(0, 0, darkBlue);
                propertyOverrideIndicator.SetPixel(1, 0, blue);

                propertyOverrideIndicator.Apply();
            }
        }

        private Rect DrawPlatformPropertyLabel(string label, Platform platform,
            params Platform.PropertyOverrideControl[] properties)
        {
            PlatformPropertyLabelData data;
            PreparePlatformPropertyLabel(platform, properties, out data);

            GUI.Label(data.labelRect, label, data.overridden ? overriddenPropertyLabelStyle : inheritedPropertyLabelStyle);

            DecoratePlatformPropertyLabel(data, platform, properties);

            return data.remainderRect;
        }

        private Rect DrawPlatformPropertyFoldout(string label, ref bool expand, Platform platform,
            params Platform.PropertyOverrideControl[] properties)
        {
            PlatformPropertyLabelData data;
            PreparePlatformPropertyLabel(platform, properties, out data);

            using (new NoIndentScope())
            {
                expand = EditorGUI.Foldout(data.labelRect, expand, label, true,
                    data.overridden ? overriddenPropertyFoldoutStyle : inheritedPropertyFoldoutStyle);
            }

            DecoratePlatformPropertyLabel(data, platform, properties);

            return data.remainderRect;
        }

        private struct PlatformPropertyLabelData
        {
            public bool hasParent;
            public bool overridden;
            public Rect labelRect;
            public Rect remainderRect;
        }

        private void PreparePlatformPropertyLabel(Platform platform, Platform.PropertyOverrideControl[] properties,
            out PlatformPropertyLabelData data)
        {
            AffirmResources();

            var rect = EditorGUILayout.GetControlRect();

            data.hasParent = (platform.Parent != null || platform is PlatformPlayInEditor);
            data.overridden = data.hasParent && properties.Any(p => p.HasValue(platform));
            data.labelRect = LabelRect(rect);
            data.remainderRect = new Rect(rect) { xMin = data.labelRect.xMax };
        }

        private void DecoratePlatformPropertyLabel(PlatformPropertyLabelData data, Platform platform,
            Platform.PropertyOverrideControl[] properties)
        {
            if (data.hasParent)
            {
                if (data.overridden)
                {
                    var indicatorRect = new Rect(data.labelRect) { x = 1, width = 2 };
                    GUI.DrawTexture(indicatorRect, propertyOverrideIndicator);
                }

                if (Event.current.type == EventType.MouseUp
                    && Event.current.button == 1
                    && data.labelRect.Contains(Event.current.mousePosition))
                {
                    var menu = new GenericMenu();

                    var revertContent = new GUIContent("Revert");

                    if (data.overridden)
                    {
                        menu.AddItem(revertContent, false, () => {
                            Undo.RecordObject(platform, "Revert FMOD Platform Properties");

                            foreach (var property in properties)
                            {
                                property.Clear(platform);
                            }
                        });
                    }
                    else
                    {
                        menu.AddDisabledItem(revertContent);
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private static int DrawPopup(Rect position, int selectedIndex, string[] displayedOptions)
        {
            using (new NoIndentScope())
            {
                return EditorGUI.Popup(position, selectedIndex, displayedOptions);
            }
        }

        private void DisplayTriStateBool(string label, Platform platform, Platform.PropertyAccessor<TriStateBool> property)
        {
            var rect = DrawPlatformPropertyLabel(label, platform, property);

            EditorGUI.BeginChangeCheck();

            var next = DrawPopup(rect, (int)property.Get(platform), ToggleDisplay);

            if (EditorGUI.EndChangeCheck())
            {
                property.Set(platform, (TriStateBool)next);
            }
        }

        private void DisplayPositionRect(string label, Platform platform, Platform.PropertyAccessor<ScreenPosition> property)
        {
            var rect = DrawPlatformPropertyLabel(label, platform, property);
            EditorGUI.BeginChangeCheck();
            var next = DrawPopup(rect, (int)property.Get(platform), OverlayLocations);
            if (EditorGUI.EndChangeCheck())
            {
                property.Set(platform, (ScreenPosition)next);
            }
        }

        private void DisplayDebugFontSelection(string label, Platform platform, Platform.PropertyAccessor<int> property)
        {
            var rect = DrawPlatformPropertyLabel(label, platform, property);
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.IntField(rect, property.Get(platform));
            if (EditorGUI.EndChangeCheck())
            {
                property.Set(platform, Mathf.Min(next, 20));
            }
        }

        private void DisplayOutputMode(string label, Platform platform)
        {
            if (platform.ValidOutputTypes != null)
            {
                var valuesChild = new string[platform.ValidOutputTypes.Length + 3];
                var valuesChildEnum = new string[platform.ValidOutputTypes.Length + 3];
                valuesChild[0] = string.Format("Auto");
                valuesChild[1] = string.Format("No Sound");
                valuesChild[2] = string.Format("Wav Writer");
                valuesChildEnum[0] = Enum.GetName(typeof(FMOD.OUTPUTTYPE), FMOD.OUTPUTTYPE.AUTODETECT);
                valuesChildEnum[1] = Enum.GetName(typeof(FMOD.OUTPUTTYPE), FMOD.OUTPUTTYPE.NOSOUND);
                valuesChildEnum[2] = Enum.GetName(typeof(FMOD.OUTPUTTYPE), FMOD.OUTPUTTYPE.WAVWRITER);
                for (var i = 0; i < platform.ValidOutputTypes.Length; i++)
                {
                    valuesChild[i + 3] = platform.ValidOutputTypes[i].displayName;
                    valuesChildEnum[i + 3] = Enum.GetName(typeof(FMOD.OUTPUTTYPE), platform.ValidOutputTypes[i].outputType);
                }
                var currentIndex = Array.IndexOf(valuesChildEnum, platform.OutputTypeName);
                if (currentIndex == -1)
                {
                    currentIndex = 0;
                    platform.OutputTypeName = Enum.GetName(typeof(FMOD.OUTPUTTYPE), FMOD.OUTPUTTYPE.AUTODETECT);
                }
                var next = EditorGUILayout.Popup(label, currentIndex, valuesChild);
                platform.OutputTypeName = valuesChildEnum[next];
            }
        }

        private void DisplayThreadAffinity(string label, Platform platform)
        {
            if (platform.CoreCount > 0 && DisplayThreadAffinityFoldout(label, platform))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DisplayThreadAffinityGroups(platform);
                }
            }
        }

        private bool DisplayThreadAffinityFoldout(string label, Platform platform)
        {
            var headerRect = EditorGUILayout.GetControlRect();

            var labelRect = headerRect;
            labelRect.width = EditorGUIUtility.labelWidth;

            expandThreadAffinity = EditorGUI.Foldout(labelRect, expandThreadAffinity, label, true);

            var useDefaults = !platform.ThreadAffinitiesProperty.HasValue;

            EditorGUI.BeginChangeCheck();

            var toggleRect = headerRect;
            toggleRect.xMin = labelRect.xMax;

            useDefaults = GUI.Toggle(toggleRect, useDefaults, "Use Defaults");

            if (EditorGUI.EndChangeCheck())
            {
                if (useDefaults)
                {
                    platform.ThreadAffinitiesProperty.Value.Clear();
                    platform.ThreadAffinitiesProperty.HasValue = false;
                }
                else
                {
                    platform.ThreadAffinitiesProperty.Value = new List<ThreadAffinityGroup>();
                    platform.ThreadAffinitiesProperty.HasValue = true;

                    foreach (var group in platform.DefaultThreadAffinities)
                    {
                        platform.ThreadAffinitiesProperty.Value.Add(new ThreadAffinityGroup(group));
                    }
                }
            }

            return expandThreadAffinity;
        }

        private void DisplayThreadAffinityGroups(Platform platform)
        {
            var affinityStyle = EditorStyles.miniButton;
            var affinityWidth = affinityStyle.CalcSize(new GUIContent("00")).x;

            var anyButtonContent = new GUIContent("Any");
            var anyButtonWidth = affinityStyle.CalcSize(anyButtonContent).x;

            var threadsWidth = EditorGUIUtility.labelWidth;
            var affinitiesWidth = affinityWidth * THREAD_AFFINITY_CORES_PER_ROW + anyButtonWidth;

            var editable = platform.ThreadAffinitiesProperty.HasValue;

            if (platform.ThreadAffinities.Any())
            {
                DisplayThreadAffinitiesHeader(threadsWidth, affinitiesWidth);

                using (new EditorGUI.DisabledScope(!editable))
                {
                    ThreadAffinityGroup groupToDelete = null;

                    foreach (var group in platform.ThreadAffinities)
                    {
                        bool delete;
                        DisplayThreadAffinityGroup(group, platform, threadsWidth, affinitiesWidth,
                            anyButtonWidth, anyButtonContent, affinityStyle, affinityWidth, out delete);

                        if (delete)
                        {
                            groupToDelete = group;
                        }
                    }

                    if (groupToDelete != null)
                    {
                        platform.ThreadAffinitiesProperty.Value.Remove(groupToDelete);
                    }
                }
            }
            else
            {
                var messageRect = EditorGUILayout.GetControlRect();
                messageRect.width = threadsWidth + affinitiesWidth;
                messageRect = EditorGUI.IndentedRect(messageRect);

                GUI.Label(messageRect, "List is Empty");
            }

            if (editable)
            {
                var addButtonRect = EditorGUILayout.GetControlRect();
                addButtonRect.width = threadsWidth + affinitiesWidth;
                addButtonRect = EditorGUI.IndentedRect(addButtonRect);

                if (GUI.Button(addButtonRect, "Add"))
                {
                    platform.ThreadAffinitiesProperty.Value.Add(new ThreadAffinityGroup());
                }
            }
        }

        private void DisplayThreadAffinitiesHeader(float threadsWidth, float affinitiesWidth)
        {
            var controlRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            var threadsRect = controlRect;
            threadsRect.width = threadsWidth;

            threadsRect = EditorGUI.IndentedRect(threadsRect);

            GUI.Label(threadsRect, "Threads");

            var coresRect = controlRect;
            coresRect.x = threadsRect.xMax;
            coresRect.width = affinitiesWidth;

            GUI.Label(coresRect, "Cores");
        }

        private void DisplayThreadAffinityGroup(ThreadAffinityGroup group, Platform platform,
            float threadsWidth, float affinitiesWidth, float anyButtonWidth, GUIContent anyButtonContent,
            GUIStyle affinityStyle, float affinityWidth, out bool delete)
        {
            delete = false;

            var editButtonStyle = EditorStyles.popup;

            var editButtonContent = new GUIContent("Edit");
            var editButtonRect = new Rect(Vector2.zero, editButtonStyle.CalcSize(editButtonContent));

            var threadsHeight = group.threads.Count * EditorGUIUtility.singleLineHeight;

            var editable = platform.ThreadAffinitiesProperty.HasValue;

            if (editable)
            {
                 threadsHeight += EditorGUIUtility.standardVerticalSpacing + editButtonRect.height;
            }

            var affinitiesHeight =
                Mathf.Ceil(platform.CoreCount / (float)THREAD_AFFINITY_CORES_PER_ROW) * EditorGUIUtility.singleLineHeight;

            var controlRect = EditorGUILayout.GetControlRect(false, Math.Max(threadsHeight, affinitiesHeight));

            var threadsRect = controlRect;
            threadsRect.width = threadsWidth;

            threadsRect = EditorGUI.IndentedRect(threadsRect);

            var boxStyle = EditorStyles.textArea;

            GUI.Box(threadsRect, string.Empty, boxStyle);

            var threadRect = threadsRect;
            threadRect.height = EditorGUIUtility.singleLineHeight;

            foreach (var thread in group.threads)
            {
                GUI.Label(threadRect, thread.DisplayName());
                threadRect.y += threadRect.height;
            }

            if (editable)
            {
                editButtonRect.y = threadsRect.yMax - editButtonRect.height - editButtonStyle.margin.bottom;
                editButtonRect.center = new Vector2(threadsRect.center.x, editButtonRect.center.y);

                if (EditorGUI.DropdownButton(editButtonRect, editButtonContent, FocusType.Passive, editButtonStyle))
                {
                    ThreadListEditor.Show(editButtonRect, group, platform, this);
                }
            }

            var affinitiesRect = controlRect;
            affinitiesRect.xMin = threadsRect.xMax;
            affinitiesRect.width = affinitiesWidth;

            GUI.Box(affinitiesRect, string.Empty, boxStyle);

            var anyButtonRect = affinitiesRect;
            anyButtonRect.height = affinitiesHeight;
            anyButtonRect.width = anyButtonWidth;

            if (GUI.Toggle(anyButtonRect, group.affinity == ThreadAffinity.Any, anyButtonContent, affinityStyle))
            {
                group.affinity = ThreadAffinity.Any;
            }

            var affinityRect = affinitiesRect;
            affinityRect.x = anyButtonRect.xMax;
            affinityRect.height = EditorGUIUtility.singleLineHeight;
            affinityRect.width = affinityWidth;

            for (var i = 0; i < platform.CoreCount; ++i)
            {
                var mask = (ThreadAffinity)(1U << i);

                if (GUI.Toggle(affinityRect, (group.affinity & mask) == mask, i.ToString(), affinityStyle))
                {
                    group.affinity |= mask;
                }
                else
                {
                    group.affinity &= ~mask;
                }

                if (i % THREAD_AFFINITY_CORES_PER_ROW == THREAD_AFFINITY_CORES_PER_ROW - 1)
                {
                    affinityRect.x = anyButtonRect.xMax;
                    affinityRect.y += affinityRect.height;
                }
                else
                {
                    affinityRect.x += affinityRect.width;
                }
            }

            if (editable)
            {
                var deleteButtonStyle = GUI.skin.button;
                var deleteButtonContent = new GUIContent("Delete");

                var deleteButtonRect = controlRect;
                deleteButtonRect.x = affinitiesRect.xMax;
                deleteButtonRect.width = deleteButtonStyle.CalcSize(deleteButtonContent).x;

                if (GUI.Button(deleteButtonRect, deleteButtonContent, deleteButtonStyle))
                {
                    delete = true;
                }
            }
        }

        private class ThreadListEditor : EditorWindow
        {
            private ThreadAffinityGroup group;
            private Platform platform;
            private Editor parent;

            public static void Show(Rect buttonRect, ThreadAffinityGroup group, Platform platform, Editor parent)
            {
                var editor = CreateInstance<ThreadListEditor>();
                editor.group = group;
                editor.platform = platform;
                editor.parent = parent;

                var rect = new Rect(GUIUtility.GUIToScreenPoint(buttonRect.position), buttonRect.size);

                editor.ShowAsDropDown(rect, CalculateSize());
            }

            private static GUIStyle FrameStyle { get { return GUI.skin.box; } }
            private static GUIStyle ThreadStyle { get { return EditorStyles.toggle; } }

            private static Vector2 CalculateSize()
            {
                var result = Vector2.zero;

                var enumValues = Enum.GetValues(typeof(ThreadType));

                foreach (ThreadType thread in enumValues)
                {
                    var size = ThreadStyle.CalcSize(new GUIContent(thread.DisplayName()));
                    result.x = Mathf.Max(result.x, size.x);
                }

                result.y = enumValues.Length * EditorGUIUtility.singleLineHeight
                    + (enumValues.Length - 1) * EditorGUIUtility.standardVerticalSpacing;

                result.x += FrameStyle.padding.horizontal;
                result.y += FrameStyle.padding.vertical;

                return result;
            }

            private void OnGUI()
            {
                var frameRect = new Rect(0, 0, position.width, position.height);

                GUI.Box(frameRect, string.Empty, FrameStyle);

                var threadRect = FrameStyle.padding.Remove(frameRect);
                threadRect.height = EditorGUIUtility.singleLineHeight;

                foreach (ThreadType thread in Enum.GetValues(typeof(ThreadType)))
                {
                    EditorGUI.BeginChangeCheck();

                    var include = EditorGUI.ToggleLeft(threadRect, thread.DisplayName(), group.threads.Contains(thread));

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(platform, EditPlatformUndoMessage);

                        if (include)
                        {
                            // Make sure each thread is only in one group
                            foreach (var other in platform.ThreadAffinities)
                            {
                                other.threads.Remove(thread);
                            }

                            group.threads.Add(thread);
                            group.threads.Sort();
                        }
                        else
                        {
                            group.threads.Remove(thread);
                        }

                        parent.Repaint();
                    }

                    threadRect.y = threadRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                }
            }
        }

        private void DisplayCodecChannels(string label, Platform platform)
        {
            if (platform is PlatformGroup)
            {
                return;
            }

            if (DisplayCodecChannelsFoldout(label, platform))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    var editable = platform.CodecChannelsProperty.HasValue;

                    using (new EditorGUI.DisabledScope(!editable))
                    {
                        foreach (var channelCount in platform.CodecChannels)
                        {
                            EditorGUI.BeginChangeCheck();

                            var channels = EditorGUILayout.IntSlider(channelCount.format.ToString(), channelCount.channels, 0, 256);

                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(platform, "Edit Codec Channels");

                                channelCount.channels = channels;
                            }
                        }
                    }
                }
            }
        }

        private bool DisplayCodecChannelsFoldout(string label, Platform platform)
        {
            var controlRect = EditorGUILayout.GetControlRect();

            var labelRect = controlRect;
            labelRect.width = EditorGUIUtility.labelWidth;

            expandCodecChannels = EditorGUI.Foldout(labelRect, expandCodecChannels, label, true);

            var useDefaults = !platform.CodecChannelsProperty.HasValue;

            EditorGUI.BeginChangeCheck();

            var toggleRect = controlRect;
            toggleRect.xMin = labelRect.xMax;

            useDefaults = GUI.Toggle(toggleRect, useDefaults, "Use Defaults");

            if (EditorGUI.EndChangeCheck())
            {
                if (useDefaults)
                {
                    platform.CodecChannelsProperty.Value = null;
                    platform.CodecChannelsProperty.HasValue = false;
                }
                else
                {
                    platform.CodecChannelsProperty.Value = new List<CodecChannelCount>();
                    platform.CodecChannelsProperty.HasValue = true;

                    foreach (var channelCount in platform.DefaultCodecChannels)
                    {
                        platform.CodecChannelsProperty.Value.Add(new CodecChannelCount(channelCount));
                    }
                }
            }

            return expandCodecChannels;
        }

        private void DisplaySampleRate(string label, Platform platform)
        {
            var property = Platform.PropertyAccessors.SampleRate;

            var rect = DrawPlatformPropertyLabel(label, platform, property);

            EditorGUI.BeginChangeCheck();

            var currentIndex = Math.Max(0, Array.IndexOf(FrequencyValues, platform.SampleRate));
            var nextIndex = DrawPopup(rect, currentIndex, FrequencyDisplay);

            if (EditorGUI.EndChangeCheck())
            {
                property.Set(platform, FrequencyValues[nextIndex]);
            }
        }

        private void DisplayProjectPlatform(string label, Platform platform)
        {
            var rect = DrawPlatformPropertyLabel(label, platform,
                Platform.PropertyAccessors.BuildDirectory, Platform.PropertyAccessors.SpeakerMode);

            var speakerModeIndex = Math.Max(0, Array.IndexOf(SpeakerModeValues, platform.SpeakerMode));
            var speakerModeName = SpeakerModeDisplay[speakerModeIndex];

            if (GUI.Button(rect, string.Format("{0} ({1})", platform.BuildDirectory, speakerModeName)))
            {
                PopupWindow.Show(rect, new ProjectPlatformSelector(platform, this));
            }
        }

        private class ProjectPlatformSelector : PopupWindowContent
        {
            private Platform platform;
            private SettingsEditor settingsEditor;
            private string[] outputSubdirectories;

            private GUIStyle headerStyle;
            private GUIStyle toggleStyle;

            private GUIContent subdirectoryHeader = new GUIContent("Output sub-directory:");
            private GUIContent speakerModeHeader = new GUIContent("Surround speaker mode:");

            private const string HelpText = "Select the output sub-directory and speaker mode that match the project " +
                "platform settings in the FMOD Studio build preferences.";
            private const string UndoText = "Edit FMOD Platform Settings";

            private const float InterColumnSpace = 25;

            private Vector2 subdirectorySize;
            private Vector2 speakerModeSize;
            private Vector2 helpButtonSize;

            private Vector2 windowSize;

            public ProjectPlatformSelector(Platform platform, SettingsEditor settingsEditor)
            {
                this.platform = platform;
                this.settingsEditor = settingsEditor;

                headerStyle = GUI.skin.label;

                toggleStyle = new GUIStyle(EditorStyles.radioButton);
                toggleStyle.margin.left = headerStyle.margin.left + 10;

                outputSubdirectories = EditorUtils.GetBankPlatforms();

                var subdirectoryHeaderSize = headerStyle.CalcSize(subdirectoryHeader);

                subdirectorySize = ToggleGroupSize(outputSubdirectories);
                subdirectorySize.x = Math.Max(subdirectoryHeaderSize.x, subdirectorySize.x);
                subdirectorySize.y += subdirectoryHeaderSize.y + headerStyle.margin.bottom;

                var speakerModeHeaderSize = headerStyle.CalcSize(speakerModeHeader);

                speakerModeSize = ToggleGroupSize(SpeakerModeDisplay);
                speakerModeSize.x = Math.Max(speakerModeHeaderSize.x, speakerModeSize.x);
                speakerModeSize.y += speakerModeHeaderSize.y + headerStyle.margin.bottom;

                helpButtonSize = EditorUtils.GetHelpButtonSize();

                var width = headerStyle.margin.left + subdirectorySize.x + InterColumnSpace + speakerModeSize.x
                            + helpButtonSize.x;
                var height = Math.Max(subdirectorySize.y, speakerModeSize.y);

                windowSize = new Vector2(width, height);
            }

            private Vector2 ToggleGroupSize(IEnumerable<string> labels)
            {
                var totalSize = Vector2.zero;

                foreach (var label in labels)
                {
                    var size = toggleStyle.CalcSize(new GUIContent(label));

                    totalSize.x = Math.Max(totalSize.x, size.x);
                    totalSize.y += size.y + toggleStyle.margin.top;
                }

                totalSize.y += toggleStyle.margin.bottom;

                return totalSize;
            }

            public override Vector2 GetWindowSize()
            {
                return windowSize;
            }

            public override void OnGUI(Rect rect)
            {
                var y = rect.y + headerStyle.margin.top;

                var subdirectoryRect = new Rect(rect.x + headerStyle.margin.left, y, subdirectorySize.x, rect.height);

                using (new GUILayout.AreaScope(subdirectoryRect))
                {
                    GUILayout.Label(subdirectoryHeader, headerStyle);

                    foreach (var buildDirectory in outputSubdirectories)
                    {
                        var selected = (platform.BuildDirectory == buildDirectory);

                        EditorGUI.BeginChangeCheck();

                        selected = GUILayout.Toggle(selected, buildDirectory, toggleStyle);

                        if (EditorGUI.EndChangeCheck() && selected)
                        {
                            Undo.RecordObject(platform, UndoText);

                            Platform.PropertyAccessors.BuildDirectory.Set(platform, buildDirectory);

                            // Ensure SpeakerMode is also overridden
                            Platform.PropertyAccessors.SpeakerMode.Set(platform, platform.SpeakerMode);

                            settingsEditor.Repaint();
                        }
                    }
                }

                var speakerModeRect = new Rect(subdirectoryRect.xMax + InterColumnSpace, y, speakerModeSize.x, rect.height);

                using (new GUILayout.AreaScope(speakerModeRect))
                {
                    GUILayout.Label(speakerModeHeader, headerStyle);

                    for (var i = 0; i < SpeakerModeValues.Length; ++i)
                    {
                        var selected = (platform.SpeakerMode == SpeakerModeValues[i]);

                        EditorGUI.BeginChangeCheck();

                        selected = GUILayout.Toggle(selected, SpeakerModeDisplay[i], toggleStyle);

                        if (EditorGUI.EndChangeCheck() && selected)
                        {
                            Undo.RecordObject(platform, UndoText);

                            Platform.PropertyAccessors.SpeakerMode.Set(platform, SpeakerModeValues[i]);

                            // Ensure BuildDirectory is also overridden
                            Platform.PropertyAccessors.BuildDirectory.Set(platform, platform.BuildDirectory);

                            settingsEditor.Repaint();
                        }
                    }
                }

                var helpButtonRect = new Rect(speakerModeRect.xMax, y, helpButtonSize.x, helpButtonSize.y);
                EditorUtils.DrawHelpButton(helpButtonRect, () => new SimpleHelp(HelpText));
            }
        }

        private void DisplaySpeakerMode(string label, Platform platform)
        {
            const string HelpText = "Select the speaker mode that matches the project " +
                "platform settings in the FMOD Studio build preferences.";

            var rect = EditorUtils.DrawHelpButtonLayout(() => new SimpleHelp(HelpText));

            var labelRect = LabelRect(rect);

            GUI.Label(labelRect, label);

            var speakerModeRect = rect;
            speakerModeRect.xMin = labelRect.xMax;

            var currentIndex = Math.Max(0, Array.IndexOf(SpeakerModeValues, platform.SpeakerMode));

            var next = DrawPopup(speakerModeRect, currentIndex, SpeakerModeDisplay);

            Platform.PropertyAccessors.SpeakerMode.Set(platform, SpeakerModeValues[next]);
        }

        private void DisplayCallbackHandler(string label, Platform platform)
        {
            var property = Platform.PropertyAccessors.CallbackHandler;

            var rect = DrawPlatformPropertyLabel(label, platform, property);

            using (new NoIndentScope())
            {
                EditorGUI.BeginChangeCheck();

                var next = EditorGUI.ObjectField(rect, property.Get(platform),
                    typeof(PlatformCallbackHandler), false) as PlatformCallbackHandler;

                if (EditorGUI.EndChangeCheck())
                {
                    property.Set(platform, next);
                }
            }
        }

        private void DisplayInt(string label, Platform platform, Platform.PropertyAccessor<int> property, int min, int max)
        {
            var currentValue = property.Get(platform);

            var rect = DrawPlatformPropertyLabel(label, platform, property);

            using (new NoIndentScope())
            {
                EditorGUI.BeginChangeCheck();

                var next = EditorGUI.IntSlider(rect, currentValue, min, max);

                if (EditorGUI.EndChangeCheck())
                {
                    property.Set(platform, next);
                }
            }
        }

        private void DisplayLiveUpdatePort(string label, Platform platform, Platform.PropertyAccessor<int> property)
        {
            var rect = DrawPlatformPropertyLabel(label, platform, property);

            var resetContent = new GUIContent("Reset");

            var resetRect = rect;
            resetRect.xMin = resetRect.xMax - GUI.skin.button.CalcSize(resetContent).x;

            var textRect = rect;
            textRect.xMax = resetRect.xMin;

            using (new NoIndentScope())
            {
                EditorGUI.BeginChangeCheck();

                var next = EditorGUI.IntField(textRect, property.Get(platform));

                if (GUI.Button(resetRect, resetContent))
                {
                    next = 9264;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    property.Set(platform, next);
                }
            }
        }

        private void DisplayPlatform(Platform platform)
        {
            if (!platform.Active)
            {
                return;
            }

            DisplayPlatformHeader(platform);

            Undo.RecordObject(platform, EditPlatformUndoMessage);

            var settings = target as Settings;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                DisplayTriStateBool("Live Update", platform, Platform.PropertyAccessors.LiveUpdate);

                if (platform.IsLiveUpdateEnabled)
                {
                    DisplayLiveUpdatePort("Live Update Port", platform, Platform.PropertyAccessors.LiveUpdatePort);
                }

                DisplayTriStateBool("Debug Overlay", platform, Platform.PropertyAccessors.Overlay);
                if (platform.IsOverlayEnabled)
                {
                    DisplayPositionRect("Debug Location", platform, Platform.PropertyAccessors.OverlayPosition);
                    DisplayDebugFontSelection("Font size", platform, Platform.PropertyAccessors.OverlayFontSize);
                }

                DisplayOutputMode("Output Mode", platform);
                DisplaySampleRate("Sample Rate", platform);

                if (settings.HasPlatforms)
                {
                    DisplayProjectPlatform("Project Platform", platform);
                }
                else if (platform is PlatformDefault)
                {
                    DisplaySpeakerMode("Speaker Mode", platform);
                }

                DisplayCallbackHandler("Callback Handler", platform);

                DisplayInt("Virtual Channel Count", platform, Platform.PropertyAccessors.VirtualChannelCount, 1, 2048);
                DisplayInt("Real Channel Count", platform, Platform.PropertyAccessors.RealChannelCount, 1, 256);

                DisplayCodecChannels("Codec Counts", platform);

                DisplayDSPBufferSettings(platform);

                string warning = null;

                var buildTargetGroup =
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
#if UNITY_2021_2_OR_NEWER
                var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
                var scriptingBackend = PlayerSettings.GetScriptingBackend(namedBuildTarget);
#else
                ScriptingImplementation scriptingBackend = PlayerSettings.GetScriptingBackend(buildTargetGroup);
#endif

                if (scriptingBackend != ScriptingImplementation.IL2CPP)
                {
                    warning = "Only supported on the IL2CPP scripting backend";
                }

                DisplayPlugins("Static Plugins", staticPluginsView, platform, ref expandStaticPlugins, warning);

                DisplayPlugins("Dynamic Plugins", dynamicPluginsView, platform, ref expandDynamicPlugins);

                DisplayThreadAffinity("Thread Affinity", platform);

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(target);
                }
            }
        }

        private void DisplayPlatformHeader(Platform platform)
        {
            string type;

            if (platform is PlatformGroup)
            {
                type = "platform group";
            }
            else if (platform.IsIntrinsic)
            {
                type = "built-in platform";
            }
            else
            {
                type = "platform";
            }

            if (platform.Parent != null || platform is PlatformPlayInEditor)
            {
                Platform parent;
                GUIContent labelContent;

                if (platform is PlatformPlayInEditor)
                {
                    labelContent = new GUIContent(string.Format("<b>{0}</b>: {1} inheriting from Unity build target: ",
                        platform.DisplayName, type));
                    parent = EditorSettings.Instance.CurrentEditorPlatform;

                    while (!parent.Active)
                    {
                        parent = parent.Parent;
                    }
                }
                else
                {
                    labelContent = new GUIContent(string.Format("<b>{0}</b>: {1} inheriting from", platform.DisplayName, type));
                    parent = platform.Parent;
                }

                var rect = EditorGUILayout.GetControlRect();

                var buttonContent = new GUIContent(string.Format("<b>{0}</b>", parent.DisplayName));
                var iconContent = EditorGUIUtility.IconContent("UnityEditor.FindDependencies");

                var labelRect = LabelRect(rect);
                labelRect.width = platformHeaderStyle.CalcSize(labelContent).x;

                var buttonRect = rect;
                buttonRect.x = labelRect.xMax;
                buttonRect.width = platformHeaderStyle.CalcSize(buttonContent).x;

                var iconRect = rect;
                iconRect.x = buttonRect.xMax;
                iconRect.width = iconContent.image.width;
                iconRect.height = iconContent.image.height;
                iconRect.y += (rect.height - iconRect.height) / 2;

                buttonRect.width += iconRect.width;

                GUI.Label(labelRect, labelContent, platformHeaderStyle);

                if (GUI.Button(buttonRect, buttonContent, platformHeaderStyle))
                {
                    platformsView.SelectAndFramePlatform(parent);
                }

                if (Event.current.type == EventType.Repaint)
                {
                    GUI.DrawTexture(iconRect, iconContent.image);
                }

                EditorGUIUtility.AddCursorRect(buttonRect, MouseCursor.Link);
            }
            else
            {
                var text = string.Format("<b>{0}</b>: {1}", platform.DisplayName, type);
                EditorGUILayout.LabelField(text, platformHeaderStyle);
            }
        }

        private void DisplayDSPBufferSettings(Platform platform)
        {
            var rect = DrawPlatformPropertyLabel("DSP Buffer Settings", platform,
                Platform.PropertyAccessors.DSPBufferLength, Platform.PropertyAccessors.DSPBufferCount);

            var useAutoDSPBufferSettings = DisplayAutoDSPBufferSettings(rect, platform);

            if (!useAutoDSPBufferSettings)
            {
                DisplayDSPBufferFields(platform);
            }
        }

        private bool DisplayAutoDSPBufferSettings(Rect rect, Platform platform)
        {
            var lengthProperty = Platform.PropertyAccessors.DSPBufferLength;
            var countProperty = Platform.PropertyAccessors.DSPBufferCount;

            var style = GUI.skin.toggle;

            var content = new GUIContent("Auto");
            rect.width = style.CalcSize(content).x;

            var useAutoDSPBufferSettings = lengthProperty.Get(platform) == 0 && countProperty.Get(platform) == 0;

            EditorGUI.BeginChangeCheck();

            useAutoDSPBufferSettings = GUI.Toggle(rect, useAutoDSPBufferSettings, content, style);

            if (EditorGUI.EndChangeCheck())
            {
                if (useAutoDSPBufferSettings)
                {
                    lengthProperty.Set(platform, 0);
                    countProperty.Set(platform, 0);

                }
                else
                {
                    // set a helpful default value (real default is 0 for auto behaviour)
                    lengthProperty.Set(platform, 512);
                    countProperty.Set(platform, 4);
                }
            }

            return useAutoDSPBufferSettings;
        }

        private void DisplayDSPBufferFields(Platform platform)
        {
            var lengthProperty = Platform.PropertyAccessors.DSPBufferLength;
            var countProperty = Platform.PropertyAccessors.DSPBufferCount;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();

                var nextLength = Mathf.Max(EditorGUILayout.IntField("DSP Buffer Length", lengthProperty.Get(platform)), 8);
                var nextCount = Mathf.Max(EditorGUILayout.IntField("DSP Buffer Count", countProperty.Get(platform)), 2);

                if (EditorGUI.EndChangeCheck())
                {
                    lengthProperty.Set(platform, nextLength);
                    countProperty.Set(platform, nextCount);
                }
            }
        }

        private void DisplayPlugins(string title, PlatformPropertyStringListView view, Platform platform,
            ref bool expand, string warning = null)
        {
            var plugins = view.property.Get(platform);

            var fullTitle = string.Format("{0}: {1}", title, plugins.Count);

            DrawPlatformPropertyFoldout(fullTitle, ref expand, platform, view.property);

            if (expand)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    if (warning != null)
                    {
                        EditorGUILayout.HelpBox(warning, MessageType.Warning);
                    }

                    view.platform = platform;
                    view.DrawLayout();
                }
            }
        }

        protected override void OnHeaderGUI()
        {
            AffirmResources();

            var text = new GUIContent("FMOD Settings");

            var textSize = mainHeaderStyle.CalcSize(text);
            var iconSize = GUI.skin.label.CalcSize(mainHeaderIcon);

            var rect = EditorGUILayout.GetControlRect(false, Math.Max(textSize.y, iconSize.y));

            var iconRect = rect;
            iconRect.width = iconSize.x;
            iconRect.height = iconSize.y;
            iconRect.y += (rect.height - iconRect.height) / 2;

            var textRect = rect;
            textRect.xMin = iconRect.xMax;
            textRect.height = textSize.y;
            textRect.y += (rect.height - textRect.height) / 2;

            GUI.Label(iconRect, mainHeaderIcon);
            GUI.Label(textRect, text, mainHeaderStyle);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var invalidSourceMessage = CheckValidSource();

            DrawImportSection(invalidSourceMessage);

            if (invalidSourceMessage != null)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.Separator();

            DrawInitializationSection();

            EditorGUILayout.Separator();

            DrawBehaviorSection();

            EditorGUILayout.Separator();

            DrawUserInterfaceSection();

            EditorGUILayout.Separator();

            DrawPlatforms();

            serializedObject.ApplyModifiedProperties();

            ApplyPendingActions();
        }

        private bool DrawSectionHeaderLayout(Section section, string title)
        {
            var rect = EditorGUILayout.GetControlRect();

            return DrawSectionHeader(rect, section, title);
        }

        private bool DrawSectionHeader(Rect rect, Section section, string title)
        {
            AffirmResources();

            var expanded = (section & sExpandedSections) == section;

            expanded = EditorGUI.Foldout(rect, expanded, title, true, sectionHeaderStyle);

            sExpandedSections = expanded ? (sExpandedSections | section) : (sExpandedSections & ~section);

            return expanded;
        }

        private void DrawImportSection(string invalidSourceMessage)
        {
            if (DrawSectionHeaderLayout(Section.BankImport, "Bank Import"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawSourceSelection(invalidSourceMessage);

                    if (invalidSourceMessage != null)
                    {
                        return;
                    }

                    DrawTargetSelection();
                }
            }
        }

        private void DrawSourceSelection(string invalidSourceMessage)
        {
            var popupRect = EditorUtils.DrawHelpButtonLayout(() => new SourceSelectionHelp());

            hasBankSourceChanged = false;

            var sourceType = hasSourceProject.boolValue
                ? SourceType.FMODStudioProject
                : (hasPlatforms.boolValue ? SourceType.MultiplePlatformBuild : SourceType.SinglePlatformBuild);

            sourceType = (SourceType)EditorGUI.EnumPopup(popupRect, "Source Type", sourceType);

            if (sourceType == SourceType.FMODStudioProject)
            {
                var oldPath = sourceProjectPath.stringValue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var newPath = EditorGUILayout.TextField("Studio Project Path", sourceProjectPath.stringValue);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (newPath.EndsWith(".fspro"))
                        {
                            sourceProjectPath.stringValue = newPath;
                        }
                    }

                    if (GUILayout.Button("Browse", GUILayout.ExpandWidth(false)))
                    {
                        GUI.FocusControl(null);
                        EditorApplication.delayCall += BrowseForSourceProjectPathAndRefresh;
                    }
                }

                // Cache in settings for runtime access in play-in-editor mode
                sourceBankPath.stringValue = GetBankDirectory(serializedObject);
                hasPlatforms.boolValue = true;
                hasSourceProject.boolValue = true;

                // First time project path is set or changes, copy to streaming assets
                if (sourceProjectPath.stringValue != oldPath)
                {
                    hasBankSourceChanged = true;
                }
            }
            else if (sourceType == SourceType.SinglePlatformBuild || sourceType == SourceType.MultiplePlatformBuild)
            {
                var oldPath = sourceBankPath.stringValue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(sourceBankPath, new GUIContent("Build Path"));

                    if (GUILayout.Button("Browse", GUILayout.ExpandWidth(false)))
                    {
                        GUI.FocusControl(null);
                        EditorApplication.delayCall += BrowseForSourceBankPathAndRefresh;
                    }
                }

                hasPlatforms.boolValue = (sourceType == SourceType.MultiplePlatformBuild);
                hasSourceProject.boolValue = false;

                // First time project path is set or changes, copy to streaming assets
                if (sourceBankPath.stringValue != oldPath)
                {
                    hasBankSourceChanged = true;
                }
            }

            if (invalidSourceMessage != null)
            {
                EditorGUILayout.HelpBox(invalidSourceMessage, MessageType.Error, true);
            }
        }

        private void BrowseForSourceProjectPathAndRefresh()
        {
            if (BrowseForSourceProjectPath(serializedObject))
            {
                Repaint();
            }
        }

        private void BrowseForSourceBankPathAndRefresh()
        {
            if (BrowseForSourceBankPath(serializedObject, hasPlatforms.boolValue))
            {
                Repaint();
            }
        }

        private string CheckValidSource()
        {
            bool validSource;
            string invalidMessage;
            EditorUtils.ValidateSource(out validSource, out invalidMessage);

            if (validSource)
            {
                return null;
            }
            else
            {
                sExpandedSections |= Section.BankImport;

                return invalidMessage + "\n\nFor detailed setup instructions, please see the FMOD/Help/Getting Started menu item.";
            }
        }

        private class SourceSelectionHelp : HelpContent
        {
            private GUIStyle style;

            private readonly GUIContent introduction = new GUIContent("Choose how to access your FMOD Studio content:");

            private readonly ListEntry[] listEntries = {
                new ListEntry("FMOD Studio Project",
                    "If you have the complete FMOD Studio project."
                ),
                new ListEntry("Single Platform Build",
                    "If you have the contents of the <b>Build</b> folder for a single platform."
                ),
                new ListEntry("Multiple Platform Build",
                    "If you have the contents of the <b>Build</b> folder for multiple platforms, " +
                    "with each platform in its own subdirectory."
                ),
            };

            protected override void Prepare()
            {
                style = new GUIStyle(GUI.skin.label) {
                    richText = true,
                    wordWrap = true,
                };
            }

            private struct ListEntry
            {
                public ListEntry(string label, string description)
                {
                    this.label = new GUIContent(label);
                    this.description = new GUIContent(description);
                }

                public GUIContent label;
                public GUIContent description;
            }

            protected override Vector2 GetContentSize()
            {
                var size = new Vector2(440, 0);

                size.y += style.margin.top;
                size.y += style.CalcHeight(introduction, size.x);

                foreach (var entry in listEntries)
                {
                    size.y += style.margin.top;
                    size.y += style.CalcHeight(entry.description, size.x - EditorGUIUtility.labelWidth);
                }

                size.y += style.margin.bottom;

                return size;
            }

            protected override void DrawContent()
            {
                EditorGUILayout.LabelField(introduction, style);

                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var entry in listEntries)
                    {
                        EditorGUILayout.LabelField(entry.label, entry.description, style);
                    }
                }
            }
        }

        private static string GetBankDirectory(SerializedObject serializedObject)
        {
            var sourceProjectPath = serializedObject.FindProperty("sourceProjectPath");
            var sourceBankPath = serializedObject.FindProperty("sourceBankPath");
            var hasSourceProject = serializedObject.FindProperty("HasSourceProject");

            if (hasSourceProject.boolValue && !string.IsNullOrEmpty(sourceProjectPath.stringValue))
            {
                var projectFolder = Path.GetDirectoryName(sourceProjectPath.stringValue);
                return RuntimeUtils.GetCommonPlatformPath(Path.Combine(projectFolder, EditorUtils.BuildFolder));
            }
            else if (!string.IsNullOrEmpty(sourceBankPath.stringValue))
            {
                return RuntimeUtils.GetCommonPlatformPath(Path.GetFullPath(sourceBankPath.stringValue));
            }
            else
            {
                return null;
            }
        }

        private void DrawTargetSelection()
        {
            var settings = target as Settings;

            hasBankTargetChanged = false;

            var importTypeNames = importType.enumDisplayNames;
            var importTypeIndex = importType.enumValueIndex;

            var newImportTypeIndex = EditorGUILayout.Popup("Import Type", importTypeIndex, importTypeNames);

            if (newImportTypeIndex != importType.enumValueIndex)
            {
                var deleteBanks = EditorUtility.DisplayDialog(
                    "FMOD Bank Import Type Changed",
                    "Do you want to delete the " + importTypeNames[importTypeIndex] + " banks in " + settings.TargetPath,
                    "Yes", "No");

                if (deleteBanks)
                {
                    // Delete the old banks
                    EventManager.RemoveBanks(settings.TargetPath);
                }

                hasBankTargetChanged = true;
                importType.enumValueIndex = newImportTypeIndex;
            }

            // ----- Asset Sub Directory -------------
            SerializedProperty targetSubFolder;
            string label;

            if (importType.intValue == (int)ImportType.AssetBundle)
            {
                targetSubFolder = targetAssetPath;
                label = "FMOD Asset Sub Folder";
            }
            else
            {
                targetSubFolder = targetBankFolder;
                label = "FMOD Bank Sub Folder";
            }

            var newSubFolder = EditorGUILayout.DelayedTextField(label, targetSubFolder.stringValue);

            if (newSubFolder != targetSubFolder.stringValue)
            {
                EventManager.RemoveBanks(settings.TargetPath);
                targetSubFolder.stringValue = newSubFolder;
                hasBankTargetChanged = true;
            }

            DisplayBankRefreshSettings(bankRefreshCooldown, showBankRefreshWindow, true);

            EditorGUILayout.PropertyField(eventLinkage);
        }

        private void DrawBehaviorSection()
        {
            if (DrawSectionHeaderLayout(Section.Behavior, "Behavior"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(stopEventsOutsideMaxDistance,
                        new GUIContent("Stop Events Outside Max Distance"));
                }
            }
        }

        private void DrawUserInterfaceSection()
        {
            if (DrawSectionHeaderLayout(Section.UserInterface, "User Interface"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.BeginChangeCheck();

                    EditorGUILayout.PropertyField(meterChannelOrdering, new GUIContent("Meter Channel Ordering"));

                    if (EditorGUI.EndChangeCheck() && EventBrowser.IsOpen)
                    {
                        EditorWindow.GetWindow<EventBrowser>("FMOD Events", false).Repaint();
                    }
                }
            }
        }

        private void DrawInitializationSection()
        {
            if (DrawSectionHeaderLayout(Section.Initialization, "Initialization"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    loggingLevel.intValue = EditorGUILayout.IntPopup("Logging Level",
                        loggingLevel.intValue, LoggingDisplay, LoggingValues);

                    EditorGUILayout.PropertyField(enableErrorCallback,
                        new GUIContent("Enable API Error Logging"));

                    EditorGUILayout.PropertyField(enableMemoryTracking, new GUIContent("Enable Memory Tracking"));

                    using (new EditorGUI.DisabledScope(importType.intValue == (int)ImportType.AssetBundle))
                    {
                        EditorGUILayout.PropertyField(bankLoadType, new GUIContent("Load Banks"));

                        switch ((BankLoadType)bankLoadType.intValue)
                        {
                            case BankLoadType.All:
                                break;
                            case BankLoadType.Specified:
                                automaticEventLoading.boolValue = false;
                                DisplayBanksToLoad();
                                break;
                            case BankLoadType.None:
                                automaticEventLoading.boolValue = false;
                                break;
                            default:
                                break;
                        }

                        using (new EditorGUI.DisabledScope(bankLoadType.intValue == (int)BankLoadType.None))
                        {
                            EditorGUILayout.PropertyField(automaticSampleLoading, new GUIContent("Load Bank Sample Data"));
                        }

                        EditorGUILayout.DelayedTextField(encryptionKey, new GUIContent("Bank Encryption Key"));
                    }
                }
            }
        }

        private void DisplayBanksToLoad()
        {
            banksToLoad.isExpanded = EditorGUILayout.Foldout(banksToLoad.isExpanded, "Specified Banks", true);

            if (banksToLoad.isExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    banksToLoadView.DrawLayout();
                }
            }
        }

        private void BrowseForBankToLoad()
        {
            var bankDirectory = CurrentBankDirectory();
            var path = EditorUtility.OpenFilePanel("Locate Bank", bankDirectory, "bank");

            if (!string.IsNullOrEmpty(path))
            {
                serializedObject.Update();

                path = RuntimeUtils.GetCommonPlatformPath(path);
                path = path.Replace(bankDirectory, "");
                path = Regex.Replace(path, "\\.bank$", "");

                banksToLoad.ArrayAdd(p => p.stringValue = path);

                serializedObject.ApplyModifiedProperties();

                Repaint();
            }
        }

        private void AddAllBanksToLoad()
        {
            var sourceDir = CurrentBankDirectory();
            var banksFound = Directory.GetFiles(sourceDir, "*.bank", SearchOption.AllDirectories);

            serializedObject.Update();

            for (var i = 0; i < banksFound.Length; i++)
            {
                var bankLongName = RuntimeUtils.GetCommonPlatformPath(Path.GetFullPath(banksFound[i]));
                var bankShortName = bankLongName.Replace(sourceDir, "");
                bankShortName = Regex.Replace(bankShortName, "\\.bank$", "");

                if (!banksToLoad.ArrayContains(p => p.stringValue == bankShortName))
                {
                    banksToLoad.ArrayAdd(p => p.stringValue = bankShortName);
                }
            }

            serializedObject.ApplyModifiedProperties();

            Repaint();
        }

        private string CurrentBankDirectory()
        {
            var settings = target as Settings;

            string bankDirectory;

            if (settings.HasPlatforms)
            {
                bankDirectory = string.Format("{0}/{1}/",
                    settings.SourceBankPath, EditorSettings.Instance.CurrentEditorPlatform.BuildDirectory);
            }
            else
            {
                bankDirectory = settings.SourceBankPath + "/";
            }

            return RuntimeUtils.GetCommonPlatformPath(Path.GetFullPath(bankDirectory));
        }

        private void DrawPlatforms()
        {
            platformsView.ReloadIfNecessary();

            if (DrawSectionHeaderLayout(Section.PlatformSpecific, "Platform Specific"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    platformsView.DrawLayout();

                    var selectedPlatform = platformsView.SelectedPlatform;

                    if (selectedPlatform != null)
                    {
                        DisplayPlatform(selectedPlatform);
                    }
                }
            }
        }

        private class PlatformsView : TreeView
        {
            private const float RowPadding = 2;

            private Settings settings;

            private static UnityEditorInternal.ReorderableList.Defaults s_Defaults;

            private const float HeaderHeight = 3;
            private const float BodyHeight = 150;
            private const float FooterHeight = 13;
            private const float TotalHeight = HeaderHeight + BodyHeight + FooterHeight;

            private const float ButtonWidth = 25;
            private const float ButtonHeight = 16;
            private const float ButtonMarginTop = 0;

            private const float FooterMarginRight = 10;

            private static readonly RectOffset BodyPadding = new RectOffset(1, 2, 1, 4);
            private static readonly RectOffset FooterPadding = new RectOffset(4, 4, 0, 0);

            private static readonly Vector2 DragHandleSize = new Vector2(10, 7);
            private static readonly Vector2 DragHandlePadding = new Vector2(5, 6);

            public PlatformsView(Settings settings, TreeViewState state) : base(state)
            {
                this.settings = settings;
                rowHeight = EditorGUIUtility.singleLineHeight + RowPadding;
            }

            public Platform SelectedPlatform
            {
                get
                {
                    var selection = GetSelection();

                    if (selection.Count != 1)
                    {
                        return null;
                    }

                    var selectedItem = FindItem(selection[0], rootItem) as PlatformItem;

                    if (selectedItem == null)
                    {
                        return null;
                    }

                    return selectedItem.platform;
                }
            }

            private static UnityEditorInternal.ReorderableList.Defaults defaultBehaviours
            {
                get
                {
                    if (s_Defaults == null)
                    {
                        s_Defaults = new UnityEditorInternal.ReorderableList.Defaults();
                    }

                    return s_Defaults;
                }
            }

            public void DrawLayout()
            {
                var rect = EditorGUILayout.GetControlRect(false, TotalHeight);
                rect = EditorGUI.IndentedRect(rect);

                var headerRect = rect;
                headerRect.height = HeaderHeight;

                var bodyRect = rect;
                bodyRect.y = headerRect.yMax;
                bodyRect.height = BodyHeight;

                var footerRect = rect;
                footerRect.xMax -= FooterMarginRight;
                footerRect.y = bodyRect.yMax;
                footerRect.height = FooterHeight;

                var removeRect = footerRect;
                removeRect.x = footerRect.xMax - FooterPadding.right - ButtonWidth;
                removeRect.y += ButtonMarginTop;
                removeRect.width = ButtonWidth;
                removeRect.height = ButtonHeight;

                var addRect = footerRect;
                addRect.x = removeRect.x - ButtonWidth;
                addRect.y += ButtonMarginTop;
                addRect.width = ButtonWidth;
                addRect.height = ButtonHeight;

                footerRect.xMin = addRect.xMin - FooterPadding.left;
                footerRect.xMax = removeRect.xMax + FooterPadding.right;

                defaultBehaviours.DrawHeaderBackground(headerRect);

                if (Event.current.type == EventType.Repaint)
                {
                    defaultBehaviours.boxBackground.Draw(bodyRect, false, false, false, false);
                }

                var contentRect = BodyPadding.Remove(bodyRect);

                using (new NoIndentScope())
                {
                    base.OnGUI(contentRect);
                }

                if (Event.current.type == EventType.Repaint)
                {
                    defaultBehaviours.footerBackground.Draw(footerRect, false, false, false, false);
                }

                if (GUI.Button(addRect, defaultBehaviours.iconToolbarPlusMore, defaultBehaviours.preButton))
                {
                    DoAddMenu(addRect);
                }

                using (new EditorGUI.DisabledScope(SelectedPlatform == null))
                {
                    if (GUI.Button(removeRect, defaultBehaviours.iconToolbarMinus, defaultBehaviours.preButton))
                    {
                        DeleteSelectedPlatform();
                    }
                }
            }

            private void DoAddMenu(Rect rect)
            {
                var menu = new GenericMenu();

                menu.AddItem(new GUIContent("New Group"), false, AddGroup);

                menu.AddSeparator(string.Empty);

                IEnumerable<Platform> missingPlatforms = settings.Platforms
                    .Where(p => !p.Active)
                    .OrderBy(p => p.DisplayName, new NaturalComparer());

                foreach (var platform in missingPlatforms)
                {
                    menu.AddItem(new GUIContent(platform.DisplayName), false, AddPlatform, platform.Identifier);
                }

                menu.DropDown(rect);
            }

            private void AddPlatform(object data)
            {
                var identifier = data as string;

                var platform = settings.FindPlatform(identifier);

                const string UndoMessage = "Add FMOD Platform";

                Undo.RecordObjects(new UnityEngine.Object[] { settings, platform, platform.Parent }, UndoMessage);

                platform.DisplaySortOrder = UpdateSortOrderForChildren(platform.Parent, platform, UndoMessage);

                settings.AddPlatformProperties(platform);

                ForceReload();

                SelectAndFramePlatform(platform);
            }

            private void AddGroup()
            {
                const string UndoMessage = "Add FMOD Platform Group";

                Undo.RecordObjects(new UnityEngine.Object[] { settings, settings.DefaultPlatform }, UndoMessage);

                var sortOrder = UpdateSortOrderForChildren(settings.DefaultPlatform, null, UndoMessage);

                var group = EditorSettings.Instance.AddPlatformGroup("New Group", sortOrder);

                Undo.RegisterCreatedObjectUndo(group, UndoMessage);

                ForceReload();

                SelectAndFramePlatform(group);

                // Bring up the rename UI
                DoubleClickedItem(group.Identifier.GetHashCode());
            }

            private int UpdateSortOrderForChildren(Platform platform, Platform skipChild, string undoMessage)
            {
                var sortOrder = 0;

                foreach (var childID in platform.ChildIdentifiers)
                {
                    var child = settings.FindPlatform(childID);

                    if (child.Active && child != skipChild)
                    {
                        Undo.RecordObject(child, undoMessage);

                        child.DisplaySortOrder = sortOrder;
                        ++sortOrder;
                    }
                }

                return sortOrder;
            }

            // Removes a platform from the inheritance hierarchy and clears its properties, thus hiding
            // it in the UI. Also destroys the platform if it is a group.
            private void DeleteSelectedPlatform()
            {
                var platform = SelectedPlatform;

                if (platform == null || platform == settings.DefaultPlatform || platform == settings.PlayInEditorPlatform)
                {
                    return;
                }


                const string UndoMessage = "Delete FMOD Platform";

                Undo.RecordObjects(new UnityEngine.Object[] { platform, platform.Parent, settings }, UndoMessage);

                while (platform.ChildIdentifiers.Count > 0)
                {
                    var child = settings.FindPlatform(platform.ChildIdentifiers[platform.ChildIdentifiers.Count - 1]);

                    SetPlatformParent(UndoMessage, settings, child, platform.Parent, (int)platform.DisplaySortOrder + 1);
                }

                if (platform is PlatformGroup)
                {
                    var group = platform as PlatformGroup;

                    settings.SetPlatformParent(group, null);
                    settings.RemovePlatform(group.Identifier);

                    Undo.DestroyObjectImmediate(group);
                }
                else
                {
                    platform.ClearProperties();

                    Undo.RecordObject(settings.DefaultPlatform, UndoMessage);

                    settings.SetPlatformParent(platform, settings.DefaultPlatform);
                }

                ForceReload();
            }

            public void SelectAndFramePlatform(Platform platform)
            {
                SetSelection(new List<int>() { platform.Identifier.GetHashCode() },
                    TreeViewSelectionOptions.RevealAndFrame);
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    defaultBehaviours.elementBackground.Draw(args.rowRect, false, args.selected, args.selected, args.focused);

                    if (IsItemDraggable(args.item))
                    {
                        var dragRect = new Rect(args.rowRect.position + DragHandlePadding, DragHandleSize);

                        defaultBehaviours.draggingHandle.Draw(dragRect, false, false, false, false);
                    }

                    var labelContent = new GUIContent(args.label);

                    var labelStyle = GUI.skin.label;

                    var labelRect = args.rowRect;
                    CenterRectUsingSingleLineHeight(ref labelRect);

                    labelRect.x = GetContentIndent(args.item);
                    labelRect.width = GUI.skin.label.CalcSize(labelContent).x;

                    var renameIcon = EditorGUIUtility.IconContent("SettingsIcon").image;

                    var canRename = CanRename(args.item);

                    if (canRename)
                    {
                        labelContent.tooltip = "Double-click to rename";
                        labelRect.width += renameIcon.width;
                    }

                    GUI.Label(labelRect, labelContent);

                    if (canRename && Event.current.type == EventType.Repaint)
                    {
                        var iconRect = new Rect() {
                            x = labelRect.xMax - renameIcon.width,
                            y = labelRect.yMax - labelStyle.padding.bottom - renameIcon.height,
                            width = renameIcon.width,
                            height = renameIcon.height,
                        };

                        GUI.DrawTexture(iconRect, renameIcon, ScaleMode.ScaleToFit,
                            true, 0, labelStyle.normal.textColor, 0, 0);
                    }
                }
            }

            public void ForceReload()
            {
                Reload();
                ExpandAll();
            }

            public void ReloadIfNecessary()
            {
                if (!isInitialized)
                {
                    ForceReload();
                }
            }

            protected override bool CanMultiSelect(TreeViewItem item)
            {
                return false;
            }

            protected override bool CanChangeExpandedState(TreeViewItem item)
            {
                return false;
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem(-1, -1);

                root.AddChild(CreateItem(settings.PlayInEditorPlatform));

                var defaultItem = CreateItem(settings.DefaultPlatform);
                root.AddChild(defaultItem);

                CreateItems(defaultItem, settings.DefaultPlatform.ChildIdentifiers);

                SetupDepthsFromParentsAndChildren(root);

                return root;
            }

            private class PlatformItem : TreeViewItem
            {
                public Platform platform;

                public PlatformItem(Platform platform)
                    : base(platform.Identifier.GetHashCode(), 0, platform.DisplayName)
                {
                    this.platform = platform;
                }
            }

            private void CreateItems(TreeViewItem parent, IEnumerable<string> platformIdentifiers)
            {
                foreach (var identifier in platformIdentifiers)
                {
                    var platform = settings.FindPlatform(identifier);

                    if (platform.Active)
                    {
                        var item = CreateItem(platform);
                        parent.AddChild(item);

                        CreateItems(item, platform.ChildIdentifiers);
                    }
                }
            }

            private static TreeViewItem CreateItem(Platform platform)
            {
                return new PlatformItem(platform);
            }

            protected override void DoubleClickedItem(int id)
            {
                var item = FindItem(id, rootItem);

                if (CanRename(item))
                {
                    BeginRename(item);
                }
            }

            protected override bool CanRename(TreeViewItem item)
            {
                var platformItem = item as PlatformItem;
                return (platformItem != null) && (platformItem.platform is PlatformGroup);
            }

            protected override void RenameEnded(RenameEndedArgs args)
            {
                if (!args.acceptedRename || string.IsNullOrEmpty(args.newName))
                {
                    return;
                }

                var item = FindItem(args.itemID, rootItem) as PlatformItem;

                if (item == null)
                {
                    return;
                }

                var group = item.platform as PlatformGroup;

                if (group == null)
                {
                    return;
                }

                // Undo.RecordObject doesn't capture PlatformGroup.displayName, maybe due to inheritance?
                // This means we need to use the SerializedObject interface instead.
                var serializedGroup = new SerializedObject(group);
                var displayName = serializedGroup.FindProperty("displayName");

                displayName.stringValue = args.newName;

                serializedGroup.ApplyModifiedProperties();

                item.displayName = args.newName;
            }

            protected override bool CanStartDrag(CanStartDragArgs args)
            {
                return IsItemDraggable(args.draggedItem);
            }

            private bool IsItemDraggable(TreeViewItem draggedItem)
            {
                var item = draggedItem as PlatformItem;

                return (item != null) && !item.platform.IsIntrinsic;
            }

            protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
            {
                var item = FindItem(args.draggedItemIDs[0], rootItem) as PlatformItem;

                if (item != null)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new UnityEngine.Object[] { item.platform };
                    DragAndDrop.StartDrag("Change FMOD Platform Inheritance");
                }
            }

            protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
            {
                if (DragAndDrop.objectReferences.Length != 1)
                {
                    return DragAndDropVisualMode.None;
                }

                var draggedPlatform = DragAndDrop.objectReferences[0] as Platform;

                if (draggedPlatform == null)
                {
                    return DragAndDropVisualMode.None;
                }

                var parentItem = args.parentItem as PlatformItem;

                if (parentItem == null)
                {
                    return DragAndDropVisualMode.None;
                }

                var parent = parentItem.platform;

                if (parent is PlatformPlayInEditor)
                {
                    return DragAndDropVisualMode.None;
                }

                switch (args.dragAndDropPosition)
                {
                    case DragAndDropPosition.UponItem:
                        return HandleDragOverPlatform(draggedPlatform, parent, args.performDrop);
                    case DragAndDropPosition.BetweenItems:
                        return HandleDragBetweenChildren(draggedPlatform, parent, args.insertAtIndex, args.performDrop);
                    case DragAndDropPosition.OutsideItems:
                        return DragAndDropVisualMode.Move;
                    default:
                        return DragAndDropVisualMode.None;
                }
            }

            private DragAndDropVisualMode HandleDragOverPlatform(Platform draggedPlatform, Platform parent, bool performDrop)
            {
                if (parent == draggedPlatform)
                {
                    return DragAndDropVisualMode.Move;
                }

                if (parent.InheritsFrom(draggedPlatform))
                {
                    return DragAndDropVisualMode.None;
                }

                if (performDrop)
                {
                    SetPlatformParent("Set FMOD Platform Inheritance", settings, draggedPlatform, parent);
                    ForceReload();
                }

                return DragAndDropVisualMode.Link;
            }

            private DragAndDropVisualMode HandleDragBetweenChildren(Platform draggedPlatform, Platform parent,
                int insertAtIndex, bool performDrop)
            {
                if (parent.InheritsFrom(draggedPlatform))
                {
                    return DragAndDropVisualMode.None;
                }

                if (performDrop)
                {
                    SetPlatformParent("Set FMOD Platform Inheritance", settings, draggedPlatform, parent, insertAtIndex);
                    ForceReload();
                }

                return DragAndDropVisualMode.Move;
            }
        }

        private class ReorderableList : UnityEditorInternal.ReorderableList
        {
            private const float ElementPadding = 2;

            public ReorderableList(SerializedProperty property)
                : base(property.serializedObject, property, true, false, true, true)
            {
                headerHeight = 3;
                elementHeight = EditorGUIUtility.singleLineHeight + ElementPadding;
                drawElementCallback = DrawElement;
            }

            public void DrawLayout()
            {
                var rect = EditorGUILayout.GetControlRect(false, GetHeight());
                rect = EditorGUI.IndentedRect(rect);

                DoList(rect);
            }

            private void DrawElement(Rect rect, int index, bool active, bool focused)
            {
                using (new NoIndentScope())
                {
                    rect.height -= ElementPadding;

                    EditorGUI.PropertyField(rect, serializedProperty.GetArrayElementAtIndex(index), GUIContent.none);
                }
            }
        }

        private class PlatformPropertyStringListView : UnityEditorInternal.ReorderableList
        {
            private const float ElementPadding = 2;

            public Platform platform;

            private List<string> displayList;

            public PlatformPropertyStringListView(Platform.PropertyAccessor<List<string>> property)
                : base(null, typeof(string), true, false, true, true)
            {
                this.property = property;

                displayList = new List<string>();
                list = displayList;

                headerHeight = 3;
                elementHeight = EditorGUIUtility.singleLineHeight + ElementPadding;

                drawElementCallback = DrawElement;
                onAddCallback = AddElement;
                onRemoveCallback = RemoveElement;
                onReorderCallback = OnReorder;
            }

            public Platform.PropertyAccessor<List<string>> property { get; private set; }

            // We need this because ReorderableList modifies the list before calling
            // onReorderCallback, meaning we can't call AffirmOverriddenList
            // soon enough.
            public void DrawLayout()
            {
                if (IsReloadNeeded())
                {
                    displayList.Clear();
                    displayList.AddRange(property.Get(platform));
                }

                var rect = EditorGUILayout.GetControlRect(false, GetHeight());
                rect = EditorGUI.IndentedRect(rect);

                DoList(rect);
            }

            public bool IsReloadNeeded()
            {
                var propertyList = property.Get(platform);

                if (displayList.Count != propertyList.Count)
                {
                    return true;
                }

                for (var i = 0; i < displayList.Count; ++i)
                {
                    if (displayList[i] != propertyList[i])
                    {
                        return true;
                    }
                }

                return false;
            }

            private void DrawElement(Rect rect, int index, bool active, bool focused)
            {
                using (new NoIndentScope())
                {
                    rect.height -= ElementPadding;

                    EditorGUI.BeginChangeCheck();

                    var newValue = EditorGUI.TextField(rect, list[index] as string);

                    if (EditorGUI.EndChangeCheck())
                    {
                        displayList[index] = newValue;
                        AffirmOverriddenList()[index] = newValue;
                    }
                }
            }

            private void AddElement(UnityEditorInternal.ReorderableList list)
            {
                AffirmOverriddenList().Add(string.Empty);
            }

            private void RemoveElement(UnityEditorInternal.ReorderableList list)
            {
                AffirmOverriddenList().RemoveAt(list.index);
            }

            private void OnReorder(UnityEditorInternal.ReorderableList list)
            {
                var propertyList = AffirmOverriddenList();

                propertyList.Clear();
                propertyList.AddRange(displayList);
            }

            private List<string> AffirmOverriddenList()
            {
                if (!property.HasValue(platform))
                {
                    var newList = new List<string>(property.Get(platform));

                    property.Set(platform, newList);
                }

                return property.Get(platform);
            }
        }

        // If insertAtIndex == -1, insert at the end
        private static void SetPlatformParent(string undoMessage, Settings settings, Platform child, Platform parent, int insertAtIndex = -1)
        {
            if (parent == child.Parent)
            {
                if (insertAtIndex > child.DisplaySortOrder)
                {
                    --insertAtIndex;
                }

                if (insertAtIndex == child.DisplaySortOrder)
                {
                    return;
                }
            }

            Undo.RecordObjects(new[] { child, child.Parent, parent }, undoMessage);

            var index = 0;

            for (var i = 0; i < parent.ChildIdentifiers.Count; ++i)
            {
                var sibling = settings.FindPlatform(parent.ChildIdentifiers[i]);

                if (sibling.Active && sibling != child)
                {
                    if (index == insertAtIndex)
                    {
                        ++index;
                    }

                    Undo.RecordObject(sibling, undoMessage);

                    sibling.DisplaySortOrder = index;
                    ++index;
                }
            }

            if (insertAtIndex == -1)
            {
                insertAtIndex = index;
            }

            child.DisplaySortOrder = insertAtIndex;

            settings.SetPlatformParent(child, parent);
        }

        private void ApplyPendingActions()
        {
            if (hasBankSourceChanged || hasBankTargetChanged)
            {
                RefreshBanks();
            }
        }

        private void RefreshBanks()
        {
            var settings = target as Settings;

            if (lastSourceBankPath != settings.SourceBankPath)
            {
                lastSourceBankPath = settings.SourceBankPath;
                EventManager.RefreshBanks();
            }
        }

        public static void DisplayBankRefreshSettings(SerializedProperty cooldown, SerializedProperty showWindow,
            bool inInspector)
        {
            var controlRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            Rect labelRect;

            if (inInspector)
            {
                labelRect = LabelRect(controlRect);
            }
            else
            {
                labelRect = EditorGUI.IndentedRect(controlRect);
                labelRect.width = GUI.skin.label.CalcSize(BankRefreshLabel).x;
            }


            var popupRect = controlRect;
            popupRect.x = labelRect.xMax;
            popupRect.width = BankRefreshCooldownLabels.Max(l => EditorStyles.popup.CalcSize(l).x);

            using (new NoIndentScope())
            {
                GUI.Label(labelRect, BankRefreshLabel);

                cooldown.intValue = EditorGUI.IntPopup(popupRect, cooldown.intValue,
                    BankRefreshCooldownLabels, BankRefreshCooldownValues);

                if (cooldown.intValue >= 0)
                {
                    var toggleRect = controlRect;
                    toggleRect.xMin = popupRect.xMax + GUI.skin.toggle.margin.left;

                    showWindow.boolValue = EditorGUI.ToggleLeft(toggleRect, "Show Status Window", showWindow.boolValue);
                }
            }
        }

        private static Rect LabelRect(Rect controlRect)
        {
            var result = controlRect;
            result.width = EditorGUIUtility.labelWidth;
            result = EditorGUI.IndentedRect(result);

            return result;
        }

        public static bool BrowseForSourceProjectPath(SerializedObject serializedObject)
        {
            serializedObject.Update();
            var sourceProjectPath = serializedObject.FindProperty("sourceProjectPath");
            var sourceBankPath = serializedObject.FindProperty("sourceBankPath");
            var hasSourceProject = serializedObject.FindProperty("HasSourceProject");
            var hasPlatforms = serializedObject.FindProperty("HasPlatforms");

            var newPath = EditorUtility.OpenFilePanel("Locate Studio Project", sourceProjectPath.stringValue, "fspro");

            if (string.IsNullOrEmpty(newPath))
            {
                return false;
            }
            else
            {
                hasSourceProject.boolValue = true;
                hasPlatforms.boolValue = true;
                newPath = MakePathRelative(newPath);
                sourceProjectPath.stringValue = newPath;
                sourceBankPath.stringValue = GetBankDirectory(serializedObject);
                serializedObject.ApplyModifiedProperties();
                EventManager.RefreshBanks();
                return true;
            }
        }

        public static bool BrowseForSourceBankPath(SerializedObject serializedObject, bool multiPlatform = false)
        {
            serializedObject.Update();
            var sourceBankPath = serializedObject.FindProperty("sourceBankPath");
            var hasSourceProject = serializedObject.FindProperty("HasSourceProject");
            var hasPlatforms = serializedObject.FindProperty("HasPlatforms");

            var newPath = EditorUtility.OpenFolderPanel("Locate Build Folder", sourceBankPath.stringValue, null);

            if (string.IsNullOrEmpty(newPath))
            {
                return false;
            }
            else
            {
                hasSourceProject.boolValue = false;
                hasPlatforms.boolValue = multiPlatform;
                newPath = MakePathRelative(newPath);
                sourceBankPath.stringValue = newPath;
                serializedObject.ApplyModifiedProperties();
                EventManager.RefreshBanks();
                return true;
            }
        }

        private static string MakePathRelative(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            var fullPath = Path.GetFullPath(path);
            var fullProjectPath = Path.GetFullPath(Environment.CurrentDirectory + Path.DirectorySeparatorChar);

            // If the path contains the Unity project path remove it and return the result
            if (fullPath.Contains(fullProjectPath))
            {
                fullPath = fullPath.Replace(fullProjectPath, "");
            }
            // If not, attempt to find a relative path on the same drive
            else if (Path.GetPathRoot(fullPath) == Path.GetPathRoot(fullProjectPath))
            {
                // Remove trailing slash from project path for split count simplicity
                if (fullProjectPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.CurrentCulture)) fullProjectPath = fullProjectPath.Substring(0, fullProjectPath.Length - 1);

                var fullPathSplit = fullPath.Split(Path.DirectorySeparatorChar);
                var projectPathSplit = fullProjectPath.Split(Path.DirectorySeparatorChar);
                var minNumSplits = Mathf.Min(fullPathSplit.Length, projectPathSplit.Length);
                var numCommonElements = 0;
                for (var i = 0; i < minNumSplits; i++)
                {
                    if (fullPathSplit[i] == projectPathSplit[i])
                    {
                        numCommonElements++;
                    }
                    else
                    {
                        break;
                    }
                }
                var result = "";
                var fullPathSplitLength = fullPathSplit.Length;
                for (var i = numCommonElements; i < fullPathSplitLength; i++)
                {
                    result += fullPathSplit[i];
                    if (i < fullPathSplitLength - 1)
                    {
                        result += '/';
                    }
                }

                var numAdditionalElementsInProjectPath = projectPathSplit.Length - numCommonElements;
                for (var i = 0; i < numAdditionalElementsInProjectPath; i++)
                {
                    result = "../" + result;
                }

                fullPath = result;
            }

            return fullPath.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
