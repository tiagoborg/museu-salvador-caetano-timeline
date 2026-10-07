
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public static class RectTransformExtensions
    {
        public static LTDescr SlideLeft(this RectTransform rect, float val, float time, float delay = 0f)
        {
            return LeanTween.value(rect.gameObject, rect.offsetMin.x, val, time).setDelay(delay).setEase(LeanTweenType.linear).setOnUpdate((float value) =>
            {
                var r = rect.offsetMin;
                r.x = value;
                rect.offsetMin = r;
            });
        }


        public static LTDescr SlideX(this RectTransform rect, float val, float time, float delay = 0f)
        {
            return LeanTween.value(rect.gameObject, rect.anchoredPosition.x, val, time).setDelay(delay).setEase(LeanTweenType.linear).setOnUpdate((float value) =>
            {
                var r = rect.anchoredPosition;
                r.x = value;
                rect.anchoredPosition = r;
            });
        }

        public static LTDescr SlideY(this RectTransform rect, float val, float time, float delay = 0f)
        {
            return LeanTween.value(rect.gameObject, rect.anchoredPosition.y, val, time).setDelay(delay).setEase(LeanTweenType.linear).setOnUpdate((float value) =>
            {
                var r = rect.anchoredPosition;
                r.y = value;
                rect.anchoredPosition = r;
            });
        }

        public static void SetX(this RectTransform rect, float val)
        {
            var vec3 = rect.anchoredPosition;
            vec3.x = val;
            rect.anchoredPosition = vec3;
        }

        public static void SetY(this RectTransform rect, float val)
        {
            var vec3 = rect.anchoredPosition;
            vec3.y = val;
            rect.anchoredPosition = vec3;
        }

        public static void SetWidth(this RectTransform rect, float val)
        {
            var size = rect.sizeDelta;
            size.x = val;
            rect.sizeDelta = size;
        }

        public static void SetHeight(this RectTransform rect, float val)
        {
            var size = rect.sizeDelta;
            size.y = val;
            rect.sizeDelta = size;

        }

        public static void SetScaleX(this RectTransform rect, float val)
        {
            var size = rect.localScale;
            size.x = val;
            rect.localScale = size;
        }

        public static void SetScaleZ(this RectTransform rect, float val)
        {
            var size = rect.localScale;
            size.z = val;
            rect.localScale = size;
        }

        public static void SetLeft(this RectTransform rt, float left)
        {
            rt.offsetMin = new Vector2(left, rt.offsetMin.y);
        }

        public static void SetRight(this RectTransform rt, float right)
        {
            rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
        }

        public static void SetTop(this RectTransform rt, float top)
        {
            rt.offsetMax = new Vector2(rt.offsetMax.x, -top);
        }

        public static void SetBottom(this RectTransform rt, float bottom)
        {
            rt.offsetMin = new Vector2(rt.offsetMin.x, bottom);
        }

        public static void SetLeftRight(this RectTransform rt, float left, float right)
        {
            rt.SetLeft(left);
            rt.SetRight(right);
        }

        public static void SetTopBottom(this RectTransform rt, float top, float bottom)
        {
            rt.SetTop(top);
            rt.SetBottom(bottom);
        }
    }
}
