using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 首次发现猫咪时使用的共用揭晓页。只负责表现，不决定是否属于首次发现。
    /// 局内育儿与猫咪招募共用这一份，避免同一件事出现两套视觉语言。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeDiscoveryReveal : MonoBehaviour
    {
        private Canvas canvas;
        private CatCafePresentation presentation;
        private GameObject root;
        private bool dismissed;

        public bool IsShowing { get { return root != null && !dismissed; } }

        public void Initialize(Canvas targetCanvas, CatCafePresentation targetPresentation)
        {
            canvas = targetCanvas;
            presentation = targetPresentation;
            if (presentation != null) presentation.Initialize();
        }

        public IEnumerator ShowAndWait(string catName, Sprite catSprite)
        {
            Show(catName, catSprite);
            // 连收起动画也算在暂停时间里，避免后续结算内容穿过正在合上的纸页。
            while (root != null) yield return null;
        }

        public void Show(string catName, Sprite catSprite, Action onClosed = null)
        {
            if (canvas == null || presentation == null)
            {
                if (onClosed != null) onClosed();
                dismissed = true;
                return;
            }

            if (root != null) Destroy(root);
            dismissed = false;
            root = NewUi("New Cat Discovery", canvas.transform);
            root.transform.SetAsLastSibling();
            Stretch(root.GetComponent<RectTransform>(), 0, 0, 0, 0);

            Image dim = root.AddComponent<Image>();
            dim.color = new Color(0.055f, 0.035f, 0.025f, 0.88f);
            dim.raycastTarget = true;

            GameObject card = NewUi("Discovery Paper", root.transform);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            Anchor(cardRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 690f));
            Image cardImage = card.AddComponent<Image>();
            presentation.ApplySurface(cardImage, PaperSurface.Modal, new Color(0.94f, 0.86f, 0.68f, 1f));

            GameObject ribbon = NewUi("Discovery Ribbon", card.transform);
            RectTransform ribbonRect = ribbon.GetComponent<RectTransform>();
            Anchor(ribbonRect, new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(590f, 112f));
            Image ribbonImage = ribbon.AddComponent<Image>();
            presentation.ApplySurface(ribbonImage, PaperSurface.TitleRibbon, new Color(0.55f, 0.25f, 0.14f, 1f));
            TMP_Text ribbonText = presentation.MakeText("发 现 新 伙 伴", ribbon.transform, 34,
                new Color(1f, 0.94f, 0.79f, 1f), TextAnchor.MiddleCenter);
            ribbonText.fontStyle = FontStyles.Bold;
            Stretch(ribbonText.rectTransform, 28, 12, 28, 12);

            GameObject portraitFrame = NewUi("Portrait Frame", card.transform);
            RectTransform portraitRect = portraitFrame.GetComponent<RectTransform>();
            Anchor(portraitRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(350f, 350f));
            Image portraitPaper = portraitFrame.AddComponent<Image>();
            presentation.ApplySurface(portraitPaper, PaperSurface.RewardCard,
                new Color(0.93f, 0.81f, 0.59f, 1f));

            // 两层暖金光片代替廉价烟花：像图鉴贴纸被盖上收藏印章。
            RectTransform haloOuter = CreateHalo(portraitFrame.transform, "Gold Halo Outer", 292f,
                new Color(1f, 0.72f, 0.20f, 0.30f));
            RectTransform haloInner = CreateHalo(portraitFrame.transform, "Gold Halo Inner", 230f,
                new Color(1f, 0.91f, 0.48f, 0.40f));

            GameObject portrait = NewUi("New Cat Sticker", portraitFrame.transform);
            RectTransform portraitImageRect = portrait.GetComponent<RectTransform>();
            Anchor(portraitImageRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(255f, 255f));
            Image portraitImage = portrait.AddComponent<Image>();
            portraitImage.sprite = catSprite;
            portraitImage.preserveAspect = true;
            portraitImage.color = catSprite == null ? Color.clear : Color.white;
            portraitImage.raycastTarget = false;
            if (catSprite == null)
            {
                TMP_Text fallback = presentation.MakeText("🐾", portrait.transform, 96,
                    new Color(0.34f, 0.20f, 0.13f, 1f), TextAnchor.MiddleCenter);
                Stretch(fallback.rectTransform, 0, 0, 0, 0);
            }

            TMP_Text name = presentation.MakeText(catName, card.transform, 42,
                new Color(0.24f, 0.14f, 0.09f, 1f), TextAnchor.MiddleCenter);
            name.fontStyle = FontStyles.Bold;
            Anchor(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 164f), new Vector2(560f, 70f));

            TMP_Text added = presentation.MakeText("已加入猫咪图鉴", card.transform, 24,
                new Color(0.46f, 0.34f, 0.23f, 1f), TextAnchor.MiddleCenter);
            Anchor(added.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 112f), new Vector2(520f, 46f));

            Button continueButton = presentation.CreateButton(card.transform, "认识新伙伴", null,
                270f, 64f, PaperButtonRole.Primary);
            RectTransform buttonRect = continueButton.transform as RectTransform;
            Anchor(buttonRect, new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(270f, 64f));
            continueButton.onClick.AddListener(() =>
            {
                if (dismissed) return;
                dismissed = true;
                StartCoroutine(Close(cardRect, onClosed));
            });

            StartCoroutine(Open(cardRect, portraitImageRect, haloOuter, haloInner));
        }

        private IEnumerator Open(RectTransform card, RectTransform portrait, RectTransform haloOuter, RectTransform haloInner)
        {
            float elapsed = 0f;
            card.localScale = new Vector3(0.86f, 0.86f, 1f);
            portrait.localScale = Vector3.zero;
            while (elapsed < 0.42f && card != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.42f);
                float cardT = 1f - Mathf.Pow(1f - t, 3f);
                card.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, cardT);
                float stickerT = Mathf.Clamp01((t - 0.25f) / 0.75f);
                float stickerScale = Mathf.Lerp(0f, 1f, 1f - Mathf.Pow(1f - stickerT, 3f)) +
                    Mathf.Sin(stickerT * Mathf.PI) * 0.12f;
                portrait.localScale = Vector3.one * stickerScale;
                haloOuter.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.18f, t);
                haloInner.localScale = Vector3.one * Mathf.Lerp(0.45f, 1f, t);
                yield return null;
            }
            if (card != null) card.localScale = Vector3.one;
            if (portrait != null) portrait.localScale = Vector3.one;
            StartCoroutine(PulseHalo(haloOuter, haloInner));
        }

        private IEnumerator PulseHalo(RectTransform outer, RectTransform inner)
        {
            while (!dismissed && outer != null && inner != null)
            {
                float wave = (Mathf.Sin(Time.unscaledTime * 2.8f) + 1f) * 0.5f;
                outer.localScale = Vector3.one * Mathf.Lerp(1.02f, 1.10f, wave);
                inner.localScale = Vector3.one * Mathf.Lerp(0.98f, 1.04f, 1f - wave);
                yield return null;
            }
        }

        private IEnumerator Close(RectTransform card, Action onClosed)
        {
            float elapsed = 0f;
            while (elapsed < 0.16f && card != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.16f);
                card.localScale = Vector3.one * Mathf.Lerp(1f, 0.92f, t);
                yield return null;
            }
            if (root != null) Destroy(root);
            root = null;
            if (onClosed != null) onClosed();
        }

        private RectTransform CreateHalo(Transform parent, string name, float size, Color color)
        {
            GameObject halo = NewUi(name, parent);
            RectTransform rect = halo.GetComponent<RectTransform>();
            Anchor(rect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            Image image = halo.AddComponent<Image>();
            presentation.PixelFrame(image, color);
            image.raycastTarget = false;
            return rect;
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            GameObject value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            return value;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
