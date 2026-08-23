using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 跨场景常驻的 BGM 播放器。
    ///
    /// 播放列表来自配置表的 bgm_tracks_&lt;场景名&gt;，逗号分隔曲目名；曲目文件放在
    /// Resources/&lt;bgm_resource_folder&gt; 下。场景切换时如果播放列表没变就不打断当前曲子，
    /// 变了才交叉淡入淡出换过去。
    ///
    /// 场景里不需要挂任何东西——和这个项目其它 UI 一样，运行时自举。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeMusicPlayer : MonoBehaviour
    {
        private const float ClipLoadTimeout = 5f;

        private static CatCafeMusicPlayer instance;

        private AudioSource primary;
        private AudioSource secondary;
        private readonly List<string> playlist = new List<string>();
        private string playlistSignature = string.Empty;
        private int trackIndex;
        private Coroutine director;
        private bool applicationSuspended;
        private bool externalSuspended;

        private bool Suspended { get { return applicationSuspended || externalSuspended; } }

        private static string ResourceFolder
        {
            get { return CatCafeConfigDatabase.GetString("bgm_resource_folder", "CatCafe/Bgm"); }
        }

        private static float BaseVolume
        {
            get { return CatCafeConfigDatabase.GetFloat("bgm_base_volume", 0.45f); }
        }

        private static float CrossfadeSeconds
        {
            get { return Mathf.Max(0.1f, CatCafeConfigDatabase.GetFloat("bgm_crossfade_seconds", 2f)); }
        }

        private static bool Shuffle
        {
            get { return CatCafeConfigDatabase.GetBool("bgm_shuffle", true); }
        }

        private float TargetVolume
        {
            get { return BaseVolume * CatCafeUserSettings.MusicVolume; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            // 关掉 domain reload 时静态字段会跨 Play 存活，这里跟配置表一样回到未初始化。
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            // 总开关。排查音频相关问题时把配置表的 bgm_enabled 关掉，
            // 整个播放器连同 AudioSource 都不会被创建。
            if (!CatCafeConfigDatabase.GetBool("bgm_enabled", true))
            {
                Debug.Log("[CatCafeBGM] bgm_enabled = false，已跳过音乐播放器。");
                return;
            }

            GameObject host = new GameObject("CatCafeMusicPlayer");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<CatCafeMusicPlayer>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            primary = CreateSource("Music A");
            secondary = CreateSource("Music B");
        }

        private void OnEnable()
        {
            CatCafeUserSettings.Changed += ApplyVolume;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            CatCafeUserSettings.Changed -= ApplyVolume;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Start()
        {
            // Bootstrap 在 AfterSceneLoad 跑，首个场景的 sceneLoaded 已经错过了。
            ApplyScene(SceneManager.GetActiveScene().name);
        }

        // 项目的 Run In Background 是关的，切出窗口时 Unity 会挂起音频，
        // AudioSource.isPlaying 随之变 false。这两个回调让播放循环知道那是"被挂起"
        // 而不是"放完了"，回来才会接着放而不是跳下一首。
        private void OnApplicationFocus(bool hasFocus)
        {
            applicationSuspended = !hasFocus;
        }

        private void OnApplicationPause(bool isPaused)
        {
            applicationSuspended = isPaused;
        }

        public static void SetExternalPause(bool paused)
        {
            if (instance == null || instance.externalSuspended == paused) return;
            instance.externalSuspended = paused;
            if (paused)
            {
                if (instance.primary != null) instance.primary.Pause();
                if (instance.secondary != null) instance.secondary.Pause();
            }
            else
            {
                if (instance.primary != null) instance.primary.UnPause();
                if (instance.secondary != null) instance.secondary.UnPause();
            }
        }

        private AudioSource CreateSource(string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);

            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.volume = 0f;
            return source;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyScene(scene.name);
        }

        private void ApplyScene(string sceneName)
        {
            List<string> tracks = ReadPlaylist(sceneName);
            string signature = string.Join(",", tracks.ToArray());
            if (signature == playlistSignature)
            {
                // 同一份歌单，让当前这首继续放完，别在切场景时把音乐打断重来。
                return;
            }

            playlistSignature = signature;
            playlist.Clear();
            playlist.AddRange(tracks);
            trackIndex = 0;
            if (Shuffle) ShufflePlaylist();

            if (director != null) StopCoroutine(director);
            director = playlist.Count > 0 ? StartCoroutine(RunPlaylist()) : null;
            if (playlist.Count == 0) FadeOutAll();
        }

        private static List<string> ReadPlaylist(string sceneName)
        {
            List<string> result = new List<string>();
            string raw = CatCafeConfigDatabase.GetString("bgm_tracks_" + sceneName);
            if (string.IsNullOrEmpty(raw)) return result;

            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string track = parts[i].Trim();
                if (track.Length > 0) result.Add(track);
            }

            return result;
        }

        private void ShufflePlaylist()
        {
            for (int i = playlist.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);
                string held = playlist[i];
                playlist[i] = playlist[swap];
                playlist[swap] = held;
            }
        }

        private IEnumerator RunPlaylist()
        {
            AudioSource current = primary;
            AudioSource next = secondary;

            while (playlist.Count > 0)
            {
                AudioClip clip = null;
                yield return LoadClip(playlist[trackIndex], loaded => clip = loaded);
                trackIndex = (trackIndex + 1) % playlist.Count;

                if (clip == null)
                {
                    // 整张歌单都缺文件时别空转，让出一帧再重试下一首。
                    yield return null;
                    continue;
                }

                if (clip.length <= 0.5f)
                {
                    // 长度取不到（0 或极短）时下面的交接点会算成 0，会让歌单按帧率狂切。
                    Debug.LogWarning("[CatCafeBGM] 曲目长度异常，已跳过：" + clip.name);
                    yield return null;
                    continue;
                }

                float crossfade = Mathf.Min(CrossfadeSeconds, clip.length * 0.5f);
                current.clip = clip;
                current.time = 0f;
                current.Play();
                yield return Crossfade(current, next, crossfade);

                // 放到只剩一个交叉淡出的长度时，再去准备下一首。
                float handoff = Mathf.Max(0.25f, clip.length - crossfade);
                float resumeAt = 0f;
                while (current.clip == clip)
                {
                    if (current.isPlaying)
                    {
                        resumeAt = current.time;
                        if (resumeAt >= handoff) break;
                    }
                    else if (!Suspended)
                    {
                        // 播放被打断（失焦、音频设备切换…）。只要还没到交接点，就从中断处
                        // 续上，而不是当成"这首放完了"去换下一首。以前拿 isPlaying 当循环
                        // 条件，切出窗口一次就会跳一首，听起来就是音乐重新播了。
                        if (resumeAt >= handoff) break;
                        current.time = Mathf.Clamp(resumeAt, 0f, clip.length - 0.05f);
                        current.Play();
                    }

                    yield return null;
                }

                AudioSource held = current;
                current = next;
                next = held;
            }

            director = null;
        }

        /// <summary>
        /// 流式 AudioClip 是异步加载的，直接 Play 会放出静音。等到 Loaded 再交出去。
        /// </summary>
        private IEnumerator LoadClip(string track, System.Action<AudioClip> onLoaded)
        {
            string path = ResourceFolder + "/" + track;
            AudioClip clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning("[CatCafeBGM] 找不到曲目：Resources/" + path);
                onLoaded(null);
                yield break;
            }

            // Streaming 的曲子由引擎在 Play 时自行取流，对它调 LoadAudioData 不受支持，
            // 在播放器里会把音频后端搞崩。只对需要预解的类型显式加载。
            if (clip.loadType != AudioClipLoadType.Streaming &&
                clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
            }

            float waited = 0f;
            while (clip.loadState == AudioDataLoadState.Loading && waited < ClipLoadTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (clip.loadType != AudioClipLoadType.Streaming &&
                clip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogWarning("[CatCafeBGM] 曲目加载失败或超时：" + track);
                onLoaded(null);
                yield break;
            }

            onLoaded(clip);
        }

        private IEnumerator Crossfade(AudioSource rising, AudioSource falling, float duration)
        {
            float startRising = rising.volume;
            float startFalling = falling.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                rising.volume = Mathf.Lerp(startRising, TargetVolume, normalized);
                falling.volume = Mathf.Lerp(startFalling, 0f, normalized);
                yield return null;
            }

            rising.volume = TargetVolume;
            falling.volume = 0f;
            falling.Stop();
            falling.clip = null;
        }

        private void FadeOutAll()
        {
            if (primary != null) { primary.Stop(); primary.clip = null; }
            if (secondary != null) { secondary.Stop(); secondary.clip = null; }
        }

        /// <summary>玩家在设置里拖音乐音量时立刻生效，正在淡入的那一路交给协程收尾。</summary>
        private void ApplyVolume()
        {
            if (primary != null && primary.isPlaying) primary.volume = TargetVolume;
            if (secondary != null && secondary.isPlaying) secondary.volume = TargetVolume;
        }
    }
}
