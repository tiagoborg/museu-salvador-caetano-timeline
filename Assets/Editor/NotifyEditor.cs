/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using UnityEngine;
using UnityEditor;

namespace EnigmaVirtual
{
	[CustomEditor( typeof( NotifyController ) )]
	public class NotifyEditor : Editor
	{
		private NotifyController _notify;
		private RectTransform _rect;

		public override void OnInspectorGUI()
		{
			_notify = ( NotifyController )target;
			_rect = _notify.GetComponent<RectTransform>();

			CoreEditorHelper.DrawHeader();
			//DrawDefaultInspector();
			DrawPropertiesExcluding(serializedObject, new string[] { "m_Script" });
			serializedObject.ApplyModifiedProperties();

			GUILayout.Space( 15 );

			if (_rect.anchoredPosition.y != 0)
			{
				if (GUILayout.Button( "Show" ))
				{
					_rect.anchoredPosition = new Vector2( _rect.anchoredPosition.x, 0f );
					_notify.gameObject.SetActive( true );
				}
			} else
			{
				if (GUILayout.Button( "Hide" ))
				{
					_rect.anchoredPosition = new Vector2( _rect.anchoredPosition.x, _rect.sizeDelta.y );
					_notify.gameObject.SetActive( false );
				}
			}
			GUILayout.Space( 15 );			
		}
	}
}
