using UnityEngine;

namespace EnigmaVirtual
{
	[CreateAssetMenu( fileName = "Descriptor", menuName = "Gema/Descriptor" )]
	public class ApplicationDescription : ScriptableObject
	{
		public string FullName = "Unity Default";
		public string Slug = "default";
		public string Client = "Hey Digital Agency";
		public int Year = 2021;
		public string Version = "1.0.0";
		public string Copyright = "(c)2017 Hey Digital Agency";
		public float DesignWidth = 1920f;
		public float DesignHeight = 1080f;
	}
}
