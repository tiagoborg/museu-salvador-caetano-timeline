/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * RankingController can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace EnigmaVirtual
{
	public enum RankingDirection
	{
		Ascending,
		Descending
	}

	public class RankingController : MonoBehaviour
	{

		public RankingDirection Direction;
		[HideInInspector]
		[SerializeField]
		public RankingData Ranking;
		public bool FakeData = false;

		[Header( "Colors" )]
		public Color DefaultBgColor;
		public Color DefaultTextColor;
		public Color HighScoreTextColor;
		public Color HighScoreBgColor;


		void Awake()
		{
		}

		void Start()
		{
			if (PlayerPrefs.HasKey( "Ranking" ))
			{
				Ranking.LoadData( PlayerPrefs.GetString( "Ranking" ) );
				DisplayData();
			}
			else
			{
				if (FakeData) CreateFakeData();
			}
		}

		public int AddScore( string playerName, int newScore )
		{
			int isHighScore = -1;
			Ranking.Data.Add( new RankingVO( -1, playerName, newScore, true ) );
			SortData();
			for (int i = 0; i < Ranking.Data.Count; i++)
			{
				RankingVO item = Ranking.Data[ i ];
				if (i < 10 && item.NewHighScore) isHighScore = i;
				item.Position = i;
				if (i >= 10)
					Ranking.Data.RemoveAt( i );
			}
			SaveData();
			return isHighScore;
		}

		public void ChangePlayerName( int position, string newName )
		{
			Ranking.Data[ position ].PlayerName = newName;
			SaveData();
		}


		private void CreateFakeData()
		{
			Development.Log( "CreateFakeData" );
			Ranking = new RankingData();
			int last = 0;
			for (int i = 0; i < 10; i++)
			{
				last += Random.Range( 10, 100 );
				Ranking.Data.Add( new RankingVO( i + 1, "Player #" + i, last, false ) );
			}
			SortData();
			SaveData();
			DisplayData();
		}

		private void SaveData()
		{
			PlayerPrefs.SetString( "Ranking", JsonUtility.ToJson( Ranking ) );
			PlayerPrefs.Save();
		}

		public void DisplayData()
		{
			int pos = 0;
			foreach (RankingItem item in GetComponentsInChildren<RankingItem>())
			{
				item.SetData( Ranking.Data[ pos ] );
				pos++;
			}
			ClearHighScore();
		}

		private void ClearHighScore()
		{
			for (int i = 0; i < Ranking.Data.Count; i++)
			{
				RankingVO item = Ranking.Data[ i ];
				item.NewHighScore = false;
			}
		}

		private void SortData()
		{
			if (Direction == RankingDirection.Ascending)
			{
				Ranking.SortAscending();
			}
			else
			{
				Ranking.SortDescending();
			}
		}

		[ContextMenu( "DeleteRankingData" )]
		void DeleteRankingData()
		{
			PlayerPrefs.DeleteKey( "Ranking" );
		}

		[ContextMenu( "AddFakeData" )]
		void AddFakeData()
		{
			AddScore( "faked_" + ( int )Random.Range( 1, 15 ), ( int )Random.Range( 10, 99999 ) );
			DisplayData();
		}
	}
}
