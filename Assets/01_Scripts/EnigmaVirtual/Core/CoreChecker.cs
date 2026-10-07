/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnigmaVirtual
{
    public class CoreChecker : MonoBehaviour
    {
        void Awake()
        {
            if (!Core.IsSet) SceneManager.LoadScene("CoreSetup");
        }

        void Start()
        {
            Destroy(gameObject);
        }
    }
}

