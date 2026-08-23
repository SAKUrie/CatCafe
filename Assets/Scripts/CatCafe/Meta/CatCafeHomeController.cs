using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 整备界面（局外 Home）：实景大厅（游走猫 + 家具 + 收钱罐）、图鉴、猫咪招募。
    /// 视觉与交互对齐 HTML 交流稿 Prototype/meta-cafe.html；数据来自 CatCatalog / CatCafeMeta。
    /// 流程：CatCafeStart → 本场景 → 「开始营业」→ CatCafeDemo → 局末返回本场景。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeHomeController : MonoBehaviour
    {
        private const string RunSceneName = "CatCafeDemo";

        private Canvas canvas;
        private Transform uiRoot;   // 设计分辨率根：大厅所有固定坐标层都挂它下面
        private Font uiFont;
        private const string HomeUiResourceRoot = "CatCafe/HomeUI/";

        private Text hudCans;
        private Text hudDex;
        private Text hudDexPercent;
        private RectTransform dexFill;
        private RectTransform popupBookRect;
        private RectTransform detailsStandRect;
        private RectTransform cansBarRect;
        private RectTransform dexBarRect;
        private Text statsLine;
        private GameObject dexOverlay;
        private Transform dexContent;
        private RectTransform dexPanelRect;
        private GameObject inviteOverlay;
        private Transform inviteContent;
        private RectTransform invitePanelRect;
        private GameObject settingsOverlay;
        private GameObject noteSettingsOverlay;
        private GameObject noteArchiveOverlay;
        private Transform noteArchiveContent;
        private RectTransform noteArchivePanelRect;
        private readonly List<Button> musicButtons = new List<Button>();
        private readonly List<Button> sfxButtons = new List<Button>();
        private readonly List<Button> screenButtons = new List<Button>();
        private Text noteSettingsState;
        private Button noteSkipButton;
        private Button noteSkipCancelButton;
        private Button noteCloseButton;
        private bool noteSkipArmed;
        private Text toastText;
        private Button startRunButton;
        private Button dexButton;
        private Button inviteButton;
        private CatCafeHoverPreview hoverPreview;
        private CatCafeLandlordNote tutorialNotes;
        private float noteHoldUntil;
        private Coroutine toastRoutine;
        private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        private readonly HashSet<string> missingSpriteWarnings = new HashSet<string>();
        private readonly List<Material> runtimeMaterials = new List<Material>();
        private CatCafePresentation presentation;
        private CatCafeDiscoveryReveal discoveryReveal;

        private void Start()
        {
            spriteCache.Clear();
            missingSpriteWarnings.Clear();
            EnsureEventSystem();
            CatCatalog.EnsureLoaded();
            CatCafeMeta.EnsureLoaded();
            uiFont = CatCafeUiFontProvider.LegacyFont;
            presentation = gameObject.GetComponent<CatCafePresentation>();
            if (presentation == null) presentation = gameObject.AddComponent<CatCafePresentation>();
            presentation.Initialize();
            BuildUi();
            CatCafeMeta.RefreshNaturalFur();
            discoveryReveal = gameObject.GetComponent<CatCafeDiscoveryReveal>();
            if (discoveryReveal == null) discoveryReveal = gameObject.AddComponent<CatCafeDiscoveryReveal>();
            discoveryReveal.Initialize(canvas, presentation);
            hoverPreview = gameObject.GetComponent<CatCafeHoverPreview>();
            if (hoverPreview == null) hoverPreview = gameObject.AddComponent<CatCafeHoverPreview>();
            hoverPreview.Initialize(canvas, uiFont, LoadSprite);
            RefreshHud();
            tutorialNotes = gameObject.AddComponent<CatCafeLandlordNote>();
            tutorialNotes.Initialize(canvas);
            tutorialNotes.SetGate(CanShowLandlordNote);
            tutorialNotes.RegisterTarget("home_start_button", startRunButton.transform as RectTransform);
            tutorialNotes.RegisterTarget("home_dex_panel", dexPanelRect);
            tutorialNotes.RegisterTarget("home_invite_panel", invitePanelRect);
            tutorialNotes.RegisterTarget("home_invite_stand", inviteButton.transform as RectTransform);
            tutorialNotes.RegisterTarget("home_new_cat", popupBookRect);
            tutorialNotes.RegisterTarget("home_menu_buttons", dexBarRect);
            tutorialNotes.RegisterTarget("home_cans_hud", cansBarRect);
            tutorialNotes.RegisterTarget("home_collection_entries", detailsStandRect);
            // 每次进入大厅最多只排一张字条。未读且条件仍成立的内容会留到下次进入时再讲，
            // 第一次营业回家优先介绍绒毛与猫咪招募；新猫水位只在这一拍没有占用时消费，
            // 避免先消费再延后提示，导致新猫字条永久丢失。
            bool queued = tutorialNotes.Notify("home_first_enter");
            if (!queued && CatCafeMeta.Runs > 0) queued = tutorialNotes.Notify("home_fur_first");
            bool broughtNewCat = false;
            if (!queued) broughtNewCat = CatCafeMeta.ConsumeNewCatHomeArrival();
            if (!queued && broughtNewCat) queued = tutorialNotes.Notify("home_first_new_cat");
            if (!queued && CatCafeMeta.Runs > 0) queued = tutorialNotes.Notify("home_first_return");
            if (!queued && CatCafeMeta.Cans > 0) queued = tutorialNotes.Notify("home_cans_first");
        }

        /// <summary>见 <see cref="CatCafeLandlordNote"/>：弹层刚打开、内容还在铺的时候不插话。</summary>
        private bool CanShowLandlordNote()
        {
            return Time.unscaledTime >= noteHoldUntil;
        }

        private void HoldLandlordNotes(float seconds)
        {
            noteHoldUntil = Mathf.Max(noteHoldUntil, Time.unscaledTime + seconds);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                CatCafeMeta.SaveNow();
                return;
            }

            if (canvas == null) return;

            int naturalFur = CatCafeMeta.RefreshNaturalFur();
            if (naturalFur <= 0) return;
            RefreshHud();
            if (inviteOverlay != null && inviteOverlay.activeSelf) RebuildInvite();
            Toast(string.Format(CopyText("ui_home_fur_natural_gain_format"), naturalFur));
        }

        private void OnApplicationQuit()
        {
            CatCafeMeta.SaveNow();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] != null) Destroy(runtimeMaterials[i]);
            }
            runtimeMaterials.Clear();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<EventSystem>().sendNavigationEvents = true;
        }

        /* ══════════════════ UI 构建 ══════════════════ */

        private void BuildUi()
        {
            GameObject canvasObject = NewUi("CatCafeHomeCanvas", transform);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1536f, 864f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            // 大厅的每一层美术都是 ui_home_* 那套 1536×864 固定坐标，画布一旦不是 16:9
            // 就会被 CanvasScaler 拉成另一个比例，图层之间互相错位（和局内当年同一个病）。
            // 这里沿用局内那套设计分辨率根：底下垫一张满屏深色兜底，内容整体等比缩放居中。
            GameObject letterboxFill = NewUi("Letterbox Fill", canvas.transform);
            Image letterboxImage = letterboxFill.AddComponent<Image>();
            letterboxImage.color = new Color(0.17f, 0.13f, 0.11f, 1f);
            letterboxImage.raycastTarget = false;
            Stretch(letterboxFill.GetComponent<RectTransform>(), 0, 0, 0, 0);

            GameObject designRootObject = NewUi("Design Root", canvas.transform);
            designRootObject.AddComponent<CatCafeDesignRootFitter>()
                .Configure(scaler.referenceResolution);
            uiRoot = designRootObject.transform;

            BuildPaperHome();

            BuildDexOverlay();
            BuildInviteOverlay();
            BuildSettingsOverlay();
            BuildNoteSettingsOverlay();

            toastText = MakeText(string.Empty, uiRoot, 17,
                new Color(0.94f, 0.84f, 0.55f), TextAnchor.MiddleCenter);
            toastText.fontStyle = FontStyle.Bold;
            Outline toastOutline = toastText.gameObject.AddComponent<Outline>();
            toastOutline.effectColor = new Color(0.08f, 0.06f, 0.05f, 0.9f);
            toastOutline.effectDistance = new Vector2(2f, -2f);
            AnchorRect(toastText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 108f), new Vector2(900f, 36f));
        }

        /// <summary>
        /// 纸艺大厅：把美术分层按 Settings 表里的坐标铺上去。
        /// 层序即创建顺序（自底向上），和美术稿的图层栈一致。
        /// 坐标基准 1536x864 左上原点，键名 ui_home_*，美术可直接在表里挪。
        /// </summary>
        private void BuildPaperHome()
        {
            PlaceLayer("home-backdrop", "ui_home_backdrop");
            popupBookRect = PlaceLayer("home-popup-book", "ui_home_popup_book").rectTransform;
            PlaceAnimatedCat("home-cat-top", "ui_home_cat_top");
            PlaceAnimatedCat("home-cat-mid", "ui_home_cat_mid");
            PlaceAnimatedCat("home-cat-right", "ui_home_cat_right");
            PlaceLayer("home-clouds-front", "ui_home_clouds_front");

            // 三个主入口：左立牌＝图鉴，右立牌＝猫咪招募，底部缎带＝开始营业。
            dexButton = MakeLayerButton("home-details-stand", "ui_home_details_stand", OpenDex);
            detailsStandRect = dexButton.transform as RectTransform;
            inviteButton = MakeLayerButton("home-relations-stand", "ui_home_relations_stand", OpenInvite);
            startRunButton = MakeLayerButton("home-start-ribbon", "ui_home_start_ribbon", StartRun);

            // 左上罐头条：底图是去文字版，数字与 ⊕ 由运行时叠上去。
            cansBarRect = PlaceLayer("home-cans-bar", "ui_home_cans_bar").rectTransform;
            hudCans = MakeTableText("HomeCans", "ui_home_cans_value", 30,
                new Color(0.36f, 0.24f, 0.15f), TextAnchor.MiddleCenter);
            hudCans.fontStyle = FontStyle.Bold;
            // ⊕ 就是原来大厅里那个实体收钱罐，位置挪到罐头条右端。
            MakeHotspot("HomeCollectJar", "ui_home_cans_collect", CollectJarTip);

            // 顶部图鉴进度条。
            dexBarRect = PlaceLayer("home-dex-bar", "ui_home_dex_bar").rectTransform;
            MakeTableText("HomeDexLabel", "ui_home_dex_label", 20,
                new Color(0.36f, 0.24f, 0.15f), TextAnchor.MiddleLeft).text =
                CatCafeConfigDatabase.GetString("ui_home_dex_label_text", "猫咪图鉴进度");
            hudDexPercent = MakeTableText("HomeDexPercent", "ui_home_dex_percent", 22,
                new Color(0.36f, 0.24f, 0.15f), TextAnchor.MiddleRight);
            hudDexPercent.fontStyle = FontStyle.Bold;
            // 去文字版把绿色填充也一起去掉了，所以进度槽要运行时画。
            RectTransform fillTrack = PlaceRect(uiRoot, "HomeDexFill", "ui_home_dex_fill");
            GameObject fillObject = NewUi("Fill", fillTrack);
            dexFill = fillObject.GetComponent<RectTransform>();
            dexFill.anchorMin = new Vector2(0f, 0f);
            dexFill.anchorMax = new Vector2(0f, 1f);
            dexFill.pivot = new Vector2(0f, 0.5f);
            dexFill.offsetMin = Vector2.zero;
            dexFill.offsetMax = Vector2.zero;
            Image fillImage = fillObject.AddComponent<Image>();
            fillImage.color = ParseColor(
                CatCafeConfigDatabase.GetString("ui_home_dex_fill_color", "#8D9268"));
            fillImage.raycastTarget = false;
            hudDex = MakeTableText("HomeDexCount", "ui_home_dex_count", 19,
                new Color(0.42f, 0.30f, 0.20f), TextAnchor.MiddleCenter);

            // 右上设置：字条开关收在这里。
            MakeLayerButton("home-settings", "ui_home_settings", OpenSettings);

            // 统计行没有专属版面，压在罐头条底下小声说。
            statsLine = MakeText(string.Empty, uiRoot,
                CatCafeConfigDatabase.GetRequiredInt("ui_home_stats_font_size"),
                new Color(0.42f, 0.34f, 0.26f), TextAnchor.MiddleLeft);
            PlaceTopLeft(statsLine.rectTransform,
                CatCafeConfigDatabase.GetRequiredFloat("ui_home_stats_x"),
                CatCafeConfigDatabase.GetRequiredFloat("ui_home_stats_y"),
                CatCafeConfigDatabase.GetRequiredFloat("ui_home_stats_width"),
                CatCafeConfigDatabase.GetRequiredFloat("ui_home_stats_height"));
        }

        /// <summary>按 Settings 表的 ui_* 四元组摆一个矩形（1536x864 左上原点）。</summary>
        private RectTransform PlaceRect(Transform parent, string name, string prefix)
        {
            GameObject holder = NewUi(name, parent);
            RectTransform rect = holder.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(
                CatCafeConfigDatabase.GetFloat(prefix + "_x"),
                -CatCafeConfigDatabase.GetFloat(prefix + "_y"));
            rect.sizeDelta = new Vector2(
                CatCafeConfigDatabase.GetFloat(prefix + "_width"),
                CatCafeConfigDatabase.GetFloat(prefix + "_height"));
            return rect;
        }

        private Image PlaceLayer(string spriteName, string prefix)
        {
            RectTransform rect = PlaceRect(uiRoot, spriteName, prefix);
            Image image = rect.gameObject.AddComponent<Image>();
            Sprite sprite = Resources.Load<Sprite>(HomeUiResourceRoot + spriteName);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
            }
            else
            {
                // 缺图不静默：铺一块洋红占位，一眼看得出是哪一层没导入。
                image.color = new Color(1f, 0f, 1f, 0.35f);
                if (missingSpriteWarnings.Add(spriteName))
                {
                    Debug.LogWarning("[CatCafeHome] 缺少大厅 UI 素材：Resources/" +
                        HomeUiResourceRoot + spriteName);
                }
            }
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 把一层装饰猫铺成序列帧图集动画。三只共用这一套，
        /// 图集路径、行列数、帧率都按 &lt;prefix&gt;_animation_* 从表里取，
        /// 美术换图集或改节奏不用动代码。
        ///
        /// 素材或 Shader 缺失时铺洋红占位并告警，与 PlaceLayer 的缺图处理一致——
        /// 一眼看得出是哪一层没接上，而不是静默留白。
        /// </summary>
        private RawImage PlaceAnimatedCat(string spriteName, string prefix)
        {
            RectTransform rect = PlaceRect(uiRoot, spriteName, prefix);
            RawImage image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;

            string resourcePath = CatCafeConfigDatabase.GetRequiredString(
                prefix + "_animation_resource");
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            Shader shader = Shader.Find("UI/CatCafeSpriteSheet");
            if (texture == null || shader == null)
            {
                image.color = new Color(1f, 0f, 1f, 0.35f);
                Debug.LogWarning("[CatCafeHome] 序列帧素材或 Shader 缺失：" + resourcePath);
                return image;
            }

            Material material = new Material(shader);
            material.name = spriteName + " SpriteSheet (Runtime)";
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Columns", Mathf.Max(1,
                CatCafeConfigDatabase.GetRequiredInt(prefix + "_animation_columns")));
            material.SetFloat("_Rows", Mathf.Max(1,
                CatCafeConfigDatabase.GetRequiredInt(prefix + "_animation_rows")));
            material.SetFloat("_FrameRate", Mathf.Max(0.01f,
                CatCafeConfigDatabase.GetRequiredFloat(prefix + "_animation_fps")));

            image.texture = texture;
            image.material = material;
            image.color = Color.white;
            runtimeMaterials.Add(material);
            return image;
        }

        /// <summary>把某一层变成可点的按钮。不规则图形按矩形判定，够用。</summary>
        private Button MakeLayerButton(string spriteName, string prefix,
            UnityEngine.Events.UnityAction action)
        {
            Image image = PlaceLayer(spriteName, prefix);
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            AddPressFeedback(button, image);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        /// <summary>
        /// 大厅纸片按钮与局内开始营业/设置按钮共用同图透明亮度层：
        /// 悬停发亮、按下压暗，原图和按钮矩形都不移动。
        /// </summary>
        private static void AddPressFeedback(Button button, Image image)
        {
            if (button == null || image == null) return;
            CatCafeImageButtonFeedback feedback =
                button.gameObject.AddComponent<CatCafeImageButtonFeedback>();
            feedback.InitializeBrightnessOverlay(image);
        }

        /// <summary>纯热区：自己没有图，压在别的层上面接点击。</summary>
        private Button MakeHotspot(string name, string prefix, UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = PlaceRect(uiRoot, name, prefix);
            Image hit = rect.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.002f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            // 热区自己是透明的，压暗看不出来，所以只做缩放。
            CatCafeImageButtonFeedback feedback =
                button.gameObject.AddComponent<CatCafeImageButtonFeedback>();
            feedback.Initialize(rect, hit, hit.color, hit.color, 1f, 0.92f);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        private Text MakeTableText(string name, string prefix, int size, Color color, TextAnchor alignment)
        {
            RectTransform rect = PlaceRect(uiRoot, name, prefix);
            Text label = MakeText(string.Empty, rect, size, color, alignment);
            Stretch(label.rectTransform, 0, 0, 0, 0);
            return label;
        }

        private static Color ParseColor(string hex)
        {
            Color parsed;
            return ColorUtility.TryParseHtmlString(hex, out parsed) ? parsed : Color.white;
        }

        /// <summary>⊕ ＝ 收今天的小费。空罐子也给一句话，别让按钮看起来是坏的。</summary>
        private void CollectJarTip()
        {
            int gain = CatCafeMeta.CollectJar();
            Toast(gain > 0
                ? string.Format(CatCafeConfigDatabase.GetString(
                    "ui_home_jar_collect", "收取小费 +{0} 罐头"), gain)
                : CatCafeConfigDatabase.GetString(
                    "ui_home_jar_empty", "罐子还是空的，让猫咪们再攒攒～"));
            RefreshHud();
        }

        /* ══════════════════ 图鉴 ══════════════════ */

        private void BuildDexOverlay()
        {
            dexOverlay = BuildOverlayShell("DexOverlay", "猫 咪 图 鉴", out dexContent, out dexPanelRect);
        }

        private void OpenDex()
        {
            RebuildDex();
            dexOverlay.SetActive(true);
            if (tutorialNotes == null) return;
            HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_overlay_hold", 0.25f));
            tutorialNotes.Notify("home_dex_first");
        }

        private void RebuildDex()
        {
            ClearChildren(dexContent);
            GridLayoutGroup grid = dexContent.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                grid = dexContent.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(
                    CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_card_width"),
                    CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_card_height"));
                grid.spacing = new Vector2(
                    CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_grid_spacing_x"),
                    CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_grid_spacing_y"));
                int gridPadding = CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_grid_padding");
                grid.padding = new RectOffset(gridPadding, gridPadding, gridPadding, gridPadding);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_grid_columns");
                ContentSizeFitter fitter = dexContent.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            List<CatCatalog.CatRow> breeds = CatCatalog.DexBreeds();
            for (int i = 0; i < breeds.Count; i++)
            {
                CatCatalog.CatRow row = breeds[i];
                bool discovered = CatCafeMeta.IsDiscovered(row.Key);
                GameObject card = NewUi("Card_" + row.Key, dexContent);
                Image cardImage = card.AddComponent<Image>();
                PixelFrame(cardImage, new Color(0.23f, 0.18f, 0.15f, 1f));
                VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(10, 10, 10, 10);
                layout.spacing = 5;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                // 立绘 / 剪影 / 文字占位
                GameObject artFrame = NewUi("Art", card.transform);
                LayoutElement artLayout = artFrame.AddComponent<LayoutElement>();
                float artHeight = CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_art_height");
                artLayout.minHeight = artHeight;
                artLayout.preferredHeight = artHeight;
                Image artBack = artFrame.AddComponent<Image>();
                artBack.color = new Color(0.29f, 0.23f, 0.20f, 1f);
                artBack.raycastTarget = false;
                Sprite sprite = LoadSprite(row.Asset);
                if (sprite != null)
                {
                    GameObject artObject = NewUi("Sprite", artFrame.transform);
                    RectTransform artRect = artObject.GetComponent<RectTransform>();
                    AnchorRect(artRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f), Vector2.zero,
                        Vector2.one * CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_art_size"));
                    Image artImage = artObject.AddComponent<Image>();
                    artImage.preserveAspect = true;   // 立绘留白不一，不锁比例会被拉变形
                    artImage.sprite = sprite;
                    artImage.color = discovered ? Color.white : new Color(0.12f, 0.09f, 0.08f, 1f); // 未解锁 = 剪影
                    artImage.raycastTarget = false;
                }
                else
                {
                    // 缺图时不写"美术待补"——那是排期用语，不该出现在玩家眼前；
                    // 已解锁的退回猫名，未解锁的仍是问号。
                    Text noArt = MakeText(discovered ? row.Name : "？", artFrame.transform,
                        discovered ? CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_meta_font_size") : 40,
                        new Color(0.55f, 0.47f, 0.38f), TextAnchor.MiddleCenter);
                    Stretch(noArt.rectTransform, 0, 0, 0, 0);
                }

                Text nameText = MakeText(discovered ? row.Name : "？？？", card.transform,
                    CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_name_font_size"),
                    new Color(0.94f, 0.88f, 0.75f), TextAnchor.MiddleCenter);
                nameText.fontStyle = FontStyle.Bold;
                AddFixedHeight(nameText.gameObject,
                    CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_name_height"));

                string rarityLabel = row.Rarity == "rare" ? "稀有" : row.Rarity == "uncommon" ? "少见" : "普通";
                if (discovered)
                {
                    int milestone = CatCafeMeta.IntimacyMilestone(row.Key);
                    int intimacy = CatCafeMeta.IntimacyOf(row.Key);
                    int nextTarget = CatCafeMeta.NextIntimacyTarget(row.Key);
                    Text metaText = MakeText(rarityLabel + " · ❤" + milestone + " · 亲密度 " + intimacy +
                        (nextTarget > intimacy ? "/" + nextTarget : "（已满）") + " · 见过×" + CatCafeMeta.CountOf(row.Key),
                        card.transform, CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_meta_font_size"),
                        new Color(0.72f, 0.65f, 0.51f), TextAnchor.MiddleCenter);
                    AddFixedHeight(metaText.gameObject,
                        CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_meta_height"));
                    string flavorCopy = milestone >= 2 ? row.DexFlavor : "再同行几次，会听到它的故事。";
                    Text flavor = MakeText(flavorCopy, card.transform,
                        CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_flavor_font_size"),
                        new Color(0.62f, 0.55f, 0.45f), TextAnchor.UpperCenter);
                    AddFixedHeight(flavor.gameObject,
                        CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_flavor_height"));
                    string hintCopy = milestone >= 3 ? row.DexHint : "再熟一些，它会告诉你和谁最合得来。";
                    Text hintText = MakeText(hintCopy, card.transform,
                        CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_hint_font_size"),
                        new Color(0.55f, 0.47f, 0.38f), TextAnchor.MiddleCenter);
                    AddFixedHeight(hintText.gameObject,
                        CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_hint_height"));
                    string unlockLabel = row.Unlock == "base"
                        ? CatCafeConfigDatabase.GetRequiredString("ui_home_piece_base_label")
                        : CatCafeConfigDatabase.GetRequiredString("ui_home_piece_unlocked_label");
                    Button unlockState = CreateButton(card.transform, unlockLabel, null, 0f, 36f, true);
                    unlockState.GetComponent<LayoutElement>().flexibleWidth = 1f;
                    unlockState.interactable = false;
                }
                else
                {
                    Text metaText = MakeText(rarityLabel, card.transform,
                        CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_meta_font_size"),
                        new Color(0.55f, 0.47f, 0.38f), TextAnchor.MiddleCenter);
                    AddFixedHeight(metaText.gameObject,
                        CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_meta_height"));
                    string hint = row.Unlock == "mutation" ? "？？？" :
                        string.IsNullOrEmpty(row.DexHint) ? "？？？" : row.DexHint;
                    Text hintText = MakeText(hint, card.transform,
                        CatCafeConfigDatabase.GetRequiredInt("ui_home_dex_hint_font_size"),
                        new Color(0.62f, 0.55f, 0.45f), TextAnchor.UpperCenter);
                    AddFixedHeight(hintText.gameObject,
                        CatCafeConfigDatabase.GetRequiredFloat("ui_home_dex_locked_hint_height"));
                }
            }
        }

        /* ══════════════════ 猫咪招募 · 呼朋唤友 ══════════════════ */

        /// <summary>表里的文案统一走这里，把 \n 还原成真正的换行。</summary>
        private static string CopyText(string key)
        {
            return CatCafeConfigDatabase.GetRequiredString(key).Replace("\\n", "\n");
        }

        private void BuildInviteOverlay()
        {
            inviteOverlay = BuildOverlayShell("InviteOverlay",
                CopyText("ui_home_invite_title"), out inviteContent, out invitePanelRect);
        }

        private void OpenInvite()
        {
            CatCafeMeta.RefreshNaturalFur();
            RebuildInvite();
            inviteOverlay.SetActive(true);
            if (tutorialNotes == null) return;
            HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_overlay_hold", 0.25f));
            tutorialNotes.Notify("home_invite_first");
        }

        /// <summary>
        /// 呼朋唤友：攒够某位已入住伙伴的绒毛 + 路上的罐头，就让它出门把新朋友请回来。
        ///
        /// 这里刻意不是"两只猫合成一只猫"——邀请者不是父母，产物也不落到棋盘上，
        /// 只点亮图鉴。谁能请到谁由 Invite 表定；越稀有的猫要的邀请者越多（表里配第二位）。
        /// 局内育儿窝那套配方（Breeding 表）与本面板无关，两条轨道各自独立。
        /// </summary>
        private void RebuildInvite()
        {
            ClearChildren(inviteContent);
            VerticalLayoutGroup layout = inviteContent.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = inviteContent.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 10f;
                layout.padding = new RectOffset(8, 8, 8, 8);
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                ContentSizeFitter fitter = inviteContent.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            Text howTo = MakeText(string.Format(CopyText("ui_home_invite_howto"),
                    CatCafeConfigDatabase.GetRequiredInt("meta_fur_natural_interval_minutes"),
                    CatCafeConfigDatabase.GetRequiredInt("meta_fur_natural_amount_per_interval"),
                    CatCafeConfigDatabase.GetRequiredInt("meta_fur_natural_cap_per_breed")),
                inviteContent, 17, new Color(0.30f, 0.20f, 0.14f), TextAnchor.MiddleCenter);
            AddFixedHeight(howTo.gameObject, 92f);

            List<CatCatalog.InviteRow> invites = CatCatalog.AllInvites();
            int shown = 0;
            for (int i = 0; i < invites.Count; i++)
            {
                CatCatalog.InviteRow invite = invites[i];
                if (CatCafeMeta.IsDiscovered(invite.Child)) continue;
                shown++;
                BuildInviteRow(invite);
            }

            if (shown == 0)
            {
                Text done = MakeText(CopyText("ui_home_invite_done"),
                    inviteContent, 17, new Color(0.30f, 0.20f, 0.14f), TextAnchor.MiddleCenter);
                AddFixedHeight(done.gameObject, 60f);
            }
        }

        // 行底是 modal-main-v2 木牌：九宫格边框 72/64，行高 116 时实际画出来的
        // 木牌实体只有 86px 高，两端还各有约 72px 的雕花端头。图标按 72px 摆会
        // 上下顶出木牌、左边压在雕花上，所以这里按木牌内框重新定尺寸和内边距。
        private const float InviteIconSize = 56f;
        private const float InviteGlyphWidth = 26f;
        private const float InviteSlotSpacing = 10f;
        /// <summary>让开左端雕花，第一个格子才落在木牌平整的部分上。</summary>
        private const int InviteRowPadX = 78;

        private void BuildInviteRow(CatCatalog.InviteRow invite)
        {
            bool invitersKnown = CanInvitersLeave(invite);
            bool furOk = HasInviteFur(invite);
            bool cansOk = CatCafeMeta.Cans >= invite.Cans;

            GameObject rowObject = NewUi("Invite_" + invite.Child, inviteContent);
            Image rowImage = rowObject.AddComponent<Image>();
            ApplyHomePaper(rowImage, "modal-main-v2", new Color(1f, 0.96f, 0.86f, 1f));
            HorizontalLayoutGroup rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(InviteRowPadX, InviteRowPadX, 8, 8);
            rowLayout.spacing = InviteSlotSpacing;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            AddFixedHeight(rowObject, 116f);

            AddInviteIcon(rowObject.transform, invite.InviterA, CatCafeMeta.IsDiscovered(invite.InviterA));
            if (invite.HasSecondInviter)
            {
                AddInviteGlyph(rowObject.transform,
                    CatCafeConfigDatabase.GetRequiredString("ui_home_invite_formula_connector"),
                    InviteGlyphWidth);
                AddInviteIcon(rowObject.transform, invite.InviterB, CatCafeMeta.IsDiscovered(invite.InviterB));
            }
            AddInviteGlyph(rowObject.transform, "→", InviteGlyphWidth);
            AddInviteIcon(rowObject.transform, invite.Child, false);

            string costLine = InviteFurLine(invite.InviterA, invite.FurA);
            if (invite.HasSecondInviter) costLine += "　" + InviteFurLine(invite.InviterB, invite.FurB);
            costLine += "　" + string.Format(CopyText("ui_home_invite_cans_format"), CatCafeMeta.Cans, invite.Cans);
            if (!invitersKnown) costLine += "\n" + CopyText("ui_home_invite_locked_hint");

            CatCatalog.CatRow child = CatCatalog.Get(invite.Child);
            string childLine = invitersKnown && child != null
                ? string.Format(CopyText("ui_home_invite_target_format"), child.Name)
                : CopyText("ui_home_invite_target_unknown");
            Text costText = MakeText(childLine + "\n" + costLine,
                rowObject.transform, 15, new Color(0.30f, 0.20f, 0.14f), TextAnchor.MiddleLeft);
            costText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Button send = CreateButton(rowObject.transform, CopyText("ui_home_invite_button"), null, 156f, 56f, true);
            send.interactable = invitersKnown && furOk && cansOk;
            CatCatalog.InviteRow captured = invite;
            send.onClick.AddListener(delegate { SendInvite(captured); });
        }

        /// <summary>
        /// 给一个格子挂上猫咪悬停预览。列表里目标猫是剪影，玩家在花掉绒毛和罐头之前
        /// 看不出请回来的是谁；悬停给正常立绘和一句介绍，让这笔消费是明白的。
        /// </summary>
        private void AttachCatHover(GameObject target, CatCatalog.CatRow row, bool revealed)
        {
            if (target == null || row == null || hoverPreview == null) return;
            CatCafeHoverTrigger trigger = target.AddComponent<CatCafeHoverTrigger>();
            CatCatalog.CatRow captured = row;
            bool capturedRevealed = revealed;
            trigger.Initialize(
                delegate(RectTransform anchorRect)
                {
                    hoverPreview.Show(captured, CatCafeConfigDatabase.RarityLabel(captured.Rarity),
                        CatIntro(captured), anchorRect, capturedRevealed);
                },
                delegate { hoverPreview.Hide(); });
        }

        /// <summary>预览里的介绍文案：优先图鉴故事，没有就退到联动提示，再没有就给一句兜底。</summary>
        private static string CatIntro(CatCatalog.CatRow row)
        {
            if (row == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(row.DexFlavor)) return row.DexFlavor;
            if (!string.IsNullOrWhiteSpace(row.DexHint)) return row.DexHint;
            if (!string.IsNullOrWhiteSpace(row.RuleText)) return row.RuleText;
            return CatCafeConfigDatabase.GetString("ui_home_preview_no_intro", "还没有人写下它的故事。");
        }

        private static string InviteFurLine(string inviterKey, int need)
        {
            CatCatalog.CatRow inviter = CatCatalog.Get(inviterKey);
            return string.Format(CopyText("ui_home_invite_fur_format"),
                inviter != null ? inviter.Name : inviterKey, CatCafeMeta.FurOf(inviterKey), need);
        }

        /// <summary>邀请者得先住进猫咖才叫得动——没解锁的猫不能替你出门。</summary>
        private static bool CanInvitersLeave(CatCatalog.InviteRow invite)
        {
            if (!CatCafeMeta.IsDiscovered(invite.InviterA)) return false;
            return !invite.HasSecondInviter || CatCafeMeta.IsDiscovered(invite.InviterB);
        }

        /// <summary>
        /// 两位邀请者可能是同一只猫（表里允许），那样绒毛要求得叠加，
        /// 不能各查一遍——否则攒够一份就能当两份花。
        /// </summary>
        private static bool HasInviteFur(CatCatalog.InviteRow invite)
        {
            if (invite.HasSecondInviter && invite.InviterA == invite.InviterB)
                return CatCafeMeta.FurOf(invite.InviterA) >= invite.FurA + invite.FurB;
            if (CatCafeMeta.FurOf(invite.InviterA) < invite.FurA) return false;
            return !invite.HasSecondInviter || CatCafeMeta.FurOf(invite.InviterB) >= invite.FurB;
        }

        private void SendInvite(CatCatalog.InviteRow invite)
        {
            if (!CanInvitersLeave(invite)) { Toast(CopyText("ui_home_invite_need_friends")); return; }
            if (!HasInviteFur(invite)) { Toast(CopyText("ui_home_invite_need_fur")); return; }
            // 罐头先扣：绒毛一旦扣了再发现罐头不够，就得往回退两笔账。
            if (!CatCafeMeta.TrySpendCans(invite.Cans)) { Toast(CopyText("ui_home_invite_need_cans")); return; }
            CatCafeMeta.TrySpendFur(invite.InviterA, invite.FurA);
            if (invite.HasSecondInviter) CatCafeMeta.TrySpendFur(invite.InviterB, invite.FurB);

            bool first;
            CatCafeMeta.Discover(invite.Child, "invite", out first);
            CatCafeMeta.SaveNow();
            CatCatalog.CatRow discoveredCat = CatCatalog.Get(invite.Child);
            string discoveredName = discoveredCat != null ? discoveredCat.Name : invite.Child;
            RefreshHud();
            RebuildInvite();
            if (first && discoveryReveal != null)
            {
                // 呼朋唤友请回来的新朋友，用与局内诞生完全相同的图鉴揭晓页。
                discoveryReveal.Show(discoveredName,
                    discoveredCat != null ? LoadSprite(discoveredCat.Asset) : null,
                    () => Toast(string.Format(CopyText("ui_home_invite_success_format"), discoveredName)));
            }
            else
            {
                Toast(string.Format(CopyText("ui_home_invite_success_format"), discoveredName));
            }
        }

        private void AddInviteIcon(Transform parent, string key, bool revealed)
        {
            CatCatalog.CatRow row = CatCatalog.Get(key);
            GameObject icon = NewUi("Icon_" + key, parent);
            AttachCatHover(icon, row, revealed);
            LayoutElement layoutElement = icon.AddComponent<LayoutElement>();
            layoutElement.minWidth = InviteIconSize;
            layoutElement.preferredWidth = InviteIconSize;
            layoutElement.minHeight = InviteIconSize;
            layoutElement.preferredHeight = InviteIconSize;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
            Image back = icon.AddComponent<Image>();
            back.color = new Color(0.29f, 0.23f, 0.20f, 1f);
            // 悬停要靠这层收指针事件；里面的立绘仍是 raycastTarget=false。
            back.raycastTarget = row != null;
            Sprite sprite = row != null ? LoadSprite(row.Asset) : null;
            if (sprite != null)
            {
                GameObject spriteObject = NewUi("Sprite", icon.transform);
                RectTransform spriteRect = spriteObject.GetComponent<RectTransform>();
                AnchorRect(spriteRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(InviteIconSize - 8f, InviteIconSize - 8f));
                Image spriteImage = spriteObject.AddComponent<Image>();
                // 立绘画布都是 256x256，但内容留白不一致：横躺的猫占满宽度、
                // 端坐的猫占满高度。不保持比例就会被拉成一大一小、顶出框外。
                spriteImage.preserveAspect = true;
                spriteImage.sprite = sprite;
                spriteImage.color = revealed ? Color.white : new Color(0.12f, 0.09f, 0.08f, 1f);
                spriteImage.raycastTarget = false;
            }
            else
            {
                Text placeholder = MakeText(revealed && row != null ? row.Name : "？", icon.transform,
                    revealed ? 12 : 26, new Color(0.55f, 0.47f, 0.38f), TextAnchor.MiddleCenter);
                Stretch(placeholder.rectTransform, 2, 2, 2, 2);
            }
        }

        private void AddInviteGlyph(Transform parent, string value, float width)
        {
            Text text = MakeText(value, parent, 22, new Color(0.55f, 0.47f, 0.38f), TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            LayoutElement layoutElement = text.gameObject.AddComponent<LayoutElement>();
            layoutElement.minWidth = width;
            layoutElement.preferredWidth = width;
            // 和图标同高，「＋」「→」才会跟两侧格子居中在同一条水平线上。
            layoutElement.minHeight = InviteIconSize;
            layoutElement.preferredHeight = InviteIconSize;
        }

        /* ══════════════════ 通用 ══════════════════ */

        private void StartRun()
        {
            CatCafeMeta.SaveNow();
            SceneManager.LoadScene(RunSceneName);
        }

        private void RefreshHud()
        {
            int discovered = CatCafeMeta.DiscoveredCount();
            int total = Mathf.Max(1, CatCatalog.DexBreeds().Count);
            float ratio = Mathf.Clamp01(discovered / (float)total);

            hudCans.text = CatCafeMeta.Cans.ToString();
            hudDex.text = discovered + "/" + total;
            if (hudDexPercent != null) hudDexPercent.text = Mathf.RoundToInt(ratio * 100f) + "%";
            if (dexFill != null)
            {
                // 槽宽来自表里的 ui_home_dex_fill_width，按发现比例拉伸填充条。
                RectTransform track = dexFill.parent as RectTransform;
                dexFill.sizeDelta = new Vector2(track.rect.width * ratio, 0f);
            }
            RefreshStats();
        }

        private void RefreshStats()
        {
            if (statsLine != null)
            {
                statsLine.text = string.Format(
                    CatCafeConfigDatabase.GetRequiredString("ui_home_stats_format"),
                    CatCafeMeta.Runs, CatCafeMeta.Wins, CatCafeMeta.DiscoveredCount());
            }
        }

        /* ══════════════════ 设置 ══════════════════ */

        private void ReplayNotes()
        {
            if (tutorialNotes == null) return;
            tutorialNotes.ReplayAll();
            CloseNoteSettings();
            CloseSettings();
            HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_overlay_hold", 0.25f));
            tutorialNotes.Notify("manual_replay_intro");
            Toast(CatCafeConfigDatabase.GetString("ui_home_note_replay_toast", "字条已重新放回盒子里"));
        }

        /// <summary>
        /// 大厅设置。视觉走 CatCafePresentation 的纸艺皮肤，和局内那套保持一致；
        /// 房东字条是偏好设置里的一个条目，收进二级面板，不摊在主面板上。
        /// </summary>
        private void BuildSettingsOverlay()
        {
            settingsOverlay = BuildPaperPanel("SettingsOverlay",
                CatCafeConfigDatabase.GetString("ui_home_settings_title", "设 置"),
                new Vector2(660f, 700f), out Transform content);

            MakeSectionLabel(content, CatCafeConfigDatabase.GetRequiredString("ui_settings_screen_title"));
            presentation.BuildToggleRow(content, CatCafeUserSettings.ScreenModeLabels, screenButtons,
                delegate(int index)
                {
                    CatCafeUserSettings.Fullscreen = index == 1;
                    RefreshSettingsToggles();
                });

            MakeSectionLabel(content, CatCafeConfigDatabase.GetString("ui_home_music_label", "音乐音量"));
            presentation.BuildToggleRow(content, CatCafeUserSettings.VolumeLabels, musicButtons,
                delegate(int index)
                {
                    CatCafeUserSettings.MusicVolume = CatCafeUserSettings.VolumeSteps[index];
                    RefreshSettingsToggles();
                });

            MakeSectionLabel(content, CatCafeConfigDatabase.GetString("ui_home_sfx_label", "音效音量"));
            presentation.BuildToggleRow(content, CatCafeUserSettings.VolumeLabels, sfxButtons,
                delegate(int index)
                {
                    CatCafeUserSettings.SfxVolume = CatCafeUserSettings.VolumeSteps[index];
                    RefreshSettingsToggles();
                });

            AddSpacer(content, 10f);
            // 字条只在这里留一个入口，具体开关在二级面板里。
            presentation.CreateButton(content,
                CatCafeConfigDatabase.GetString("ui_home_note_entry", "房东奶奶的字条"),
                OpenNoteSettings, 0f, 56f, PaperButtonRole.Secondary);
            presentation.CreateButton(content,
                CatCafeConfigDatabase.GetString("ui_home_back_to_start", "回到开始界面"),
                RequestBackToStart, 0f, 56f, PaperButtonRole.Leave);
            presentation.CreateButton(content,
                CatCafeConfigDatabase.GetString("ui_home_settings_close", "关 闭"),
                CloseSettings, 0f, 52f, PaperButtonRole.Primary);

            settingsOverlay.SetActive(false);
        }

        /// <summary>房东字条的二级面板：状态说明 + 重看 / 收起。</summary>
        private void BuildNoteSettingsOverlay()
        {
            noteSettingsOverlay = BuildPaperPanel("NoteSettingsOverlay",
                CatCafeConfigDatabase.GetString("tutorial_note_title", "房东奶奶的字条"),
                new Vector2(660f, 520f), out Transform content);

            noteSettingsState = MakeText(string.Empty, content, 15,
                new Color(0.34f, 0.23f, 0.15f), TextAnchor.UpperCenter);
            AddFixedHeight(noteSettingsState.gameObject, 132f);

            presentation.CreateButton(content,
                CatCafeConfigDatabase.GetString("ui_home_note_replay", "重新阅读全部字条"),
                ReplayNotes, 0f, 56f, PaperButtonRole.Primary);
            // 回看：不清已读记号，只是把每张字条的出现时机和内容翻出来看看。
            presentation.CreateButton(content,
                CatCafeConfigDatabase.GetString("ui_home_note_archive", "回看字条内容"),
                OpenNoteArchive, 0f, 56f, PaperButtonRole.Secondary);
            noteSkipButton = presentation.CreateButton(content,
                CatCafeConfigDatabase.GetString("ui_home_note_skip", "暂时收起字条"),
                RequestSkipNotes, 0f, 52f, PaperButtonRole.Secondary);
            noteSkipCancelButton = presentation.CreateButton(content,
                CatCafeConfigDatabase.GetString("ui_home_note_skip_cancel", "再想想"),
                CancelSkipNotes, 0f, 52f, PaperButtonRole.Secondary);
            noteSkipCancelButton.gameObject.SetActive(false);
            noteCloseButton = presentation.CreateButton(content,
                CatCafeConfigDatabase.GetString("ui_home_note_back", "返 回"),
                CloseNoteSettings, 0f, 52f, PaperButtonRole.Leave);

            noteSettingsOverlay.SetActive(false);
        }

        /// <summary>纸艺弹层外壳。实现在 CatCafePresentation，开始界面与局内共用同一份。</summary>
        private GameObject BuildPaperPanel(string name, string title, Vector2 size, out Transform content)
        {
            return presentation.BuildPaperPanel(canvas.transform, name, title, size, out content);
        }

        private void AddSpacer(Transform parent, float height)
        {
            GameObject spacer = NewUi("Spacer", parent);
            AddFixedHeight(spacer, height);
        }

        private void MakeSectionLabel(Transform parent, string text)
        {
            Text label = MakeText(text, parent, 17, new Color(0.34f, 0.23f, 0.15f), TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            AddFixedHeight(label.gameObject, 30f);
        }

        private void RefreshSettingsToggles()
        {
            presentation.MarkToggleGroup(screenButtons, CatCafeUserSettings.Fullscreen ? 1 : 0);
            presentation.MarkToggleGroup(musicButtons,
                CatCafeUserSettings.NearestVolumeStep(CatCafeUserSettings.MusicVolume));
            presentation.MarkToggleGroup(sfxButtons,
                CatCafeUserSettings.NearestVolumeStep(CatCafeUserSettings.SfxVolume));
        }

        private void OpenSettings()
        {
            RefreshSettingsToggles();
            settingsOverlay.SetActive(true);
        }

        private void CloseSettings()
        {
            settingsOverlay.SetActive(false);
        }

        private void OpenNoteSettings()
        {
            CancelSkipNotes();
            noteSettingsOverlay.transform.SetAsLastSibling();
            noteSettingsOverlay.SetActive(true);
        }

        private void CloseNoteSettings()
        {
            noteSettingsOverlay.SetActive(false);
        }

        /// <summary>回到开始界面。进度都已经落盘，这里只是换个场景，不销毁任何存档。</summary>
        private void RequestBackToStart()
        {
            CatCafeMeta.SaveNow();
            SceneManager.LoadScene(
                CatCafeConfigDatabase.GetString("scene_start", "CatCafeStart"));
        }

        /* ══════════════════ 房东字条开关 ══════════════════ */

        /// <summary>第一次点＝问一句，第二次点＝真收起。面板本身就是弹层，不再额外套一层确认。</summary>
        private void RequestSkipNotes()
        {
            if (tutorialNotes == null) return;
            if (!noteSkipArmed)
            {
                noteSkipArmed = true;
                CatCafeConfigDatabase.TutorialRow row =
                    CatCafeConfigDatabase.GetTutorialByTrigger("manual_skip_confirm");
                noteSettingsState.text = row != null
                    ? row.copy
                    : "以后不再显示房东奶奶留下的字条。确定要把它们先收起来吗？";
                SetButtonLabel(noteSkipButton,
                    CatCafeConfigDatabase.GetString("ui_home_note_skip_confirm", "确定收起"));
                noteSkipCancelButton.gameObject.SetActive(true);
                noteCloseButton.gameObject.SetActive(false);
                return;
            }
            tutorialNotes.SkipAll();
            CancelSkipNotes();
            Toast(CatCafeConfigDatabase.GetString("ui_home_note_skip_toast", "已收起后续房东字条"));
        }

        /// <summary>
        /// 改按钮文字。大厅面板里纸艺按钮用 TMP、其余旧控件仍是 Legacy Text，
        /// 只找其中一种会在换皮后直接抛空引用（就是这次字条按钮点不动的原因）。
        /// </summary>
        private static void SetButtonLabel(Button button, string text)
        {
            if (button == null) return;
            TMPro.TMP_Text tmp = button.GetComponentInChildren<TMPro.TMP_Text>();
            if (tmp != null) { tmp.text = text; return; }
            Text legacy = button.GetComponentInChildren<Text>();
            if (legacy != null) legacy.text = text;
        }

        private void CancelSkipNotes()
        {
            noteSkipArmed = false;
            SetButtonLabel(noteSkipButton,
                CatCafeConfigDatabase.GetString("ui_home_note_skip", "暂时收起字条"));
            if (noteSkipCancelButton != null) noteSkipCancelButton.gameObject.SetActive(false);
            if (noteCloseButton != null) noteCloseButton.gameObject.SetActive(true);
            RefreshNoteSettings();
        }

        private void RefreshNoteSettings()
        {
            if (noteSettingsState == null) return;
            int total = 0;
            int read = 0;
            CatCafeConfigDatabase.TutorialRow[] rows = CatCafeConfigDatabase.Data.tutorials;
            for (int i = 0; i < rows.Length; i++)
            {
                if (!rows[i].enabled || !rows[i].once) continue;
                total++;
                if (CatCafeMeta.HasReadTutorial(rows[i].id)) read++;
            }
            noteSettingsState.text = (!CatCafeUserSettings.TutorialEnabled
                    ? "现在字条是收起来的，房东奶奶不会再出声。"
                    : "字条正常显示中，遇到没见过的事她会留一张。")
                + "\n已经读过 " + read + " / " + total + " 张。"
                + "\n\n重新阅读会把已读记号全部清掉，之后再遇到同样的事会重新出现。";
        }

        /// <summary>字条回看：列出每张字条会在什么时候出现和写了什么，已读/未读一并标注。</summary>
        private void OpenNoteArchive()
        {
            if (noteArchiveOverlay == null)
            {
                noteArchiveOverlay = BuildOverlayShell("NoteArchiveOverlay",
                    CatCafeConfigDatabase.GetString("ui_home_note_archive_title", "奶 奶 的 字 条"),
                    out noteArchiveContent, out noteArchivePanelRect);
            }

            RebuildNoteArchive();
            noteArchiveOverlay.transform.SetAsLastSibling();
            noteArchiveOverlay.SetActive(true);
        }

        private void RebuildNoteArchive()
        {
            ClearChildren(noteArchiveContent);
            VerticalLayoutGroup layout = noteArchiveContent.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = noteArchiveContent.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(16, 16, 14, 14);
                layout.spacing = 12f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                ContentSizeFitter fitter = noteArchiveContent.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            CatCafeConfigDatabase.TutorialRow[] rows = CatCafeConfigDatabase.Data.tutorials;
            for (int i = 0; i < rows.Length; i++)
            {
                CatCafeConfigDatabase.TutorialRow row = rows[i];
                // 与二级面板的已读统计同一口径：设置面板自身的确认/引导（once=false）不算字条。
                if (!row.enabled || !row.once) continue;
                CreateNoteArchiveEntry(row);
            }
        }

        private void CreateNoteArchiveEntry(CatCafeConfigDatabase.TutorialRow row)
        {
            bool hasRead = CatCafeMeta.HasReadTutorial(row.id);
            GameObject entry = NewUi("Note_" + row.id, noteArchiveContent);
            Image paper = entry.AddComponent<Image>();
            PixelFrame(paper, new Color(0.97f, 0.92f, 0.78f, 1f));
            VerticalLayoutGroup layout = entry.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 12, 14);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            string appear = string.IsNullOrEmpty(row.appear_note)
                ? CatCafeConfigDatabase.GetString("ui_home_note_archive_no_condition", "出现时机待补充")
                : row.appear_note;
            Text header = MakeText(
                "出现时机：" + appear + (hasRead ? "　·　已读" : "　·　未读"),
                entry.transform, 15,
                hasRead ? new Color(0.52f, 0.42f, 0.31f) : new Color(0.62f, 0.32f, 0.16f),
                TextAnchor.MiddleLeft);
            header.fontStyle = FontStyle.Bold;
            LayoutElement headerLayout = header.gameObject.AddComponent<LayoutElement>();
            headerLayout.minHeight = 24f;

            MakeText(row.copy, entry.transform, 17,
                new Color(0.28f, 0.19f, 0.12f), TextAnchor.UpperLeft);
        }

        private GameObject BuildOverlayShell(string name, string title, out Transform content, out RectTransform panelRectOut)
        {
            GameObject overlay = NewUi(name, canvas.transform);
            Image dim = overlay.AddComponent<Image>();
            dim.color = new Color(0.08f, 0.05f, 0.03f, 0.82f);
            Stretch(overlay.GetComponent<RectTransform>(), 0, 0, 0, 0);

            GameObject rearPage = NewUi("RearPaperLayer", overlay.transform);
            Image rearImage = rearPage.AddComponent<Image>();
            ApplyHomePaper(rearImage, "modal-main-v2", new Color(0.58f, 0.38f, 0.25f, 1f));
            AnchorRect(rearPage.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-18f, 10f), new Vector2(1500f, 842f));

            GameObject middlePage = NewUi("MiddlePaperLayer", overlay.transform);
            Image middleImage = middlePage.AddComponent<Image>();
            ApplyHomePaper(middleImage, "modal-main-v2", new Color(0.82f, 0.68f, 0.50f, 1f));
            AnchorRect(middlePage.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(14f, -8f), new Vector2(1492f, 834f));

            GameObject panel = NewUi("Panel", overlay.transform);
            Image panelImage = panel.AddComponent<Image>();
            ApplyHomePaper(panelImage, "modal-main-v2", new Color(1f, 0.96f, 0.86f, 1f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            AnchorRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1480f, 820f));
            panelRectOut = panelRect;

            GameObject ribbon = NewUi("TitleRibbon", panel.transform);
            Image ribbonImage = ribbon.AddComponent<Image>();
            ApplyHomePaper(ribbonImage, "title-ribbon-v2", Color.white);
            AnchorRect(ribbon.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, 12f), new Vector2(720f, 100f));
            Text titleText = MakeText(title, ribbon.transform, 27, new Color(1f, 0.93f, 0.78f), TextAnchor.MiddleCenter);
            titleText.fontStyle = FontStyle.Bold;
            Stretch(titleText.rectTransform, 70f, 18f, 70f, 20f);

            GameObject scrollObject = NewUi("Scroll", panel.transform);
            RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(38f, 86f);
            scrollRect.offsetMax = new Vector2(-38f, -92f);
            Image scrollBack = scrollObject.AddComponent<Image>();
            scrollBack.color = new Color(0.91f, 0.82f, 0.65f, 0.30f);
            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            // viewport 不显式指定时 ScrollRect 会拿自己的 rect 兜底，但裁剪要靠 RectMask2D，
            // 两者必须是同一个矩形，否则内容会滑出面板外。
            scrollObject.AddComponent<RectMask2D>();
            scroll.viewport = scrollRect;

            GameObject contentObject = NewUi("Content", scrollObject.transform);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            // 滚动手感：滚轮交给 CatCafeSmoothScroll 做指数缓动（灵敏度置 0 屏蔽 ScrollRect
            // 自带的硬跳），边界用 Clamped 钳住——滚到顶/底就停，不再弹出内容外露白。
            scroll.scrollSensitivity = 0f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate =
                CatCafeConfigDatabase.GetFloat("ui_home_dex_scroll_deceleration", 0.135f);
            CatCafeSmoothScroll smoothScroll = scrollObject.AddComponent<CatCafeSmoothScroll>();
            smoothScroll.Configure(scroll,
                CatCafeConfigDatabase.GetFloat("ui_home_dex_scroll_step", 110f),
                CatCafeConfigDatabase.GetFloat("ui_home_dex_scroll_smoothing", 12f));
            content = contentObject.transform;

            Button close = CreateButton(panel.transform, "关 闭", delegate { overlay.SetActive(false); }, 190f, 52f, false);
            RectTransform closeRect = close.GetComponent<RectTransform>();
            AnchorRect(closeRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(190f, 52f));
            Object.Destroy(close.GetComponent<LayoutElement>());

            overlay.SetActive(false);
            return overlay;
        }

        private void Toast(string message)
        {
            toastText.text = message;
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(HideToast());
        }

        private IEnumerator HideToast()
        {
            yield return new WaitForSecondsRealtime(2.2f);
            toastText.text = string.Empty;
        }

        private Sprite LoadSprite(string asset)
        {
            if (string.IsNullOrEmpty(asset)) return null;
            Sprite sprite;
            if (spriteCache.TryGetValue(asset, out sprite) && sprite != null) return sprite;
            sprite = Resources.Load<Sprite>("CatCafe/" + asset);
            if (sprite != null)
            {
                spriteCache[asset] = sprite;
                missingSpriteWarnings.Remove(asset);
                return sprite;
            }

            spriteCache.Remove(asset);
            if (missingSpriteWarnings.Add(asset))
            {
                Debug.LogError("[CatCafeUI] 配置资源加载失败：Resources/CatCafe/" + asset);
            }
            return sprite;
        }

        private static void AddFixedHeight(GameObject target, float height)
        {
            LayoutElement layoutElement = target.AddComponent<LayoutElement>();
            layoutElement.minHeight = height;
            layoutElement.preferredHeight = height;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
        }

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action,
            float width, float height, bool primary)
        {
            GameObject buttonObject = NewUi(label, parent);
            Image image = buttonObject.AddComponent<Image>();
            ApplyHomePaper(image, primary ? "button-primary-v2" : "button-secondary-v2", Color.white);
            Button button = buttonObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(action);
            LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
            if (width > 0f) { layoutElement.minWidth = width; layoutElement.preferredWidth = width; }
            layoutElement.minHeight = height;
            layoutElement.preferredHeight = height;
            Text text = MakeText(label, buttonObject.transform, 17,
                primary ? new Color(1f, 0.94f, 0.82f) : new Color(0.25f, 0.16f, 0.10f), TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            Stretch(text.rectTransform, 0, 0, 0, 0);
            buttonObject.AddComponent<CatCafeButtonFeedback>().Initialize();
            return button;
        }

        private void ApplyHomePaper(Image image, string asset, Color tint)
        {
            if (presentation != null)
            {
                presentation.ApplyNamedSkin(image, asset, tint);
                image.raycastTarget = true;
                return;
            }
            PixelFrame(image, tint);
        }

        private Text MakeText(string value, Transform parent, int size, Color color, TextAnchor alignment)
        {
            GameObject textObject = NewUi("Text", parent);
            Text label = textObject.AddComponent<Text>();
            label.font = uiFont;
            label.fontSize = CatCafeUiFontProvider.ScaleSize(size);
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void PixelFrame(Image image, Color fill)
        {
            image.color = fill;
            Outline outline = image.gameObject.GetComponent<Outline>();
            if (outline == null) outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.10f, 0.07f, 0.055f, 0.95f);
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = true;
        }

        private static void AnchorRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
