using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Builds the in-run atmosphere behind the machine without requiring a texture asset.
    /// The generated sprites are owned and released by this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeBackdrop : MonoBehaviour
    {
        private Texture2D gradientTexture;
        private Texture2D glowTexture;
        private Texture2D patternTexture;
        private Sprite gradientSprite;
        private Sprite glowSprite;
        private Sprite patternSprite;
        private CanvasGroup leftGlow;
        private CanvasGroup rightGlow;
        private Coroutine ambienceRoutine;
        private RectTransform[] dustRects;
        private CanvasGroup[] dustGroups;
        private float[] dustSpeeds;
        private bool initialized;

public void Initialize(Image baseImage)
        {
            if (initialized || baseImage == null) return;

            gradientSprite = CreateGradientSprite();
            baseImage.sprite = gradientSprite;
            baseImage.type = Image.Type.Simple;
            baseImage.color = Color.white;
            baseImage.raycastTarget = false;

            glowSprite = CreateGlowSprite();
            patternSprite = CreatePatternSprite();

            CreateCafeLayers();
            CreatePatternLayer();
            CreateGlowLayer("Warm Ambient", new Vector2(0.13f, 0.78f),
                new Vector2(620f, 620f), new Color(0.94f, 0.43f, 0.20f, 0.12f), out leftGlow);
            CreateGlowLayer("Cool Ambient", new Vector2(0.88f, 0.28f),
                new Vector2(710f, 710f), new Color(0.20f, 0.55f, 0.72f, 0.10f), out rightGlow);

            ambienceRoutine = StartCoroutine(AnimateAmbience());
            initialized = true;
        }

        private void CreatePatternLayer()
        {
            GameObject patternObject = new GameObject("Cafe Texture", typeof(RectTransform), typeof(Image));
            patternObject.transform.SetParent(transform, false);
            patternObject.transform.SetAsLastSibling();

            RectTransform rect = patternObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = patternObject.GetComponent<Image>();
            image.sprite = patternSprite;
            image.type = Image.Type.Tiled;
            image.color = new Color(1f, 0.82f, 0.60f, 0.055f);
            image.raycastTarget = false;
        }

private void CreateCafeLayers()
        {
            GameObject layers = new GameObject("Cafe Depth Layers", typeof(RectTransform));
            layers.transform.SetParent(transform, false);
            layers.transform.SetAsFirstSibling();
            RectTransform layerRect = layers.GetComponent<RectTransform>();
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;

            CreateLayerImage("Back Wall", layers.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(0.10f, 0.075f, 0.075f, 0.82f), null);
            CreateLayerImage("Window", layers.transform, new Vector2(0.68f, 0.56f),
                new Vector2(0.92f, 0.86f), Vector2.zero, Vector2.zero,
                new Color(0.08f, 0.23f, 0.29f, 0.40f), null);
            CreateLayerImage("Window Light", layers.transform, new Vector2(0.70f, 0.60f),
                new Vector2(0.90f, 0.82f), Vector2.zero, Vector2.zero,
                new Color(0.95f, 0.73f, 0.40f, 0.10f), glowSprite);
            CreateLayerImage("Shelf Back", layers.transform, new Vector2(0.06f, 0.62f),
                new Vector2(0.64f, 0.66f), Vector2.zero, Vector2.zero,
                new Color(0.22f, 0.12f, 0.085f, 0.90f), null);
            CreateLayerImage("Shelf Edge", layers.transform, new Vector2(0.05f, 0.60f),
                new Vector2(0.66f, 0.625f), Vector2.zero, Vector2.zero,
                new Color(0.68f, 0.38f, 0.20f, 0.72f), null);
            CreateLayerImage("Mid Counter", layers.transform, new Vector2(0f, 0.12f),
                new Vector2(1f, 0.31f), Vector2.zero, Vector2.zero,
                new Color(0.12f, 0.075f, 0.06f, 0.92f), null);
            CreateLayerImage("Counter Edge", layers.transform, new Vector2(0f, 0.30f),
                new Vector2(1f, 0.33f), Vector2.zero, Vector2.zero,
                new Color(0.66f, 0.35f, 0.18f, 0.72f), null);
            CreateLayerImage("Foreground Table", layers.transform, new Vector2(0.08f, 0.02f),
                new Vector2(0.92f, 0.12f), Vector2.zero, Vector2.zero,
                new Color(0.24f, 0.13f, 0.09f, 0.92f), null);
            CreateLayerImage("Left Foreground Prop", layers.transform, new Vector2(0.03f, 0.02f),
                new Vector2(0.16f, 0.26f), Vector2.zero, Vector2.zero,
                new Color(0.07f, 0.045f, 0.04f, 0.92f), null);
            CreateLayerImage("Right Foreground Prop", layers.transform, new Vector2(0.84f, 0.01f),
                new Vector2(0.98f, 0.22f), Vector2.zero, Vector2.zero,
                new Color(0.075f, 0.045f, 0.035f, 0.92f), null);

            GameObject dustRoot = new GameObject("Light Dust", typeof(RectTransform));
            dustRoot.transform.SetParent(layers.transform, false);
            dustRoot.transform.SetAsLastSibling();
            RectTransform dustRootRect = dustRoot.GetComponent<RectTransform>();
            dustRootRect.anchorMin = Vector2.zero;
            dustRootRect.anchorMax = Vector2.one;
            dustRootRect.offsetMin = Vector2.zero;
            dustRootRect.offsetMax = Vector2.zero;

            const int dustCount = 16;
            dustRects = new RectTransform[dustCount];
            dustGroups = new CanvasGroup[dustCount];
            dustSpeeds = new float[dustCount];
            for (int i = 0; i < dustCount; i++)
            {
                GameObject dust = new GameObject("Dust " + i, typeof(RectTransform),
                    typeof(Image), typeof(CanvasGroup));
                dust.transform.SetParent(dustRoot.transform, false);
                RectTransform rect = dust.GetComponent<RectTransform>();
                float x = 0.10f + (i * 0.173f) % 0.80f;
                float y = 0.08f + (i * 0.317f) % 0.80f;
                rect.anchorMin = new Vector2(x, y);
                rect.anchorMax = new Vector2(x, y);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                float size = 7f + (i % 4) * 2f;
                rect.sizeDelta = new Vector2(size, size);

                Image image = dust.GetComponent<Image>();
                image.sprite = glowSprite;
                image.color = new Color(1f, 0.82f, 0.48f, 0.24f);
                image.raycastTarget = false;
                dustRects[i] = rect;
                dustGroups[i] = dust.GetComponent<CanvasGroup>();
                dustGroups[i].alpha = 0.32f + (i % 3) * 0.18f;
                dustGroups[i].interactable = false;
                dustGroups[i].blocksRaycasts = false;
                dustSpeeds[i] = 0.006f + (i % 5) * 0.0025f;
            }
        }

private GameObject CreateLayerImage(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color color,
            Sprite sprite)
        {
            GameObject layer = new GameObject(name, typeof(RectTransform), typeof(Image));
            layer.transform.SetParent(parent, false);
            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            Image image = layer.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return layer;
        }



        private void CreateGlowLayer(
            string name,
            Vector2 anchor,
            Vector2 size,
            Color color,
            out CanvasGroup group)
        {
            GameObject glowObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            glowObject.transform.SetParent(transform, false);
            glowObject.transform.SetAsLastSibling();

            RectTransform rect = glowObject.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            Image image = glowObject.GetComponent<Image>();
            image.sprite = glowSprite;
            image.color = color;
            image.raycastTarget = false;

            group = glowObject.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

private IEnumerator AnimateAmbience()
        {
            while (true)
            {
                float time = Time.unscaledTime;
                if (leftGlow != null)
                {
                    leftGlow.alpha = 0.82f + 0.12f *
                        (0.5f + 0.5f * Mathf.Sin(time * 0.55f));
                }

                if (rightGlow != null)
                {
                    rightGlow.alpha = 0.82f + 0.10f *
                        (0.5f + 0.5f * Mathf.Sin(time * 0.42f + 1.7f));
                }

                AnimateDust(time);
                yield return new WaitForSecondsRealtime(0.033f);
            }
        }

private void AnimateDust(float time)
        {
            if (dustRects == null || dustGroups == null || dustSpeeds == null) return;

            for (int i = 0; i < dustRects.Length; i++)
            {
                if (dustRects[i] == null) continue;
                float x = dustRects[i].anchorMin.x;
                float y = Mathf.Repeat(
                    0.08f + i * 0.317f + time * dustSpeeds[i], 1.08f) - 0.04f;
                dustRects[i].anchorMin = new Vector2(x, y);
                dustRects[i].anchorMax = new Vector2(x, y);
                dustRects[i].localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.Sin(time * (0.7f + i * 0.04f)) * 18f);
                dustGroups[i].alpha = 0.18f + 0.24f *
                    (0.5f + 0.5f * Mathf.Sin(time * (0.8f + i * 0.06f) + i));
            }
        }


        private Sprite CreateGradientSprite()
        {
            const int width = 128;
            const int height = 128;
            gradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            gradientTexture.name = "CatCafe Runtime Gradient";
            gradientTexture.filterMode = FilterMode.Bilinear;
            gradientTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[width * height];
            Color upper = new Color(0.07f, 0.06f, 0.085f, 1f);
            Color lower = new Color(0.24f, 0.12f, 0.105f, 1f);
            Color side = new Color(0.055f, 0.13f, 0.16f, 1f);

            for (int y = 0; y < height; y++)
            {
                float vertical = y / (height - 1f);
                for (int x = 0; x < width; x++)
                {
                    float horizontal = x / (width - 1f);
                    Color color = Color.Lerp(lower, upper, vertical);
                    color = Color.Lerp(color, side, Mathf.Abs(horizontal - 0.5f) * 0.32f);
                    float centerGlow = Mathf.Clamp01(1f -
                        Vector2.Distance(new Vector2(horizontal, vertical), new Vector2(0.5f, 0.52f)) * 1.6f);
                    color = Color.Lerp(color, new Color(0.33f, 0.18f, 0.13f, 1f), centerGlow * 0.14f);
                    pixels[y * width + x] = color;
                }
            }

            gradientTexture.SetPixels(pixels);
            gradientTexture.Apply(false, true);
            return Sprite.Create(gradientTexture, new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f), 100f);
        }

        private Sprite CreateGlowSprite()
        {
            const int size = 128;
            glowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            glowTexture.name = "CatCafe Runtime Glow";
            glowTexture.filterMode = FilterMode.Bilinear;
            glowTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(x / (size - 1f), y / (size - 1f)), new Vector2(0.5f, 0.5f)) * 2f;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 2.25f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            glowTexture.SetPixels(pixels);
            glowTexture.Apply(false, true);
            return Sprite.Create(glowTexture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f);
        }

        private Sprite CreatePatternSprite()
        {
            const int size = 64;
            patternTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            patternTexture.name = "CatCafe Runtime Pattern";
            patternTexture.filterMode = FilterMode.Point;
            patternTexture.wrapMode = TextureWrapMode.Repeat;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool gridLine = x % 16 == 0 || y % 16 == 0;
                    bool dot = (x + y * 3) % 29 == 0;
                    pixels[y * size + x] = gridLine
                        ? new Color(1f, 1f, 1f, 0.55f)
                        : dot
                            ? new Color(1f, 0.82f, 0.60f, 0.70f)
                            : Color.clear;
                }
            }

            patternTexture.SetPixels(pixels);
            patternTexture.Apply(false, true);
            return Sprite.Create(patternTexture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f);
        }

        private void OnDisable()
        {
            if (ambienceRoutine != null)
            {
                StopCoroutine(ambienceRoutine);
                ambienceRoutine = null;
            }
        }

        private void OnDestroy()
        {
            DestroyGenerated(gradientSprite);
            DestroyGenerated(glowSprite);
            DestroyGenerated(patternSprite);
            DestroyGenerated(gradientTexture);
            DestroyGenerated(glowTexture);
            DestroyGenerated(patternTexture);
        }

        private static void DestroyGenerated(Object value)
        {
            if (value != null) Destroy(value);
        }
    

        private void OnEnable()
        {
            if (initialized && ambienceRoutine == null)
            {
                ambienceRoutine = StartCoroutine(AnimateAmbience());
            }
        }
}
}