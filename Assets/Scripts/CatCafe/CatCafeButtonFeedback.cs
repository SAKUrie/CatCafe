using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Lightweight pointer feedback for runtime cafe buttons.
    /// It is event-driven and uses unscaled time so feedback remains responsive while paused.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeButtonFeedback : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        ISelectHandler,
        IDeselectHandler,
        ISubmitHandler
    {
        private RectTransform rectTransform;
        private Vector3 baseScale = Vector3.one;
        private Coroutine transition;
        private bool pointerInside;
        private Button button;
        private Image background;
        private Outline outline;
        private TMP_Text label;
        private CatCafeAudioFeedback audioFeedback;
        private Color baseColor = Color.white;
        private Color baseOutlineColor = Color.clear;
        private Color baseLabelColor = Color.white;
        private bool selected;
        private bool initialized;

        public void Initialize()
        {
            rectTransform = transform as RectTransform;
            if (rectTransform == null) return;

            button = GetComponent<Button>();
            background = GetComponent<Image>();
            outline = GetComponent<Outline>();
            label = GetComponentInChildren<TMP_Text>(true);
            audioFeedback = GetComponentInParent<CatCafeAudioFeedback>();
            baseScale = rectTransform.localScale;
            baseColor = background == null ? Color.white : background.color;
            baseOutlineColor = outline == null
                ? new Color(0.10f, 0.07f, 0.055f, 0.95f)
                : outline.effectColor;
            baseLabelColor = label == null ? Color.white : label.color;

            if (button != null)
            {
                button.transition = Selectable.Transition.None;
            }

            initialized = true;
            ApplyImmediate(1f, baseColor, baseOutlineColor, baseLabelColor);
        }

        /// <summary>
        /// 外部改按钮常态配色时用这个，别直接改 Image.color 再重新 Initialize。
        /// 点击瞬间 OnPointerUp / OnSelect 已经起了一条基于旧常态色的过渡动画，
        /// 单纯改颜色会被那条动画覆盖回去；而且 EventSystem 选中态会让 Update 不再复位，
        /// 于是新选中的档位一直停在旧色上（看起来就是"灰的"）。
        /// </summary>
        public void SetRestingColors(Color background, Color labelColor)
        {
            if (!initialized) Initialize();

            baseColor = background;
            baseLabelColor = labelColor;

            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }

            bool active = pointerInside || selected;
            ApplyImmediate(
                active ? 1.03f : 1f,
                active ? GetBackgroundColor(1.10f) : baseColor,
                active ? GetOutlineColor(true) : baseOutlineColor,
                active ? GetLabelColor(true) : baseLabelColor);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!CanReact()) return;

            pointerInside = true;
            if (audioFeedback != null) audioFeedback.PlayHover();
            AnimateTo(1.03f, GetBackgroundColor(1.10f),
                GetOutlineColor(true), GetLabelColor(true));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            if (!initialized || selected) return;

            AnimateTo(1f, baseColor, baseOutlineColor, baseLabelColor);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanReact()) return;

            if (audioFeedback != null) audioFeedback.PlayClick();
            AnimateTo(0.96f, GetBackgroundColor(0.88f),
                GetOutlineColor(false), baseLabelColor);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!CanReact()) return;

            bool active = pointerInside || selected;
            AnimateTo(active ? 1.03f : 1f,
                active ? GetBackgroundColor(1.10f) : baseColor,
                active ? GetOutlineColor(true) : baseOutlineColor,
                active ? GetLabelColor(true) : baseLabelColor);
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (!CanReact()) return;

            selected = true;
            if (audioFeedback != null) audioFeedback.PlayHover();
            AnimateTo(1.03f, GetBackgroundColor(1.10f),
                GetOutlineColor(true), GetLabelColor(true));
        }

        public void OnDeselect(BaseEventData eventData)
        {
            selected = false;
            if (!initialized || pointerInside) return;

            AnimateTo(1f, baseColor, baseOutlineColor, baseLabelColor);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!CanReact()) return;

            if (audioFeedback != null) audioFeedback.PlayClick();
            AnimateTo(0.96f, GetBackgroundColor(0.88f),
                GetOutlineColor(false), baseLabelColor);
        }

private void Update()
        {
            if (!initialized || button == null) return;

            if (!button.interactable)
            {
                if (transition != null)
                {
                    StopCoroutine(transition);
                    transition = null;
                }

                Color disabledBackground = new Color(
                    baseColor.r * 0.55f, baseColor.g * 0.55f, baseColor.b * 0.55f,
                    baseColor.a * 0.68f);
                Color disabledOutline = new Color(
                    baseOutlineColor.r * 0.62f, baseOutlineColor.g * 0.62f,
                    baseOutlineColor.b * 0.62f, baseOutlineColor.a * 0.55f);
                Color disabledLabel = new Color(
                    baseLabelColor.r * 0.58f, baseLabelColor.g * 0.58f,
                    baseLabelColor.b * 0.58f, baseLabelColor.a * 0.52f);
                ApplyImmediate(1f, disabledBackground, disabledOutline, disabledLabel);
                return;
            }

            if (transition == null && !pointerInside && !selected)
            {
                ApplyImmediate(1f, baseColor, baseOutlineColor, baseLabelColor);
            }
        }




        private void AnimateTo(
            float multiplier,
            Color targetColor,
            Color targetOutlineColor,
            Color targetLabelColor)
        {
            if (!initialized || rectTransform == null) return;

            if (transition != null)
            {
                StopCoroutine(transition);
            }

            transition = StartCoroutine(AnimateState(
                baseScale * multiplier,
                targetColor,
                targetOutlineColor,
                targetLabelColor,
                0.08f));
        }

        private IEnumerator AnimateState(
            Vector3 targetScale,
            Color targetColor,
            Color targetOutlineColor,
            Color targetLabelColor,
            float duration)
        {
            Vector3 startScale = rectTransform.localScale;
            Color startColor = background == null ? Color.white : background.color;
            Color startOutlineColor = outline == null ? Color.clear : outline.effectColor;
            Color startLabelColor = label == null ? Color.white : label.color;
            float elapsed = 0f;

            while (elapsed < duration && rectTransform != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t);
                rectTransform.localScale = Vector3.LerpUnclamped(startScale, targetScale, t);

                if (background != null)
                {
                    background.color = Color.LerpUnclamped(startColor, targetColor, t);
                }

                if (outline != null)
                {
                    outline.effectColor = Color.LerpUnclamped(
                        startOutlineColor, targetOutlineColor, t);
                }

                if (label != null)
                {
                    label.color = Color.LerpUnclamped(startLabelColor, targetLabelColor, t);
                }

                yield return null;
            }

            ApplyImmediate(targetScale.x / Mathf.Max(0.0001f, baseScale.x),
                targetColor, targetOutlineColor, targetLabelColor);
            transition = null;
        }

        private bool CanReact()
        {
            return initialized && (button == null || button.interactable);
        }

        private Color GetBackgroundColor(float multiplier)
        {
            return new Color(
                Mathf.Clamp01(baseColor.r * multiplier),
                Mathf.Clamp01(baseColor.g * multiplier),
                Mathf.Clamp01(baseColor.b * multiplier),
                baseColor.a);
        }

        private Color GetOutlineColor(bool highlighted)
        {
            if (!highlighted) return baseOutlineColor;

            return new Color(
                Mathf.Clamp01(baseOutlineColor.r * 1.12f + 0.04f),
                Mathf.Clamp01(baseOutlineColor.g * 1.08f + 0.03f),
                Mathf.Clamp01(baseOutlineColor.b * 0.88f),
                Mathf.Clamp01(baseOutlineColor.a + 0.08f));
        }

        private Color GetLabelColor(bool highlighted)
        {
            if (!highlighted) return baseLabelColor;

            return Color.Lerp(baseLabelColor, new Color(1f, 0.90f, 0.66f, 1f), 0.22f);
        }

        private void ApplyImmediate(
            float multiplier,
            Color color,
            Color outlineColor,
            Color labelColor)
        {
            if (rectTransform != null)
            {
                rectTransform.localScale = baseScale * multiplier;
            }

            if (background != null) background.color = color;
            if (outline != null) outline.effectColor = outlineColor;
            if (label != null) label.color = labelColor;
        }


        private void OnDisable()
        {
            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }

            pointerInside = false;
            selected = false;
            ApplyImmediate(1f, baseColor, baseOutlineColor, baseLabelColor);
        }
    }
}