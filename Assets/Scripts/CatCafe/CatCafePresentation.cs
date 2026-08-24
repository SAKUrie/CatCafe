using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 一块 UI 底板的用途。调用方建的时候就知道自己在建什么，直接说明用途，
    /// 不要让表现层反过来靠节点名去猜。取不到对应素材时回退程序化框线并告警。
    /// </summary>
    public enum PaperSurface
    {
        /// <summary>明确要程序化框线，不参与纸艺皮肤。</summary>
        Procedural,
        /// <summary>不铺底，只占位。</summary>
        Transparent,
        /// <summary>弹层主面板。</summary>
        Modal,
        /// <summary>三选一奖励卡卡面。</summary>
        RewardCard,
        /// <summary>弹层标题绶带。</summary>
        TitleRibbon,
    }

    /// <summary>按钮的语义角色，决定用哪张纸艺按钮素材和文字配色。</summary>
    public enum PaperButtonRole
    {
        /// <summary>程序化色块按钮，用于档位格子这类不该套纸艺的地方。</summary>
        Procedural,
        /// <summary>主行动：营业、继续、确认。</summary>
        Primary,
        /// <summary>次要行动：关闭、跳过、取消、换一批。</summary>
        Secondary,
        /// <summary>离开当前流程：打烊、返回猫咖。</summary>
        Leave,
    }

    /// <summary>
    /// Owns shared runtime UI primitives, text styling and pixel cafe chrome.
    /// It deliberately contains no game rules or settlement decisions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafePresentation : MonoBehaviour
    {
        [Header("Settlement Glow")]
        [SerializeField]
        private Material pieceSoftGlowMaterial;
        private const int PanelTextureSize = 24;
        private const int FadeTextureWidth = 8;
        private const int FadeTextureHeight = 64;

        private Texture2D panelTexture;
        private Texture2D verticalFadeTexture;
        private Sprite panelSprite;
        private Texture2D glowTexture;
        private Sprite glowSprite;
        private CatCafePawnGlowAtlasMap pawnGlowAtlas;
        private Sprite verticalFadeSprite;
        private const string PaperSkinFolder = "CatCafe/PaperSkin/";

        private readonly Dictionary<PaperSurface, Sprite> surfaceSprites =
            new Dictionary<PaperSurface, Sprite>();
        private readonly Dictionary<PaperButtonRole, Sprite> buttonSprites =
            new Dictionary<PaperButtonRole, Sprite>();
        private readonly Dictionary<string, Sprite> rarityBadges =
            new Dictionary<string, Sprite>();
        private readonly HashSet<string> missingSkinWarnings = new HashSet<string>();
                private readonly Dictionary<string, Sprite> pawnGlowSprites =
            new Dictionary<string, Sprite>();
private bool initialized;

        public TMP_FontAsset UiFont { get; private set; }
        public Sprite VerticalFadeSprite { get { return verticalFadeSprite; } }
        public Sprite PanelSprite { get { return panelSprite; } }


public void Initialize()
        {
            if (initialized) return;

            UiFont = CatCafeUiFontProvider.TmpFont;

            panelSprite = CreatePanelSprite();
            verticalFadeSprite = CreateVerticalFadeSprite();

            // V2 素材按独立层导入：主纸板、标题、卡片和按钮互不烘焙在同一张图里。
            surfaceSprites[PaperSurface.Modal] = LoadSkin("modal-main-v2");
            surfaceSprites[PaperSurface.RewardCard] = LoadSkin("reward-card-v2");
            surfaceSprites[PaperSurface.TitleRibbon] = LoadSkin("title-ribbon-v2");
            buttonSprites[PaperButtonRole.Primary] = LoadSkin("button-primary-v2");
            buttonSprites[PaperButtonRole.Secondary] = LoadSkin("button-secondary-v2");
            buttonSprites[PaperButtonRole.Leave] = LoadSkin("button-leave-v2");
            pawnGlowAtlas =
                Resources.Load<CatCafePawnGlowAtlasMap>(
                    "CatCafe/PawnGlow/CatCafePawnGlowAtlasMap");

            initialized = true;
        }

        private static Sprite LoadSkin(string asset)
        {
            // 表格可直接给出 Resources 相对路径；旧配置未写路径时仍从通用纸艺目录读取。
            string resourcePath = asset.IndexOf('/') >= 0 ? asset : PaperSkinFolder + asset;
            return Resources.Load<Sprite>(resourcePath);
        }

        public void ApplyNamedSkin(Image image, string asset, Color tint)
        {
            if (image == null) return;
            Sprite sprite = LoadSkin(asset);
            if (sprite == null)
            {
                WarnMissing("NamedPaperLayer", asset);
                PixelFrame(image, tint);
                return;
            }

            ApplyPaperSprite(image, sprite);
            image.color = tint;
        }

        /// <summary>
        /// 稀有度徽章。素材名由 Rarities 表的 badge 列给出，美术加新品质不用改代码。
        /// </summary>
        public Sprite RarityBadgeSprite(string rarityKey)
        {
            if (string.IsNullOrEmpty(rarityKey)) return null;

            Sprite cached;
            if (rarityBadges.TryGetValue(rarityKey, out cached)) return cached;

            string asset = CatCafeConfigDatabase.RarityBadge(rarityKey);
            Sprite sprite = string.IsNullOrEmpty(asset) ? null : LoadSkin(asset);
            if (sprite == null && !string.IsNullOrEmpty(asset))
            {
                WarnMissing("Rarities." + rarityKey + ".badge", asset);
            }

            rarityBadges[rarityKey] = sprite;
            return sprite;
        }

        /// <summary>
        /// 按用途铺底。有对应纸艺素材就用素材，没有就回退程序化框线——
        /// 但会告警一次，不像以前那样静默降级。
        /// </summary>
        public void ApplySurface(Image image, PaperSurface surface, Color proceduralFill)
        {
            if (image == null) return;

            if (surface == PaperSurface.Transparent)
            {
                image.sprite = null;
                image.color = Color.clear;
                return;
            }

            if (surface != PaperSurface.Procedural)
            {
                Sprite sprite;
                if (surfaceSprites.TryGetValue(surface, out sprite) && sprite != null)
                {
                    ApplyPaperSprite(image, sprite);
                    return;
                }

                WarnMissing("PaperSurface." + surface, "(见 Initialize 的加载列表)");
            }

            PixelFrame(image, proceduralFill);
        }

        private void WarnMissing(string requestedBy, string asset)
        {
            string key = requestedBy + "|" + asset;
            if (!missingSkinWarnings.Add(key)) return;

            string resourcePath = asset.IndexOf('/') >= 0 ? asset : PaperSkinFolder + asset;
            Debug.LogWarning("[CatCafeUI] 纸艺素材缺失，已回退程序化框线：" +
                requestedBy + " -> Resources/" + resourcePath);
        }

        public GameObject NewUi(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        public Button CreateButton(
            Transform parent,
            string label,
            UnityEngine.Events.UnityAction action,
            float width,
            float height,
            PaperButtonRole role = PaperButtonRole.Procedural)
        {
            GameObject buttonObject = NewUi(label, parent);
            Image image = buttonObject.AddComponent<Image>();

            Sprite buttonSkin;
            if (role != PaperButtonRole.Procedural &&
                buttonSprites.TryGetValue(role, out buttonSkin) && buttonSkin != null)
            {
                ApplyPaperSprite(image, buttonSkin);
            }
            else
            {
                if (role != PaperButtonRole.Procedural)
                {
                    WarnMissing("PaperButtonRole." + role, "(见 Initialize 的加载列表)");
                }

                PixelFrame(image, ButtonFill(role));
            }

            Button button = buttonObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(action);

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.minHeight = height;
            layout.preferredHeight = height;

            TMP_Text text = MakeText(label, buttonObject.transform, 20,
                ButtonTextColor(role), TextAnchor.MiddleCenter);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform, 0, 0, 0, 0);

            CatCafeButtonFeedback feedback = buttonObject.AddComponent<CatCafeButtonFeedback>();
            feedback.Initialize();
            return button;
        }

        public TMP_Text MakeText(
            string value,
            Transform parent,
            int size,
            Color color,
            TextAnchor alignment)
        {
            GameObject textObject = NewUi("Text", parent);
            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.font = UiFont;
            float scaledSize = CatCafeUiFontProvider.ScaleSize(size);
            label.fontSize = scaledSize;
            label.color = color;
            label.alignment = ToTextAlignment(alignment);
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(9f, scaledSize - 6f);
            label.fontSizeMax = scaledSize;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }

        public TMP_Text CreateLabelFrame(
            Transform parent,
            string name,
            string value,
            int size,
            Color color,
            TextAnchor alignment,
            float height,
            float flexibleHeight = 0f,
            PaperSurface surface = PaperSurface.Procedural)
        {
            GameObject frame = NewUi(name, parent);
            Image image = frame.AddComponent<Image>();
            ApplySurface(image, surface, new Color(0.87f, 0.81f, 0.66f, 1f));
            image.raycastTarget = false;

            LayoutElement layout = frame.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = flexibleHeight;

            TMP_Text label = MakeText(value, frame.transform, size, color, alignment);
            Stretch(label.rectTransform, 10, 4, 10, 4);
            return label;
        }

        /// <summary>程序化框线。这里不再暗中改用纸艺素材——要纸艺就显式调 ApplySurface。</summary>
        public void PixelFrame(Image image, Color fill)
        {
            if (image == null) return;

            image.sprite = panelSprite;
            image.type = Image.Type.Sliced;
            image.color = fill;

            Outline outline = image.gameObject.GetComponent<Outline>();
            if (outline == null) outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.08f, 0.055f, 0.04f, 0.92f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            outline.useGraphicAlpha = true;

            Shadow shadow = image.gameObject.GetComponent<Shadow>();
            if (shadow == null) shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.03f, 0.018f, 0.012f, 0.42f);
            shadow.effectDistance = new Vector2(4f, -5f);
            shadow.useGraphicAlpha = true;

            AddInnerBevel(image);
            AddTopHighlight(image);
        }

        public void GlowFrame(Image image, Color glow, float alpha, float distance)
        {
            if (image == null) return;

            if (glowSprite == null) glowSprite = CreateGlowSprite();
            image.sprite = glowSprite;
            image.type = Image.Type.Sliced;
            // 使用透明中心的框线精灵，保留 CanvasRenderer 的可见 alpha，
            // 避免像 Color.clear 一样把 Outline 整个裁掉。
            image.color = new Color(
                glow.r, glow.g, glow.b, Mathf.Clamp01(alpha * 0.62f));
            image.raycastTarget = false;

            Outline outline = image.gameObject.GetComponent<Outline>();
            if (outline == null) outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(
                glow.r, glow.g, glow.b, Mathf.Clamp01(alpha));
            outline.effectDistance = new Vector2(distance, distance);
            outline.useGraphicAlpha = false;

            Shadow shadow = image.gameObject.GetComponent<Shadow>();
            if (shadow != null) shadow.enabled = false;
        }

public void ShapeGlow(
            Image image,
            Sprite pieceSprite,
            Color glow,
            float alpha)
        {
            if (image == null || pieceSprite == null)
            {
                return;
            }

            Material glowMaterial = PieceSoftGlowMaterial(
                pieceSprite,
                image.rectTransform.rect.size,
                out Sprite atlasSprite);
            if (glowMaterial == null || atlasSprite == null)
            {
                image.enabled = false;
                return;
            }

            image.enabled = true;
            image.sprite = atlasSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.material = glowMaterial;
            image.SetMaterialDirty();
            image.SetVerticesDirty();
            image.maskable = false;
            image.raycastTarget = false;
            image.color = new Color(
                glow.r,
                glow.g,
                glow.b,
                Mathf.Clamp01(alpha));

            Shadow[] effects = image.GetComponents<Shadow>();
            for (int i = 0; i < effects.Length; i++)
            {
                effects[i].enabled = false;
            }
        }

public void ShapeEdgeGlow(
            Image image, Sprite pieceSprite, Color glow, float alpha, float distance)
        {
            if (image == null || pieceSprite == null) return;

            // A standard UI material keeps a crisp, shape-aware edge visible even on
            // platforms where the derivative-based soft shader is unavailable.
            image.sprite = pieceSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.material = null;
            image.color = new Color(
                glow.r, glow.g, glow.b, Mathf.Clamp01(alpha * 0.72f));
            image.raycastTarget = false;

            Outline outline = image.gameObject.GetComponent<Outline>();
            if (outline == null) outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(
                glow.r, glow.g, glow.b, Mathf.Clamp01(alpha * 1.55f));
            outline.effectDistance = new Vector2(distance, distance);
            outline.useGraphicAlpha = false;

            Shadow shadow = image.gameObject.GetComponent<Shadow>();
            if (shadow != null) shadow.enabled = false;
        }


private Material PieceSoftGlowMaterial(
            Sprite pieceSprite,
            Vector2 baseSize,
            out Sprite atlasSprite)
        {
            atlasSprite = null;
            if (pieceSprite == null)
            {
                return null;
            }

            if (pawnGlowAtlas == null)
            {
                pawnGlowAtlas =
                    Resources.Load<CatCafePawnGlowAtlasMap>(
                        "CatCafe/PawnGlow/CatCafePawnGlowAtlasMap");
            }

            if (pawnGlowAtlas == null)
            {
                return null;
            }

            string atlasKey = pieceSprite.name;
            bool found = pawnGlowAtlas.TryGetRegion(
                atlasKey,
                out Texture2D rgbaAtlas,
                out Rect atlasRect);
            if (!found &&
                pieceSprite.texture != null &&
                pieceSprite.texture.name != atlasKey)
            {
                atlasKey = pieceSprite.texture.name;
                found = pawnGlowAtlas.TryGetRegion(
                    atlasKey,
                    out rgbaAtlas,
                    out atlasRect);
            }

            if (!found || rgbaAtlas == null)
            {
                return null;
            }

            if (!pawnGlowSprites.TryGetValue(
                    atlasKey,
                    out atlasSprite) ||
                atlasSprite == null)
            {
                Rect pixelRect = new Rect(
                    Mathf.Round(atlasRect.x * rgbaAtlas.width),
                    Mathf.Round(atlasRect.y * rgbaAtlas.height),
                    Mathf.Round(atlasRect.width * rgbaAtlas.width),
                    Mathf.Round(atlasRect.height * rgbaAtlas.height));
                atlasSprite = Sprite.Create(
                    rgbaAtlas,
                    pixelRect,
                    new Vector2(0.5f, 0.5f),
                    pawnGlowAtlas.ContentSize,
                    0,
                    SpriteMeshType.FullRect);
                atlasSprite.name =
                    atlasKey + " RGBA Glow Sprite";
                pawnGlowSprites[atlasKey] = atlasSprite;
            }

            Shader shader =
                Shader.Find("UI/CatCafePieceAlphaGlow");
            if (shader == null && pieceSoftGlowMaterial == null)
            {
                return null;
            }

            Material material = pieceSoftGlowMaterial != null
                ? new Material(pieceSoftGlowMaterial)
                : new Material(shader);
            material.name =
                atlasKey + " RGBA Atlas Glow";
            // Keep the generated RGBA atlas explicit on the runtime material.
            // CanvasRenderer normally supplies _MainTex from Image.sprite, but
            // that implicit binding is not reliable for a runtime-created atlas
            // sprite and would make the shader sample the material's empty
            // default texture instead of the generated alpha mask.
            material.SetTexture(
                "_MainTex",
                rgbaAtlas);
            material.SetTexture(
                "_PawnAtlas",
                rgbaAtlas);
            material.SetVector(
                "_AtlasUvRect",
                new Vector4(
                    atlasRect.x,
                    atlasRect.y,
                    atlasRect.width,
                    atlasRect.height));
            material.SetFloat(
                "_AtlasContentSize",
                pawnGlowAtlas.ContentSize);
            material.SetVector(
                "_GlowBaseSize",
                new Vector4(
                    baseSize.x,
                    baseSize.y,
                    0f,
                    0f));

            return material;
        }




        private static void ApplyPaperSprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
            image.color = Color.white;

            Outline outline = image.gameObject.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
            Shadow shadow = image.gameObject.GetComponent<Shadow>();
            if (shadow != null) shadow.enabled = false;
        }

        public void AnchorRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// 纸艺系统弹层外壳：压暗遮罩 + Modal 纸面 + 绶带标题，返回的 content 就是纸面本身，
        /// 直接往里加子节点即可（纸面自带 VerticalLayoutGroup）。
        ///
        /// 开始界面、大厅、局内的系统面板共用这一份，免得三处各拼一遍纸面又慢慢长歪。
        /// 标题用浅墨：绶带素材是深棕的，深墨字压上去基本看不清。
        /// </summary>
        public GameObject BuildPaperPanel(Transform parent, string name, string title,
            Vector2 size, out Transform content)
        {
            GameObject overlay = NewUi(name, parent);
            Image dim = overlay.AddComponent<Image>();
            dim.color = new Color(0.10f, 0.065f, 0.04f, 0.78f);
            Stretch(overlay.GetComponent<RectTransform>(), 0, 0, 0, 0);

            GameObject panel = NewUi("Panel", overlay.transform);
            Image panelImage = panel.AddComponent<Image>();
            ApplySurface(panelImage, PaperSurface.Modal, new Color(0.95f, 0.87f, 0.71f, 1f));
            AnchorRect(panel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(46, 46, 26, 30);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // 标题尺寸跟局内弹层一致：绶带素材接近 5:1，压到 62 高会明显变形。
            CreateLabelFrame(panel.transform, "Title", title, 27,
                new Color(1f, 0.92f, 0.76f), TextAnchor.MiddleCenter, 88f, 0f,
                PaperSurface.TitleRibbon);

            content = panel.transform;
            return overlay;
        }

        /// <summary>
        /// 一排离散档位按钮（音量 / 演出速度 / 显示模式 / 开关）。开始界面、大厅、局内共用这一份。
        ///
        /// 刻意用 Procedural 色块而不是纸艺按钮素材：选中态靠 <see cref="MarkToggle"/>
        /// 换底色来表达，纸艺贴图乘上颜色会脏，只改文字颜色又几乎看不出来选了哪个。
        /// 也不用 uGUI Slider——程序化拼要四层节点，这些设置本来就是离散档。
        /// </summary>
        public void BuildToggleRow(Transform parent, string[] labels, List<Button> sink,
            System.Action<int> onSelect, float rowWidth = 360f, float buttonHeight = 42f)
        {
            GameObject row = NewUi("ToggleRow", parent);
            LayoutElement size = row.AddComponent<LayoutElement>();
            size.minWidth = rowWidth;
            size.preferredWidth = rowWidth;
            size.flexibleWidth = 0f;
            size.minHeight = buttonHeight + 4f;
            size.preferredHeight = buttonHeight + 4f;
            size.flexibleHeight = 0f;

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            sink.Clear();
            float width = labels.Length > 3 ? 66f : 104f;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                sink.Add(CreateButton(row.transform, labels[i],
                    delegate { onSelect(index); }, width, buttonHeight));
            }
        }

        /// <summary>把一排档位按钮的选中态刷成 selected 那一个。</summary>
        public void MarkToggleGroup(List<Button> buttons, int selected)
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Count; i++) MarkToggle(buttons[i], i == selected);
        }

        public void MarkToggle(Button button, bool selected)
        {
            if (button == null) return;

            Color background = selected
                ? new Color(0.55f, 0.24f, 0.15f, 1f)
                : new Color(0.82f, 0.72f, 0.53f, 1f);
            Color labelColor = selected
                ? new Color(1f, 0.94f, 0.82f, 1f)
                : new Color(0.25f, 0.16f, 0.10f, 1f);

            // 交给反馈组件改常态色：它会打断正在播的 hover / 选中过渡，
            // 否则那条动画会拿旧的常态色把这里刚设的颜色盖回去。
            CatCafeButtonFeedback feedback = button.GetComponent<CatCafeButtonFeedback>();
            if (feedback != null)
            {
                feedback.SetRestingColors(background, labelColor);
                return;
            }

            Image image = button.targetGraphic as Image;
            if (image != null) image.color = background;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = labelColor;
        }

        public void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static Color ButtonFill(PaperButtonRole role)
        {
            switch (role)
            {
                case PaperButtonRole.Primary: return new Color(0.66f, 0.31f, 0.20f, 1f);
                case PaperButtonRole.Secondary: return new Color(0.86f, 0.75f, 0.54f, 1f);
                case PaperButtonRole.Leave: return new Color(0.47f, 0.25f, 0.18f, 1f);
                default: return new Color(0.58f, 0.36f, 0.23f, 1f);
            }
        }

        private static Color ButtonTextColor(PaperButtonRole role)
        {
            return role == PaperButtonRole.Secondary
                ? new Color(0.25f, 0.16f, 0.10f, 1f)
                : new Color(1f, 0.94f, 0.82f, 1f);
        }

        private void AddTopHighlight(Image image)
        {
            Transform existing = image.transform.Find("Top Highlight");
            if (existing != null) return;

            GameObject highlight = NewUi("Top Highlight", image.transform);
            highlight.transform.SetAsFirstSibling();
            Image highlightImage = highlight.AddComponent<Image>();
            highlightImage.sprite = panelSprite;
            highlightImage.type = Image.Type.Sliced;
            highlightImage.color = new Color(1f, 0.88f, 0.64f, 0.13f);
            highlightImage.raycastTarget = false;

            LayoutElement layout = highlight.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            RectTransform rect = highlight.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(4f, -7f);
            rect.offsetMax = new Vector2(-4f, -2f);
        }

private void AddInnerBevel(Image image)
        {
            Transform existing = image.transform.Find("Inner Shadow");
            if (existing != null) return;

            GameObject bevel = NewUi("Inner Shadow", image.transform);
            bevel.transform.SetAsLastSibling();
            Image bevelImage = bevel.AddComponent<Image>();
            bevelImage.sprite = panelSprite;
            bevelImage.type = Image.Type.Sliced;
            bevelImage.color = new Color(0.06f, 0.038f, 0.025f, 0.13f);
            bevelImage.raycastTarget = false;

            LayoutElement layout = bevel.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            RectTransform rect = bevel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 4f);
            rect.offsetMax = new Vector2(-4f, -4f);
        }


        private Sprite CreatePanelSprite()
        {
            panelTexture = new Texture2D(
                PanelTextureSize,
                PanelTextureSize,
                TextureFormat.RGBA32,
                false);
            panelTexture.name = "CatCafe Runtime 9 Slice";
            panelTexture.filterMode = FilterMode.Point;
            panelTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[PanelTextureSize * PanelTextureSize];
            int border = 5;
            for (int y = 0; y < PanelTextureSize; y++)
            {
                for (int x = 0; x < PanelTextureSize; x++)
                {
                    bool edge = x < border || x >= PanelTextureSize - border ||
                        y < border || y >= PanelTextureSize - border;
                    pixels[y * PanelTextureSize + x] = edge
                        ? new Color(0.72f, 0.62f, 0.45f, 1f)
                        : Color.white;
                }
            }

            panelTexture.SetPixels(pixels);
            panelTexture.Apply(false, true);
            return Sprite.Create(
                panelTexture,
                new Rect(0f, 0f, PanelTextureSize, PanelTextureSize),
                new Vector2(0.5f, 0.5f),
                1f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }

        private Sprite CreateGlowSprite()
        {
            const int textureSize = 24;
            const int border = 4;

            glowTexture = new Texture2D(
                textureSize, textureSize, TextureFormat.RGBA32, false);
            glowTexture.name = "CatCafe Runtime Glow Frame";
            glowTexture.filterMode = FilterMode.Bilinear;
            glowTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[textureSize * textureSize];
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    bool edge = x < border || x >= textureSize - border ||
                        y < border || y >= textureSize - border;
                    pixels[y * textureSize + x] = edge
                        ? Color.white
                        : Color.clear;
                }
            }

            glowTexture.SetPixels(pixels);
            glowTexture.Apply(false, true);
            return Sprite.Create(
                glowTexture,
                new Rect(0f, 0f, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                1f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }


        private Sprite CreateVerticalFadeSprite()
        {
            verticalFadeTexture = new Texture2D(
                FadeTextureWidth,
                FadeTextureHeight,
                TextureFormat.RGBA32,
                false);
            verticalFadeTexture.name = "CatCafe Runtime Vertical Fade";
            verticalFadeTexture.filterMode = FilterMode.Bilinear;
            verticalFadeTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[FadeTextureWidth * FadeTextureHeight];
            for (int y = 0; y < FadeTextureHeight; y++)
            {
                float alpha = y / (FadeTextureHeight - 1f);
                for (int x = 0; x < FadeTextureWidth; x++)
                {
                    pixels[y * FadeTextureWidth + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            verticalFadeTexture.SetPixels(pixels);
            verticalFadeTexture.Apply(false, true);
            return Sprite.Create(
                verticalFadeTexture,
                new Rect(0f, 0f, FadeTextureWidth, FadeTextureHeight),
                new Vector2(0.5f, 0.5f),
                1f);
        }

        private static TextAlignmentOptions ToTextAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

private void OnDestroy()
        {
            foreach (Sprite sprite in pawnGlowSprites.Values)
            {
                DestroyGenerated(sprite);
            }

            pawnGlowSprites.Clear();
            DestroyGenerated(panelSprite);
            DestroyGenerated(verticalFadeSprite);
            DestroyGenerated(glowSprite);
            DestroyGenerated(panelTexture);
            DestroyGenerated(verticalFadeTexture);
            DestroyGenerated(glowTexture);
        }

        private static void DestroyGenerated(Object value)
        {
            if (value != null) Destroy(value);
        }

}
}
