/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * SaveScreenshot cannot be copied and/or distributed without 
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/
using EnigmaVirtual;
using System;
using System.Collections;
using UnityEngine;

namespace EnigmaVirtual
{
    [RequireComponent( typeof( Camera ) )]
    public class SaveScreenshot : MonoBehaviour
    {
        private Camera _camera;
        public string path = "";
        public byte[] ByteArray;

        [Header("Snap Size Settings")]
        public int resolutionWidth;
        public int resolutionHeight;
        public int snapWidth;
        public int snapHeight;
        public int startX;

        private int _relativeWidth;
        private int _relativeHeight;
        private int _relativeStartX;

        public Action PhotoSavedEvent;
        Texture2D _userPhoto;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _relativeWidth = Screen.width * snapWidth / resolutionWidth;
            _relativeHeight = Screen.height * snapHeight / resolutionHeight;
            _relativeStartX = _relativeWidth * startX / snapWidth;
        }

        void Start()
        {

        }

        // return file name
        string ScreenShotName( int width, int height )
        {
            return string.Format( "print_{0}x{1}_{2}.jpg",
                width, height,
                System.DateTime.Now.ToString( "yyyy-MM-dd_HH-mm-ss" ) );
        }

        public IEnumerator TakeScreenShot()
        {
            yield return new WaitForEndOfFrame();

            RenderTexture rt = new RenderTexture( Screen.width, Screen.height, 24 );
            _camera.targetTexture = rt;
            Texture2D tempPhoto = new Texture2D( _relativeWidth, _relativeHeight, TextureFormat.RGB24, false );
            _camera.Render();
            RenderTexture.active = rt;
            tempPhoto.ReadPixels( new Rect( _relativeStartX, 0, _relativeWidth, _relativeHeight), 0, 0 );
            _camera.targetTexture = null;
            RenderTexture.active = null;
            Destroy( rt );
            CopyTex( tempPhoto );
            byte[] bytes = tempPhoto.EncodeToJPG();
            Destroy( tempPhoto );
            ByteArray = bytes;
            PhotoSavedEvent.SafeInvoke();
        }

        public IEnumerator TakeScreenShot(Rect rectangle)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(.2f);

            RenderTexture rt = new RenderTexture( _camera.pixelWidth, _camera.pixelHeight, 24 );
            _camera.targetTexture = rt;
            Texture2D tempPhoto = new Texture2D( _camera.pixelWidth, _camera.pixelHeight, TextureFormat.RGB24, false );
            _camera.Render();
            RenderTexture.active = rt;
            tempPhoto.ReadPixels( new Rect( 0, 0, resolutionWidth, resolutionHeight), 0, 0 );
            _camera.targetTexture = null;
            RenderTexture.active = null;
            Destroy( rt );
            CopyTex( tempPhoto );
            byte[ ] bytes = tempPhoto.EncodeToJPG();
            string filename = ScreenShotName( resolutionWidth, resolutionHeight );
            System.IO.File.WriteAllBytes( filename, bytes );
            Development.Log( string.Format( "Took screenshot to: {0}", filename ) );
            Destroy( tempPhoto );
            PhotoSavedEvent.SafeInvoke();
        }


        private void CopyTex( Texture2D taken )
        {
            Color32[ ] pix = taken.GetPixels32();
            _userPhoto = new Texture2D( taken.width, taken.height );
            _userPhoto.SetPixels32( pix );
            _userPhoto.Apply();
        }

        public Texture2D UserPhoto
        {
            get { return _userPhoto; }
        }
    }
}
