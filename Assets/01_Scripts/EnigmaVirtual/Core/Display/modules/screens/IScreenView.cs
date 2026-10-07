/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * StatsAPI cannot be copied and/or distributed without
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using EnigmaVirtual.Screens;
using UnityEngine;

namespace EnigmaVirtual
{
	public interface IScreenView
	{
		void Init(ScreenControllerBase controller);
		void Show();
		void Hide();
		void EnableGroup();
		void DisableGroup();
		ScreenType GetScreenType();
		CanvasGroup Group { get; }
		bool IsShown { get; }
	}
}