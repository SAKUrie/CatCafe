using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    [DisallowMultipleComponent]
    public sealed class CatCafeRewardFx : MonoBehaviour
    {
        private sealed class TrailView
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Image;
        }

        private sealed class CoinView
        {
            public GameObject Root;
            public RectTransform Rect;
            public List<TrailView> Trails;
            public Image Highlight;
            public CanvasGroup Group;
            public bool InUse;
        }

        private sealed class FurView
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Image;
            public CanvasGroup Group;
            public bool InUse;
        }

        private sealed class LabelView
        {
            public GameObject Root;
            public RectTransform Rect;
            public CanvasGroup Group;
            public TMP_Text Value;
            public bool InUse;
        }

        private readonly List<CoinView> coinPool = new List<CoinView>();
        private readonly List<FurView> furPool = new List<FurView>();
        private readonly List<LabelView> labelPool = new List<LabelView>();

        /// <summary>
        /// 纯文字飘条，没有金币图标也没有光晕。券之类"没有常驻显示位、飞过去无处可落"的
        /// 收获就用它在原地交代一句。刻意不复用 <see cref="LabelView"/>：那套自带金币贴图，
        /// 用在券上会在文字旁边挂一枚金币。
        /// </summary>
        private sealed class NoteView
        {
            public GameObject Root;
            public RectTransform Rect;
            public CanvasGroup Group;
            public TMP_Text Value;
            public bool InUse;
        }

        private readonly List<NoteView> notePool = new List<NoteView>();
        private readonly System.Random visualRandom = new System.Random(2417);

        private Canvas ownerCanvas;
        private RectTransform fxRoot;
        private RectTransform moneyHud;
        private RectTransform moneyTarget;
        private TMP_Text moneyValue;
        private TMP_FontAsset uiFont;
        private Color moneyValueBaseColor;
        private Vector3 moneyHudBaseScale;
        private Vector3 moneyValueBaseScale = Vector3.one;
        private Coroutine moneyHudPulse;
        private Texture2D coinTexture;
        private Sprite coinSprite;
        private Sprite furSprite;
        private CatCafeRareChainCoinBurst rareChainCoinBurst;
        private bool coinSpriteIsRuntimeGenerated;
        private bool initialized;
        private float RewardSpeedMultiplier
        {
            get
            {
                return CatCafeUserSettings.ScaleSpeed(
                    CatCafeConfigDatabase.GetFloat("reward_fx_speed_multiplier", 1.5f));
            }
        }


        public void Initialize(Canvas canvas, RectTransform hud, RectTransform target, TMP_Text value, TMP_FontAsset font)
        {
            if (initialized) return;
            ownerCanvas = canvas;
            moneyHud = hud;
            moneyTarget = target;
            moneyValue = value;
            uiFont = font;
            moneyValueBaseColor = value == null ? Color.white : value.color;
            moneyHudBaseScale = hud == null ? Vector3.one : hud.localScale;
            moneyValueBaseScale = value == null ? Vector3.one : value.rectTransform.localScale;

            GameObject layer = CreateUiObject("RewardFxLayer", canvas.transform);
            fxRoot = layer.GetComponent<RectTransform>();
            Stretch(fxRoot);
            fxRoot.SetAsLastSibling();
            EnsureCoinSprite();
            furSprite = Resources.Load<Sprite>(
                CatCafeConfigDatabase.GetRequiredString("ui_fur_fx_resource"));
            if (furSprite == null)
                Debug.LogError("[CatCafe] 绒毛特效资源不存在：" +
                    CatCafeConfigDatabase.GetRequiredString("ui_fur_fx_resource"));

            GameObject rareChainBurstObject = CreateUiObject(
                "Rare Chain Coin Burst", fxRoot);
            rareChainCoinBurst = rareChainBurstObject.AddComponent<CatCafeRareChainCoinBurst>();
            rareChainCoinBurst.Initialize(fxRoot);

            for (int i = 0; i < 10; i++) ReleaseCoin(CreateCoin());
            for (int i = 0; i < 5; i++) ReleaseLabel(CreateLabel());
            initialized = true;
        }

        /// <summary>从掉落猫咪身上弹出绒毛贴纸并飘散淡出，同时显示本次获得数量。</summary>
        public void PlayFurReward(RectTransform source, int amount, string text, Color textColor)
        {
            if (!initialized || fxRoot == null || source == null || amount <= 0) return;

            Vector2 position = GetRewardSourcePosition(source);
            PlayFloatingNote(position, text, textColor);
            StartCoroutine(AnimateSourcePunch(source));
            if (furSprite == null) return;

            int count = CatCafeConfigDatabase.GetRequiredInt("ui_fur_fx_particle_count");
            float stagger = CatCafeConfigDatabase.GetRequiredFloat("ui_fur_fx_stagger_seconds");
            for (int i = 0; i < count; i++)
            {
                FurView fur = AcquireFur();
                StartCoroutine(AnimateFlyingFur(fur, GetFxPosition(source),
                    i * stagger / RewardSpeedMultiplier));
            }
        }

        private IEnumerator AnimateFlyingFur(FurView fur, Vector2 source, float delay)
        {
            fur.Group.alpha = 0f;
            fur.Rect.anchoredPosition = source;
            fur.Rect.localScale = Vector3.one * 0.45f;
            fur.Rect.localRotation = Quaternion.Euler(0f, 0f, RandomRange(-18f, 18f));
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            float burstX = CatCafeConfigDatabase.GetRequiredFloat("ui_fur_fx_burst_x");
            float riseMin = CatCafeConfigDatabase.GetRequiredFloat("ui_fur_fx_rise_min");
            float riseMax = CatCafeConfigDatabase.GetRequiredFloat("ui_fur_fx_rise_max");
            float drift = CatCafeConfigDatabase.GetRequiredFloat("ui_fur_fx_drift");
            float rotation = CatCafeConfigDatabase.GetRequiredFloat("ui_fur_fx_rotation_degrees");
            float duration = CatCafeConfigDatabase.GetRequiredFloat("ui_fur_fx_duration_seconds") /
                RewardSpeedMultiplier;
            Vector2 burstEnd = source + new Vector2(
                RandomRange(-burstX, burstX), RandomRange(riseMin, riseMax));
            Vector2 control = Vector2.Lerp(source, burstEnd, 0.55f) +
                new Vector2(RandomRange(-drift, drift), RandomRange(12f, riseMin));
            float spin = RandomRange(-rotation, rotation);

            float elapsed = 0f;
            while (elapsed < duration && fur.Root != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 2f);
                fur.Rect.anchoredPosition = QuadraticBezier(source, control, burstEnd, eased);
                fur.Rect.localScale = Vector3.one *
                    (Mathf.Lerp(0.45f, 1f, Mathf.Min(1f, t * 4f)) * Mathf.Lerp(1f, 0.62f, t));
                fur.Rect.localRotation = Quaternion.Euler(0f, 0f, spin * t);
                fur.Group.alpha = t < 0.18f ? Mathf.InverseLerp(0f, 0.18f, t) :
                    1f - Mathf.InverseLerp(0.58f, 1f, t);
                yield return null;
            }

            ReleaseFur(fur);
        }

        private FurView AcquireFur()
        {
            for (int i = 0; i < furPool.Count; i++)
            {
                if (furPool[i].InUse) continue;
                furPool[i].InUse = true;
                furPool[i].Root.SetActive(true);
                furPool[i].Root.transform.SetAsLastSibling();
                return furPool[i];
            }

            GameObject root = CreateUiObject("FlyingFur", fxRoot);
            RectTransform rect = root.GetComponent<RectTransform>();
            float size = CatCafeConfigDatabase.GetRequiredFloat("ui_fur_fx_size");
            SetCenteredRect(rect, Vector2.zero, new Vector2(size, size));
            Image image = root.AddComponent<Image>();
            image.sprite = furSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            FurView created = new FurView
            {
                Root = root, Rect = rect, Image = image, Group = group, InUse = true
            };
            furPool.Add(created);
            return created;
        }

        private static void ReleaseFur(FurView fur)
        {
            fur.InUse = false;
            fur.Group.alpha = 0f;
            fur.Rect.localScale = Vector3.one;
            fur.Rect.localRotation = Quaternion.identity;
            fur.Root.SetActive(false);
        }

        public void PlayRareChainCoinBurst(
            Vector2 sourcePosition, float settlementSeconds)
        {
            if (rareChainCoinBurst == null) return;
            StartCoroutine(rareChainCoinBurst.Play(sourcePosition, settlementSeconds));
        }

        public Vector2 GetFxPosition(RectTransform source)
        {
            if (source == null || fxRoot == null) return Vector2.zero;
            Canvas.ForceUpdateCanvases();
            Camera camera = ownerCanvas != null && ownerCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? ownerCanvas.worldCamera
                : null;
            Vector3 world = source.TransformPoint(source.rect.center);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, world);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(fxRoot, screen, camera, out local);
            return local;
        }

        public Vector2 GetRewardSourcePosition(RectTransform source)
        {
            Vector2 position = GetFxPosition(source);
            if (source == null) return position;

            float labelOffsetY = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_offset_y");
            float labelHeight = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_height");
            const float headGap = 8f;
            return position + Vector2.up * (
                source.rect.height * 0.5f + labelHeight * 0.5f + headGap - labelOffsetY);
        }


        /// <summary>
        /// 在 <paramref name="position"/> 原地飘一条提示文字：弹出 → 停一拍 → 上浮淡出。
        /// 不飞向任何落点，节奏与金币的 +N 飘字一致，只是去掉了金币图标。
        /// </summary>
        public void PlayFloatingNote(Vector2 position, string text, Color color)
        {
            if (!initialized || fxRoot == null || string.IsNullOrEmpty(text)) return;
            NoteView note = AcquireNote();
            note.Value.text = text;
            note.Value.color = color;
            note.Rect.anchoredPosition = position;
            StartCoroutine(AnimateNote(note));
        }

        private IEnumerator AnimateNote(NoteView note)
        {
            Vector2 start = note.Rect.anchoredPosition;
            float popRise = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_pop_rise");
            float fadeRise = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_fade_rise");
            note.Group.alpha = 0f;
            note.Rect.localScale = Vector3.one * 0.56f;

            float elapsed = 0f;
            float popDuration = 0.17f / RewardSpeedMultiplier;
            while (elapsed < popDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / popDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                note.Rect.anchoredPosition = start + Vector2.up * (popRise * eased);
                note.Rect.localScale = Vector3.one * (Mathf.Lerp(0.56f, 1f, eased) + Mathf.Sin(t * Mathf.PI) * 0.20f);
                note.Group.alpha = t;
                yield return null;
            }

            // 券不像金币那样有个落点收尾，停留稍长一点，让玩家来得及读完这行字。
            yield return new WaitForSecondsRealtime(
                CatCafeConfigDatabase.GetFloat("ui_note_hold_seconds", 0.42f) / RewardSpeedMultiplier);

            Vector2 fadeStart = note.Rect.anchoredPosition;
            elapsed = 0f;
            float fadeDuration = CatCafeConfigDatabase.GetFloat("ui_note_fade_seconds", 0.34f) / RewardSpeedMultiplier;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                note.Rect.anchoredPosition = fadeStart + Vector2.up * (fadeRise * t);
                note.Rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.84f, t);
                note.Group.alpha = 1f - t;
                yield return null;
            }

            ReleaseNote(note);
        }

        private NoteView AcquireNote()
        {
            for (int i = 0; i < notePool.Count; i++)
            {
                if (notePool[i].InUse) continue;
                NoteView existing = notePool[i];
                existing.InUse = true;
                existing.Root.SetActive(true);
                existing.Root.transform.SetAsLastSibling();
                return existing;
            }

            NoteView created = CreateNote();
            created.InUse = true;
            created.Root.SetActive(true);
            created.Root.transform.SetAsLastSibling();
            return created;
        }

        private NoteView CreateNote()
        {
            GameObject root = CreateUiObject("FloatingNote", fxRoot);
            RectTransform rect = root.GetComponent<RectTransform>();
            SetCenteredRect(rect, Vector2.zero, new Vector2(
                CatCafeConfigDatabase.GetFloat("ui_note_width", 340f),
                CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_height")));
            CanvasGroup group = root.AddComponent<CanvasGroup>();

            TMP_Text value = CreateText(string.Empty, root.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_reward_label_font_size"),
                Color.white, TextAnchor.MiddleCenter);
            value.fontStyle = FontStyles.Bold;
            Outline outline = value.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.18f, 0.08f, 0.02f, 0.96f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            SetCenteredRect(value.rectTransform, Vector2.zero, new Vector2(
                CatCafeConfigDatabase.GetFloat("ui_note_width", 340f),
                CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_height")));

            NoteView view = new NoteView { Root = root, Rect = rect, Group = group, Value = value };
            notePool.Add(view);
            return view;
        }

        private void ReleaseNote(NoteView note)
        {
            note.InUse = false;
            note.Group.alpha = 0f;
            note.Rect.localScale = Vector3.one;
            note.Root.SetActive(false);
        }

        public IEnumerator PlayReward(RectTransform source, int amount, Action<int> onCoinArrived)
        {
            yield return PlayReward(source, amount, null, onCoinArrived);
        }

        public IEnumerator PlayReward(RectTransform source, int amount, string sourceLabel,
            Action<int> onCoinArrived)
        {
            yield return PlayRewardInternal(
                GetRewardSourcePosition(source), source, amount, sourceLabel, onCoinArrived);
        }

        public IEnumerator PlayReward(Vector2 sourcePosition, int amount, Action<int> onCoinArrived)
        {
            yield return PlayRewardInternal(sourcePosition, null, amount, null, onCoinArrived);
        }

        private IEnumerator PlayRewardInternal(Vector2 sourcePosition, RectTransform source,
            int amount, string sourceLabel, Action<int> onCoinArrived)
        {
            if (amount <= 0) yield break;
            if (!initialized || fxRoot == null || moneyTarget == null)
            {
                if (onCoinArrived != null) onCoinArrived(amount);
                yield break;
            }

            Canvas.ForceUpdateCanvases();
            Vector2 targetPosition = GetFxPosition(moneyTarget);
            LabelView label = AcquireLabel();
            label.Value.text = string.Format(
                CatCafeConfigDatabase.GetRequiredString("ui_reward_amount_format"), amount);
            float labelOffsetY = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_offset_y");
            float coinX = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_coin_x");
            // The coin icon is the visual anchor: place it over the source center, then lay out +N to its right.
            Vector2 settlementPosition = sourcePosition + new Vector2(-coinX, labelOffsetY);
            Vector2 coinStartPosition = settlementPosition + new Vector2(coinX, 0f);
            label.Rect.anchoredPosition = settlementPosition;
            StartCoroutine(AnimateLabel(label));

            if (source != null) StartCoroutine(AnimateSourcePunch(source));
            yield return new WaitForSecondsRealtime(0.09f / RewardSpeedMultiplier);

            int coinCount = Mathf.Clamp(amount, 1, 6);
            int baseValue = amount / coinCount;
            int remainder = amount % coinCount;
            int pendingCoins = coinCount;

            for (int i = 0; i < coinCount; i++)
            {
                int coinValue = baseValue + (i < remainder ? 1 : 0);
                CoinView coin = AcquireCoin();
                float delay = i * 0.038f / RewardSpeedMultiplier;
                Vector2 burstOffset = new Vector2(RandomRange(-44f, 44f), RandomRange(52f, 82f));
                float arcSide = RandomRange(-82f, 82f);
                float flightDuration = RandomRange(0.34f, 0.42f) / RewardSpeedMultiplier;

                StartCoroutine(AnimateFlyingCoin(
                    coin, coinStartPosition, burstOffset, targetPosition, arcSide, delay, flightDuration,
                    delegate
                    {
                        if (onCoinArrived != null) onCoinArrived(coinValue);
                        StartMoneyHudHit();
                        pendingCoins -= 1;
                    }));
            }

            while (pendingCoins > 0) yield return null;
            yield return new WaitForSecondsRealtime(0.06f / RewardSpeedMultiplier);
        }

private IEnumerator AnimateFlyingCoin(CoinView coin, Vector2 source, Vector2 burstOffset,
            Vector2 target, float arcSide, float delay, float flightDuration, Action onArrived)
        {
            coin.Group.alpha = 1f;
            coin.Rect.anchoredPosition = source;
            coin.Rect.localScale = Vector3.one * 0.66f;
            coin.Rect.localRotation = Quaternion.identity;
            if (coin.Highlight != null)
            {
                coin.Highlight.color = new Color(1f, 0.98f, 0.76f, 0f);
                coin.Highlight.rectTransform.localRotation = Quaternion.identity;
            }
            ResetTrail(coin);
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            Vector2 burstEnd = source + burstOffset;
            float elapsed = 0f;
            float burstDuration = 0.105f / RewardSpeedMultiplier;
            while (elapsed < burstDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / burstDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                Vector2 previous = coin.Rect.anchoredPosition;
                coin.Rect.anchoredPosition = Vector2.LerpUnclamped(source, burstEnd, eased);
                UpdateTrail(coin, coin.Rect.anchoredPosition,
                    coin.Rect.anchoredPosition - previous, Mathf.Lerp(0f, 0.34f, t));
                coin.Rect.localScale = Vector3.one * Mathf.Lerp(0.66f, 1.06f, eased);
                coin.Rect.Rotate(0f, 0f, 260f * Time.unscaledDeltaTime);
                UpdateCoinHighlight(coin, t, 0.32f);
                yield return null;
            }

            Vector2 control = Vector2.Lerp(burstEnd, target, 0.48f) +
                new Vector2(arcSide, 96f + Mathf.Abs(arcSide) * 0.20f);
            elapsed = 0f;
            while (elapsed < flightDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / flightDuration);
                float magneticT = Mathf.Pow(t, 1.68f);
                Vector2 previous = coin.Rect.anchoredPosition;
                coin.Rect.anchoredPosition = QuadraticBezier(burstEnd, control, target, magneticT);
                UpdateTrail(coin, coin.Rect.anchoredPosition,
                    coin.Rect.anchoredPosition - previous, Mathf.Lerp(0.30f, 0.68f, t));
                float pulse = 1f + Mathf.Sin(t * Mathf.PI * 5f) * 0.075f * (1f - t);
                coin.Rect.localScale = Vector3.one * Mathf.Lerp(1.06f, 0.64f, t) * pulse;
                coin.Rect.Rotate(0f, 0f, Mathf.Lerp(340f, 900f, t) * Time.unscaledDeltaTime);
                UpdateCoinHighlight(coin, t, Mathf.Lerp(0.52f, 0.84f, t));
                if (t > 0.90f) coin.Group.alpha = 1f - Mathf.InverseLerp(0.90f, 1f, t);
                yield return null;
            }

            coin.Rect.anchoredPosition = target;
            if (onArrived != null) onArrived();
            ReleaseCoin(coin);
        }

private void UpdateCoinHighlight(CoinView coin, float normalizedTime, float alpha)
        {
            if (coin.Highlight == null) return;

            float shimmer = 0.5f + 0.5f *
                Mathf.Sin((normalizedTime * 2.4f + Time.unscaledTime * 0.9f) * Mathf.PI);
            RectTransform rect = coin.Highlight.rectTransform;
            rect.localRotation = Quaternion.Euler(
                0f, 0f, Mathf.Lerp(-18f, 32f, normalizedTime) +
                shimmer * 18f);
            rect.anchoredPosition = new Vector2(
                -8f + shimmer * 3f, 9f + Mathf.Sin(normalizedTime * Mathf.PI) * 2f);
            Color color = coin.Highlight.color;
            color.a = alpha * (0.28f + shimmer * 0.34f);
            coin.Highlight.color = color;
        }


        private IEnumerator AnimateLabel(LabelView label)
        {
            Vector2 start = label.Rect.anchoredPosition;
            float popRise = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_pop_rise");
            float fadeRise = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_fade_rise");
            label.Group.alpha = 0f;
            label.Rect.localScale = Vector3.one * 0.56f;
            float elapsed = 0f;
            float popDuration = 0.17f / RewardSpeedMultiplier;
            while (elapsed < popDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / popDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                float scale = Mathf.Lerp(0.56f, 1f, eased) + Mathf.Sin(t * Mathf.PI) * 0.20f;
                label.Rect.anchoredPosition = start + Vector2.up * (popRise * eased);
                label.Rect.localScale = Vector3.one * scale;
                label.Group.alpha = t;
                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.17f / RewardSpeedMultiplier);
            Vector2 fadeStart = label.Rect.anchoredPosition;
            elapsed = 0f;
            float fadeDuration = 0.23f / RewardSpeedMultiplier;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                label.Rect.anchoredPosition = fadeStart + Vector2.up * (fadeRise * t);
                label.Rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.84f, t);
                label.Group.alpha = 1f - t;
                yield return null;
            }

            ReleaseLabel(label);
        }

        private IEnumerator AnimateSourcePunch(RectTransform source)
        {
            if (source == null) yield break;
            Vector3 originalScale = source.localScale;
            float elapsed = 0f;
            float duration = 0.30f / RewardSpeedMultiplier;
            while (elapsed < duration && source != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float spring = Mathf.Exp(-4.6f * t) * Mathf.Sin(t * Mathf.PI * 4.4f);
                source.localScale = originalScale * (1f + spring * 0.18f);
                yield return null;
            }
            if (source != null) source.localScale = originalScale;
        }

        private void StartMoneyHudHit()
        {
            if (moneyHud == null) return;
            if (moneyHudPulse != null) StopCoroutine(moneyHudPulse);
            moneyHudPulse = StartCoroutine(AnimateMoneyHudHit());
        }

private IEnumerator AnimateMoneyHudHit()
        {
            if (moneyHud == null) yield break;
            moneyHud.localScale = moneyHudBaseScale * 1.15f;
            if (moneyValue != null)
            {
                moneyValue.rectTransform.localScale = moneyValueBaseScale * 1.18f;
                moneyValue.color = new Color(1f, 0.57f, 0.08f, 1f);
            }

            float elapsed = 0f;
            float duration = 0.20f / RewardSpeedMultiplier;
            while (elapsed < duration && moneyHud != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float spring = Mathf.Exp(-5.2f * t) *
                    Mathf.Cos(t * Mathf.PI * 3.2f);
                moneyHud.localScale = moneyHudBaseScale * (1f + 0.15f * spring);
                if (moneyValue != null)
                {
                    moneyValue.rectTransform.localScale =
                        moneyValueBaseScale * (1f + 0.20f * spring);
                    moneyValue.color = Color.Lerp(
                        new Color(1f, 0.57f, 0.08f, 1f), moneyValueBaseColor, t);
                }
                yield return null;
            }

            if (moneyHud != null) moneyHud.localScale = moneyHudBaseScale;
            if (moneyValue != null)
            {
                moneyValue.rectTransform.localScale = moneyValueBaseScale;
                moneyValue.color = moneyValueBaseColor;
            }
            moneyHudPulse = null;
        }









        private CoinView AcquireCoin()
        {
            for (int i = 0; i < coinPool.Count; i++)
            {
                if (coinPool[i].InUse) continue;

                CoinView view = coinPool[i];
                view.InUse = true;
                view.Root.SetActive(true);
                SetTrailActive(view, true);
                view.Root.transform.SetAsLastSibling();
                return view;
            }

            CoinView created = CreateCoin();
            created.InUse = true;
            created.Root.SetActive(true);
            SetTrailActive(created, true);
            created.Root.transform.SetAsLastSibling();
            return created;
        }

private CoinView CreateCoin()
        {
            GameObject root = CreateUiObject("FlyingCoin", fxRoot);
            RectTransform rect = root.GetComponent<RectTransform>();
            SetCenteredRect(rect, Vector2.zero, new Vector2(40f, 40f));
            CanvasGroup group = root.AddComponent<CanvasGroup>();

            List<TrailView> trails = new List<TrailView>(4);
            for (int i = 0; i < 4; i++)
            {
                GameObject trailObject = CreateUiObject("Coin Trail " + i, fxRoot);
                RectTransform trailRect = trailObject.GetComponent<RectTransform>();
                SetCenteredRect(trailRect, Vector2.zero, new Vector2(22f, 22f));
                Image trailImage = trailObject.AddComponent<Image>();
                trailImage.sprite = coinSprite;
                trailImage.color = Color.clear;
                trailImage.raycastTarget = false;
                trailObject.SetActive(false);

                trails.Add(new TrailView
                {
                    Root = trailObject,
                    Rect = trailRect,
                    Image = trailImage
                });
            }

            GameObject faceObject = CreateUiObject("Face", root.transform);
            RectTransform faceRect = faceObject.GetComponent<RectTransform>();
            SetCenteredRect(faceRect, Vector2.zero, new Vector2(36f, 36f));
            Image face = faceObject.AddComponent<Image>();
            face.sprite = coinSprite;
            face.raycastTarget = false;

            GameObject highlightObject = CreateUiObject("Rotating Highlight", faceObject.transform);
            RectTransform highlightRect = highlightObject.GetComponent<RectTransform>();
            SetCenteredRect(highlightRect, new Vector2(-8f, 9f), new Vector2(15f, 7f));
            Image highlight = highlightObject.AddComponent<Image>();
            highlight.sprite = coinSprite;
            highlight.color = new Color(1f, 0.98f, 0.76f, 0f);
            highlight.raycastTarget = false;

            CoinView view = new CoinView
            {
                Root = root,
                Rect = rect,
                Trails = trails,
                Highlight = highlight,
                Group = group
            };
            coinPool.Add(view);
            return view;
        }

private void ReleaseCoin(CoinView coin)
        {
            coin.InUse = false;
            coin.Group.alpha = 0f;
            coin.Rect.localScale = Vector3.one;
            coin.Rect.localRotation = Quaternion.identity;
            if (coin.Highlight != null)
            {
                coin.Highlight.color = new Color(1f, 0.98f, 0.76f, 0f);
                coin.Highlight.rectTransform.localRotation = Quaternion.identity;
                coin.Highlight.rectTransform.anchoredPosition = new Vector2(-8f, 9f);
            }
            ResetTrail(coin);
            SetTrailActive(coin, false);
            coin.Root.SetActive(false);
        }

        private LabelView AcquireLabel()
        {
            for (int i = 0; i < labelPool.Count; i++)
            {
                if (labelPool[i].InUse) continue;
                LabelView view = labelPool[i];
                view.InUse = true;
                view.Root.SetActive(true);
                view.Root.transform.SetAsLastSibling();
                return view;
            }
            LabelView created = CreateLabel();
            created.InUse = true;
            created.Root.SetActive(true);
            return created;
        }

        private LabelView CreateLabel()
        {
            GameObject root = CreateUiObject("CoinRewardLabel", fxRoot);
            RectTransform rect = root.GetComponent<RectTransform>();
            SetCenteredRect(rect, Vector2.zero, new Vector2(
                CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_width"),
                CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_height")));
            CanvasGroup group = root.AddComponent<CanvasGroup>();

            float coinX = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_coin_x");
            GameObject glowObject = CreateUiObject("Glow", root.transform);
            RectTransform glowRect = glowObject.GetComponent<RectTransform>();
            float glowSize = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_glow_size");
            SetCenteredRect(glowRect, new Vector2(coinX, 0f), new Vector2(glowSize, glowSize));
            Image glow = glowObject.AddComponent<Image>();
            glow.sprite = coinSprite;
            glow.color = new Color(1f, 0.72f, 0.14f, 0.25f);
            glow.raycastTarget = false;

            GameObject coinObject = CreateUiObject("Coin", root.transform);
            RectTransform coinRect = coinObject.GetComponent<RectTransform>();
            float coinSize = CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_coin_size");
            SetCenteredRect(coinRect, new Vector2(coinX, 0f), new Vector2(coinSize, coinSize));
            Image coin = coinObject.AddComponent<Image>();
            coin.sprite = coinSprite;
            coin.raycastTarget = false;

            TMP_Text value = CreateText(string.Empty, root.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_reward_label_font_size"),
                new Color(1f, 0.80f, 0.20f, 1f), TextAnchor.MiddleLeft);
            value.fontStyle = FontStyles.Bold;
            Outline outline = value.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.18f, 0.08f, 0.02f, 0.96f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            SetCenteredRect(value.rectTransform,
                new Vector2(CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_value_x"), 0f),
                new Vector2(
                    CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_value_width"),
                    CatCafeConfigDatabase.GetRequiredFloat("ui_reward_label_height")));

            LabelView view = new LabelView
            {
                Root = root, Rect = rect, Group = group, Value = value
            };
            labelPool.Add(view);
            return view;
        }

        private void ReleaseLabel(LabelView label)
        {
            label.InUse = false;
            label.Group.alpha = 0f;
            label.Rect.localScale = Vector3.one;
            label.Root.SetActive(false);
        }













        private void UpdateTrail(
            CoinView coin,
            Vector2 position,
            Vector2 velocity,
            float alpha)
        {
            if (coin.Trails == null || coin.Trails.Count == 0) return;
            if (velocity.sqrMagnitude < 0.0001f)
            {
                ResetTrail(coin);
                return;
            }

            Vector2 direction = velocity.normalized;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            for (int i = 0; i < coin.Trails.Count; i++)
            {
                TrailView trail = coin.Trails[i];
                float normalized = i / Mathf.Max(1f, coin.Trails.Count - 1f);
                float distance = 10f + i * 11f + Mathf.Clamp(velocity.magnitude * 0.08f, 0f, 8f);
                float size = Mathf.Lerp(24f, 13f, normalized);
                float trailAlpha = alpha * Mathf.Lerp(0.52f, 0.12f, normalized);

                trail.Rect.anchoredPosition = position - direction * distance;
                trail.Rect.sizeDelta = new Vector2(size, size);
                trail.Rect.localScale = Vector3.one * Mathf.Lerp(0.96f, 0.72f, normalized);
                trail.Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
                trail.Image.color = new Color(1f, 0.72f, 0.14f, trailAlpha);
            }
        }

        private void ResetTrail(CoinView coin)
        {
            if (coin.Trails == null) return;

            for (int i = 0; i < coin.Trails.Count; i++)
            {
                TrailView trail = coin.Trails[i];
                trail.Image.color = Color.clear;
                trail.Rect.localScale = Vector3.one;
                trail.Rect.localRotation = Quaternion.identity;
            }
        }

        private void SetTrailActive(CoinView coin, bool active)
        {
            if (coin.Trails == null) return;

            for (int i = 0; i < coin.Trails.Count; i++)
            {
                TrailView trail = coin.Trails[i];
                if (trail.Root != null) trail.Root.SetActive(active);
            }
        }




        private float RandomRange(float min, float max)
        {
            return Mathf.Lerp(min, max, (float)visualRandom.NextDouble());
        }

        private void EnsureCoinSprite()
        {
            if (coinSprite != null) return;

            coinSprite = Resources.Load<Sprite>("CatCafe/InGameUI/coin");
            if (coinSprite != null)
            {
                coinSpriteIsRuntimeGenerated = false;
                return;
            }

            Debug.LogWarning(
                "[CatCafe] Missing coin UI sprite at Resources/CatCafe/InGameUI/coin. " +
                "Falling back to a generated coin for this session.");

            const int size = 64;
            coinTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            coinTexture.name = "Runtime Reward Coin";
            coinTexture.filterMode = FilterMode.Bilinear;
            coinTexture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.47f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 delta = new Vector2(x, y) - center;
                    float normalized = delta.magnitude / radius;
                    Color pixel = Color.clear;
                    if (normalized <= 1f)
                    {
                        if (normalized > 0.87f)
                        {
                            pixel = new Color(0.55f, 0.25f, 0.025f, 1f);
                        }
                        else if (normalized > 0.73f)
                        {
                            pixel = Color.Lerp(
                                new Color(1f, 0.80f, 0.17f, 1f),
                                new Color(0.82f, 0.42f, 0.035f, 1f),
                                Mathf.InverseLerp(0.73f, 0.87f, normalized));
                        }
                        else
                        {
                            float highlight = Mathf.Clamp01(1f -
                                Vector2.Distance(delta / radius, new Vector2(-0.28f, 0.32f)) / 1.25f);
                            pixel = Color.Lerp(
                                new Color(0.93f, 0.52f, 0.055f, 1f),
                                new Color(1f, 0.92f, 0.34f, 1f),
                                highlight);
                        }
                    }
                    pixels[y * size + x] = pixel;
                }
            }

            coinTexture.SetPixels(pixels);
            coinTexture.Apply(false, false);
            coinSprite = Sprite.Create(
                coinTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            coinSprite.name = "Runtime Reward Coin Sprite";
            coinSpriteIsRuntimeGenerated = true;
        }

        private TMP_Text CreateText(string text, Transform parent, int fontSize,
            Color color, TextAnchor alignment)
        {
            GameObject root = CreateUiObject("TMP_Text", parent);
            TMP_Text label = root.AddComponent<TextMeshProUGUI>();
            label.font = uiFont;
            label.fontSize = CatCafeUiFontProvider.ScaleSize(fontSize);
            label.color = color;
            label.alignment = ToTextAlignment(alignment);
            label.text = text;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

private static TextAlignmentOptions ToTextAlignment(TextAnchor alignment)
        {
            switch (alignment)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }


        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            return root;
        }

        private static void SetCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Vector2 QuadraticBezier(Vector2 start, Vector2 control, Vector2 end, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
        }

        private void OnDestroy()
        {
            if (moneyHud != null) moneyHud.localScale = moneyHudBaseScale;
            if (moneyValue != null)
            {
                moneyValue.rectTransform.localScale = moneyValueBaseScale;
                moneyValue.color = moneyValueBaseColor;
            }
            if (coinSpriteIsRuntimeGenerated && coinSprite != null) Destroy(coinSprite);
            if (coinTexture != null) Destroy(coinTexture);
        }
    }
}
