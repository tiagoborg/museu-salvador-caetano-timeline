using System.Linq;
using UnityEngine;

namespace EnigmaVirtual
{
    public static class TransformExtensions
    {
        public static void SlideX(this Transform tf, float duration, float endX, bool toogleActiveVal = false, LeanTweenType tweenType = LeanTweenType.easeOutCirc)
        {
            if (!toogleActiveVal)
            {
                tf.gameObject.SetActive(true);
                LeanTween.moveLocalX(tf.gameObject, endX, duration).setEase(tweenType);
            }
            else
                LeanTween.moveLocalX(tf.gameObject, endX, duration).setEase(tweenType).setOnComplete(() => ToggleEnabled(tf, false));
        }

        public static void SlideY(this Transform tf, float duration, float endY, bool toogleActiveVal, LeanTweenType tweenType = LeanTweenType.easeOutCirc)
        {
            if (!toogleActiveVal)
            {
                tf.gameObject.SetActive(true);
                LeanTween.moveLocalY(tf.gameObject, endY, duration).setEase(tweenType);
            }
            else
                LeanTween.moveLocalY(tf.gameObject, endY, duration).setEase(tweenType).setOnComplete(() => ToggleEnabled(tf, false));
        }

        public static void SlideZ(this Transform tf, float duration, float endY, bool toogleActiveVal, LeanTweenType tweenType = LeanTweenType.easeOutCirc)
        {
            if (!toogleActiveVal)
            {
                tf.gameObject.SetActive(true);
                LeanTween.moveLocalZ(tf.gameObject, endY, duration).setEase(tweenType);
            }
            else
                LeanTween.moveLocalZ(tf.gameObject, endY, duration).setEase(tweenType).setOnComplete(() => ToggleEnabled(tf, false));
        }

        private static void ToggleEnabled(Transform target, bool value)
        {
            target.gameObject.SetActive(value);
        }

        public static LTDescr ScaleAnim(this Transform tf, float time, float scaleStart, float scaleEnd, LeanTweenType ease = LeanTweenType.easeInOutCirc)
        {
            tf.localScale = new Vector3(scaleStart, scaleStart, 1f);
            var vec = new Vector3(scaleEnd, scaleEnd, 1f);
            return LeanTween.scale(tf.gameObject, vec, time).setEase(ease);
        }

        public static void SetLocalX(this Transform transform, float val)
        {
            var vec3 = transform.localPosition;
            vec3.x = val;
            transform.localPosition = vec3;
        }

        public static void SetLocalY(this Transform transform, float val)
        {
            var vec3 = transform.localPosition;
            vec3.y = val;
            transform.localPosition = vec3;
        }

        public static void SetLocalZ(this Transform transform, float val)
        {
            var vec3 = transform.localPosition;
            vec3.z = val;
            transform.localPosition = vec3;
        }

        public static void SetX(this Transform transform, float val)
        {
            var vec3 = transform.position;
            vec3.x = val;
            transform.position = vec3;
        }

        public static void SetY(this Transform transform, float val)
        {
            var vec3 = transform.position;
            vec3.y = val;
            transform.position = vec3;
        }

        public static void SetZ(this Transform transform, float val)
        {
            var vec3 = transform.position;
            vec3.z = val;
            transform.position = vec3;
        }

        public static void RemoveAllChildren(this Transform parent)
        {
            var children = (from Transform child in parent select child.gameObject).ToList();
            children.ForEach(Object.DestroyImmediate);
        }

    }
}
