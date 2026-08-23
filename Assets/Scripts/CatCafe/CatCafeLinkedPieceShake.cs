using UnityEngine;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Plays the short, damped reaction used when a pawn is affected by
    /// another pawn's settlement interaction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeLinkedPieceShake : MonoBehaviour
    {
        private const float MaximumDurationSeconds = 0.30f;
        private const float MinimumDurationSeconds = 0.12f;
        private const float HorizontalAmplitude = 7f;
        private const float VerticalAmplitude = 2.2f;
        private const float RotationAmplitudeDegrees = 4f;
        private const float OscillationCount = 4.25f;

        private RectTransform target;
        private RectTransform companion;
        private Vector2 companionRestingAnchoredPosition;
        private Quaternion companionRestingLocalRotation;
        private bool companionPoseCaptured;

        private Vector2 restingAnchoredPosition;
        private Quaternion restingLocalRotation;
        private float duration;
        private float startTime;
        private bool poseCaptured;
        private bool playing;

public void Begin(
            float availableSeconds,
            RectTransform synchronizedGlow)
        {
            if (target == null)
            {
                target = transform as RectTransform;
            }

            if (target == null)
            {
                return;
            }

            StopImmediately();

            restingAnchoredPosition = target.anchoredPosition;
            restingLocalRotation = target.localRotation;
            poseCaptured = true;

            companion = synchronizedGlow;
            if (companion != null)
            {
                companionRestingAnchoredPosition =
                    companion.anchoredPosition;
                companionRestingLocalRotation =
                    companion.localRotation;
                companionPoseCaptured = true;
            }

            float preferredDuration = Mathf.Clamp(
                availableSeconds * 0.72f,
                MinimumDurationSeconds,
                MaximumDurationSeconds);
            duration = Mathf.Min(
                preferredDuration,
                Mathf.Max(0.01f, availableSeconds));
            startTime = Time.unscaledTime;
            playing = true;
        }

private void Update()
        {
            if (!playing || target == null)
            {
                return;
            }

            float normalizedTime =
                (Time.unscaledTime - startTime) / duration;
            if (normalizedTime >= 1f)
            {
                StopImmediately();
                return;
            }

            float decay = 1f - Smooth01(normalizedTime);
            float phase =
                normalizedTime * OscillationCount * Mathf.PI * 2f;
            float horizontal =
                Mathf.Sin(phase) * HorizontalAmplitude * decay;
            float vertical =
                Mathf.Sin(phase * 1.5f) * VerticalAmplitude * decay;
            float rotation =
                Mathf.Sin(phase * 0.9f) *
                RotationAmplitudeDegrees * decay;
            Vector2 offset = new Vector2(horizontal, vertical);
            Quaternion rotationOffset =
                Quaternion.Euler(0f, 0f, rotation);

            target.anchoredPosition =
                restingAnchoredPosition + offset;
            target.localRotation =
                restingLocalRotation * rotationOffset;

            if (companion != null && companionPoseCaptured)
            {
                companion.anchoredPosition =
                    companionRestingAnchoredPosition + offset;
                companion.localRotation =
                    companionRestingLocalRotation * rotationOffset;
            }
        }

        public void StopImmediately()
        {
            playing = false;
            RestorePose();
        }

private void RestorePose()
        {
            if (poseCaptured && target != null)
            {
                target.anchoredPosition = restingAnchoredPosition;
                target.localRotation = restingLocalRotation;
            }

            if (companionPoseCaptured && companion != null)
            {
                companion.anchoredPosition =
                    companionRestingAnchoredPosition;
                companion.localRotation =
                    companionRestingLocalRotation;
            }

            poseCaptured = false;
            companionPoseCaptured = false;
            companion = null;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private void OnDisable()
        {
            StopImmediately();
        }

        private void OnDestroy()
        {
            StopImmediately();
        }
    }
}
