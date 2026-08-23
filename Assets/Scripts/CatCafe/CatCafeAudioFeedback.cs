using UnityEngine;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Small, asset-free sound palette for the runtime prototype.
    /// Clips are synthesized once per scene and played through one 2D source.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeAudioFeedback : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const float BaseVolume = 0.62f;

        private AudioSource source;
        private AudioClip hoverClip;
        private AudioClip clickClip;
        private AudioClip rollStartClip;
        private AudioClip rollStopClip;
        private AudioClip rewardClip;
        private AudioClip guidanceClip;
        private bool initialized;

        public void Initialize()
        {
            if (initialized) return;

            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            ApplyVolume();

            hoverClip = CreateTone("CatCafe Hover", 520f, 650f, 0.045f, 0.20f);
            clickClip = CreateTone("CatCafe Click", 180f, 120f, 0.075f, 0.34f);
            rollStartClip = CreateTone("CatCafe Roll Start", 180f, 420f, 0.22f, 0.30f);
            rollStopClip = CreateTone("CatCafe Roll Stop", 420f, 235f, 0.13f, 0.32f);
            rewardClip = CreateTone("CatCafe Reward", 620f, 980f, 0.16f, 0.38f);
            guidanceClip = CreateTone("CatCafe Guidance", 360f, 540f, 0.11f, 0.24f);
            initialized = true;
        }

        public void PlayHover()
        {
            Play(hoverClip, 0.24f);
        }

        public void PlayClick()
        {
            Play(clickClip, 0.34f);
        }

        public void PlayRollStart()
        {
            Play(rollStartClip, 0.34f);
        }

        public void PlayRollStop()
        {
            Play(rollStopClip, 0.36f);
        }

        public void PlayReward()
        {
            Play(rewardClip, 0.42f);
        }

        public void PlayGuidance()
        {
            Play(guidanceClip, 0.28f);
        }

        private void Play(AudioClip clip, float volume)
        {
            if (!initialized || source == null || clip == null) return;
            source.PlayOneShot(clip, volume);
        }

        private void OnEnable()
        {
            CatCafeUserSettings.Changed += ApplyVolume;
        }

        private void OnDisable()
        {
            CatCafeUserSettings.Changed -= ApplyVolume;
        }

        /// <summary>玩家在设置里改音量时立即生效，不需要等下一次 Initialize。</summary>
        private void ApplyVolume()
        {
            if (source == null) return;
            source.volume = BaseVolume * CatCafeUserSettings.SfxVolume;
        }

        private static AudioClip CreateTone(
            string name,
            float startFrequency,
            float endFrequency,
            float duration,
            float amplitude)
        {
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(duration * SampleRate));
            float[] samples = new float[sampleCount];
            float phase = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float normalized = i / (sampleCount - 1f);
                float frequency = Mathf.Lerp(startFrequency, endFrequency, normalized);
                phase += frequency / SampleRate;
                float envelope = Mathf.Clamp01(normalized * 22f) *
                    Mathf.Clamp01((1f - normalized) * 12f);
                float fundamental = Mathf.Sin(phase * Mathf.PI * 2f);
                float overtone = Mathf.Sin(phase * Mathf.PI * 4f) * 0.18f;
                samples[i] = (fundamental + overtone) * envelope * amplitude;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            DestroyClip(hoverClip);
            DestroyClip(clickClip);
            DestroyClip(rollStartClip);
            DestroyClip(rollStopClip);
            DestroyClip(rewardClip);
            DestroyClip(guidanceClip);
        }

        private static void DestroyClip(AudioClip clip)
        {
            if (clip != null) Destroy(clip);
        }
    }
}