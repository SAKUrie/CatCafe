using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    [DisallowMultipleComponent]
    public sealed class CatCafeStartController : MonoBehaviour
    {
        // 流程：开始界面 → 整备界面（CatCafeHome，局外）→ 局内（CatCafeDemo）
        private const string StartUiResourceRoot = "CatCafe/StartUI/";

        private Canvas canvas;
        private CatCafePresentation presentation;
        private Material startGlowMaterial;
        private const string StartGlowMaterialResource =
            StartUiResourceRoot + "start-glow-breathing";
        private TMP_FontAsset uiFont;
        private GameObject shopOverlay;
        private Transform shopList;
        private CatCafeOverlay shopOverlayView;
        private GameObject confirmOverlay;
        private CatCafePvPlayer pvPlayer;
        private CatCafeOverlay confirmOverlayView;
        private TMP_Text confirmCopy;
        private TMP_Text confirmAcceptText;
        private System.Action confirmAction;
        private GameObject settingsOverlay;
        private CatCafeOverlay settingsOverlayView;
        private readonly List<Button> musicButtons = new List<Button>();
        private readonly List<Button> sfxButtons = new List<Button>();
        private readonly List<Button> speedButtons = new List<Button>();
        private readonly List<Button> tutorialButtons = new List<Button>();
        private readonly List<Button> screenButtons = new List<Button>();

        private void Start()
        {
            EnsureEventSystem();
            BuildUi();
            RefreshCurrentShopLine();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<EventSystem>().sendNavigationEvents = true;
        }

private void BuildUi()
        {
            presentation = GetComponent<CatCafePresentation>();
            if (presentation == null)
            {
                presentation = gameObject.AddComponent<CatCafePresentation>();
            }

            presentation.Initialize();
            uiFont = presentation.UiFont;

            // 按钮音效的宿主。CatCafeButtonFeedback 用 GetComponentInParent 找它，
            // 挂在控制器上，画布及其子节点都能取到。
            CatCafeAudioFeedback audio = GetComponent<CatCafeAudioFeedback>();
            if (audio == null) audio = gameObject.AddComponent<CatCafeAudioFeedback>();
            audio.Initialize();

            GameObject canvasObject = NewUi("CatCafeStartCanvas", transform);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1536f, 864f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject background = NewUi("Background", canvas.transform);
            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = new Color(0.17f, 0.13f, 0.11f, 1f);
            backgroundImage.raycastTarget = false;
            CatCafeBackdrop backdrop = background.AddComponent<CatCafeBackdrop>();
            backdrop.Initialize(backgroundImage);
            Stretch(background.GetComponent<RectTransform>(), 0, 0, 0, 0);

            // 和大厅/局内同一套：内容挂进恒为 1536×864 的设计分辨率根，
            // 屏幕比例不是 16:9 时整体等比缩放居中，背景照旧铺满兜底。
            GameObject designRootObject = NewUi("Design Root", canvas.transform);
            designRootObject.AddComponent<CatCafeDesignRootFitter>()
                .Configure(scaler.referenceResolution);
            Transform uiRoot = designRootObject.transform;

            // 纸艺开始界面：每张源图都是 1536×864 的整层，直接按层序叠起来即可，
            // 不需要各自定位。按钮文字是画在图里的，所以交互靠一层透明热区，
            // 热区矩形走配置表，美术挪按钮时不用改代码。
            //
            PlaceStartLayer(uiRoot, "start-backdrop");
            PlaceStartLayer(uiRoot, "start-glow");
            Image playArt = PlaceStartLayer(uiRoot, "start-button-play");
            Image storyArt = PlaceStartLayer(uiRoot, "start-button-shops");
            Image settingsArt = PlaceStartLayer(uiRoot, "start-button-settings");
            Image quitArt = PlaceStartLayer(uiRoot, "start-button-quit");

            // 按钮画在整层美术里，热区是透明的，所以按下手感要驱动对应的那一层。
            CreateHotspot(uiRoot, "开始游戏", "ui_start_play", StartGame, playArt);
            CreateHotspot(uiRoot, "故事", "ui_start_shops", OpenStory, storyArt);
            CreateHotspot(uiRoot, "设置", "ui_start_settings", OpenStartSettings, settingsArt);
            CreateHotspot(uiRoot, "退出", "ui_start_quit", ConfirmQuit, quitArt);

            BuildSettingsOverlay();
            BuildConfirmOverlay();
        }

        /// <summary>铺一张整层美术。缺图不静默：铺洋红占位，一眼看得出哪层没导入。</summary>
        private Image PlaceStartLayer(Transform parent, string spriteName)
        {
            GameObject layer = NewUi(spriteName, parent);
            Stretch(layer.GetComponent<RectTransform>(), 0, 0, 0, 0);
            Image image = layer.AddComponent<Image>();
            image.raycastTarget = false;

            Sprite sprite = Resources.Load<Sprite>(StartUiResourceRoot + spriteName);
            if (sprite == null)
            {
                image.color = new Color(1f, 0f, 1f, 0.35f);
                Debug.LogWarning("[CatCafeStart] 缺图：Resources/" + StartUiResourceRoot + spriteName);
                return image;
            }

            image.sprite = sprite;
            image.color = Color.white;

            if (spriteName == "start-glow")
            {
                Material template = Resources.Load<Material>(StartGlowMaterialResource);
                Shader shader = template == null ?
                    Shader.Find("UI/CatCafe Start Glow Breathing") : template.shader;
                if (shader == null || !shader.isSupported)
                {
                    Debug.LogError("[CatCafeStart] Start glow shader is missing or unsupported.");
                }
                else
                {
                    startGlowMaterial = template != null ?
                        new Material(template) : new Material(shader);
                    startGlowMaterial.name =
                        "CatCafe Start Glow Breathing (Runtime)";
                    startGlowMaterial.hideFlags = HideFlags.HideAndDontSave;
                    startGlowMaterial.SetTexture("_MainTex", sprite.texture);
                    image.material = startGlowMaterial;
                    image.canvasRenderer.SetMaterial(startGlowMaterial, 0);
                    image.SetMaterialDirty();
                    image.SetVerticesDirty();

                    Debug.Log("[CatCafeStart] Start glow material bound: " +
                        startGlowMaterial.name + " / " +
                        startGlowMaterial.shader.name);

                    if (template == null)
                    {
                        Debug.LogWarning("[CatCafeStart] Start glow material template was not found; " +
                            "using Shader.Find fallback.");
                    }
                }
            }

            return image;
        }

        /// <summary>
        /// 透明热区。按钮样子画在美术层里，这里只提供可点区域；
        /// 位置从表里取，美术调整按钮位置时改表即可。
        /// </summary>
        private Button CreateHotspot(Transform parent, string name, string prefix,
            UnityEngine.Events.UnityAction action, Image visual = null)
        {
            GameObject hotspot = NewUi(name, parent);
            Image hit = hotspot.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            PlaceTopLeft(hotspot.GetComponent<RectTransform>(), prefix);

            Button button = hotspot.AddComponent<Button>();
            // 自带的 ColorTint 只会给透明热区自己变色，等于没有反馈，所以关掉，
            // 手感交给 CatCafeImageButtonFeedback 去驱动真正的美术层。
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener(action);

            // 音效走 CatCafeButtonFeedback：它的视觉部分作用在热区自己的图上，
            // 而热区是 Color.clear，乘任何系数仍然透明，不会显形；要的只是它带的
            // 悬停/按下音。这样不用给 ImageButtonFeedback 加音频，
            // 免得把大厅和局内已有按钮的手感一起改掉。
            hotspot.AddComponent<CatCafeButtonFeedback>().Initialize();

            if (visual != null && visual.sprite != null)
            {
                CatCafeImageButtonFeedback feedback =
                    hotspot.AddComponent<CatCafeImageButtonFeedback>();
                // 与局内开始营业/设置按钮共用同图透明亮度层：悬停发亮，
                // 原始整屏美术层的位置、尺寸和缩放保持不变。
                feedback.InitializeBrightnessOverlay(visual);
            }

            return button;
        }

        private void OpenStory()
        {
            if (pvPlayer == null)
            {
                pvPlayer = gameObject.AddComponent<CatCafePvPlayer>();
                pvPlayer.Initialize(canvas);
            }
            pvPlayer.Play();
        }

        /// <summary>按配置表的 &lt;prefix&gt;_x/_y/_width/_height 摆放，左上角为原点。</summary>
        private static void PlaceTopLeft(RectTransform rect, string prefix)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(
                CatCafeConfigDatabase.GetRequiredFloat(prefix + "_x"),
                -CatCafeConfigDatabase.GetRequiredFloat(prefix + "_y"));
            rect.sizeDelta = new Vector2(
                CatCafeConfigDatabase.GetRequiredFloat(prefix + "_width"),
                CatCafeConfigDatabase.GetRequiredFloat(prefix + "_height"));
        }

        /* ══════════════════ 设置 ══════════════════ */

        /// <summary>
        /// 开始界面的设置面板。外壳走 CatCafePresentation.BuildPaperPanel，
        /// 和大厅、局内营业菜单是同一份实现，观感不会各走各的。
        ///
        /// 这里只放设备级偏好（音量 / 演出速度 / 字条开关）——它们存在 PlayerPrefs，
        /// 不属于任何一个存档档位，在还没选定小店的开始界面上改也不会有歧义。
        /// 「重看全部字条」故意不放进来：那个写的是当前存档的已读位，
        /// 而开始界面上"当前存档"随时可能被换掉。
        /// </summary>
        private void BuildSettingsOverlay()
        {
            settingsOverlay = presentation.BuildPaperPanel(canvas.transform, "SettingsOverlay",
                CatCafeConfigDatabase.GetRequiredString("ui_start_settings_title"),
                new Vector2(660f, 740f), out Transform content);

            MakeSectionLabel(content, CatCafeConfigDatabase.GetRequiredString("ui_settings_screen_title"));
            BuildToggleRow(content, CatCafeUserSettings.ScreenModeLabels, screenButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.Fullscreen = index == 1;
                    RefreshSettingsToggles();
                });

            MakeSectionLabel(content, CatCafeConfigDatabase.GetRequiredString("ui_start_music_label"));
            BuildToggleRow(content, CatCafeUserSettings.VolumeLabels, musicButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.MusicVolume = CatCafeUserSettings.VolumeSteps[index];
                    RefreshSettingsToggles();
                });

            MakeSectionLabel(content, CatCafeConfigDatabase.GetRequiredString("ui_start_sfx_label"));
            BuildToggleRow(content, CatCafeUserSettings.VolumeLabels, sfxButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.SfxVolume = CatCafeUserSettings.VolumeSteps[index];
                    RefreshSettingsToggles();
                });

            MakeSectionLabel(content, CatCafeConfigDatabase.GetRequiredString("ui_start_speed_label"));
            BuildToggleRow(content, CatCafeUserSettings.SpeedLabels, speedButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.SpeedTierIndex = index;
                    RefreshSettingsToggles();
                });

            MakeSectionLabel(content, CatCafeConfigDatabase.GetRequiredString("ui_settings_tutorial_title"));
            BuildToggleRow(content, new[]
                {
                    CatCafeConfigDatabase.GetRequiredString("ui_settings_tutorial_off_label"),
                    CatCafeConfigDatabase.GetRequiredString("ui_settings_tutorial_on_label")
                }, tutorialButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.TutorialEnabled = index == 1;
                    RefreshSettingsToggles();
                });

            AddSpacer(content, 8f);
            presentation.CreateButton(content,
                CatCafeConfigDatabase.GetRequiredString("ui_start_settings_close"),
                CloseStartSettings, 0f, 56f, PaperButtonRole.Primary);

            settingsOverlay.SetActive(false);
            settingsOverlayView = settingsOverlay.AddComponent<CatCafeOverlay>();
            settingsOverlayView.Initialize(
                settingsOverlay.transform.Find("Panel") as RectTransform, true, CloseStartSettings);
        }

        private void OpenStartSettings()
        {
            RefreshSettingsToggles();
            settingsOverlay.transform.SetAsLastSibling();
            settingsOverlayView.Show();
        }

        private void CloseStartSettings()
        {
            settingsOverlayView.Hide();
        }

        private void MakeSectionLabel(Transform parent, string text)
        {
            TMP_Text label = MakeText(text, parent, 17,
                new Color(0.34f, 0.23f, 0.15f), TextAnchor.MiddleCenter);
            label.fontStyle = FontStyles.Bold;
            AddFixedHeight(label.gameObject, 30f);
        }

        private void BuildToggleRow(Transform parent, string[] labels, List<Button> sink,
            System.Action<int> onSelect)
        {
            presentation.BuildToggleRow(parent, labels, sink, onSelect);
        }

        private void RefreshSettingsToggles()
        {
            presentation.MarkToggleGroup(screenButtons, CatCafeUserSettings.Fullscreen ? 1 : 0);
            presentation.MarkToggleGroup(musicButtons,
                CatCafeUserSettings.NearestVolumeStep(CatCafeUserSettings.MusicVolume));
            presentation.MarkToggleGroup(sfxButtons,
                CatCafeUserSettings.NearestVolumeStep(CatCafeUserSettings.SfxVolume));
            presentation.MarkToggleGroup(speedButtons, CatCafeUserSettings.SpeedTierIndex);
            presentation.MarkToggleGroup(tutorialButtons, CatCafeUserSettings.TutorialEnabled ? 1 : 0);
        }

        private void AddSpacer(Transform parent, float height)
        {
            AddFixedHeight(NewUi("Spacer", parent), height);
        }

        private static void AddFixedHeight(GameObject target, float height)
        {
            LayoutElement layout = target.GetComponent<LayoutElement>();
            if (layout == null) layout = target.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private void ConfirmQuit()
        {
            ShowConfirm(CatCafeConfigDatabase.GetRequiredString("ui_start_quit_confirm"),
                CatCafeConfigDatabase.GetRequiredString("ui_start_quit_accept"), QuitGame);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 「开始游戏」直接进当前那家店。全新玩家从没开过店，当前档位是回落出来的
        /// 1 号店且文件不存在——这一下就等于新开一家，必须走和「开这家」相同的复位
        /// 路径，否则设备级的教程开关会带着上一个人按下的「确定收起」。
        /// </summary>
        private void StartGame()
        {
            int slot = CatCafeSaveSlots.Current;
            if (!CatCafeSaveSlots.Exists(slot)) CatCafeSaveSlots.BeginNewShop(slot);
            SceneManager.LoadScene(
                CatCafeConfigDatabase.GetRequiredString("scene_meta_collection"));
        }

        /* ══════════════════ 小店（存档档位） ══════════════════ */

        private static string ShopName(int slot)
        {
            string[] ordinals = { "第一家", "第二家", "第三家", "第四家", "第五家",
                "第六家", "第七家", "第八家", "第九家" };
            return (slot >= 1 && slot <= ordinals.Length ? ordinals[slot - 1] : "第 " + slot + " 家") + "小店";
        }

        private static string ShopSummary(int slot)
        {
            CatCafeSaveSlots.Summary summary = CatCafeSaveSlots.Read(slot);
            if (!summary.Exists) return CatCafeConfigDatabase.GetRequiredString("ui_shop_summary_empty");
            return string.Format(CatCafeConfigDatabase.GetRequiredString("ui_shop_summary_format"),
                summary.Runs, summary.Wins, summary.Discovered);
        }

        /// <summary>
        /// 开始界面不再显示「正在照看第 N 家小店」那行字——纸艺整图排得满，
        /// 一行小字压在书页上既不好看也没人看。存档信息在「存档」列表里都有。
        /// 保留这个方法是因为收摊之后要刷新那份列表。
        /// </summary>
        private void RefreshCurrentShopLine()
        {
            if (shopOverlayView != null && shopOverlay != null && shopOverlay.activeSelf)
            {
                RebuildShopList();
            }
        }

        /// <summary>
        /// 存档列表。外壳和设置面板、大厅、局内共用 BuildPaperPanel——
        /// 开始界面上这两个面板是并排的入口，不该一个纸艺一个程序化框线。
        /// </summary>
        private void BuildShopOverlay()
        {
            shopOverlay = presentation.BuildPaperPanel(canvas.transform, "ShopOverlay",
                CatCafeConfigDatabase.GetRequiredString("ui_start_shops_title"),
                new Vector2(880f, 660f), out Transform content);

            TMP_Text hint = MakeText(CatCafeConfigDatabase.GetRequiredString("ui_start_shops_hint"),
                content, 15, new Color(0.42f, 0.31f, 0.22f), TextAnchor.MiddleCenter);
            AddFixedHeight(hint.gameObject, 26f);

            GameObject list = NewUi("ShopList", content);
            LayoutElement listSize = list.AddComponent<LayoutElement>();
            listSize.flexibleHeight = 1f;
            VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 10;
            listLayout.childAlignment = TextAnchor.UpperCenter;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            shopList = list.transform;

            presentation.CreateButton(content,
                CatCafeConfigDatabase.GetRequiredString("ui_start_shops_back"),
                CloseShopList, 0f, 52f, PaperButtonRole.Leave);
            shopOverlay.SetActive(false);
            shopOverlayView = shopOverlay.AddComponent<CatCafeOverlay>();
            shopOverlayView.Initialize(
                shopOverlay.transform.Find("Panel") as RectTransform, true, CloseShopList);
        }

        private void OpenShopList()
        {
            RebuildShopList();
            // 三个弹层是画布的同级子节点，谁后打开谁在上面，否则先开过设置就永远压着存档。
            shopOverlay.transform.SetAsLastSibling();
            shopOverlayView.Show();
        }

        private void CloseShopList()
        {
            shopOverlayView.Hide();
        }

        private void RebuildShopList()
        {
            for (int i = shopList.childCount - 1; i >= 0; i--) Destroy(shopList.GetChild(i).gameObject);

            int active = CatCafeSaveSlots.Current;
            for (int slot = 1; slot <= CatCafeSaveSlots.SlotCount; slot++)
            {
                bool exists = CatCafeSaveSlots.Exists(slot);
                bool isActive = slot == active;

                GameObject row = NewUi("Shop" + slot, shopList);
                Image rowImage = row.AddComponent<Image>();
                PixelFrame(rowImage, isActive
                    ? new Color(0.55f, 0.38f, 0.27f, 1f)
                    : new Color(0.32f, 0.24f, 0.19f, 1f));
                rowImage.raycastTarget = false;
                LayoutElement rowSize = row.AddComponent<LayoutElement>();
                rowSize.minHeight = 96f;
                rowSize.preferredHeight = 96f;
                HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.padding = new RectOffset(14, 14, 10, 10);
                rowLayout.spacing = 10;
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = true;

                GameObject info = NewUi("Info", row.transform);
                info.AddComponent<LayoutElement>().flexibleWidth = 1f;
                VerticalLayoutGroup infoLayout = info.AddComponent<VerticalLayoutGroup>();
                infoLayout.spacing = 2;
                infoLayout.childAlignment = TextAnchor.MiddleLeft;
                infoLayout.childControlWidth = true;
                infoLayout.childControlHeight = true;
                infoLayout.childForceExpandWidth = true;
                infoLayout.childForceExpandHeight = false;
                TMP_Text name = MakeText(ShopName(slot) + (isActive ? "（正在照看）" : string.Empty),
                    info.transform, 19, new Color(0.96f, 0.90f, 0.78f), TextAnchor.MiddleLeft);
                name.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
                TMP_Text detail = MakeText(ShopSummary(slot), info.transform, 14,
                    new Color(0.80f, 0.73f, 0.61f), TextAnchor.MiddleLeft);
                detail.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

                int captured = slot;
                if (exists)
                {
                    // 已经在照看的那家不用再"进"一次，按钮换成灰的更省事：直接不建。
                    if (!isActive)
                    {
                        presentation.CreateButton(row.transform, "进这家",
                            delegate { EnterShop(captured); }, 132f, 56f, PaperButtonRole.Secondary);
                    }
                    presentation.CreateButton(row.transform, "收 摊",
                        delegate { RequestDeleteShop(captured); }, 110f, 56f, PaperButtonRole.Leave);
                }
                else
                {
                    presentation.CreateButton(row.transform, "开这家",
                        delegate { EnterShop(captured); }, 132f, 56f, PaperButtonRole.Primary);
                }
            }
        }

        /// <summary>选中并直接进店。空档位进去就是新开一家：BeginNewShop 会建档并复位教程。</summary>
        private void EnterShop(int slot)
        {
            if (CatCafeSaveSlots.Exists(slot)) CatCafeSaveSlots.Select(slot);
            else CatCafeSaveSlots.BeginNewShop(slot);
            StartGame();
        }

        private void RequestDeleteShop(int slot)
        {
            ShowConfirm(ShopName(slot) + "要收摊了吗？\n店里的猫、罐头和图鉴都会散掉，找不回来的。",
                "确认收摊", delegate
                {
                    CatCafeSaveSlots.Delete(slot);
                    RebuildShopList();
                    RefreshCurrentShopLine();
                });
        }

        /* ══════════════════ 二次确认 ══════════════════ */

        private void BuildConfirmOverlay()
        {
            confirmOverlay = presentation.BuildPaperPanel(canvas.transform, "ConfirmOverlay",
                CatCafeConfigDatabase.GetRequiredString("ui_start_confirm_title"),
                new Vector2(640f, 380f), out Transform content);

            confirmCopy = MakeText(string.Empty, content, 17,
                new Color(0.30f, 0.20f, 0.14f), TextAnchor.MiddleCenter);
            AddFixedHeight(confirmCopy.gameObject, 108f);
            Button accept = presentation.CreateButton(content, "确 认", AcceptConfirm,
                0f, 56f, PaperButtonRole.Primary);
            confirmAcceptText = accept.GetComponentInChildren<TMP_Text>();
            presentation.CreateButton(content, "再想想", CloseConfirm,
                0f, 52f, PaperButtonRole.Secondary);

            confirmOverlay.SetActive(false);
            // 破坏性操作不给点空白关闭，避免误触落在"确认"上。
            confirmOverlayView = confirmOverlay.AddComponent<CatCafeOverlay>();
            confirmOverlayView.Initialize(
                confirmOverlay.transform.Find("Panel") as RectTransform, false, null);
        }

        private void ShowConfirm(string body, string acceptLabel, System.Action onAccept)
        {
            confirmAction = onAccept;
            confirmCopy.text = body;
            if (confirmAcceptText != null) confirmAcceptText.text = acceptLabel;
            confirmOverlay.transform.SetAsLastSibling();
            confirmOverlayView.Show();
        }

        private void CloseConfirm()
        {
            confirmAction = null;
            confirmOverlayView.Hide();
        }

        private void AcceptConfirm()
        {
            System.Action pending = confirmAction;
            confirmAction = null;
            confirmOverlayView.Hide();
            if (pending != null) pending();
        }

private TMP_Text MakeText(string value, Transform parent, int size, Color color, TextAnchor alignment)
        {
            return presentation.MakeText(value, parent, size, color, alignment);
        }

        private GameObject NewUi(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

private void PixelFrame(Image image, Color fill)
        {
            presentation.PixelFrame(image, fill);
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void OnDestroy()
        {
            if (startGlowMaterial != null)
            {
                Destroy(startGlowMaterial);
                startGlowMaterial = null;
            }
        }
    }
}
