/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * CoreEditorHelper can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using UnityEditor;
using System.Collections;

namespace EnigmaVirtual
{
    public class CoreEditorHelper
    {

        public static Texture LogoHeader = EditorGUIUtility.LoadRequired(string.Format("{0}/CoreIcon.png", "Gema")) as Texture;


        public static void DrawHeader()
        {
            GUILayout.Space(10);
            Texture LogoTexture = EditorGUIUtility.LoadRequired(string.Format("{0}/CoreHeader.png", "Gema")) as Texture;
            var rect = GUILayoutUtility.GetRect(0f, 0f);
            rect.width = LogoTexture.width;
            rect.height = LogoTexture.height;
            GUILayout.Space(rect.height);
            GUI.DrawTexture(rect, LogoTexture);
            GUILayout.Space(rect.height / 2);
        }

    }
}
