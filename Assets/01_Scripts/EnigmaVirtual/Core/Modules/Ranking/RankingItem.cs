/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * RankingItem can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using System;

namespace EnigmaVirtual
{
	public class RankingItem : MonoBehaviour
	{
		public Text Position;
		public Text PlayerName;
		public Text Score;
		private Image _image;
		private RankingController _controller;

		void Awake()
		{
			_image = GetComponent<Image>();
			_controller = GetComponentInParent<RankingController>();
		}

		public void SetData( RankingVO rank )
		{
			Position.text = rank.Position.ToString();
			PlayerName.text = rank.PlayerName;
			Score.text = rank.Score.ToString();
			if (rank.NewHighScore)
			{
				SetHighScore();
				rank.NewHighScore = false;
			}
			else
			{
				SetDefault();
			}
		}

		private void SetHighScore()
		{
			Position.color = _controller.HighScoreTextColor;
			PlayerName.color = _controller.HighScoreTextColor;
			Score.color = _controller.HighScoreTextColor;
			_image.color = _controller.HighScoreBgColor;
		}


		private void SetDefault()
		{
			Position.color = _controller.DefaultTextColor;
			PlayerName.color = _controller.DefaultTextColor;
			Score.color = _controller.DefaultTextColor;
			_image.color = _controller.DefaultBgColor;
		}
	}
}
