using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class OtherExtensions
{
    /// <summary>
    /// Sets a layer to the source's attached GameObject and all of its children
    /// in the hierarchy.
    /// </summary>
    public static T SetLayerRecursively<T>(this T source, string layerName) where T : Component
    {
        source.gameObject.SetLayerRecursively(LayerMask.NameToLayer(layerName));
        return source;
    }

    /// <summary>
    /// Sets a layer to the source's attached GameObject and all of its children
    /// in the hierarchy.
    /// </summary>
    public static T SetLayerRecursively<T>(this T source, int layer) where T : Component
    {
        source.gameObject.SetLayerRecursively(layer);
        return source;
    }

    /// <summary>
    /// Get all components of specified Layer in childs
    /// </summary>
    public static List<Transform> GetObjectsOfLayerInChilds(this Component component, string layer)
        => component.GetObjectsOfLayerInChilds(LayerMask.NameToLayer(layer));

    /// <summary>
    /// Get all components of specified Layer in childs
    /// </summary>
    public static List<Transform> GetObjectsOfLayerInChilds(this Component component, int layer)
        => component.gameObject.GetObjectsOfLayerInChilds(layer);

    /// <summary>
    /// Find all Components of specified interface
    /// </summary>
    public static T[] FindObjectsOfInterface<T>() where T : class
    {
        return Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Select(behaviour => behaviour.GetComponent(typeof(T)))
            .OfType<T>()
            .ToArray();
    }

    /// <summary>
    /// Find all Components of specified interface along with Component itself
    /// </summary>
    public static ComponentOfInterface<T>[] FindObjectsOfInterfaceAsComponents<T>() where T : class
    {
        return Object.FindObjectsByType<Component>(FindObjectsSortMode.None)
            .Where(c => c is T)
            .Select(c => new ComponentOfInterface<T>(c, c as T))
            .ToArray();
    }

    public struct ComponentOfInterface<T>
    {
        public readonly Component Component;
        public readonly T Interface;

        public ComponentOfInterface(Component component, T @interface)
        {
            Component = component;
            Interface = @interface;
        }
    }

    /// <summary>
    /// Get components with unique Instance ID
    /// </summary>
    public static T[] OnePerInstance<T>(this T[] components) where T : Component
    {
        if (components == null || components.Length == 0) return null;
        return components.GroupBy(h => h.transform.GetInstanceID()).Select(g => g.First()).ToArray();
    }
}
