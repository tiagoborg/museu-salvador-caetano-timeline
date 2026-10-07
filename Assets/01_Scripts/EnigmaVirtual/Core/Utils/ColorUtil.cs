/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * ColorUtil can not be copied and/or distributed without
 * the express permission of Gema Digital
 *******************************************************/
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{

    public class ColorUtil
    {

        /// <summary>
        ///
        /// </summary>
        /// <param name="tex"></param>
        /// <returns></returns>
        public static Color32 AverageColorFromTexture(Texture2D tex)
        {
            Color32[] texColors = tex.GetPixels32();

            int total = texColors.Length;

            float r = 0;
            float g = 0;
            float b = 0;

            for (int i = 0; i < total; i++)
            {
                r += texColors[i].r;
                g += texColors[i].g;
                b += texColors[i].b;
            }
            return new Color32((byte)(r / total), (byte)(g / total), (byte)(b / total), 0);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="texColors"></param>
        /// <returns></returns>
        public static Color32 AverageColorFromArray(Color32[] texColors)
        {
            int total = texColors.Length;

            float r = 0;
            float g = 0;
            float b = 0;

            for (int i = 0; i < total; i++)
            {
                r += texColors[i].r;
                g += texColors[i].g;
                b += texColors[i].b;
            }
            return new Color32((byte)(r / total), (byte)(g / total), (byte)(b / total), 0);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="image"></param>
        /// <param name="value"></param>
        public static void SetAlpha(Image image, float value)
        {
            Color c = image.color;
            c.a = value;
            image.color = c;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="color"></param>
        /// <returns></returns>
        public static string ColorToHex(Color32 color)
        {
            string hex = color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2");
            return hex;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="color"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string RichTextColor(Color32 color, string text)
        {
            return "<color=#" + ColorUtil.ColorToHex(color)+">" + text + "</color>";
        }
    }

}
