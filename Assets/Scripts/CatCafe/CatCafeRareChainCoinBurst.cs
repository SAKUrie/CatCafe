using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Shows the settlement coin burst as regular UI Images so it shares the
    /// ScreenSpaceCamera canvas with the rest of the CatCafe interface.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeRareChainCoinBurst : MonoBehaviour
    {
        private sealed class BurstCoin
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Image;
            public CanvasGroup Group;
            public int Version;
        }

        private readonly List<BurstCoin> coins = new List<BurstCoin>();
        private RectTransform coordinateRoot;
        private Sprite coinSprite;
        private bool initialized;

        public void Initialize(RectTransform root)
        {
            if (initialized) return;

            coordinateRoot = root;
            coinSprite = Resources.Load<Sprite>(CatCafeConfigDatabase.GetRequiredString(
                "settlement_rare_chain_coin_resource"));
            if (coinSprite == null)
            {
                Debug.LogWarning("[CatCafe] Rare chain coin burst disabled: configured coin sprite is missing.");
            }

            initialized = true;
        }

        public IEnumerator Play(Vector2 sourcePosition, float settlementSeconds)
        {
            if (!initialized || coordinateRoot == null || coinSprite == null)
                yield break;

            int count = Mathf.Max(1, CatCafeConfigDatabase.GetRequiredInt(
                "settlement_rare_chain_coin_count"));
            float lifetime = Mathf.Max(0.01f, settlementSeconds *
                CatCafeConfigDatabase.GetRequiredFloat(
                    "settlement_rare_chain_coin_lifetime_multiplier") +
                CatCafeConfigDatabase.GetRequiredFloat(
                    "settlement_rare_chain_coin_lifetime_add_seconds"));
            float minScale = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_min_scale");
            float maxScale = Mathf.Max(minScale, CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_max_scale"));
            float minSpeed = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_min_speed");
            float maxSpeed = Mathf.Max(minSpeed, CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_max_speed"));
            float distanceMultiplier = Mathf.Max(0f, CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_distance_multiplier"));
            float minGravity = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_gravity_min");
            float maxGravity = Mathf.Max(minGravity, CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_gravity_max"));
            float minRotation = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_rotation_min");
            float maxRotation = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_rotation_max");
            float baseSize = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_base_size");
            float fadeStagger = Mathf.Clamp01(CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_fade_stagger"));
            float fadeCenter = Mathf.Clamp01(CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_fade_center"));
            float angleJitter = Mathf.Max(0f, CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_rare_chain_coin_angle_jitter"));

            EnsureCoinCount(count);
            coordinateRoot.SetAsLastSibling();

            float firstFade = Mathf.Clamp01(fadeCenter - fadeStagger * 0.5f);
            float lastFade = Mathf.Clamp01(fadeCenter + fadeStagger * 0.5f);
            List<int> playVersions = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                BurstCoin coin = coins[i];
                int version = ++coin.Version;
                playVersions.Add(version);
                float sequence = count == 1 ? 0.5f : (float)i / (count - 1);
                float angle = Mathf.PI * 2f * i / count +
                    Random.Range(-angleJitter, angleJitter);
                Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) *
                    Random.Range(minSpeed, maxSpeed) * distanceMultiplier;
                float fadeStart = Mathf.Lerp(firstFade, lastFade, sequence);

                coin.Rect.anchoredPosition = sourcePosition;
                coin.Rect.sizeDelta = Vector2.one * baseSize *
                    Random.Range(minScale, maxScale);
                coin.Rect.localRotation = Quaternion.Euler(
                    0f, 0f, Random.Range(0f, Mathf.PI * 2f) * Mathf.Rad2Deg);
                coin.Image.color = Color.white;
                coin.Group.alpha = 1f;
                coin.Root.SetActive(true);
                coin.Root.transform.SetAsLastSibling();

                StartCoroutine(AnimateCoin(
                    coin,
                    version,
                    sourcePosition,
                    velocity,
                    Random.Range(minGravity, maxGravity),
                    Random.Range(minRotation, maxRotation),
                    lifetime,
                    fadeStart));
            }

            yield return new WaitForSecondsRealtime(lifetime + 0.08f);
            for (int i = 0; i < count; i++)
            {
                BurstCoin coin = coins[i];
                if (coin.Root == null) continue;
                if (coin.Version != playVersions[i]) continue;
                coin.Version++;
                coin.Group.alpha = 0f;
                coin.Root.SetActive(false);
            }
        }

        private IEnumerator AnimateCoin(
            BurstCoin coin,
            int version,
            Vector2 startPosition,
            Vector2 velocity,
            float gravity,
            float rotationSpeed,
            float lifetime,
            float fadeStart)
        {
            float elapsed = 0f;
            while (elapsed < lifetime &&
                coin.Root != null &&
                coin.Root.activeSelf &&
                coin.Version == version)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / lifetime);
                coin.Rect.anchoredPosition = startPosition +
                    velocity * elapsed +
                    Vector2.down * (gravity * elapsed * elapsed * 0.5f);
                coin.Rect.Rotate(
                    Vector3.forward,
                    rotationSpeed * Mathf.Rad2Deg * Time.unscaledDeltaTime);
                coin.Group.alpha = t <= fadeStart
                    ? 1f
                    : 1f - Mathf.InverseLerp(fadeStart, 1f, t);
                yield return null;
            }

            if (coin.Root != null && coin.Version == version)
            {
                coin.Group.alpha = 0f;
                coin.Root.SetActive(false);
            }
        }

        private void EnsureCoinCount(int count)
        {
            while (coins.Count < count)
            {
                GameObject root = new GameObject(
                    "Rare Chain Coin",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(CanvasGroup));
                root.transform.SetParent(coordinateRoot, false);

                RectTransform rect = root.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                Image image = root.GetComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.sprite = coinSprite;

                CanvasGroup group = root.GetComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;
                group.alpha = 0f;

                root.SetActive(false);
                coins.Add(new BurstCoin
                {
                    Root = root,
                    Rect = rect,
                    Image = image,
                    Group = group,
                });
            }
        }
    }
}
