using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Enables the URP post-processing path required by the screen-space-camera
    /// settlement canvas and creates a runtime-only Bloom profile for HDR glow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeUiBloomBootstrap : MonoBehaviour
    {
        [Header("Settlement Bloom")]
        [SerializeField, Min(0f)]
        private float bloomThreshold = 0.9f;
        [SerializeField, Min(0f)]
        private float bloomIntensity = 0.8f;
        [SerializeField, Range(0f, 1f)]
        private float bloomScatter = 0.85f;
        [SerializeField, Min(0f)]
        private float bloomClamp = 12f;

        private Volume runtimeVolume;
        private VolumeProfile runtimeProfile;
        private bool initialized;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Start()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            Camera targetCamera = Camera.main;
            if (targetCamera == null)
            {
                Debug.LogWarning("[CatCafeUI] Main Camera not found; settlement Bloom is disabled.");
                return;
            }

            targetCamera.allowHDR = true;
            UniversalAdditionalCameraData cameraData =
                targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = targetCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            cameraData.renderPostProcessing = true;

            GameObject volumeObject = new GameObject(
                "CatCafe UI Bloom Volume",
                typeof(Volume));
            volumeObject.transform.SetParent(transform, false);
            volumeObject.hideFlags = HideFlags.DontSave;

            runtimeVolume = volumeObject.GetComponent<Volume>();
            runtimeVolume.isGlobal = true;
            runtimeVolume.priority = 100f;
            runtimeVolume.weight = 1f;

            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.hideFlags = HideFlags.DontSave;

            Bloom bloom = runtimeProfile.Add<Bloom>(true);
            bloom.threshold.Override(bloomThreshold);
            bloom.intensity.Override(bloomIntensity);
            bloom.scatter.Override(bloomScatter);
            bloom.clamp.Override(bloomClamp);
            bloom.tint.Override(new Color(1f, 0.82f, 0.38f, 1f));

            runtimeVolume.sharedProfile = runtimeProfile;
            initialized = true;
        }

        private void OnDestroy()
        {
            if (runtimeVolume != null)
            {
                Destroy(runtimeVolume.gameObject);
                runtimeVolume = null;
            }

            if (runtimeProfile != null)
            {
                Destroy(runtimeProfile);
                runtimeProfile = null;
            }
        }
    }
}
