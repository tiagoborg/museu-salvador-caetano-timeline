/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * ApiControllerEditor can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using UnityEditor;

using System.Collections;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(ApiController))]
    public class ApiControllerEditor : Editor
    {
        private ApiController _controller;

        public override void OnInspectorGUI()
        {
            _controller = (ApiController)target;

            CoreEditorHelper.DrawHeader();
            //if (Application.isPlaying)
            //{
            //	GUILayout.Space( 15 );
            //	if (GUILayout.Button( "Test API" ))
            //	{
            //		_controller.Test( _controller.TestCallBack );
            //	}
            //	GUILayout.Space( 15 );
            //}

            //DrawDefaultInspector();
            DrawPropertiesExcluding(serializedObject, new string[] { "m_Script" });
            serializedObject.ApplyModifiedProperties();
        }
    }
}
