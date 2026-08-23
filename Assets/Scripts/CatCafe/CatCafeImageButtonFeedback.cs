using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Drives a separate artwork layer from a transparent uGUI button's pointer states.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeImageButtonFeedback : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        public const string BrightnessShaderName = "UI/CatCafe Brightness";
        private const float TransitionDuration = 0.06f;

        // 默认＝局内那套（只压深、不缩放）。大厅纸片按钮用重载传自己的手感，
        // 免得改这里把局内的表现一起动了。
        private float hoverScale = 1f;
        private float pressedScale = 1f;

        private Button button;
        private RectTransform visual;
        private Graphic graphic;
        private Vector3 baseScale;
        private Color baseColor;
        private Color hoverColor;
        private Color pressedColor;
        private Coroutine transition;
        private Material ownedBrightnessMaterial;
        private bool pointerInside;
        private bool initialized;

        /// <summary>
        /// 给图片按钮建立与局内开始营业/设置按钮完全相同的亮度反馈层。
        /// 原图保持不动，悬停与按下只驱动覆盖其上的同图透明层。
        /// </summary>
        public void InitializeBrightnessOverlay(Image sourceImage)
        {
            if (sourceImage == null || sourceImage.sprite == null)
            {
                Debug.LogError("[CatCafeUI] 图片按钮缺少可用于亮度反馈的源图。");
                return;
            }

            Shader brightnessShader = Shader.Find(BrightnessShaderName);
            if (brightnessShader == null)
            {
                Debug.LogError("[CatCafeUI] Missing " + BrightnessShaderName + " shader.");
                return;
            }

            GameObject overlayObject = new GameObject(
                "Button Brightness Feedback", typeof(RectTransform), typeof(Image));
            overlayObject.transform.SetParent(sourceImage.transform, false);
            overlayObject.transform.SetAsLastSibling();

            RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.anchoredPosition = Vector2.zero;
            overlayRect.sizeDelta = Vector2.zero;
            overlayRect.localRotation = Quaternion.identity;
            overlayRect.localScale = Vector3.one;

            Image overlay = overlayObject.GetComponent<Image>();
            overlay.sprite = sourceImage.sprite;
            overlay.type = sourceImage.type;
            overlay.preserveAspect = sourceImage.preserveAspect;
            overlay.fillCenter = sourceImage.fillCenter;
            overlay.fillMethod = sourceImage.fillMethod;
            overlay.fillAmount = sourceImage.fillAmount;
            overlay.fillClockwise = sourceImage.fillClockwise;
            overlay.fillOrigin = sourceImage.fillOrigin;
            overlay.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
            overlay.maskable = sourceImage.maskable;
            overlay.raycastTarget = false;
            overlay.color = new Color(1f, 1f, 1f, 0f);

            ownedBrightnessMaterial = new Material(brightnessShader)
            {
                name = sourceImage.name + " Button Brightness (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            overlay.material = ownedBrightnessMaterial;
            Initialize(overlayRect, overlay);
        }

        public void Initialize(RectTransform target, Graphic targetGraphic)
        {
            Initialize(target, targetGraphic, new Color(1f, 1f, 1f, 1f),
                new Color(0f, 0f, 0f, 1f), 1f, 1f);
        }

        /// <summary>
        /// 可调手感的版本。纸艺按钮通常要"按下去略暗略缩"，而不是局内那种整块压深。
        /// </summary>
        public void Initialize(RectTransform target, Graphic targetGraphic,
            Color hoverTint, Color pressedTint, float hoverScaleValue, float pressedScaleValue)
        {
            visual = target;
            graphic = targetGraphic;
            button = GetComponent<Button>();
            if (visual == null || graphic == null) return;

            baseScale = visual.localScale;
            baseColor = graphic.color;
            hoverColor = hoverTint;
            pressedColor = pressedTint;
            hoverScale = hoverScaleValue;
            pressedScale = pressedScaleValue;
            initialized = true;
            ApplyImmediate(1f, baseColor);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!CanReact()) return;

            pointerInside = true;
            AnimateTo(hoverScale, hoverColor);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            if (!initialized) return;

            AnimateTo(1f, baseColor);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanReact()) return;

            AnimateTo(pressedScale, pressedColor);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!CanReact()) return;

            AnimateTo(pointerInside ? hoverScale : 1f,
                pointerInside ? hoverColor : baseColor);
        }

        private bool CanReact()
        {
            return initialized && (button == null || button.interactable);
        }

        private void AnimateTo(float scale, Color color)
        {
            if (!initialized) return;
            if (transition != null) StopCoroutine(transition);
            transition = StartCoroutine(AnimateState(baseScale * scale, color));
        }

        private IEnumerator AnimateState(Vector3 targetScale, Color targetColor)
        {
            Vector3 startScale = visual.localScale;
            Color startColor = graphic.color;
            float elapsed = 0f;

            while (elapsed < TransitionDuration && visual != null && graphic != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / TransitionDuration);
                t = t * t * (3f - 2f * t);
                visual.localScale = Vector3.LerpUnclamped(startScale, targetScale, t);
                graphic.color = Color.LerpUnclamped(startColor, targetColor, t);
                yield return null;
            }

            ApplyImmediate(targetScale.x / Mathf.Max(0.0001f, baseScale.x), targetColor);
            transition = null;
        }

        private void ApplyImmediate(float scale, Color color)
        {
            if (visual != null) visual.localScale = baseScale * scale;
            if (graphic != null) graphic.color = color;
        }

        private void OnDisable()
        {
            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }

            pointerInside = false;
            if (initialized) ApplyImmediate(1f, baseColor);
        }

        private void OnDestroy()
        {
            if (ownedBrightnessMaterial != null)
            {
                Destroy(ownedBrightnessMaterial);
                ownedBrightnessMaterial = null;
            }
        }
    }
}
