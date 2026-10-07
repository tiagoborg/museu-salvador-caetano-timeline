using EnigmaVirtual.Components.UIElements;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class Auto_MagneticInfiniteScroll : MonoBehaviour
    {
        public UI_MagneticInfiniteScroll magneticInfiniteScroll;

        public GameObject prefab;

        private IEnumerator Start()
        {
            Debug.LogError("START");

            List<Transform> ll = new List<Transform> { };

            magneticInfiniteScroll.OnNewSelect += MagneticInfiniteScroll_OnNewSelect;

            for (int i = 0; i < 6; i++)
            {
                GameObject go = Instantiate(prefab);
                go.GetComponent<Image>().color = new Color(Random.Range(0F, 1F), Random.Range(0, 1F), Random.Range(0, 1F));
                ll.Add(go.transform);
            }
            magneticInfiniteScroll.SetNewItems(ref ll);

            yield return new WaitForEndOfFrame();
            magneticInfiniteScroll.Init();
            magneticInfiniteScroll.SetInitContent();
        }

        private void MagneticInfiniteScroll_OnNewSelect(GameObject obj)
        {
            Development.Log(obj.name);
        }
    }
}
