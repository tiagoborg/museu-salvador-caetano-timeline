using System.Collections.Generic;
using UnityEngine;

public static class GameObjectExtensions
{
    /// <summary>
    /// Sets a layer to the source GameObject and all of its children in the
    /// hierarchy.
    /// </summary>
    public static GameObject SetLayerRecursively(this GameObject source,
        string layerName)
    {
        source.SetLayerRecursively(LayerMask.NameToLayer(layerName));
        return source;
    }

    /// <summary>
    /// Sets a layer to the source GameObject and all of its children in the
    /// hierarchy.
    /// </summary>
    public static GameObject SetLayerRecursively(this GameObject source, int layer)
    {
        var allTransforms = source.GetComponentsInChildren<Transform>(true);
        foreach (var tf in allTransforms)
            tf.gameObject.layer = layer;
        return source;
    }

    public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();

        if (component != null) 
            return component;

        return gameObject.AddComponent<T>();
    }

    public static T GetOrAddComponent<T>(this Component component) where T : Component
        => GetOrAddComponent<T>(component.gameObject);
    public static bool HasComponent<T>(this GameObject gameObject) => gameObject.GetComponent<T>() != null;
    public static bool HasComponent<T>(this Component component) => component.GetComponent<T>() != null;

    /// <summary>
    /// Get all components of specified Layer in childs
    /// </summary>
    public static List<Transform> GetObjectsOfLayerInChilds(this GameObject gameObject, int layer)
        => gameObject.transform.GetChildrenWhere( t => t.gameObject.layer == layer);


    /// <summary>
    /// Get all components of specified Layer in childs
    /// </summary>
    public static List<Transform> GetObjectsOfLayerInChilds(this GameObject gameObject, string layer)
        => gameObject.GetObjectsOfLayerInChilds(LayerMask.NameToLayer(layer));
}
