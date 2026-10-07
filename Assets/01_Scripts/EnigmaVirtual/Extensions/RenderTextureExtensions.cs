using UnityEngine;

public static class RenderTextureExtensions
{
    /// <summary>
    /// Allows you to save the contents of a RenderTexture as a PNG image
    /// </summary>
    /// <param name="rt"></param>
    /// <returns>Byte array (byte[]) representing the PNG image data. If the RenderTexture is null or not created, the method returns null.</returns>
    public static byte[] SaveRenderTextureAsPng(this RenderTexture rt)
    {
        if (rt == null || !rt.IsCreated())
            return null;

        // Allocate
        var sRgbRenderTex = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, mipChain: false, linear: false);

        // Linear to Gamma Conversion
        Graphics.Blit(rt, sRgbRenderTex);

        // Copy memory from RenderTexture
        var tmp = RenderTexture.active;
        RenderTexture.active = sRgbRenderTex;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = tmp;

        try
        {
            // Get PNG bytes
            return tex.EncodeToPNG();
        }
        finally
        {
            // Destroy
            MonoBehaviour.Destroy(tex);
            RenderTexture.ReleaseTemporary(sRgbRenderTex);
        }
    }

    /// <summary>
    /// Allows you to save the contents of a RenderTexture as a JPG image
    /// </summary>
    /// <param name="rt"></param>
    /// <returns>Byte array (byte[]) representing the JPG image data. If the RenderTexture is null or not created, the method returns null.</returns>
    public static byte[] SaveRenderTextureAsJpg(this RenderTexture rt)
    {
        if (rt == null || !rt.IsCreated())
            return null;

        // Allocate
        var sRgbRenderTex = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, mipChain: false, linear: false);

        // Linear to Gamma Conversion
        Graphics.Blit(rt, sRgbRenderTex);

        // Copy memory from RenderTexture
        var tmp = RenderTexture.active;
        RenderTexture.active = sRgbRenderTex;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = tmp;

        try
        {
            // Get PNG bytes
            return tex.EncodeToJPG();
        }
        finally
        {
            // Destroy
            MonoBehaviour.Destroy(tex);
            RenderTexture.ReleaseTemporary(sRgbRenderTex);
        }
    }
}
