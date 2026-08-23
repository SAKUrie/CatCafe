using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Coordinates transient interaction feedback without owning gameplay rules.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeInteractionFeedback : MonoBehaviour
    {
        private CatCafeAudioFeedback audioFeedback;
        private Color toastBaseColor = Color.white;
        private CatCafeRewardFx rewardFx;
        private TMP_Text toastText;
        private Coroutine toastRoutine;
        private bool initialized;

        public void Initialize(
            Canvas canvas,
            RectTransform moneyHudRect,
            RectTransform moneyTarget,
            TMP_Text moneyValue,
            TMP_FontAsset font,
            TMP_Text toast)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));

            toastText = toast;
            rewardFx = canvas.GetComponent<CatCafeRewardFx>();
            if (rewardFx == null)
            {
                rewardFx = canvas.gameObject.AddComponent<CatCafeRewardFx>();
            }

            rewardFx.Initialize(canvas, moneyHudRect, moneyTarget, moneyValue, font);

            audioFeedback = GetComponent<CatCafeAudioFeedback>();
            if (audioFeedback == null)
            {
                audioFeedback = gameObject.AddComponent<CatCafeAudioFeedback>();
            }

            audioFeedback.Initialize();
            toastBaseColor = toastText == null ? Color.white : toastText.color;
            initialized = true;
        }

        public void RegisterButtons(IEnumerable<Button> buttons)
        {
            if (buttons == null) return;

            foreach (Button button in buttons)
            {
                if (button == null) continue;

                CatCafeButtonFeedback feedback = button.GetComponent<CatCafeButtonFeedback>();
                if (feedback == null)
                {
                    feedback = button.gameObject.AddComponent<CatCafeButtonFeedback>();
                }

                feedback.Initialize();
            }
        }

        public void ShowToast(string message)
        {
            if (!initialized || toastText == null) return;

            if (audioFeedback != null) audioFeedback.PlayGuidance();
            if (toastRoutine != null)
            {
                StopCoroutine(toastRoutine);
            }

            toastRoutine = StartCoroutine(ShowGuidance(message));
        }

        public IEnumerator PlayReward(RectTransform source, int amount, Action<int> onCoinArrived)
        {
            yield return PlayReward(source, amount, null, onCoinArrived);
        }

        public IEnumerator PlayReward(RectTransform source, int amount, string sourceLabel,
            Action<int> onCoinArrived)
        {
            if (amount > 0 && audioFeedback != null) audioFeedback.PlayReward();

            if (rewardFx == null)
            {
                if (onCoinArrived != null) onCoinArrived(amount);
                yield break;
            }

            yield return rewardFx.PlayReward(source, amount, sourceLabel, onCoinArrived);
        }

        public IEnumerator PlayReward(Vector2 sourcePosition, int amount, Action<int> onCoinArrived)
        {
            if (amount > 0 && audioFeedback != null) audioFeedback.PlayReward();

            if (rewardFx == null)
            {
                if (onCoinArrived != null) onCoinArrived(amount);
                yield break;
            }

            yield return rewardFx.PlayReward(sourcePosition, amount, onCoinArrived);
        }

        public void PlayRareChainCoinBurst(
            Vector2 sourcePosition, float settlementSeconds)
        {
            if (rewardFx == null) return;
            rewardFx.PlayRareChainCoinBurst(sourcePosition, settlementSeconds);
        }

        /// <summary>原地飘一条提示文字，不飞落点。用于券这类没有常驻显示位的收获。</summary>
        public void PlayFloatingNote(Vector2 position, string text, Color color)
        {
            if (rewardFx == null) return;
            rewardFx.PlayFloatingNote(position, text, color);
        }

        public void PlayFurReward(RectTransform source, int amount, string text, Color color)
        {
            if (rewardFx == null) return;
            rewardFx.PlayFurReward(source, amount, text, color);
        }

        public Vector2 GetFxPosition(RectTransform source)
        {
            return rewardFx == null ? Vector2.zero : rewardFx.GetFxPosition(source);
        }

        public Vector2 GetRewardSourcePosition(RectTransform source)
        {
            return rewardFx == null ? GetFxPosition(source) : rewardFx.GetRewardSourcePosition(source);
        }


        public void PlayRollStart()
        {
            if (audioFeedback != null) audioFeedback.PlayRollStart();
        }

        public void PlayRollStop()
        {
            if (audioFeedback != null) audioFeedback.PlayRollStop();
        }

        private IEnumerator ShowGuidance(string message)
        {
            toastText.text = message;
            float elapsed = 0f;
            while (elapsed < 0.16f)
            {
                elapsed += Time.unscaledDeltaTime;
                SetGuidanceAlpha(Mathf.Clamp01(elapsed / 0.16f));
                yield return null;
            }

            SetGuidanceAlpha(1f);
            yield return new WaitForSecondsRealtime(1.8f);

            elapsed = 0f;
            while (elapsed < 0.24f)
            {
                elapsed += Time.unscaledDeltaTime;
                SetGuidanceAlpha(1f - Mathf.Clamp01(elapsed / 0.24f));
                yield return null;
            }

            SetGuidanceAlpha(0f);
            toastText.text = string.Empty;
            toastRoutine = null;
        }

        private void SetGuidanceAlpha(float alpha)
        {
            if (toastText != null)
            {
                toastText.color = new Color(
                    toastBaseColor.r, toastBaseColor.g, toastBaseColor.b,
                    toastBaseColor.a * alpha);
            }
        }

        private void OnDisable()
        {
            if (toastRoutine != null)
            {
                StopCoroutine(toastRoutine);
                toastRoutine = null;
            }

            SetGuidanceAlpha(0f);
            if (toastText != null) toastText.text = string.Empty;
        }



        private IEnumerator ClearToast()
        {
            yield return new WaitForSecondsRealtime(2f);

            if (toastText != null)
            {
                toastText.text = string.Empty;
            }

            toastRoutine = null;
        }
    }
}
