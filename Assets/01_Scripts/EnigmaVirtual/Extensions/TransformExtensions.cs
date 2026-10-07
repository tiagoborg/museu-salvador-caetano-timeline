using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

static class TransformExtensions
{
    /// <summary>
    /// Returns a random point inside transform.
    /// </summary>
    /// <param name="transform"></param>
    /// <returns></returns>
    public static Vector3 insideTransform(this Transform transform)
    {
        return transform.position + new Vector3(Random.Range(-transform.localScale.x / 2, transform.localScale.x / 2),
                Random.Range(-transform.localScale.y / 2, transform.localScale.y / 2), Random.Range(-transform.localScale.z / 2, transform.localScale.z / 2));
    }

    /// <summary>
    /// Set position to Vector3.zero
    /// </summary>
    public static void ResetPosition(this Transform transform) => transform.position = Vector3.zero;


    /// <summary>
    /// Sets the lossy scale of the source Transform.
    /// </summary>
    public static Transform SetLossyScale(this Transform source,
        Vector3 targetLossyScale)
    {
        source.localScale = source.lossyScale.Pow(-1).ScaleBy(targetLossyScale)
            .ScaleBy(source.localScale);
        return source;
    }

    /// <summary>
    ///  Recursively traverse through the child hierarchy of a given Transform and return 
    ///  a list of child transforms that satisfy a specified condition.
    /// </summary>
    /// <param name="transform">Source Transform</param>
    /// <param name="match">Condition for selecting child transforms.</param>
    /// <returns></returns>
    public static List<Transform> GetChildrenWhere(this Transform transform, Predicate<Transform> match)
    {
        List<Transform> matchingChildren = new List<Transform>();
        RecursiveCheck(transform);
        return matchingChildren;

        void RecursiveCheck(Transform parent)
        {
            foreach (Transform child in parent)
            {
                RecursiveCheck(child);

                if (match.Invoke(child))
                    matchingChildren.Add(child);
            }
        }
    }

}
