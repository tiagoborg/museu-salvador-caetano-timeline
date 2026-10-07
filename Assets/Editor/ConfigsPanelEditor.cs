/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * ConfigsPanelEditor can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEditor;
using UnityEngine;

namespace EnigmaVirtual
{
    [CustomEditor( typeof( ConfigsPanel ) )]
	public class ConfigsPanelEditor : Editor
	{
		public override void OnInspectorGUI()
		{

			if (PlayerPrefs.HasKey("Settings"))
			{
				GUILayout.Space( 15 );
				if (GUILayout.Button( "Delete Saved Data" ))
				{
					PlayerPrefs.DeleteKey( "Settings" );
				}
				GUILayout.Space( 15 );
			}

			DrawDefaultInspector();
		}
	}
}
