using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 挂在任意 UI 元素上的悬停触发器：指针进入时把一段内容交给宿主去显示，移出时收起。
    /// 自己不画任何东西——面板由宿主统一持有，避免每一行都建一份浮层。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeHoverTrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        private System.Action<RectTransform> onEnter;
        private System.Action onExit;

        public void Initialize(System.Action<RectTransform> enter, System.Action exit)
        {
            onEnter = enter;
            onExit = exit;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (onEnter != null) onEnter(transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (onExit != null) onExit();
        }

        private void OnDisable()
        {
            if (onExit != null) onExit();
        }
    }

    /// <summary>
    /// 猫咪悬停预览浮层：立绘 + 名称 + 稀有度 + 一句介绍。
    ///
    /// 「呼朋唤友」列表里目标猫是黑色剪影（还没请到），玩家在花掉绒毛和罐头之前
    /// 不知道请回来的是谁。悬停给出名称、稀有度和介绍，让这笔消费是明白的；
    /// 但立绘跟随解锁状态——没请到的仍是剪影，长相留到真正请回来那一刻。
    /// 浮层只有一份，跟着当前悬停的行走；位置会自动避开屏幕边缘。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeHoverPreview : MonoBehaviour
    {
        private const float Width = 300f;
        private const float Gap = 14f;
        /// <summary>与列表格子里未解锁猫用的是同一个压深色，两处观感要一致。</summary>
        private static readonly Color SilhouetteColor = new Color(0.12f, 0.09f, 0.08f, 1f);

        private RectTransform canvasRect;
        private RectTransform root;
        private CanvasGroup group;
        private Image portrait;
        private Text portraitFallback;
        private Text title;
        private Text meta;
        private Text body;
        private Font uiFont;
        private System.Func<string, Sprite> spriteLoader;

        public void Initialize(Canvas canvas, Font font, System.Func<string, Sprite> loader)
        {
            if (root != null || canvas == null) return;
            canvasRect = canvas.transform as RectTransform;
            uiFont = font;
            spriteLoader = loader;

            GameObject holder = NewUi("HoverPreview", canvas.transform);
            root = holder.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(Width, 360f);
            group = holder.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;   // 浮层不能挡住底下的按钮，否则悬停会闪
            group.interactable = false;

            Image back = holder.AddComponent<Image>();
            back.color = new Color(0.16f, 0.11f, 0.08f, 0.96f);
            back.raycastTarget = false;

            VerticalLayoutGroup layout = holder.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 14);
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = holder.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject frame = NewUi("Portrait", holder.transform);
            LayoutElement frameSize = frame.AddComponent<LayoutElement>();
            frameSize.minHeight = 150f;
            frameSize.preferredHeight = 150f;
            Image frameBack = frame.AddComponent<Image>();
            frameBack.color = new Color(0.27f, 0.21f, 0.17f, 1f);
            frameBack.raycastTarget = false;

            GameObject art = NewUi("Art", frame.transform);
            RectTransform artRect = art.GetComponent<RectTransform>();
            artRect.anchorMin = artRect.anchorMax = new Vector2(0.5f, 0.5f);
            artRect.pivot = new Vector2(0.5f, 0.5f);
            artRect.anchoredPosition = Vector2.zero;
            artRect.sizeDelta = new Vector2(138f, 138f);
            portrait = art.AddComponent<Image>();
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;

            portraitFallback = MakeText(string.Empty, frame.transform, 34,
                new Color(0.80f, 0.73f, 0.60f), TextAnchor.MiddleCenter);
            Stretch(portraitFallback.rectTransform, 6, 6, 6, 6);

            title = MakeText(string.Empty, holder.transform, 20,
                new Color(0.97f, 0.92f, 0.80f), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            AddHeight(title.gameObject, 28f);

            meta = MakeText(string.Empty, holder.transform, 14,
                new Color(0.80f, 0.72f, 0.58f), TextAnchor.MiddleCenter);
            AddHeight(meta.gameObject, 22f);

            body = MakeText(string.Empty, holder.transform, 14,
                new Color(0.87f, 0.81f, 0.70f), TextAnchor.UpperLeft);
            LayoutElement bodySize = body.gameObject.AddComponent<LayoutElement>();
            bodySize.minHeight = 40f;
            bodySize.flexibleHeight = 1f;

            root.gameObject.SetActive(false);
        }

        /// <summary>
        /// 显示某只猫的预览，浮层贴在 anchor 右侧；右边放不下就翻到左侧。
        /// revealed=false 时立绘保持剪影——还没请到的猫留个悬念，
        /// 但名称、稀有度和介绍照给，玩家仍能判断这笔绒毛和罐头值不值。
        /// </summary>
        public void Show(CatCatalog.CatRow row, string rarityLabel, string intro,
            RectTransform anchor, bool revealed)
        {
            if (root == null || row == null) return;

            title.text = row.Name;
            meta.text = rarityLabel;
            body.text = intro;

            Sprite sprite = spriteLoader != null ? spriteLoader(row.Asset) : null;
            portrait.gameObject.SetActive(sprite != null);
            portraitFallback.gameObject.SetActive(sprite == null);
            if (sprite != null)
            {
                portrait.sprite = sprite;
                portrait.color = revealed ? Color.white : SilhouetteColor;
            }
            else
            {
                portraitFallback.text = revealed ? row.Name : "？";
            }

            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            Place(anchor);
        }

        public void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        /// <summary>贴着锚点摆，并夹回画布内——列表最后一行的浮层不能掉到屏幕外面。</summary>
        private void Place(RectTransform anchor)
        {
            if (anchor == null || canvasRect == null) return;

            Vector3[] corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
                    RectTransformUtility.WorldToScreenPoint(null, corners[i]), null, out local);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }

            Rect full = canvasRect.rect;
            Vector2 size = root.rect.size;
            root.pivot = new Vector2(0f, 0.5f);
            float x = max.x + Gap;
            if (x + size.x > full.xMax) x = min.x - Gap - size.x;   // 右边放不下就翻到左边
            x = Mathf.Clamp(x, full.xMin + 4f, full.xMax - size.x - 4f);
            float y = Mathf.Clamp((min.y + max.y) * 0.5f,
                full.yMin + size.y * 0.5f + 4f, full.yMax - size.y * 0.5f - 4f);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(x, y);
        }

        private static void AddHeight(GameObject target, float height)
        {
            LayoutElement element = target.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
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

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
