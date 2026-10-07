/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * KeyboardController can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.Events;

namespace EnigmaVirtual
{
	public class KeyboardController : MonoBehaviour
	{

		public RectTransform Keyboard;
		public float AnimSpeed = .6f;
		public bool ShowOnStart = false;
		[Tooltip( "The place where we should deliver the output" )]
		public InputField TargetInput;
		[Header("Special Keys")]
		// Configuring special characters
		public string DeleteKey = "DELETE";
		public string ReturnKey = "RETURN";

		public UnityEvent OnKeyboardShown = new UnityEvent();
		public UnityEvent OnKeyboardHided = new UnityEvent();


		void Start()
		{
			if (ShowOnStart) 
                Show();
		}

		[ContextMenu( "Show" )]
		public void Show()
		{
			OnKeyboardShown.Invoke();
			LeanTween.moveY( Keyboard, Keyboard.sizeDelta.y, AnimSpeed );
		}

		[ContextMenu( "Hide" )]
		public void Hide()
		{
			OnKeyboardHided.Invoke();
			LeanTween.moveY( Keyboard, 0f, AnimSpeed );
		}

		public void SetTarget( InputField textBox = null )
		{
			TargetInput = textBox;
		}

		public void ReceivedString( UIKeyboard whichKeyboard, string input )
		{
			if (TargetInput == null) return;

			// We operate on the text field. Check for special characters
			if (input.Equals( DeleteKey ))
			{
				// Delete a character from text
				if (TargetInput.text.Length > 0)
				{
					if (TargetInput.caretPosition < TargetInput.text.Length)
					{
						TargetInput.text = TargetInput.text.Remove( TargetInput.caretPosition, 1 );
						TargetInput.caretPosition--;
					}
					else
					{
						TargetInput.text = TargetInput.text.Substring( 0, TargetInput.text.Length - 1 );
					}
				}
			}
			else if (input.Equals( ReturnKey ))
			{
				Hide();
			}
			else if (input.Equals( "Change" ))
			{
				// Handles any other custom things in this manner!
				whichKeyboard.FontSize = Random.Range( 25, 50 );
				whichKeyboard.FontColor = new Color( Random.value, Random.value, Random.value );
				whichKeyboard.KeyColor = new Color( Random.value, Random.value, Random.value );
				whichKeyboard.ShouldUpdateKeyColor = true;
				// Invoke the first "Apply" button in the editor
				whichKeyboard.ChangeKeyImageAndColor();

				// Invoke the second "Apply" button in the editor
				whichKeyboard.ChangeLabelFontAndSize();
			}
			else
			{
				TargetInput.text = TargetInput.text.Insert( TargetInput.caretPosition, input );
				TargetInput.caretPosition++;
			}
		}

	}
}
