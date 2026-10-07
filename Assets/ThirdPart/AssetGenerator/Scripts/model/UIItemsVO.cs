using UnityEngine;
using System.Collections.Generic;

namespace UIGenerator
{
    [System.Serializable]
    public class UIItemsVO
    {
        public string type;
        public string name;
        public List<AssetsVO> layers;

		public void ReverseData()
		{
			layers.Reverse();
		}
	}
}

