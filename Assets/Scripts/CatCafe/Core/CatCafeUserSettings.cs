using System;
using UnityEngine;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 设备级玩家偏好（音量、演出速度、新手教程）。
    ///
    /// 刻意不写进 CatCafeMeta：那份存档是玩家进度（罐头 / 图鉴 / 绒毛），换设备要跟着走；
    /// 这里的偏好只属于本机，混进存档还得为它升 version。
    ///
    /// 速度档是"策划基准 × 玩家档位"：基准值仍由配置表的
    /// settlement_speed_multiplier / reward_fx_speed_multiplier 控制，
    /// 玩家偏好只在其上乘一个系数，不覆盖策划意图。默认档位为 1.0，行为与调档前完全一致。
    /// </summary>
    public static class CatCafeUserSettings
    {
        private const string SfxVolumeKey = "catcafe_sfx_volume";
        private const string MusicVolumeKey = "catcafe_music_volume";
        private const string SpeedTierKey = "catcafe_speed_tier";
        // 旧版本把教程开关存在这里（设备级）。现在只在启动时用来清理残留。
        private const string LegacyTutorialEnabledKey = "catcafe_tutorial_enabled";
        private const string FullscreenKey = "catcafe_fullscreen";

        private const float DefaultVolume = 1f;
        private const int DefaultSpeedTierIndex = 1;


        /// <summary>音量档位。UI 用格子按钮呈现，比程序化拼 Slider 更贴纸艺风也更少节点。</summary>
        public static readonly float[] VolumeSteps = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        public static readonly string[] VolumeLabels = { "静音", "25", "50", "75", "100" };

        /// <summary>
        /// 演出速度档位系数，作用在配置表基准值之上。
        /// 最高档是"瞬间"：倍率大到所有时长都被除成一帧以内，等同于幸运房东的 Instant。
        /// 第 30 回合没人想再看一遍 16 枚棋子逐个弹，这一档是长线留存的必需品。
        /// </summary>
        public static readonly float[] SpeedTiers = { 0.75f, 1f, 1.5f, InstantSpeedTier };
        public static readonly string[] SpeedLabels = { "慢", "标准", "快", "瞬间" };

        /// <summary>瞬间档的倍率。除下来任何一段演出都不足一帧，但仍会按帧推进到终态。</summary>
        public const float InstantSpeedTier = 1000f;

        public static event Action Changed;

        private static bool loaded;
        private static float sfxVolume = DefaultVolume;
        private static float musicVolume = DefaultVolume;
        private static int speedTierIndex = DefaultSpeedTierIndex;
        private static bool fullscreen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            // Enter Play Mode 可能不重载 AppDomain，这里跟着配置表一起回到未加载状态。
            loaded = false;
            Changed = null;
        }

        /// <summary>点击、翻转、金币等程序合成音效的音量。</summary>
        public static float SfxVolume
        {
            get { EnsureLoaded(); return sfxVolume; }
            set
            {
                EnsureLoaded();
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(sfxVolume, clamped)) return;

                sfxVolume = clamped;
                PlayerPrefs.SetFloat(SfxVolumeKey, clamped);
                PlayerPrefs.Save();
                Raise();
            }
        }

        /// <summary>BGM 音量，作用在配置表 bgm_base_volume 之上。</summary>
        public static float MusicVolume
        {
            get { EnsureLoaded(); return musicVolume; }
            set
            {
                EnsureLoaded();
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(musicVolume, clamped)) return;

                musicVolume = clamped;
                PlayerPrefs.SetFloat(MusicVolumeKey, clamped);
                PlayerPrefs.Save();
                Raise();
            }
        }

        public static int SpeedTierIndex
        {
            get { EnsureLoaded(); return speedTierIndex; }
            set
            {
                EnsureLoaded();
                int clamped = Mathf.Clamp(value, 0, SpeedTiers.Length - 1);
                if (speedTierIndex == clamped) return;

                speedTierIndex = clamped;
                PlayerPrefs.SetInt(SpeedTierKey, clamped);
                PlayerPrefs.Save();
                Raise();
            }
        }

        /// <summary>
        /// 是否显示表格中启用的新手教程。
        ///
        /// 存储**已经挪进存档**（<see cref="CatCafeMeta.TutorialEnabled"/>），这里只做转发，
        /// 让三个设置面板的调用点不用改。原先存 PlayerPrefs 时是设备级的，导致两类
        /// 事故：「确定收起」按一次，这台机器上之后新开的每一家店都零教程；而 Windows
        /// 的 PlayerPrefs 在注册表里，卸载重装也不清，「全新安装」照样继承关闭状态。
        /// 其余偏好（音量、全屏、速度）仍然是设备级，那是对的，只有教程不是。
        /// </summary>
        public static bool TutorialEnabled
        {
            get { return CatCafeMeta.TutorialEnabled; }
            set
            {
                if (CatCafeMeta.TutorialEnabled == value) return;
                CatCafeMeta.TutorialEnabled = value;
                Raise();
            }
        }


        /// <summary>显示模式档位文案，顺序即档位下标：0 窗口、1 全屏。三个设置面板共用。</summary>
        public static string[] ScreenModeLabels
        {
            get
            {
                return new[]
                {
                    CatCafeConfigDatabase.GetString("ui_settings_screen_windowed_label", "窗口"),
                    CatCafeConfigDatabase.GetString("ui_settings_screen_fullscreen_label", "全屏"),
                };
            }
        }

        /// <summary>
        /// 当前是不是全屏。
        ///
        /// 真值以 Screen 为准而不是存下来的偏好：Player Settings 允许 Alt+Enter 切换，
        /// 玩家那样切完之后偏好还是旧的，设置面板上的高亮就会和眼前的窗口对不上。
        /// PlayerPrefs 只负责"下次启动时恢复成上次的样子"。
        /// 编辑器里不真的切窗口（会把 Game 视图搅乱），读存下来的偏好，方便调 UI。
        /// </summary>
        public static bool Fullscreen
        {
            get
            {
                EnsureLoaded();
                return Application.isEditor ? fullscreen : Screen.fullScreen;
            }
            set
            {
                EnsureLoaded();
                if (fullscreen != value)
                {
                    fullscreen = value;
                    PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
                    PlayerPrefs.Save();
                }
                ApplyScreenMode();
                Raise();
            }
        }

        /// <summary>
        /// 把偏好落到真实窗口上。
        ///
        /// 全屏用无边框窗口（FullScreenWindow）而不是独占全屏：独占要切显示模式，
        /// Alt-Tab 会黑屏几秒，这种慢节奏小游戏没有理由付这个代价。
        /// 回窗口时显式给一次尺寸，否则首次从全屏退回来会拿到一个奇怪的默认值。
        /// </summary>
        public static void ApplyScreenMode()
        {
            EnsureLoaded();
            // 编辑器里 SetResolution 改的是 Game 视图，调 UI 时会莫名其妙地跳，不碰。
            if (Application.isEditor) return;

            if (fullscreen)
            {
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight,
                    FullScreenMode.FullScreenWindow);
                return;
            }

            Screen.SetResolution(
                Mathf.Max(640, CatCafeConfigDatabase.GetInt("window_width", 1280)),
                Mathf.Max(360, CatCafeConfigDatabase.GetInt("window_height", 720)),
                FullScreenMode.Windowed);
        }

        /// <summary>启动时把上次的显示模式恢复回来。场景里不需要挂任何东西。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RestoreScreenMode()
        {
            ApplyScreenMode();
        }

        /// <summary>把配置表的基准倍率乘上玩家档位。这些倍率都是除在时长上的，越大越快。</summary>
        public static float ScaleSpeed(float configuredMultiplier)
        {
            return configuredMultiplier * SpeedTiers[SpeedTierIndex];
        }

        /// <summary>玩家已经把档位拉到瞬间时，自动提速那一层就没有意义了。</summary>
        public static bool IsInstantSpeed
        {
            get { EnsureLoaded(); return SpeedTiers[speedTierIndex] >= InstantSpeedTier; }
        }

        /// <summary>把任意音量值吸附到最近的档位下标，用于刷新格子按钮的选中态。</summary>
        public static int NearestVolumeStep(float value)
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < VolumeSteps.Length; i++)
            {
                float distance = Mathf.Abs(VolumeSteps[i] - value);
                if (distance >= best) continue;
                best = distance;
                nearest = i;
            }

            return nearest;
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;

            sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, DefaultVolume));
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultVolume));
            speedTierIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(SpeedTierKey, DefaultSpeedTierIndex), 0, SpeedTiers.Length - 1);
            fullscreen = PlayerPrefs.GetInt(FullscreenKey,
                CatCafeConfigDatabase.GetBool("fullscreen_default", false) ? 1 : 0) != 0;

            // 教程开关已经挪进存档。旧版本在这里留下的设备级键必须主动删掉：
            // Windows 上 PlayerPrefs 落在注册表，卸载重装都不清，留着它等于给
            // 「全新安装继承上一次的关闭状态」这个 bug 留一条后路。
            if (PlayerPrefs.HasKey(LegacyTutorialEnabledKey))
            {
                PlayerPrefs.DeleteKey(LegacyTutorialEnabledKey);
                PlayerPrefs.Save();
            }
        }

        private static void Raise()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }
    }
}
