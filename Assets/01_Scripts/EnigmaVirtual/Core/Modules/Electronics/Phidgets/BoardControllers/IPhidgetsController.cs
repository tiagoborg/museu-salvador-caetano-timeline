using UnityEngine.Events;

namespace EnigmaVirtual
{
	internal interface IPhidgetsController
	{
		void Init( PhidgetSettings settings, PhidgetsManager manager );
		void CloseAll();
	}
}