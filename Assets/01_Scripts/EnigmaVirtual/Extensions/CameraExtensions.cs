using UnityEngine;

public static class CameraExtensions
{
    public static bool IsWorldPointInViewport(this Camera camera, Vector3 point)
    {
        Vector3 position = camera.WorldToViewportPoint(point);
        return position.x > 0 && position.y > 0;
    }

    /// <summary>
    /// Gets a point with the same screen point as the source point,
    /// but at the specified distance from camera.
    /// </summary>
    public static Vector3 WorldPointOffsetByDepth(this Camera camera, Vector3 source, float distanceFromCamera,
        Camera.MonoOrStereoscopicEye eye = Camera.MonoOrStereoscopicEye.Mono)
    {
        Vector3 screenPoint = camera.WorldToScreenPoint(source, eye);
        return camera.ScreenToWorldPoint(screenPoint.SetZ(distanceFromCamera), eye);
    }
}
