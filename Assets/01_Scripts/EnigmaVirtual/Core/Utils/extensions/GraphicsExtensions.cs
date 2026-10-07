
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public static class GraphicsExtensions
    {

        public static void SetAlpha(this MaskableGraphic graphic, float value)
        {
            var cor = graphic.color;
            cor.a = value;
            graphic.color = cor;
        }

        public static LTDescr FadeIn(this MaskableGraphic graphic, float time, float delay = 0f, float starValue = 0f, float endValue = 1f)
        {
            graphic.SetAlpha(starValue);
            return LeanTween.value(graphic.gameObject, graphic.color.a, endValue, time).setDelay(delay).setOnUpdate((float val) =>
                  {
                      graphic.SetAlpha(val);
                  });
        }

        public static LTDescr FadeOut(this MaskableGraphic graphic, float time, float delay = 0f, float starValue = 1f)
        {
            graphic.SetAlpha(starValue);
            return LeanTween.value(graphic.gameObject, graphic.color.a, 0f, time).setDelay(delay).setOnUpdate((float val) =>
            {
                graphic.SetAlpha(val);
            });
        }
	}
}
