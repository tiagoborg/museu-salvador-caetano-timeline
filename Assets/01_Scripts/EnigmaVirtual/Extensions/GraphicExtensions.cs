using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public static class GraphicExtensions
{
    /// <summary>
    ///   How Use, e.g 
    ///   Image.CrossFadeAlphaFixed(1f, 1f. false, () => print("Finished"));
    /// </summary>
    /// <param name="graphic"></param>
    /// <param name="alpha"></param>
    /// <param name="duration"></param>
    /// <param name="callback"></param>
    public static void CrossFadeAlphaFixed(this Graphic graphic, float alpha, float duration, Action callback)
    {
        if (graphic == null)
        {
            Debug.LogError("Graphic is null. CrossFadeAlphaFixed will not execute.");
            return;
        }

        MonoBehaviour monoBehaviour = graphic.GetComponent<MonoBehaviour>();
        monoBehaviour.StartCoroutine(CrossFadeAlphaFixed_Coroutine(graphic, alpha, duration, callback));
    }

    /// <summary>
    ///  Fixed-duration crossfade
    /// </summary>
    /// <param name="graphic"></param>
    /// <param name="alpha"></param>
    /// <param name="duration"></param>
    /// <param name="callback"></param>
    /// <returns></returns>
    private static IEnumerator CrossFadeAlphaFixed_Coroutine(Graphic graphic, float alpha, float duration, Action callback)
    {
        Color currentColor = graphic.color;
        Color visibleColor = graphic.color;
        visibleColor.a = alpha;
        float startTime = Time.realtimeSinceStartup;

        while (true)
        {
            float elapsed = Time.realtimeSinceStartup - startTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            graphic.color = Color.Lerp(currentColor, visibleColor, progress);

            if (progress >= 1f)
            {
                graphic.color = visibleColor; // Ensure the final color is set accurately.
                callback?.Invoke();
                yield break; // Exit the coroutine when the animation is complete.
            }

            yield return null;
        }
    }
}
