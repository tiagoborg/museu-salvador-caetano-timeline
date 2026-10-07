using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using EnigmaVirtual;

public class SceneSelector : EditorWindow
{
    private Vector2 scrollPos;
    private CustomEditorSceneManager manager = new CustomEditorSceneManager();
    private bool showOnlyOneScene = true;

    [MenuItem("Gema/Scene Selector", false, 1)]
    static void Init()
    {
        SceneSelector window = (SceneSelector)EditorWindow.GetWindow(typeof(SceneSelector));
        window.titleContent = new GUIContent("Scene Selector");
        window.Show();
    }


    void OnFocus()
    {
        manager.GenerateList();
    }

    void OnGUI()
    {
        showOnlyOneScene = EditorGUILayout.Toggle("Show only one", showOnlyOneScene);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUIStyle.none);

        for (int i = 0; i < manager.Categories.Count; i++)
        {
            GUILayout.Label(manager.Categories[i], EditorStyles.boldLabel);

            EditorSceneData[] scenes = manager.GetSceneByCategory(manager.Categories[i]);

            foreach (EditorSceneData scene in scenes)
            {
                if (GUILayout.Button(scene.Name, GUILayout.Height(32)))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene(scene.Path, showOnlyOneScene ? OpenSceneMode.Single : OpenSceneMode.Additive);
                        EditorSceneManager.SetActiveScene(SceneManager.GetSceneByPath(scene.Path));
                        EditorGUIUtility.ExitGUI();
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }


    class EditorSceneData
    {
        public string Name { get; private set; }
        public string Path { get; private set; }
        public string Category { get; private set; }

        public EditorSceneData(string scene)
        {
            string name = scene.Substring(scene.LastIndexOf('/') + 1);
            name = name.Substring(0, name.Length - 6);
            name = FormatName(name);

            string pathWithoutName = scene.Remove(scene.LastIndexOf('/'));
            string category = pathWithoutName.Substring(pathWithoutName.LastIndexOf('/') + 1);

            this.Name = name;
            this.Path = scene;
            this.Category = category;
        }

        string FormatName(string name)
        {
            string newName = "";
            string[] splittedName = name.Split(' ');
            for (int i = 0; i < splittedName.Length; i++)
            {
                newName += splittedName[i];
                if (i < splittedName.Length - 1)
                    newName += "\n";
            }

            return newName;
        }
    };

    class CustomEditorSceneManager
    {
        private List<EditorSceneData> scenes = new List<EditorSceneData>();
        public List<string> Categories = new List<string>();


        public void GenerateList()
        {
            scenes.Clear();
            Categories.Clear();


            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    EditorSceneData data = new EditorSceneData(scene.path);

                    if (!Categories.Contains(data.Category))
                    {
                        Categories.Add(data.Category);
                    }

                    scenes.Add(data);
                }
            }
        }

        public EditorSceneData[] GetSceneByCategory(string category)
        {
            List<EditorSceneData> reqScenes = new List<EditorSceneData>();

            foreach (EditorSceneData scene in scenes)
            {
                if (string.Compare(scene.Category, category) == 0)
                {
                    reqScenes.Add(scene);
                }
            }

            return reqScenes.ToArray();
        }
    }
}

public class ClearPlayerPrefs : EditorWindow
{
    [MenuItem("Gema/Clear PlayerPrefs", false, 10)]
    private static void NewMenuOption()
    {
        PlayerPrefs.DeleteAll();
    }
}

