using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ManyFace.CatCafe
{
    [DisallowMultipleComponent]
    public sealed class CatCafePvPlayer : MonoBehaviour
    {
        private GameObject overlay;
        private RawImage screen;
        private AspectRatioFitter aspect;
        private VideoPlayer player;
        private bool playing;

        public void Initialize(Canvas canvas)
        {
            if (overlay != null || canvas == null) return;

            overlay = NewUi("PV Overlay", canvas.transform);
            RectTransform overlayRect = overlay.GetComponent<RectTransform>();
            Stretch(overlayRect);
            Image blocker = overlay.AddComponent<Image>();
            blocker.color = Color.black;
            Button close = overlay.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.targetGraphic = blocker;
            close.onClick.AddListener(Close);

            GameObject videoObject = NewUi("PV Video", overlay.transform);
            RectTransform videoRect = videoObject.GetComponent<RectTransform>();
            Stretch(videoRect);
            screen = videoObject.AddComponent<RawImage>();
            screen.color = Color.white;
            screen.raycastTarget = false;
            aspect = videoObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 16f / 9f;

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = false;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;
            player.renderMode = VideoRenderMode.APIOnly;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.prepareCompleted += OnPrepared;
            player.loopPointReached += OnFinished;
            player.errorReceived += OnError;
            overlay.SetActive(false);
        }

        public void Play()
        {
            if (player == null || playing) return;
            string path = ResolvePvPath();
            if (string.IsNullOrEmpty(path)) return;

            playing = true;
            screen.texture = null;
            overlay.transform.SetAsLastSibling();
            overlay.SetActive(true);
            CatCafeMusicPlayer.SetExternalPause(true);
            player.url = new Uri(path).AbsoluteUri;
            player.Prepare();
        }

        private static string ResolvePvPath()
        {
            string folder = Application.streamingAssetsPath;
            if (!Directory.Exists(folder))
            {
                Debug.LogError("[CatCafePV] StreamingAssets 目录不存在：" + folder);
                return null;
            }

            string[] files = Directory.GetFiles(folder, "*.mp4", SearchOption.TopDirectoryOnly);
            if (files.Length != 1)
            {
                Debug.LogError("[CatCafePV] StreamingAssets 中必须且只能有一个 MP4，当前数量：" + files.Length);
                return null;
            }
            return files[0];
        }

        private void OnPrepared(VideoPlayer prepared)
        {
            if (!playing) return;
            screen.texture = prepared.texture;
            if (prepared.height > 0) aspect.aspectRatio = (float)prepared.width / prepared.height;
            if (prepared.audioTrackCount > 0)
            {
                prepared.EnableAudioTrack(0, true);
                prepared.SetDirectAudioVolume(0, CatCafeUserSettings.MusicVolume);
            }
            prepared.Play();
        }

        private void OnFinished(VideoPlayer finished)
        {
            Close();
        }

        private void OnError(VideoPlayer source, string message)
        {
            Debug.LogError("[CatCafePV] 播放失败：" + message);
            Close();
        }

        private void Update()
        {
            if (playing && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Close();
        }

        private void Close()
        {
            if (!playing) return;
            playing = false;
            if (player != null) player.Stop();
            if (overlay != null) overlay.SetActive(false);
            CatCafeMusicPlayer.SetExternalPause(false);
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.prepareCompleted -= OnPrepared;
                player.loopPointReached -= OnFinished;
                player.errorReceived -= OnError;
            }
            if (playing) CatCafeMusicPlayer.SetExternalPause(false);
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
