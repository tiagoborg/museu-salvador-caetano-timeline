/**
 * Project L
 * 
 * Author(s): Flying Banana
 * Created: 17-Jun-2015
 */
using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEngine.Events;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EnigmaVirtual
{
    /**
     * The keyboard represents a virtual, customisable keyboard that have many sub-
     * panels toggled by a button. Each of these panels is a sub-keyboard that has
     * the same function. When a button is pressed, its immediate keyboard script is
     * responsible of forwarding the pressed key (usually just the name of the 
     * GameObject that has the Button script), to its delegate.
     *
     * Currently dynamic keyboard adding is not supported. You should have made them
     * as prefabs outside the game. However, you can change the keys, though.
     */
    public class UIKeyboard : MonoBehaviour
    {
        // Keyboard properties

        public Color KeyColor = new Color(1f, 1f, 1f);

        public Sprite KeyImage;

        public Image.Type KeyImageType = Image.Type.Sliced;

        public bool UpdateKeyColor;

        public Font FontFace;

        public int FontSize = 40;

        public Color FontColor = new Color(0, 0, 0);

        public bool ShouldUpdateKeyColor;

        // Recursively find all labels and set their properties
        public void ChangeLabelFontAndSize()
        {
            UpdateRecursiveHelper(transform, false);

#if UNITY_EDITOR
            Canvas.ForceUpdateCanvases();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

        public void ChangeKeyImageAndColor()
        {
            UpdateRecursiveHelper(transform, true);

#if UNITY_EDITOR
            Canvas.ForceUpdateCanvases();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

        void UpdateRecursiveHelper(Transform trans, bool changeImage)
        {
            if (changeImage)
            {
                Button button = trans.GetComponent<Button>();

                if (button != null)
                {
                    Image image = button.GetComponent<Image>();
                    if (UpdateKeyColor)
                        image.color = KeyColor;
                    image.type = KeyImageType;
                    image.sprite = KeyImage;
                }
            }
            else
            {
                Text text = trans.GetComponent<Text>();

                if (text != null)
                {
                    text.font = FontFace;
                    text.fontSize = FontSize;
                    text.color = FontColor;
                }
            }

            UIKeyboard keyboard = trans.GetComponent<UIKeyboard>();
            if (keyboard != null)
            {
                if (UpdateKeyColor)
                    keyboard.KeyColor = KeyColor;
                keyboard.KeyImage = KeyImage;
                keyboard.KeyImageType = KeyImageType;
                keyboard.FontFace = FontFace;
                keyboard.FontSize = FontSize;
                keyboard.FontColor = FontColor;
            }

            for (int i = 0; i < trans.childCount; i++)
            {
                UpdateRecursiveHelper(trans.GetChild(i), changeImage);
            }
        }

        // Custom mappings
        [System.Serializable]
        public class Dictionary
        {
            public string ButtonName;
            public string OutputString;
        }

        public List<Dictionary> CustomMappings;

        // All the keyboards we cycle through.
        public List<UIKeyboard> Subkeyboards;


        /**
         * Use this to define the custom mappings for keys which you can't enter as 
         * object name.
         */

        int SelectedPanel;


        // public IUIKeyboardDelegate Delegate;

        // The key that cycles through the sub-keyboards. Specify None if cycling is
        // not required.

        public List<KeyData> CycleKeys = new List<KeyData>();

        // This defines cycle key behavior
        [System.Serializable]
        public enum CycleKeyBehavior
        {
            CycleKey, ShiftKey, ShiftAndCapKey
        }


        public CycleKeyBehavior Behavior;

        enum CycleKeyState
        {
            NORMAL, SHIFTED, CAPPED
        }

        CycleKeyState _cycleKeyState;
        CycleKeyState cycleKeyState
        {
            get
            {
                return _cycleKeyState;
            }

            set
            {
                _cycleKeyState = value;

                // Change button appearance
                if (CycleKeys != null)
                {
                    foreach (KeyData key in CycleKeys)
                    {
                        if (key != null)
                            key.gameObject.SetActive(value == CycleKeyState.NORMAL);
                    }
                }

                if (CycleKeyShift != null)
                    CycleKeyShift.gameObject.SetActive(value == CycleKeyState.NORMAL);
                if (CycleKeyShifted != null)
                    CycleKeyShifted.gameObject.SetActive(value == CycleKeyState.SHIFTED || value == CycleKeyState.CAPPED);
                if (CycleKeyCap != null)
                    CycleKeyCap.gameObject.SetActive(value == CycleKeyState.NORMAL || value == CycleKeyState.SHIFTED);
                if (CycleKeyCapped != null)
                    CycleKeyCapped.gameObject.SetActive(value == CycleKeyState.CAPPED);
            }
        }


        public float DoubleTapTime = 0.25f;


        public Button CycleKeyShift;
        public Button CycleKeyShifted;


        public Button CycleKeyCap;
        public Button CycleKeyCapped;

        DateTime lastShifted;

        // Configure such that this instead of the delegate is needed!!!

        [Serializable]
        public class OnKeyboardInputEvent : UnityEvent<UIKeyboard, string> { }


        public OnKeyboardInputEvent OnKeyboardInput;

        // Use this for initialization
        void Start()
        {
            // Register event and remove empty entries

            for (int i = CycleKeys.Count - 1; i >= 0; i--)
            {
                if (CycleKeys[i] == null)
                    CycleKeys.RemoveAt(i);
            }

            foreach (KeyData key in CycleKeys)
            {
                if (key.button != null)
                {
                    var idd = key.data_id;
                    key.button.onClick.AddListener(delegate
                    {
                        CycleSubkeyboard(idd);
                    });
                }
            }

            if (CycleKeyShift)
            {
                CycleKeyShift.onClick.AddListener(delegate { CycleState(CycleKeyState.SHIFTED); });
            }

            if (CycleKeyShifted)
            {
                CycleKeyShifted.onClick.AddListener(delegate { CycleState(CycleKeyState.NORMAL); });
            }

            if (CycleKeyCap)
            {
                CycleKeyCap.onClick.AddListener(delegate { CycleState(CycleKeyState.CAPPED); });
            }

            if (CycleKeyCapped)
            {
                CycleKeyCapped.onClick.AddListener(delegate { CycleState(CycleKeyState.NORMAL); });
            }

            // Register all the other keys and subkeyboards
            // Recursive find under other UI objects as well (must have RectTransform).
            ConfigureTransform(transform);

            // Finally assign all delegates from the list of subkeyboards
            for (int i = 0; i < Subkeyboards.Count; i++)
            {
                UIKeyboard sub = Subkeyboards[i];

                if (sub != null)
                {
                    sub.OnKeyboardInput.AddListener(KeyPressed);

                    // and enable/disable them
                    sub.gameObject.SetActive(SelectedPanel == i);
                }
            }

            // Configure cycle keys
            // We first clear out the cycle keys we don't need
            if (Behavior == CycleKeyBehavior.CycleKey)
            {
                CycleKeyShift = null;
                CycleKeyShifted = null;
                CycleKeyCap = null;
                CycleKeyCapped = null;
            }
            else if (Behavior == CycleKeyBehavior.ShiftKey)
            {
                if (CycleKeys.Count > 0)
                    //CycleKeys = new Button[1] { CycleKeys[0] };
                    CycleKeys = new List<KeyData>() { CycleKeys[0] };
                else
                    CycleKeys = null;

                CycleKeyCapped = null;
                CycleKeyCap = null;
            }
            else if (Behavior == CycleKeyBehavior.ShiftAndCapKey)
            {
                if (CycleKeys.Count > 0)
                    CycleKeys = new List<KeyData>() { CycleKeys[0] };
                else
                    CycleKeys = null;
            }

            if (Behavior == CycleKeyBehavior.CycleKey)
            {
                for (int i = 0; i < CycleKeys.Count; i++)
                {
                    KeyData key = CycleKeys[i];

                    if (key != null)
                        key.gameObject.SetActive(SelectedPanel != key.data_id);
                }
            }

        }

        void ConfigureTransform(Transform trans)
        {
            for (int i = 0; i < trans.childCount; i++)
            {
                Transform child = trans.GetChild(i);

                Button b = child.GetComponent<Button>();
                UIKeyboard k = child.GetComponent<UIKeyboard>();

                bool notCycleKeys = true;


                foreach (KeyData key in CycleKeys)
                {
                    if (key.button == b)
                    {
                        notCycleKeys = false;
                        break;
                    }
                }

                if (b != null && notCycleKeys && b != CycleKeyShifted && b != CycleKeyShift && b != CycleKeyCap && b != CycleKeyCapped)
                {
                    b.onClick.AddListener(delegate { KeyPressed(b); });
                }

                // Recursively set up children to allow easy managing
                if (b == null && k == null && child.GetComponent<RectTransform>() != null)
                    ConfigureTransform(child);
            }

            // Set font and size
            // UpdateRecursiveHelper(transform, false);
        }

        void KeyPressed(UIKeyboard keyboard, string key)
        {
            // A keyboard sent this key. We check for replacement
            string finalKey = key;

            for (int i = 0; i < CustomMappings.Count; i++)
            {
                Dictionary map = CustomMappings[i];
                if (map.ButtonName.Equals(key))
                {
                    finalKey = map.OutputString;
                    break;
                }
            }

            OnKeyboardInput.Invoke(this, finalKey);

            // Reset shift key

            ResetShift();
        }

        // When a button is pressed within a keyboard
        void KeyPressed(Button key)
        {
            string finalKey = key.gameObject.name;

            for (int i = 0; i < CustomMappings.Count; i++)
            {
                Dictionary map = CustomMappings[i];
                if (map.ButtonName.Equals(key.gameObject.name))
                {
                    finalKey = map.OutputString;
                    break;
                }
            }

            OnKeyboardInput.Invoke(this, finalKey);
        }

        // Resetting the shift as a result of typing a letter

        void ResetShift()
        {
            if (cycleKeyState == CycleKeyState.SHIFTED)
            {
                CycleKeyShift.gameObject.SetActive(true);
                CycleKeyShifted.gameObject.SetActive(false);
                cycleKeyState = CycleKeyState.NORMAL;
                Subkeyboards[SelectedPanel].gameObject.SetActive(false);
                SelectedPanel = (SelectedPanel - 1) % Subkeyboards.Count;
                Subkeyboards[SelectedPanel].gameObject.SetActive(true);
            }
        }

        void CycleState(CycleKeyState state)
        {

            if (Behavior != CycleKeyBehavior.CycleKey)
            {
                var index = SelectedPanel;
                cycleKeyState = state;
                switch (cycleKeyState)
                {
                    case CycleKeyState.NORMAL:
                        {
                            index = 0;
                            break;
                        }
                    case CycleKeyState.SHIFTED:
                        {
                            index = 1;
                            break;
                        }
                    case CycleKeyState.CAPPED:
                        {
                            index = 1;
                            break;
                        }
                }

                if (index != SelectedPanel)
                {
                    Subkeyboards[SelectedPanel].gameObject.SetActive(false);
                    SelectedPanel = index;
                    Subkeyboards[SelectedPanel].gameObject.SetActive(true);
                }
            }
        }

        void CycleSubkeyboard(int selectPanel = -1)
        {
            // Check if we already cycled
            int cycleAmount = 1;
            if (Behavior != CycleKeyBehavior.CycleKey)
            {
                switch (cycleKeyState)
                {
                    case CycleKeyState.NORMAL:
                        {
                            cycleKeyState = CycleKeyState.SHIFTED;

                            if (Behavior == CycleKeyBehavior.ShiftAndCapKey)
                            {
                                lastShifted = DateTime.Now;
                            }

                            break;
                        }
                    case CycleKeyState.SHIFTED:
                        {
                            double interval = DateTime.Now.Subtract(lastShifted).TotalSeconds;

                            if (Behavior == CycleKeyBehavior.ShiftAndCapKey && interval < DoubleTapTime)
                            {
                                // Stick to it - we don't need to do anything
                                cycleAmount = 0;
                                cycleKeyState = CycleKeyState.CAPPED;
                            }
                            else
                            {
                                cycleKeyState = CycleKeyState.NORMAL;
                                // Cycle back
                                cycleAmount = -1;
                            }

                            break;
                        }
                    case CycleKeyState.CAPPED:
                        {
                            cycleKeyState = CycleKeyState.NORMAL;
                            cycleAmount = -1;
                            break;
                        }
                }
            }

            // Find the next index
            if (Subkeyboards.Count < 2)
            {
                Development.LogWarning("Cannot cycle keyboards with less than two keyboards!");
                return;
            }

            if (cycleAmount != 0)
            {
                if (Behavior == CycleKeyBehavior.CycleKey && selectPanel >= 0)
                {
                    Subkeyboards[SelectedPanel].gameObject.SetActive(false);
                    SelectedPanel = selectPanel;
                    Subkeyboards[SelectedPanel].gameObject.SetActive(true);

                    foreach (KeyData key in CycleKeys)
                    {
                        key.gameObject.SetActive(key.data_id != SelectedPanel);
                    }
                }
                else
                {
                    Subkeyboards[SelectedPanel].gameObject.SetActive(false);
                    SelectedPanel = (SelectedPanel + cycleAmount) % Subkeyboards.Count;
                    Subkeyboards[SelectedPanel].gameObject.SetActive(true);
                }
            }
        }
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(UIKeyboard))]
    public class UIKeyboardEditor : Editor
    {
        bool showCustomMapping;
        bool showChildKeyboards;
        bool showCycleKeys;

        public void FindAndAdd<T>(List<UnityEngine.Object> list, Transform parent)
        {
            if (parent.GetComponent<T>() != null)
            {
                list.Add(parent.gameObject);
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                FindAndAdd<T>(list, parent.GetChild(i));
            }
        }

        public override void OnInspectorGUI()
        {
            UIKeyboard keyboard = target as UIKeyboard;
            EditorGUILayout.LabelField("General key properties");

            keyboard.KeyImage = (Sprite)EditorGUILayout.ObjectField("Key image", keyboard.KeyImage, typeof(Sprite), true);
            keyboard.KeyImageType = (Image.Type)EditorGUILayout.EnumPopup("Key image type", keyboard.KeyImageType);
            keyboard.KeyColor = EditorGUILayout.ColorField("Key color", keyboard.KeyColor);
            keyboard.UpdateKeyColor = EditorGUILayout.Toggle("Apply key color", keyboard.UpdateKeyColor);


            if (GUILayout.Button("Apply"))
            {
                keyboard.ChangeKeyImageAndColor();
            }

            keyboard.FontFace = (Font)EditorGUILayout.ObjectField("Key font", keyboard.FontFace, typeof(Font), true);
            keyboard.FontSize = Math.Max(0, EditorGUILayout.IntField("Font size", keyboard.FontSize));
            keyboard.FontColor = EditorGUILayout.ColorField("Font color", keyboard.FontColor);

            if (GUILayout.Button("Apply"))
            {
                keyboard.ChangeLabelFontAndSize();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick Selection");

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("All Buttons"))
            {
                List<UnityEngine.Object> list = new List<UnityEngine.Object>();
                FindAndAdd<Button>(list, keyboard.transform);
                Selection.objects = list.ToArray();
            }

            if (GUILayout.Button("All Texts"))
            {
                List<UnityEngine.Object> list = new List<UnityEngine.Object>();
                FindAndAdd<Text>(list, keyboard.transform);
                Selection.objects = list.ToArray();
            }

            if (GUILayout.Button("Collapse All"))
            {
                var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
                var methodInfo = type.GetMethod("SetExpandedRecursive");

                EditorApplication.ExecuteMenuItem("Window/Hierarchy");
                var window = EditorWindow.focusedWindow;

                methodInfo.Invoke(window, new object[] { keyboard.gameObject.GetInstanceID(), false });

            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Keyboard structure");

            showChildKeyboards = EditorGUILayout.Foldout(showChildKeyboards, "Sub-keyboards");

            if (showChildKeyboards)
            {
                List<UIKeyboard> removing = new List<UIKeyboard>();
                for (int i = 0; i < keyboard.Subkeyboards.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    keyboard.Subkeyboards[i] = (UIKeyboard)EditorGUILayout.ObjectField(string.Format("Subkeyboard {0}", i), keyboard.Subkeyboards[i], typeof(UIKeyboard), true);

                    if (GUILayout.Button("-", GUILayout.Width(23f)))
                    {
                        removing.Add(keyboard.Subkeyboards[i]);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                foreach (UIKeyboard key in removing)
                {
                    keyboard.Subkeyboards.Remove(key);
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("+", GUILayout.Width(23f)))
                {
                    // Add new object to list
                    keyboard.Subkeyboards.Add(null);
                }
                EditorGUILayout.EndHorizontal();
            }


            EditorGUILayout.Space();

            // Object fields
            if (keyboard.Subkeyboards != null && keyboard.Subkeyboards.Count >= 2 && keyboard.CycleKeys != null)
            {
                var ListSize = keyboard.CycleKeys.Count;
                ListSize = EditorGUILayout.IntField("CycleKeys Size", ListSize);

                if (ListSize != keyboard.CycleKeys.Count)
                {
                    while (ListSize > keyboard.CycleKeys.Count)
                    {
                        keyboard.CycleKeys.Add(null);
                    }
                    while (ListSize < keyboard.CycleKeys.Count)
                    {
                        keyboard.CycleKeys.RemoveAt(keyboard.CycleKeys.Count - 1);
                    }
                }

                showCycleKeys = EditorGUILayout.Foldout(showCycleKeys, "Sub-CycleKeys");

                if (showCycleKeys)
                {
                    for (int i = 0; i < keyboard.CycleKeys.Count; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        keyboard.CycleKeys[i] = (KeyData)EditorGUILayout.ObjectField(string.Format("subkeydata {0}", i), keyboard.CycleKeys[i], typeof(KeyData), true);
                        EditorGUILayout.EndHorizontal();
                    }
                }

                //// Resize array if too small
                //if (keyboard.Subkeyboards.Count * 2 > keyboard.CycleKeys.Length)
                //{
                //    Array.Resize<Button>(ref keyboard.CycleKeys, keyboard.Subkeyboards.Count * 2);
                //}

                //for (int i = 0; i < keyboard.CycleKeys.Length; i++)
                //{
                //    keyboard.CycleKeys[i] = (Button)EditorGUILayout.ObjectField(string.Format("Cycle Key (subkb {0})", i), keyboard.CycleKeys[i], typeof(Button), true);
                //    if (keyboard.Behavior != UIKeyboard.CycleKeyBehavior.CycleKey)
                //        break;
                //}

                EditorGUILayout.Space();

                if (keyboard.Behavior == UIKeyboard.CycleKeyBehavior.ShiftKey)
                {
                    keyboard.CycleKeyShifted = (Button)EditorGUILayout.ObjectField("Cycle Key (SHIFTED)", keyboard.CycleKeyShifted, typeof(Button), true);
                    keyboard.CycleKeyShift = (Button)EditorGUILayout.ObjectField("Cycle Key (SHIFT)", keyboard.CycleKeyShift, typeof(Button), true);
                }

                if (keyboard.Behavior == UIKeyboard.CycleKeyBehavior.ShiftAndCapKey)
                {
                    keyboard.CycleKeyShifted = (Button)EditorGUILayout.ObjectField("Cycle Key (SHIFTED)", keyboard.CycleKeyShifted, typeof(Button), true);
                    keyboard.CycleKeyShift = (Button)EditorGUILayout.ObjectField("Cycle Key (SHIFT)", keyboard.CycleKeyShift, typeof(Button), true);
                    keyboard.CycleKeyCapped = (Button)EditorGUILayout.ObjectField("Cycle Key (CAPPED)", keyboard.CycleKeyCapped, typeof(Button), true);
                    keyboard.CycleKeyCap = (Button)EditorGUILayout.ObjectField("Cycle Key (CAPS)", keyboard.CycleKeyCap, typeof(Button), true);
                }

                // Cycle key setup
                keyboard.Behavior = (UIKeyboard.CycleKeyBehavior)EditorGUILayout.EnumPopup("Cycle Key Behavior", keyboard.Behavior);

                if (keyboard.Behavior == UIKeyboard.CycleKeyBehavior.ShiftAndCapKey)
                {
                    keyboard.DoubleTapTime = EditorGUILayout.Slider("Double Tap Time", keyboard.DoubleTapTime, 0.1f, 1f);
                }
            }


            EditorGUILayout.Space();

            showCustomMapping = EditorGUILayout.Foldout(showCustomMapping, "Custom output mappings");

            if (showCustomMapping)
            {
                List<UIKeyboard.Dictionary> removing = new List<UIKeyboard.Dictionary>();
                foreach (UIKeyboard.Dictionary dict in keyboard.CustomMappings)
                {

                    EditorGUILayout.BeginHorizontal();
                    dict.ButtonName = EditorGUILayout.TextField("Button name", dict.ButtonName, GUILayout.Width(200f));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("-", GUILayout.Width(23f)))
                    {
                        removing.Add(dict);
                    }
                    EditorGUILayout.EndHorizontal();

                    dict.OutputString = EditorGUILayout.TextField("Replace with", dict.OutputString, GUILayout.Width(200f));

                    EditorGUILayout.Space();
                }

                foreach (UIKeyboard.Dictionary dict in removing)
                {
                    keyboard.CustomMappings.Remove(dict);
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("+", GUILayout.Width(23f)))
                {
                    // Add new object to list
                    keyboard.CustomMappings.Add(new UIKeyboard.Dictionary());
                }
                EditorGUILayout.EndHorizontal();
            }


            EditorGUILayout.Space();

            // GUI Event
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Keyboard events");
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnKeyboardInput"), true);
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}