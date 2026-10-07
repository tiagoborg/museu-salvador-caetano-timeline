/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * RankingData can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace EnigmaVirtual
{
	[System.Serializable]
	public class RankingData
	{
		[SerializeField]
		public List<RankingVO> Data = new List<RankingVO>();

		public void LoadData( string json )
		{
			JsonUtility.FromJsonOverwrite( json, this );
		}

		public void SortDescending()
		{
			Data = Data.OrderByDescending( ob => ob.Score ).ToList() as List<RankingVO>;
		}

		public void SortAscending()
		{
			Data = Data.OrderBy( ob => ob.Score ).ToList() as List<RankingVO>;
		}
	}
}
