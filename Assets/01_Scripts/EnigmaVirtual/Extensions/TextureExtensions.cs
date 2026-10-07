using EnigmaVirtual;
using System;
using System.Collections.Generic;
using UnityEngine;

public static class TextureExtensions
{
    /// <summary>
    /// Resizes the given texture to the specified width and height.
    /// </summary>
    /// <param name="source">The original Texture2D to resize.</param>
    /// <param name="newWidth">The desired width.</param>
    /// <param name="newHeight">The desired height.</param>
    /// <returns>A new Texture2D resized to the given dimensions.</returns>
    public static Texture2D ResizeTexture(this Texture2D source, int newWidth, int newHeight)
    {
        // Create a temporary RenderTexture with the target dimensions.
        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
        rt.filterMode = FilterMode.Bilinear; // Ensure smooth scaling.

        // Copy the source texture to the RenderTexture.
        Graphics.Blit(source, rt);

        // Backup the currently active RenderTexture.
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        // Create a new Texture2D and read the RenderTexture pixels into it.
        Texture2D newTexture = new Texture2D(newWidth, newHeight, source.format, false);
        newTexture.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        newTexture.Apply();

        // Restore the previously active RenderTexture and release the temporary one.
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return newTexture;
    }
    public static bool IsReadable(this Texture2D tex)
    {
        try
        {
            tex.GetPixel(0, 0);
            return true;
        }
        catch (UnityException)
        {
            // Narrowed from a bare catch. "Not readable" is what GetPixel throws
            // UnityException for, so that case is the answer, not a failure - but the bare catch
            // also swallowed a null tex as "not readable", which is a different bug reported as
            // a routine false. Anything that is not the readability check now propagates.
            return false;
        }
    }

    public static bool IsLinear(this Texture tex)
    {
#if UNITY_EDITOR
        //this works for saved texture, but not for texture2darray:
        //string path = UnityEditor.AssetDatabase.GetAssetPath(tex);
        //UnityEditor.AssetImporter importer = UnityEditor.TextureImporter.GetAtPath(path);
        //Debug.Log(importer.GetType());
        //UnityEditor.TextureImporter teximporter = (UnityEditor.TextureImporter)importer;
        //return teximporter.sRGBTexture;

        System.Reflection.Assembly a = typeof(UnityEditor.EditorWindow).Assembly;
        System.Type t = a.GetType("UnityEditor.TextureUtil");
        string s = (string)t.GetMethod("GetTextureColorSpaceString").Invoke(null, new object[] { tex });
        return s == "Linear";
        //you know the better way to get texture array color space?
#else
                return false;
#endif
    }

    /// <summary>
    /// A CPU-readable copy of <paramref name="tex"/>, which may itself be unreadable. The caller owns the
    /// result and must <c>Destroy</c> it.
    /// </summary>
    /// <remarks>
    /// <b>This did not work, and failed silently, from the day it was written until v0.32.0.</b>
    /// It was <c>Graphics.CopyTexture</c> followed by <c>Apply()</c>, and:
    /// <list type="number">
    /// <item><c>CopyTexture</c> is a <b>GPU-side</b> copy. Populating the destination's CPU pixels is a
    /// separate conditional step that needs the source to have CPU data - and when it does not, the
    /// engine emits no diagnostic at all. That case was mute.</item>
    /// <item><c>mipChain: true</c> against a <c>DownloadHandlerTexture</c> texture, whose
    /// <c>mipmapCount</c> is 1, trips "Graphics.CopyTexture called with mismatching mip counts" and
    /// copies <b>nothing</b>.</item>
    /// <item>Then <c>Apply(updateMipmaps: false)</c> uploaded the freshly-allocated, uninitialised CPU
    /// buffer back <b>over</b> whatever the GPU copy had managed, so both halves ended up garbage.</item>
    /// </list>
    /// The result reported <c>isReadable == true</c> and passed <see cref="IsReadable"/> - it failed a
    /// readability check by *passing* it - so a caller got a plausible non-null texture full of noise and
    /// no exception. <see cref="SaveAsPNG"/> is the composed consequence: it clones an unreadable
    /// texture, reads the garbage, and writes a blank PNG.
    /// <para>
    /// Blit through a RenderTexture and <c>ReadPixels</c> back instead, which is the only route that
    /// crosses GPU to CPU. <c>RGBA32</c> rather than the source format on purpose: <c>ReadPixels</c>
    /// needs a format it can write into, and a compressed source (DXT/ASTC) would fail. Neither this nor
    /// its only caller has ever run, so this is the right mechanism rather than a proven one - see the
    /// v0.32.0 session log for the two-minute check.
    /// </para>
    /// </remarks>
    public static Texture2D ReadableClone(this Texture2D tex)
    {
        if (tex == null)
        {
            Development.LogError("ReadableClone called with a null texture.");
            return null;
        }

        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
            RenderTextureFormat.Default,
            tex.IsLinear() ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

        RenderTexture previous = RenderTexture.active;

        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;

            Texture2D readTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32,
                mipChain: false, linear: tex.IsLinear());

            readTex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readTex.Apply(updateMipmaps: false);

            return readTex;
        }
        finally
        {
            // finally, so a throw between Blit and Apply cannot leave RenderTexture.active pointing at a
            // released target - which corrupts whatever draws next, somewhere else entirely.
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>
    /// Just using UnityEditor.EditorUtility.CompressTexture will not convert compressed to compressed (DXT5 to DXT1, for example)
    /// Texture should be readable
    /// </summary>
    /// <param name="tex"></param>
    /// <returns></returns>
    public static Texture2D UncompressedClone(this Texture2D tex)
    {
        int mipmapCount = tex.mipmapCount;
        Texture2D resultTex = new Texture2D(tex.width, tex.height, TextureFormat.ARGB32, mipChain: mipmapCount != 1, linear: tex.IsLinear());
        for (int m = 0; m < mipmapCount; m++)
        {
            Color[] colors = tex.GetPixels(m);
            resultTex.SetPixels(colors, m);
        }
        resultTex.Apply(updateMipmaps: false);
        return resultTex;
    }

    /// <summary>
    /// Rotates a Texture.
    /// </summary>
    /// <param name="originalTexture"></param>
    /// <param name="clockwise"></param>
    /// <returns></returns>
    public static Texture2D Rotate(this Texture2D originalTexture, bool clockwise = true)
    {
        Color32[] original = originalTexture.GetPixels32();
        Color32[] rotated = new Color32[original.Length];
        int width = originalTexture.width;
        int height = originalTexture.height;

        int iRotated, iOriginal;

        for (int j = 0; j < height; ++j)
        {
            for (int i = 0; i < width; ++i)
            {
                iRotated = (i + 1) * height - j - 1;
                iOriginal = clockwise ? original.Length - 1 - (j * width + i) : j * width + i;
                rotated[iRotated] = original[iOriginal];
            }
        }

        Texture2D rotatedTexture = new Texture2D(height, width);
        rotatedTexture.SetPixels32(rotated);
        rotatedTexture.Apply();
        return rotatedTexture;
    }

    //static public Texture2D ResizedClone(this Texture2D tex, int newWidth, int newHeight)
    ///// Texture should be be readable and not compressed
    //{
    //    Texture2D newTex = new Texture2D(newWidth, newHeight, TextureFormat.ARGB32, mipmap: true, linear: tex.IsLinear()); //has to be ARGB32 to set pixels
    //    newTex.name = tex.name;

    //    Color[] pixels = tex.GetPixels();
    //    pixels = pixels.ResizeColorArray(tex.width, tex.height, newWidth, newHeight);
    //    newTex.SetPixels(pixels);

    //    newTex.Apply(updateMipmaps: true);
    //    return newTex;
    //}


    //public static Color[] ResizeColorArray(this Color[] srcColors, int oldWidth, int oldHeight, int newWidth, int newHeight)
    ///// Helper for GetPixelsResize
    //{
    //    Color[] dstColors = new Color[newWidth * newHeight];

    //    Matrix src = new Matrix(new CoordRect(0, 0, oldWidth, oldHeight));
    //    Matrix dst = new Matrix(new CoordRect(0, 0, newWidth, newHeight));

    //    //reds
    //    for (int i = 0; i < srcColors.Length; i++)
    //        src.array[i] = srcColors[i].r;

    //    dst = src.Resize(dst.rect, dst);

    //    for (int i = 0; i < dstColors.Length; i++)
    //        dstColors[i].r = dst.array[i];

    //    //greens
    //    for (int i = 0; i < srcColors.Length; i++)
    //        src.array[i] = srcColors[i].g;

    //    dst = src.Resize(dst.rect, dst);

    //    for (int i = 0; i < dstColors.Length; i++)
    //        dstColors[i].g = dst.array[i];

    //    //blues
    //    //when I was arrested I was dressed in black they put me on a train and they took me back
    //    for (int i = 0; i < srcColors.Length; i++)
    //        src.array[i] = srcColors[i].b;

    //    dst = src.Resize(dst.rect, dst);

    //    for (int i = 0; i < dstColors.Length; i++)
    //        dstColors[i].b = dst.array[i];

    //    //alphas
    //    for (int i = 0; i < srcColors.Length; i++)
    //        src.array[i] = srcColors[i].a;

    //    dst = src.Resize(dst.rect, dst);

    //    for (int i = 0; i < dstColors.Length; i++)
    //        dstColors[i].a = dst.array[i];

    //    return dstColors;
    //}


    /// <summary>
    /// Generates a new Texture2D with a specified width, height, and color. 
    /// It allows you to create a solid-colored texture for various purposes, 
    /// such as creating placeholders, backgrounds, or materials.
    /// </summary>
    /// <param name="width">The width of the generated texture.</param>
    /// <param name="height">The height of the generated texture.</param>
    /// <param name="color">The color of the texture.</param>
    /// <param name="linear">: A boolean flag indicating whether the texture should use linear rendering. The default is true.</param>
    /// <returns>Texture with a solid-colored image of the specified dimensions.</returns>
    public static Texture2D CreateColoredTexture(int width, int height, Color color, bool linear = true)
    {
        Texture2D result = new Texture2D(width, height, TextureFormat.ARGB32, true, linear);
        Color[] pixels = new Color[width * height];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;

        result.SetPixels(pixels);
        result.Apply();

        return result;
    }

    /// <summary>
    /// Creates a clone (duplicate) of a given Texture2D object.
    /// </summary>
    /// <param name="sourceTexture">Source texture to clone.</param>
    /// <returns>If the copying and applying process is successful, it returns the cloned texture. 
    /// If any exceptions occur during the process, it logs an error and returns null.</returns>
    public static Texture2D Clone(this Texture2D sourceTexture)
    {
        if (sourceTexture == null)
        {
            Development.LogError("Source texture is null. Cannot clone.");
            return null;
        }

        int width = sourceTexture.width;
        int height = sourceTexture.height;

        Texture2D clonedTexture = new Texture2D(width, height, sourceTexture.format, sourceTexture.mipmapCount != 1);

        try
        {
            Graphics.CopyTexture(sourceTexture, clonedTexture);
            clonedTexture.Apply(updateMipmaps: false);
            return clonedTexture;
        }
        catch (Exception e)
        {
            Development.LogError($"Failed to copy texture. Cloning aborted. Error: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Modifies a Texture2D by setting the alpha channel (transparency) of all pixels to 1,
    /// effectively making the entire texture fully opaque (no transparency).
    /// </summary>
    /// <param name="texture">Source texture</param>
    public static void ClearAlpha(this Texture2D texture)
    {
        Color[] colors = texture.GetPixels();

        for (int i = 0; i < colors.Length; i++)
            colors[i].a = 1;  // Clear alpha to 1 (white alpha)

        texture.SetPixels(colors);
        texture.Apply();
    }

    /// <summary>
    /// Applies gamma correction to the colors of a given Texture2D
    /// </summary>
    /// <param name="texture">Source texture</param>
    /// <param name="gamma">gamma value, and the default is set to 2.2</param>
    public static void ApplyGamma(this Texture2D texture, float gamma = 2.2f)
    {
        float invVal = 1 / gamma;
        Color[] colors = texture.GetPixels();

        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = new Color(
                Mathf.Pow(colors[i].r, invVal),
                Mathf.Pow(colors[i].g, invVal),
                Mathf.Pow(colors[i].b, invVal),
                colors[i].a
            );
        }

        texture.SetPixels(colors);
        texture.Apply();
    }

    /// <summary>
    ///  Adjusts the color values in a texture assumed to be a normal map to ensure that 
    ///  the normal vectors encoded in the RGB channels represent valid 3D directions.
    /// </summary>
    /// <param name="texture">Source texture</param>
    public static void RestoreNormalmap(this Texture2D texture)
    {
        // Retrieve the array of colors from the texture
        Color[] colors = texture.GetPixels();

        // Iterate through each color in the array
        for (int i = 0; i < colors.Length; i++)
        {
            // Extract the X and Y components of the normal map from the green (g) and alpha (a) channels
            Vector2 normXY = new Vector2(colors[i].g * 2 - 1, colors[i].a * 2 - 1);

            // Calculate the Z component of the normal map to ensure a valid 3D vector
            float normZ = Mathf.Sqrt(1 - Mathf.Clamp01(Vector3.Dot(normXY, normXY)));

            // Set the modified color back into the array, ensuring the normal map is normalized
            colors[i] = new Color(colors[i].g, colors[i].a, normZ / 2 + 0.5f, 1);
        }

        // Apply the modified array of colors back to the texture
        texture.SetPixels(colors);
        texture.Apply();
    }

    /// <summary>
    ///  Saves a given texture as a PNG file to a specified file path. 
    ///  Additionally, it provides options to perform gamma correction and 
    ///  restore normal maps before saving the texture.
    /// </summary>
    /// <param name="origTex">Source Texture</param>
    /// <param name="savePath">Save path</param>
    /// <param name="linear">A boolean flag indicating whether the texture should use linear rendering. The default is false.</param>
    /// <param name="normal">A boolean flag indicating if its a normal map.</param>
    public static void SaveAsPNG(this Texture2D origTex, string savePath, bool linear = false, bool normal = false)
    {
        // Make a copy of the original texture
        Texture2D tex = origTex;

        // Ensure the texture is readable, creating a readable clone if needed
        if (!tex.IsReadable())
            tex = tex.ReadableClone();

        // Ensure the texture is uncompressed, creating an uncompressed clone if needed
        if (tex.format.IsCompressed())
            tex = tex.UncompressedClone();

        // Apply gamma correction if specified
        if (linear)
        {
            // Clone the texture if it is the original one
            if (tex == origTex)
                tex = tex.Clone();

            // Apply gamma correction
            tex.ApplyGamma(0.4545454545454545f);
            tex.Apply(updateMipmaps: false);
        }

        // Restore normal map if specified
        if (normal)
        {
            // Clone the texture if it is the original one
            if (tex == origTex)
                tex = tex.Clone();

            // Restore the normal map
            tex.RestoreNormalmap();
            tex.Apply(updateMipmaps: false);
        }

        // Encode the texture to PNG format and save it to the specified path
        System.IO.File.WriteAllBytes(savePath, tex.EncodeToPNG());

        // Refresh the asset database in the Unity Editor to reflect changes
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }


    public static Hash128 GetHash(this Texture2D tex)
    {
#if NEVER
                return tex.imageContentsHash;
#else

#if UNITY_EDITOR

        UnityEditor.SerializedProperty property = new UnityEditor.SerializedObject(tex).FindProperty("m_ImageContentsHash");

        //by Broxxar 
        //https://answers.unity.com/questions/1249181/how-to-get-texture-image-contents-hash-property.html

        if (property.type != "Hash128")
        {
            throw new Exception("SerializedProperty does not represent a Hash128 struct.");
        }

        var bytes = new byte[4][];

        for (int i = 0; i < 4; i++)
        {
            bytes[i] = new byte[4];

            for (int j = 0; j < 4; j++)
            {
                property.Next(true);
                bytes[i][j] = (byte)property.intValue;
            }
        }

        Hash128 hash = new Hash128(BitConverter.ToUInt32(bytes[0], 0), BitConverter.ToUInt32(bytes[1], 0), BitConverter.ToUInt32(bytes[2], 0), BitConverter.ToUInt32(bytes[3], 0));

        return hash;

#else
        return new Hash128();
#endif

#endif
    }

    #region Formats and Compression

    public static readonly HashSet<TextureFormat> uncompressedFormats = new HashSet<TextureFormat>(
        new TextureFormat[] {
                    TextureFormat.Alpha8, TextureFormat.ARGB32, TextureFormat.ARGB4444, TextureFormat.R16, TextureFormat.R8, TextureFormat.RFloat,
                    TextureFormat.RG16, TextureFormat.RGB24, TextureFormat.RGB565, TextureFormat.RGB9e5Float, TextureFormat.RGBA32, TextureFormat.RGBA4444,
                    TextureFormat.RGBAFloat, TextureFormat.RGBAHalf, TextureFormat.RGFloat, TextureFormat.RHalf });

    public static bool IsCompressed(this TextureFormat format)
    {
        if (uncompressedFormats.Contains(format))
            return false;
        else
            return true;
    }

    public enum TextureType { RGBA, RGB, Normal, Monochrome, MonochromeFloat, Manual };

    public static TextureFormat AutoFormat(TextureType type, bool compressed)
    {
        if (compressed)
        {
            //list of used texture formats: https://docs.unity3d.com/Manual/class-TextureImporterOverride.html
            //using high quality settings
#if UNITY_EDITOR
            UnityEditor.BuildTarget buildTarget = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            switch (buildTarget)
            {
                case UnityEditor.BuildTarget.StandaloneWindows64:
                case UnityEditor.BuildTarget.StandaloneWindows:
                case UnityEditor.BuildTarget.WSAPlayer:
                    UnityEngine.Rendering.GraphicsDeviceType[] deviceTypes = UnityEditor.PlayerSettings.GetGraphicsAPIs(buildTarget);
                    for (int i = 0; i < deviceTypes.Length; i++)
                        if (deviceTypes[i] == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ||
                            deviceTypes[i] == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12)
                        {
                            if (type == TextureType.Normal) return TextureFormat.BC5;
                            else if (type == TextureType.Monochrome) return TextureFormat.BC4;
                            else if (type == TextureType.MonochromeFloat) return TextureFormat.BC6H;
                            else return TextureFormat.BC7;
                        }
                    return TextureFormat.DXT5;
#if UNITY_2017_3_OR_NEWER
                case UnityEditor.BuildTarget.StandaloneOSX:
#endif
                case UnityEditor.BuildTarget.StandaloneLinux64:
                case UnityEditor.BuildTarget.PS4:
                case UnityEditor.BuildTarget.XboxOne:
                    if (type == TextureType.Normal) return TextureFormat.BC5;
                    else if (type == TextureType.Monochrome) return TextureFormat.BC4;
                    else if (type == TextureType.MonochromeFloat) return TextureFormat.BC6H;
                    else return TextureFormat.BC7;
                case UnityEditor.BuildTarget.Android:
                    if (type == TextureType.RGBA) return TextureFormat.ETC2_RGBA8;
                    else return TextureFormat.ETC2_RGB;
                case UnityEditor.BuildTarget.iOS: return TextureFormat.ASTC_4x4;
                case UnityEditor.BuildTarget.tvOS: return TextureFormat.ASTC_4x4;
                default: return TextureFormat.DXT5;
            }
#else
                    return TextureFormat.DXT5;
#endif
        }
        else
        {
            switch (type)
            {
                case TextureType.RGB: return TextureFormat.RGB24;
                case TextureType.Normal: return TextureFormat.RG16;
                case TextureType.Monochrome: return TextureFormat.R8;
                case TextureType.MonochromeFloat: return TextureFormat.RFloat;
                default: return TextureFormat.RGBA32;
            }
        }
    }

    #endregion

    /// <summary>
    /// Converts a Texture2D object into a Unity Sprite.
    /// </summary>
    /// <param name="texture">Source Texture</param>
    /// <param name="ppu">Pixels Per Unit (PPU) value. Default value = 100f.</param>
    /// <param name="spriteMeshType"> Type of mesh generated for the sprite.</param>
    /// <returns></returns>
    public static Sprite AsSprite(this Texture2D texture, float ppu = 100f, SpriteMeshType spriteMeshType = SpriteMeshType.FullRect)
    {
        Rect rect = new Rect(0, 0, texture.width, texture.height);
        Vector2 pivot = new Vector2(0.5f, 0.5f);
        return Sprite.Create(texture, rect, pivot, ppu, 0, spriteMeshType);
    }

    /// <summary>
    /// Resamples a given Texture2D to a target width and height using bilinear filtering.
    /// </summary>
    /// <param name="texture">Source Texture</param>
    /// <param name="targetWidth">Target width.</param>
    /// <param name="targetHeight">Target height.</param>
    /// <returns></returns>
    public static Texture2D Resample(this Texture2D texture, int targetWidth, int targetHeight)
    {
        int sourceWidth = texture.width;
        int sourceHeight = texture.height;
        float sourceAspect = (float)sourceWidth / sourceHeight;
        float targetAspect = (float)targetWidth / targetHeight;

        int xOffset = 0;
        int yOffset = 0;
        float factor;

        if (sourceAspect > targetAspect)
        {
            // crop width
            factor = (float)targetHeight / sourceHeight;
            xOffset = (int)((sourceWidth - sourceHeight * targetAspect) * 0.5f);
        }
        else
        {
            // crop height
            factor = (float)targetWidth / sourceWidth;
            yOffset = (int)((sourceHeight - sourceWidth / targetAspect) * 0.5f);
        }

        var data = texture.GetPixels32();
        var data2 = new Color32[targetWidth * targetHeight];

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                var p = new Vector2(Mathf.Clamp(xOffset + x / factor, 0, sourceWidth - 1), Mathf.Clamp(yOffset + y / factor, 0, sourceHeight - 1));
                // bilinear filtering
                var c11 = data[Mathf.FloorToInt(p.x) + sourceWidth * (Mathf.FloorToInt(p.y))];
                var c12 = data[Mathf.FloorToInt(p.x) + sourceWidth * (Mathf.CeilToInt(p.y))];
                var c21 = data[Mathf.CeilToInt(p.x) + sourceWidth * (Mathf.FloorToInt(p.y))];
                var c22 = data[Mathf.CeilToInt(p.x) + sourceWidth * (Mathf.CeilToInt(p.y))];

                data2[x + y * targetWidth] = Color.Lerp(Color.Lerp(c11, c12, p.y), Color.Lerp(c21, c22, p.y), p.x);
            }
        }

        var tex = new Texture2D(targetWidth, targetHeight);
        tex.SetPixels32(data2);
        tex.Apply(true);
        return tex;
    }

    /// <summary>
    /// Ccrop a specified region from the original texture and optionally adjust its brightness.
    /// </summary>
    /// <param name="texture">Source Texture</param>
    /// <param name="left">Parameter for defining the left crop region</param>
    /// <param name="right">Parameter for defining the right crop region</param>
    /// <param name="top">Parameter for defining the top crop region</param>
    /// <param name="down">Parameter for defining the down crop region</param>
    /// <param name="brightnessOffset">Brightness offset</param>
    /// <returns></returns>
    public static Texture2D Crop(this Texture2D texture, int left, int right, int top, int down, float brightnessOffset = 0)
    {
        // Calculate the dimensions of the resulting cropped texture
        int x = left + right;
        int y = top + down;
        int resW = texture.width - x;
        int resH = texture.height - y;

        // Extract pixels from the specified region
        var pixels = texture.GetPixels(left, down, resW, resH);

        // Adjust brightness if needed
        if (!Mathf.Approximately(brightnessOffset, 0))
        {
            // Loop through each pixel and apply brightness offset
            System.Array.ForEach(pixels, p => p = p.BrightnessOffset(brightnessOffset));
        }

        // Create a new texture with the cropped and adjusted pixels
        Texture2D result = new Texture2D(resW, resH, TextureFormat.RGB24, false);
        result.SetPixels(pixels);
        result.Apply();

        // Return the resulting texture
        return result;
    }
}
