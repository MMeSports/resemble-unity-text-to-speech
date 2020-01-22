﻿using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;

[CustomEditor(typeof(Pod)), CanEditMultipleObjects]
public class Pod_Editor : Editor
{
    private Pod pod;
    private bool rename;
    private bool firstRename;
    private string renameLabel;
    private Event e;
    private int renameControlID;
    private Editor clipEditor;
    private AudioImporter importer;
    private bool dirtySettings;
    private PlaceHolderAPIBridge.ClipRequest request;

    protected override void OnHeaderGUI()
    {
        //Init resources
        Styles.Load();
        pod = target as Pod;
        e = Event.current;

        //Bar
        Rect rect = new Rect(0, 0, Screen.width, 46);
        GUI.Box(rect, "", Styles.bigTitle);

        //Icon
        rect.Set(6, 6, 32, 32);
        GUI.DrawTexture(rect, Resemble_Resources.instance.icon);

        //Name & Rename field
        float width = Styles.header.CalcSize(new GUIContent(pod.set.name + " > ")).x;
        rect.Set(44, 4, width, 22);
        if (GUI.Button(rect, pod.set.name + " > ", Styles.header))
            Selection.activeObject = pod.set;
        rect.Set(rect.x + rect.width, rect.y, Screen.width - (rect.x + rect.width + 50), rect.height);

        //Autorename
        if (pod.autoRename)
        {
            pod.autoRename = false;
            rename = true;
            renameLabel = pod.name;
        }

        if (RenameableField(rect, ref rename, ref renameLabel, pod.name, out renameControlID))
        {
            pod.name = string.IsNullOrWhiteSpace(renameLabel) ? "Untitled" : renameLabel;
            if (pod.clip != null)
                pod.clip.name = pod.name;
            if (pod.clipCopy != null)
                pod.clipCopy.name = pod.name;
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(pod));
            Repaint();
        }

        //Resemble.ai link
        rect.Set(Screen.width - 127, rect.y + 24, 125, 16);
        if (GUI.Button(rect, "Show in Resemble.ai", EditorStyles.linkLabel))
            Application.OpenURL("https://www.resemble.ai");
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

        //Help button
        rect.Set(Screen.width - 37, 6, 16, 16);
        if (GUI.Button(rect, Styles.characterSetHelpBtn, GUIStyle.none))
            Application.OpenURL("https://www.resemble.ai");

        //Options button
        rect.x += 18;
        if (GUI.Button(rect, Styles.popupBtn, GUIStyle.none))
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Regenerate pod"), false, RegeneratePod);
            menu.AddItem(new GUIContent("Export in wav"), false, ExportPod);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Reset"), false, Reset);
            menu.AddItem(new GUIContent("Delete"), false, Delete);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Help"), false, GUIUtils.OpenResembleWebSite);
            menu.AddItem(new GUIContent("Settings"), false, GUIUtils.OpenResembleSettings);
            menu.DropDown(rect);
        }

        GUILayout.Space(50);
    }

    public override void OnInspectorGUI()
    {
        //Draw Text gui
        if (pod.text.OnGUI(request))
        {
            request = PlaceHolderAPIBridge.GetAudioClip(pod.text.userString, GetClipCallback);
            if (pod.clip != null)
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(pod.clip));
            if (pod.clipCopy != null)
                AssetDatabase.RemoveObjectFromAsset(pod.clipCopy);
            pod.clip = null;
            pod.clipCopy = null;
            clipEditor = null;
        }

        if (pod.clip != null)
        {
            //Audio preview
            GUILayout.Space(10);
            GUIUtils.DrawSeparator();
            GUILayout.Space(10);
            GUILayout.Label("Preview", EditorStyles.largeLabel);

            DrawAudioPlayer();

            //Import settings
            GUILayout.Space(10);
            GUIUtils.DrawSeparator();
            GUILayout.Label("Import settings", EditorStyles.largeLabel);
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            importer.loadInBackground = EditorGUILayout.Toggle(new GUIContent("Load In Background",
                "When the flag is set, the loading of the clip will happen delayed without blocking the main thread."),
                importer.loadInBackground);
            dirtySettings |= EditorGUI.EndChangeCheck();

            //Apply button
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!dirtySettings);
            if (GUILayout.Button("Apply"))
            {
                dirtySettings = false;
                importer.SaveAndReimport();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        GUIUtils.ConnectionRequireMessage();
    }

    private void GetClipCallback(AudioClip clip, Error error)
    {
        request = null;

        //Create copy
        AudioClip clipCopy = Instantiate<AudioClip>(clip);
        clipCopy.name = pod.name;
        AssetDatabase.AddObjectToAsset(clipCopy, pod);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(pod), ImportAssetOptions.ForceUpdate);

        pod.clip = clip;
        pod.clipCopy = clipCopy;
        Repaint();
    }

    public void OnEnable()
    {
        System.Reflection.FieldInfo info = typeof(EditorApplication).GetField("globalEventHandler", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        EditorApplication.CallbackFunction value = (EditorApplication.CallbackFunction)info.GetValue(null);
        value += ApplicationUpdate;
        info.SetValue(null, value);
        Pod pod = target as Pod;
        if (pod != null && pod.text != null)
            pod.text.Refresh();
    }

    public void OnDisable()
    {
        System.Reflection.FieldInfo info = typeof(EditorApplication).GetField("globalEventHandler", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        EditorApplication.CallbackFunction value = (EditorApplication.CallbackFunction)info.GetValue(null);
        value -= ApplicationUpdate;
        info.SetValue(null, value);
    }

    private void ApplicationUpdate()
    {
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
        {
            Object[] pods = Selection.objects.Where(x => x is Pod).ToArray();
            if (pods.Length == 1 && pods[0] == pod)
                Delete();
            else if (pods.Length > 1)
                DeleteMultiple(pods);
        }
    }

    public static bool RenameableField(Rect rect, ref bool rename, ref string renameLabel, string originName, out int controlID)
    {
        KeyCode keycode = KeyCode.A;
        controlID = GUIUtility.GetControlID("renameLabel".GetHashCode(), FocusType.Passive, rect);
        if (Event.current.GetTypeForControl(controlID) == EventType.KeyDown)
            keycode = Event.current.keyCode;
        renameLabel = GUI.TextField(rect, rename ? renameLabel : originName, rename ? Styles.headerField : Styles.header);
        TextEditor textEdit = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);

        switch (keycode)
        {
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                if (rename)
                {
                    controlID = 0;
                    GUI.FocusControl("None");
                }
                break;
            case KeyCode.Escape:
                if (rename)
                {
                    controlID = 0;
                    renameLabel = originName;
                    GUI.FocusControl("None");
                }
                break;
        }

        if (controlID != textEdit.controlID - 1)
        {
            if (rename)
            {
                rename = false;
                return true;
            }
        }
        else
        {
            if (!rename)
            {
                rename = true;
                renameLabel = originName;
            }
        }
        return false;
    }

    public void DrawAudioPlayer()
    {
        if (clipEditor == null || clipEditor.target != pod.clip)
        {
            clipEditor = Editor.CreateEditor(pod.clip);
        }

        if (importer == null)
        {
            importer = AudioImporter.GetAtPath(AssetDatabase.GetAssetPath(pod.clip)) as AudioImporter;
        }


        //Preview toolbar
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Play", EditorStyles.toolbarButton))
        { AudioPreview.PlayClip(pod.clip); }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        Rect rect = GUILayoutUtility.GetRect(Screen.width, 100);
        clipEditor.OnPreviewGUI(rect, GUI.skin.box);
    }

    public void RegeneratePod()
    {

    }

    public void ExportPod()
    {

    }

    public void Reset()
    {

    }

    public void Delete()
    {
        pod = target as Pod;
        string path = AssetDatabase.GetAssetPath(pod);
        if (!EditorUtility.DisplayDialog("Delete pod ?", path + "\nYou cannot undo this action.", "Delete", "Cancel"))
            return;
        AssetDatabase.RemoveObjectFromAsset(pod);
        EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<CharacterSet>(path));
        AssetDatabase.ImportAsset(path);
    }

    public void DeleteMultiple(Object[] targets)
    {
        string[] paths = targets.Select(x => AssetDatabase.GetAssetPath(x)).ToArray();
        string allPath = "";
        for (int i = 0; i < paths.Length; i++)
        {
            if (i == 3)
            {
                allPath += "...";
                break;
            }
            allPath += paths[i] + "/" + targets[i].name + "\n";
        }
        if (!EditorUtility.DisplayDialog("Delete pods ?", allPath + "\nYou cannot undo this action.", "Delete", "Cancel"))
            return;

        List<string> sets = new List<string>();
        for (int i = 0; i < targets.Length; i++)
        {
            AssetDatabase.RemoveObjectFromAsset(targets[i]);
            if (!sets.Contains(paths[i]))
                sets.Add(paths[i]);
        }

        for (int i = 0; i < sets.Count; i++)
        {
            EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<CharacterSet>(sets[i]));
            AssetDatabase.ImportAsset(sets[i]);
        }
    }

}