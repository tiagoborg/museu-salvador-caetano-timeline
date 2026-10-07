/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * RankingVO can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using System.Collections;

namespace EnigmaVirtual
{
	[System.Serializable]
	public class RankingVO  {

		[SerializeField]
		public string PlayerName;
		public int Score;
		public int Position;
		public bool NewHighScore = false;

		public RankingVO(int position, string playerName, int score, bool newHigh)
		{
			Position = position;
			PlayerName = playerName;
			Score = score;
			NewHighScore = newHigh;
		}
	}
}
