/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * CSVWriter can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using System.Collections;
using System.IO;
using System;

namespace EnigmaVirtual
{
    public class CSVWriter : MonoBehaviour
    {
        private string _filePath;
        private const string LINE_END = "\n";

        /// <summary>
        ///
        /// </summary>
        void Start()
        {
            DontDestroyOnLoad( gameObject );

            _filePath = Application.dataPath + "/StreamingAssets/registos.csv";
            File.AppendAllText( _filePath, TimeStamp() + ",," + LINE_END );
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="name"></param>
        /// <param name="email"></param>
        /// <param name="age"></param>
        public void AddData( string name, string email, string age )
        {
            File.AppendAllText( _filePath, TimeStamp() + "," + name + "," + email + "," + age + LINE_END );
        }

        private string TimeStamp()
        {
            return DateTime.Now.ToString();
        }
    }

}
