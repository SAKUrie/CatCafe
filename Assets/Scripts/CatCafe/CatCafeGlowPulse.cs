using UnityEngine;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Owns the local settlement glow envelope: soft fade-in, breathing pulse,
    /// and a non-abrupt fade-out. The settlement controller only starts the
    /// animation; this component owns per-marker presentation state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeGlowPulse : MonoBehaviour
    {
        private static readonly int GlowStrengthId =
            Shader.PropertyToID("_GlowStrength");
        private static readonly int GlowRadiusId =
            Shader.PropertyToID("_GlowRadius");
        private static readonly int GlowSoftnessId =
            Shader.PropertyToID("_GlowSoftness");
        private static readonly int GlowEmissionId =
            Shader.PropertyToID("_GlowEmission");

        private const float AttackSeconds = 0.18f;
        private const float ReleaseSeconds = 0.24f;
        private const float BreathPeriodSeconds = 0.9f;
        private const float MinimumDurationSeconds = 0.01f;

        private Image glowImage;
        private Material glowMaterial;
        private CanvasGroup canvasGroup;
        private RectTransform markerRect;
        private float maxAlpha;
        private float intensityMultiplier;
        private float baseGlowStrength;
        private float baseGlowRadius;
        private float baseGlowSoftness;
        private float baseGlowEmission;

        private float peakScale;
        private float visibleUntil;
        private float elapsed;
        private float startTime;
        private bool playing;

        public void Initialize(Image image, float alpha, bool primary)
        {
            glowImage = image;
            Material assignedMaterial = image == null ? null : image.material;
            glowMaterial = assignedMaterial != null &&
                assignedMaterial != Graphic.defaultGraphicMaterial
                ? assignedMaterial
                : null;
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            markerRect = transform as RectTransform;
            maxAlpha = Mathf.Clamp01(alpha);
            intensityMultiplier = primary ? 1f : 0.68f;

            baseGlowStrength = ReadMaterialFloat(
                glowMaterial, GlowStrengthId, 0.62f);
            baseGlowRadius = ReadMaterialFloat(
                glowMaterial, GlowRadiusId, 15f);
            baseGlowSoftness = ReadMaterialFloat(
                glowMaterial, GlowSoftnessId, 8f);
            baseGlowEmission = ReadMaterialFloat(
                glowMaterial, GlowEmissionId, 6.4f);

            peakScale = 1f;
            visibleUntil = 0f;
            elapsed = 0f;
            playing = false;

            canvasGroup.alpha = 0f;
            if (markerRect != null)
            {
                markerRect.localScale = Vector3.one;
            }

            ApplyMaterial(0f);
        }

        public void Begin(float visibleSeconds, float requestedPeakScale)
        {
            if (glowImage == null || canvasGroup == null)
            {
                return;
            }

            peakScale = Mathf.Max(0.001f, requestedPeakScale);
            visibleUntil = Mathf.Max(
                MinimumDurationSeconds,
                visibleSeconds);
            elapsed = 0f;
            startTime = Time.unscaledTime;
            playing = true;
            ApplyFrame(0f, 0f);
        }

        private void Update()
        {
            if (playing)
            {
                Tick();
            }
        }

private void Tick()
        {
            elapsed = Time.unscaledTime - startTime;

            // 淡入、淡出按当前结算拍长度自适应，绝不跨到下一枚棋子。
            float attackSeconds = Mathf.Min(
                AttackSeconds,
                visibleUntil * 0.45f);
            float releaseSeconds = Mathf.Min(
                ReleaseSeconds,
                visibleUntil * 0.45f);
            float fadeOutStart = Mathf.Max(
                attackSeconds,
                visibleUntil - releaseSeconds);

            float envelope;
            if (elapsed <= fadeOutStart)
            {
                envelope = Smooth01(
                    Mathf.Clamp01(
                        elapsed / Mathf.Max(attackSeconds, 0.001f)));
            }
            else
            {
                float releaseT = Mathf.Clamp01(
                    (elapsed - fadeOutStart) /
                    Mathf.Max(releaseSeconds, 0.001f));
                envelope = 1f - Smooth01(releaseT);
            }

            float breathTime = Mathf.Max(0f, elapsed - attackSeconds);
            float breath = CalculateBreath(breathTime);
            ApplyFrame(envelope, breath);

            if (elapsed < visibleUntil)
            {
                return;
            }

            playing = false;
            canvasGroup.alpha = 0f;
        }

        private static float ReadMaterialFloat(
            Material material,
            int propertyId,
            float fallback)
        {
            return material != null && material.HasProperty(propertyId)
                ? material.GetFloat(propertyId)
                : fallback;
        }

        
private void ApplyFrame(float envelope, float breath)
        {
            float breathingIntensity = Mathf.Lerp(0.82f, 1f, breath);

            canvasGroup.alpha = envelope * maxAlpha;
            if (markerRect != null)
            {
                // Size is fixed at the authored peak. Only opacity and HDR
                // intensity breathe, so pawn scaling cannot resize the halo.
                markerRect.localScale = Vector3.one * peakScale;
            }

            ApplyMaterial(breathingIntensity);
        }

private void ApplyMaterial(float breathingIntensity)
        {
            if (glowMaterial == null)
            {
                return;
            }

            float breathT = Mathf.InverseLerp(
                0.82f, 1f, breathingIntensity);
            float glowScale = Mathf.Lerp(0.94f, 1f, breathT);
            float emissionScale = Mathf.Lerp(0.92f, 1.08f, breathT);

            // The material stores the fixed maximum radius and softness.
            // Breathing is restricted to luminance so its footprint is stable.
            glowMaterial.SetFloat(
                GlowStrengthId,
                baseGlowStrength * intensityMultiplier * glowScale);
            glowMaterial.SetFloat(
                GlowRadiusId,
                baseGlowRadius);
            glowMaterial.SetFloat(
                GlowSoftnessId,
                baseGlowSoftness);
            glowMaterial.SetFloat(
                GlowEmissionId,
                baseGlowEmission * intensityMultiplier * emissionScale);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private void OnDisable()
        {
            playing = false;
        }

private void OnDestroy()
        {
            if (glowImage != null &&
                glowImage.material == glowMaterial)
            {
                glowImage.material = null;
            }

            if (glowMaterial != null)
            {
                Destroy(glowMaterial);
                glowMaterial = null;
            }
        }


        private static float CalculateBreath(float breathTime)
        {
            return 0.5f - 0.5f * Mathf.Cos(
                (breathTime / BreathPeriodSeconds) *
                Mathf.PI * 2f);
        }
    }
}
