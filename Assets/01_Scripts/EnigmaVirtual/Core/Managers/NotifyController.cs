/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public enum NotifyType
	{
		Success = 0,
		Error,		
		Warning,
		Info
	}

	[RequireComponent(typeof(Image))]
	public class NotifyController : MonoBehaviour
	{
		[Header( "Icons" )]
		[SerializeField]
		private Image SuccessIcon;
		[SerializeField]
		private Image ErrorIcon;
		[SerializeField]
		private Image WarningIcon;
		[SerializeField]
		private Image InfoIcon;
		[Header( "Box Colors" )]
		[SerializeField]
		private Color SuccessColor;
		[SerializeField]
		private Color ErrorColor;
		[SerializeField]
		private Color WarningColor;
		[SerializeField]
		private Color InfoColor;
		[Header("NotifyText")]
		[SerializeField]
		private Text header;
		[SerializeField]
		private Text label;

		private RectTransform _rect;
		private Image _image;
		private bool _isShown = false;

		/// <summary>The pending auto-hide. Held so that a second notification arriving while the
		/// first is still up replaces its timer instead of stacking beside it - Invoke had no
		/// CancelInvoke, so a 5s notification followed by a 10s one was hidden after 5s.</summary>
		private Coroutine _pendingHide;
		private Image _lastIcon;

		private void SetColor(NotifyType type)
		{
			if (_image == null) _image = GetComponent<Image>();
			if (_lastIcon != null) _lastIcon.gameObject.SetActive( false );
			switch (type)
			{
				case NotifyType.Success:
					_image.color = SuccessColor;
					SuccessIcon.gameObject.SetActive( true );
					_lastIcon = SuccessIcon;
					break;
				case NotifyType.Error:
					_image.color = ErrorColor;
					ErrorIcon.gameObject.SetActive( true );
					_lastIcon = ErrorIcon;
					break;
				case NotifyType.Warning:
					_image.color = WarningColor;
					WarningIcon.gameObject.SetActive( true );
					_lastIcon = WarningIcon;
					break;
				case NotifyType.Info:
					_image.color = InfoColor;
					InfoIcon.gameObject.SetActive( true );
					_lastIcon = InfoIcon;
					break;
			}
		}

		public void Init()
		{
			_rect = GetComponent<RectTransform>();
			_image = GetComponent<Image>();
			_rect.anchoredPosition = new Vector2( 0f, _rect.sizeDelta.y );
			gameObject.SetActive( false );
		}

		/// <summary>
		/// Shows the panel, replacing whatever it was showing.
		/// </summary>
		/// <remarks>
		/// <para>Cancels whatever is in flight first. A <see cref="Hide"/> in progress is still moving
		/// the panel off-screen, and its <c>onComplete</c> would switch the object off half a second
		/// from now - deactivating <i>this</i> notification, not the one it was hiding.
		/// <c>LeanTween.cancel</c> does not run <c>onComplete</c>, so cancelling is what defuses it.</para>
		///
		/// <para><b>The entrance then runs unconditionally, and that is the point.</b> Cancelling
		/// abandons <c>anchoredPosition</c> wherever the outgoing move had reached - up to
		/// <c>sizeDelta.y</c>, fully off-screen - so a replacing notification has to be moved back or
		/// it never appears. The move is idempotent for a panel already parked at y = 0.</para>
		/// </remarks>
		public void Show(NotifyType type, string header, string label)
		{
			// One call, not two: cancel(RectTransform) forwards to cancel(rect.gameObject), and the
			// RectTransform tweens are registered against this same GameObject.
			LeanTween.cancel( gameObject );

			// A pending auto-hide belongs to the notification being replaced, not to this one. Without
			// this, a timed notification followed by an untimed one is hidden by the dead timer a few
			// seconds in, with nothing logged.
			// Static, not an extension: the coroutine runs on CallLater's persistent host, and Unity
			// only stops a coroutine from the MonoBehaviour that started it.
			MonoBehaviourExtensions.StopCallLater( _pendingHide );
			_pendingHide = null;

			// activeSelf, not _isShown: the flag records the last CALL, and an outgoing Deactivate
			// could switch the object off behind it.
			bool alreadyVisible = gameObject.activeSelf;

			_isShown = true;
			gameObject.SetActive(true);

			// Only the instant zero is conditional - it is the frame a fade would start from, so it
			// belongs to a fresh entrance. Note it currently does nothing on this panel: the
			// GameObject overload of LeanTween.alpha writes Renderer materials, and an Image has a
			// CanvasRenderer, so the panel moves without fading. Kept, and left targeted as it is,
			// because retargeting it is a visible behaviour change for every branched project.
			if (!alreadyVisible)
				LeanTween.alpha(gameObject, 0f, 0f);

			LeanTween.moveY(_rect, 0f, 0.8f).setEase(LeanTweenType.easeOutQuad);

			// Inert, same as the zero above: the GameObject overload writes Renderer materials and this
			// panel renders through a CanvasRenderer. The entrance is moveY alone. Retargeting to
			// LeanTween.alpha(_rect, ...) would work and is deliberately not done - it would hand every
			// branched project a fade the panel has never had.
			LeanTween.alpha( gameObject, 1f, 0.8f );

			SetColor( type );
			this.label.text = label;
			this.header.text = header;
		}

		public void Show( NotifyType type, string header, string label, float timeToShow )
		{
			Show( type, header, label );
			this.CallLater( timeToShow, Hide, ref _pendingHide );
		}

		public void Hide(  )
		{
			if (!_isShown) return;
			
			LeanTween.moveY( _rect, _rect.sizeDelta.y, .5f ).setEase( LeanTweenType.easeOutQuad ).setOnComplete(Deactivate);

			// Inert - see Show. The exit is moveY, and Deactivate runs from its onComplete, so nothing
			// here depends on the alpha finishing.
			LeanTween.alpha( gameObject, 0f, 0.5f );
			_isShown = false;
		}

		/// <summary>
		/// Switches the panel off once <see cref="Hide"/>'s exit tween has finished.
		/// </summary>
		/// <remarks>
		/// The guard is <b>unreachable defence</b>, not the fix: <c>LeanTween.cancel</c> in
		/// <see cref="Show"/> does not run <c>onComplete</c>, so no path invokes this after a newer
		/// <see cref="Show"/>. Kept because a silently missing notifications panel is the one failure
		/// this class cannot afford - on an unattended kiosk it is the only thing that ever tells an
		/// operator anything - and because a future change to how <see cref="Show"/> clears its tweens
		/// would make the guard load-bearing without anyone noticing.
		/// </remarks>
		private void Deactivate()
		{
			if (_isShown)
				return;

			gameObject.SetActive(false);
		}
	}
}
