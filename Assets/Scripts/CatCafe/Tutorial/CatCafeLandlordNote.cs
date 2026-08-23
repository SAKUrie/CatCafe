using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 表驱动的房东奶奶字条。改触发时机前先读这三条约束：
    ///
    /// 1. 表是唯一权威。调用点只用 <see cref="Notify"/> / <see cref="Interject"/> 报告
    ///    "这件事刚发生"，弹不弹、弹几次由 Tutorials 表的 enabled/once 与存档已读位决定。
    ///    调用点不许再用局数、回合数这类计数自己判一遍——那会和已读位打架，
    ///    老存档一旦越过计数就永远看不到开局字条。
    /// 2. 只在静默点插话。Notify 进队列，由 Update 泵出，并且要等宿主的 gate 放行
    ///    （宿主在播弹层出入场、发牌、结算动画时关掉 gate）。真正需要"停在某一拍"的
    ///    字条走 Interject，由调用方的协程显式等它读完再继续演出。
    /// 3. 聚光只对准已经完成布局的目标。出场当帧强制刷新 Canvas 布局再取角点；
    ///    目标缺失或不可见时降级成全屏压暗，而不是画一个错位的框。
    ///
    /// 出场带淡入与一小段不可关闭窗口，避免玩家连点时字条刚亮起就被同一串点击收掉。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeLandlordNote : MonoBehaviour
    {
        private sealed class PendingNote
        {
            public CatCafeConfigDatabase.TutorialRow Row;
            /// <summary>聚光框住的目标，多个时取并集（例如"这一对挨在一起的猫和客人"）。</summary>
            public RectTransform[] Targets;
            /// <summary>Interject 排的队：调用方已经挑好拍点，不再等 gate，也插到队首。</summary>
            public bool Immediate;
            public bool Done;
        }

        private Canvas canvas;
        private RectTransform root;
        private CanvasGroup rootGroup;
        private RectTransform panel;
        private Text title;
        private Text body;
        private readonly Image[] dimmers = new Image[4];
        private readonly Image[] focusEdges = new Image[4];
        private readonly List<PendingNote> queue = new List<PendingNote>();
        private readonly Dictionary<string, RectTransform> targets = new Dictionary<string, RectTransform>();
        private PendingNote current;
        private Func<bool> gate;
        private float fadeElapsed;
        private float dismissableAt;
        private float nextAllowedShow;
        private bool spotlightLayoutPending;

        private static float FadeSeconds
        {
            get { return CatCafeConfigDatabase.GetFloat("tutorial_note_fade_seconds", 0.16f); }
        }

        /// <summary>出场后的不可关闭窗口，挡掉玩家触发字条那一串连点。</summary>
        private static float InputLockSeconds
        {
            get { return CatCafeConfigDatabase.GetFloat("tutorial_note_input_lock_seconds", 0.35f); }
        }

        /// <summary>两条字条之间的间隔，连续两条时不会看起来像闪了一下。</summary>
        private static float GapSeconds
        {
            get { return CatCafeConfigDatabase.GetFloat("tutorial_note_gap_seconds", 0.22f); }
        }

        public bool IsShowing { get { return current != null; } }

        public void Initialize(Canvas owner)
        {
            if (root != null || owner == null) return;
            canvas = owner;
            GameObject rootObject = NewUi("LandlordNoteOverlay", canvas.transform);
            root = rootObject.GetComponent<RectTransform>();
            Stretch(root, 0f, 0f, 0f, 0f);
            rootGroup = rootObject.AddComponent<CanvasGroup>();
            Image clickBlocker = rootObject.AddComponent<Image>();
            clickBlocker.color = new Color(0f, 0f, 0f, 0.001f);
            Button close = rootObject.AddComponent<Button>();
            close.targetGraphic = clickBlocker;
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(Dismiss);

            for (int i = 0; i < dimmers.Length; i++)
            {
                GameObject dim = NewUi("Dim" + i, root);
                dimmers[i] = dim.AddComponent<Image>();
                dimmers[i].color = new Color(0.055f, 0.035f, 0.025f, 0.78f);
                dimmers[i].raycastTarget = false;
            }
            for (int i = 0; i < focusEdges.Length; i++)
            {
                GameObject edge = NewUi("FocusEdge" + i, root);
                focusEdges[i] = edge.AddComponent<Image>();
                focusEdges[i].color = new Color(1f, 0.82f, 0.36f, 0.95f);
                focusEdges[i].raycastTarget = false;
            }

            GameObject panelObject = NewUi("LandlordNotePaper", root);
            panel = panelObject.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 42f);
            panel.sizeDelta = new Vector2(900f, 214f);
            Image paper = panelObject.AddComponent<Image>();
            paper.sprite = Resources.Load<Sprite>("CatCafe/PaperSkin/" +
                CatCafeConfigDatabase.GetString("tutorial_note_modal_sprite", "modal-main-v2"));
            paper.type = paper.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            paper.color = paper.sprite != null ? Color.white : new Color(0.91f, 0.81f, 0.63f, 1f);
            paper.raycastTarget = false;

            GameObject portraitObject = NewUi("LandlordPortrait", panel);
            RectTransform portraitRect = portraitObject.GetComponent<RectTransform>();
            portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0f, 0.5f);
            portraitRect.pivot = new Vector2(0f, 0.5f);
            portraitRect.anchoredPosition = new Vector2(
                CatCafeConfigDatabase.GetRequiredFloat("tutorial_note_portrait_x"),
                CatCafeConfigDatabase.GetRequiredFloat("tutorial_note_portrait_y"));
            portraitRect.sizeDelta = new Vector2(
                CatCafeConfigDatabase.GetRequiredFloat("tutorial_note_portrait_width"),
                CatCafeConfigDatabase.GetRequiredFloat("tutorial_note_portrait_height"));
            Image portrait = portraitObject.AddComponent<Image>();
            portrait.sprite = Resources.Load<Sprite>(
                CatCafeConfigDatabase.GetRequiredString("tutorial_note_portrait_resource"));
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            if (portrait.sprite == null)
            {
                Debug.LogError("[CatCafeTutorial] 房东奶奶立绘资源缺失：" +
                    CatCafeConfigDatabase.GetRequiredString("tutorial_note_portrait_resource"));
                portraitObject.SetActive(false);
            }

            title = MakeText(string.Empty,
                panel, 28, new Color(0.32f, 0.19f, 0.12f), TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            AnchorRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(-96f, 40f));
            SetHorizontalInsets(title.rectTransform,
                CatCafeConfigDatabase.GetRequiredFloat("tutorial_note_text_left_inset"),
                CatCafeConfigDatabase.GetRequiredFloat("tutorial_note_text_right_inset"));
            body = MakeText(string.Empty, panel, 25, new Color(0.25f, 0.16f, 0.11f), TextAnchor.MiddleLeft);
            body.lineSpacing = 1.15f;
            AnchorRect(body.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(-96f, -80f));
            SetHorizontalInsets(body.rectTransform,
                CatCafeConfigDatabase.GetRequiredFloat("tutorial_note_text_left_inset"),
                CatCafeConfigDatabase.GetRequiredFloat("tutorial_note_text_right_inset"));
            Text hint = MakeText("点任意处收起", panel, 16, new Color(0.47f, 0.35f, 0.25f), TextAnchor.MiddleRight);
            AnchorRect(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-86f, 28f));
            root.gameObject.SetActive(false);
        }

        public void RegisterTarget(string key, RectTransform target)
        {
            if (!string.IsNullOrEmpty(key) && target != null) targets[key] = target;
        }

        /// <summary>宿主用来声明"现在别插话"。返回 false 时空闲字条留在队列里等下一帧。</summary>
        public void SetGate(Func<bool> canShowNow)
        {
            gate = canShowNow;
        }

        /// <summary>报告一个事件。返回 true 表示这条字条已进队列（迟早会弹），false 表示这辈子不弹了。</summary>
        public bool Notify(string triggerKey, params RectTransform[] overrideTargets)
        {
            return Enqueue(triggerKey, overrideTargets, false) != null;
        }

        /// <summary>
        /// 演出协程里的"停一拍"。调用方 yield 等它读完再继续，因此不受 gate 限制，
        /// 并且会插到空闲字条前面——否则被 gate 挡住的空闲字条会把调用方永远堵在这里。
        /// </summary>
        public IEnumerator Interject(string triggerKey, params RectTransform[] overrideTargets)
        {
            PendingNote note = Enqueue(triggerKey, overrideTargets, true);
            if (note == null) yield break;
            while (!note.Done) yield return null;
        }

        public void SkipAll()
        {
            ReleaseAll();
            CatCafeUserSettings.TutorialEnabled = false;
        }

        public void ReplayAll()
        {
            ReleaseAll();
            CatCafeUserSettings.TutorialEnabled = true;
            CatCafeMeta.ResetTutorials();
        }

        public void ApplyEnabledPreference()
        {
            if (!CatCafeUserSettings.TutorialEnabled) ReleaseAll();
        }

        private void Update()
        {
            if (root == null) return;

            if (current != null)
            {
                if (fadeElapsed >= FadeSeconds) return;
                fadeElapsed += Time.unscaledDeltaTime;
                rootGroup.alpha = Mathf.Clamp01(fadeElapsed / Mathf.Max(0.0001f, FadeSeconds));
                return;
            }

            if (queue.Count == 0 || Time.unscaledTime < nextAllowedShow) return;

            int index = -1;
            for (int i = 0; i < queue.Count; i++)
            {
                if (!queue[i].Immediate) continue;
                index = i;
                break;
            }
            if (index < 0)
            {
                if (gate != null && !gate()) return;
                index = 0;
            }
            Show(index);
        }

        private void LateUpdate()
        {
            if (!spotlightLayoutPending || current == null || root == null || !root.gameObject.activeInHierarchy)
                return;

            // 大厅的固定设计根会在 LateUpdate 里按 16:9 画布完成首次缩放。
            // Show 发生在更早的 Update 时，目标世界角点可能仍挤在 (0,0)，因此等所有
            // 布局组件本帧落位后再读一次，避免聚光退化成看不见的小点。
            Canvas.ForceUpdateCanvases();
            LayoutSpotlight(current.Targets);
            spotlightLayoutPending = false;
        }

        private PendingNote Enqueue(string triggerKey, RectTransform[] overrideTargets, bool immediate)
        {
            if (root == null || !CatCafeUserSettings.TutorialEnabled) return null;
            CatCafeConfigDatabase.TutorialRow row = CatCafeConfigDatabase.GetTutorialByTrigger(triggerKey);
            if (row == null || (row.once && CatCafeMeta.HasReadTutorial(row.id))) return null;
            // 同一条字条可能被多个调用点报告（例如选牌与跳过都报告"选完了"）。
            // 已读位要等读完才写，所以这里必须自己去重，不然会连弹两次。
            if (current != null && current.Row == row) return null;
            for (int i = 0; i < queue.Count; i++) if (queue[i].Row == row) return null;

            // 调用方可能传进来一个 null（比如纸艺分支才有的按钮，legacy 分支下是空的）。
            // 先把 null 滤掉，否则"传了但全是空"会挡住表里配的聚光目标。
            List<RectTransform> given = new List<RectTransform>();
            for (int i = 0; overrideTargets != null && i < overrideTargets.Length; i++)
                if (overrideTargets[i] != null) given.Add(overrideTargets[i]);
            RectTransform[] resolved = given.Count > 0 ? given.ToArray() : null;
            if (resolved == null && !string.IsNullOrEmpty(row.spotlight_target))
            {
                RectTransform fromTable;
                if (targets.TryGetValue(row.spotlight_target, out fromTable)) resolved = new[] { fromTable };
            }
            PendingNote note = new PendingNote { Row = row, Targets = resolved, Immediate = immediate };
            queue.Add(note);
            return note;
        }

        private void Show(int index)
        {
            current = queue[index];
            queue.RemoveAt(index);
            // 纯机制说明不该由房东奶奶来背——她有自己的人设。
            // 走系统口吻的字条按 Tutorial.id 在表里点名（tutorial_system_voice_ids）。
            title.text = IsSystemVoice(current.Row.id)
                ? CatCafeConfigDatabase.GetString("tutorial_system_note_title", "系统提示")
                : CatCafeConfigDatabase.GetString("tutorial_note_title", "房东奶奶的字条");
            body.text = current.Row.copy;
            rootGroup.alpha = 0f;
            fadeElapsed = 0f;
            dismissableAt = Time.unscaledTime + InputLockSeconds;
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
            // 聚光要读目标的世界角点：先把本帧挂起的布局刷完，
            // 否则刚 BuildUi 完就报告的字条会框到一个还没排好的矩形上。
            Canvas.ForceUpdateCanvases();
            LayoutSpotlight(current.Targets);
            spotlightLayoutPending = true;
        }

        /// <summary>这条字条走系统口吻还是房东奶奶口吻。名单在表里，代码不硬编码 id。</summary>
        private static bool IsSystemVoice(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string list = CatCafeConfigDatabase.GetString("tutorial_system_voice_ids");
            if (string.IsNullOrEmpty(list)) return false;
            string[] ids = list.Split(',');
            for (int i = 0; i < ids.Length; i++)
                if (ids[i].Trim() == id) return true;
            return false;
        }

        private void Dismiss()
        {
            if (current == null || Time.unscaledTime < dismissableAt) return;
            if (current.Row.once) CatCafeMeta.MarkTutorialRead(current.Row.id);
            current.Done = true;
            current = null;
            spotlightLayoutPending = false;
            nextAllowedShow = Time.unscaledTime + GapSeconds;
            root.gameObject.SetActive(false);
        }

        /// <summary>收起全部字条。等在 Interject 上的协程必须一起放行，否则演出会卡死。</summary>
        private void ReleaseAll()
        {
            for (int i = 0; i < queue.Count; i++) queue[i].Done = true;
            queue.Clear();
            if (current != null) current.Done = true;
            current = null;
            spotlightLayoutPending = false;
            if (root != null) root.gameObject.SetActive(false);
        }

        private void LayoutSpotlight(RectTransform[] spotTargets)
        {
            RectTransform canvasRect = canvas.transform as RectTransform;
            Rect full = canvasRect.rect;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            Vector3[] corners = new Vector3[4];
            bool any = false;
            for (int t = 0; spotTargets != null && t < spotTargets.Length; t++)
            {
                RectTransform target = spotTargets[t];
                if (target == null || !target.gameObject.activeInHierarchy) continue;
                if (target.rect.width <= 1f || target.rect.height <= 1f) continue;
                target.GetWorldCorners(corners);
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
                        RectTransformUtility.WorldToScreenPoint(camera, corners[i]), camera, out local);
                    min = Vector2.Min(min, local);
                    max = Vector2.Max(max, local);
                }
                any = true;
            }
            if (!any)
            {
                dimmers[0].gameObject.SetActive(true);
                SetRect(dimmers[0].rectTransform, full.center, full.size);
                for (int i = 1; i < dimmers.Length; i++) dimmers[i].gameObject.SetActive(false);
                for (int i = 0; i < focusEdges.Length; i++) focusEdges[i].gameObject.SetActive(false);
                return;
            }

            float padding = CatCafeConfigDatabase.GetRequiredFloat("tutorial_spotlight_padding");
            min -= Vector2.one * padding;
            max += Vector2.one * padding;
            min.x = Mathf.Clamp(min.x, full.xMin, full.xMax);
            min.y = Mathf.Clamp(min.y, full.yMin, full.yMax);
            max.x = Mathf.Clamp(max.x, full.xMin, full.xMax);
            max.y = Mathf.Clamp(max.y, full.yMin, full.yMax);
            for (int i = 0; i < dimmers.Length; i++) dimmers[i].gameObject.SetActive(true);
            SetRect(dimmers[0].rectTransform, new Vector2((full.xMin + min.x) * 0.5f, full.center.y), new Vector2(min.x - full.xMin, full.height));
            SetRect(dimmers[1].rectTransform, new Vector2((max.x + full.xMax) * 0.5f, full.center.y), new Vector2(full.xMax - max.x, full.height));
            SetRect(dimmers[2].rectTransform, new Vector2((min.x + max.x) * 0.5f, (full.yMin + min.y) * 0.5f), new Vector2(max.x - min.x, min.y - full.yMin));
            SetRect(dimmers[3].rectTransform, new Vector2((min.x + max.x) * 0.5f, (max.y + full.yMax) * 0.5f), new Vector2(max.x - min.x, full.yMax - max.y));
            for (int i = 0; i < focusEdges.Length; i++) focusEdges[i].gameObject.SetActive(true);
            float thickness = CatCafeConfigDatabase.GetRequiredFloat("tutorial_spotlight_edge_thickness");
            SetRect(focusEdges[0].rectTransform, new Vector2((min.x + max.x) * 0.5f, min.y), new Vector2(max.x - min.x, thickness));
            SetRect(focusEdges[1].rectTransform, new Vector2((min.x + max.x) * 0.5f, max.y), new Vector2(max.x - min.x, thickness));
            SetRect(focusEdges[2].rectTransform, new Vector2(min.x, (min.y + max.y) * 0.5f), new Vector2(thickness, max.y - min.y));
            SetRect(focusEdges[3].rectTransform, new Vector2(max.x, (min.y + max.y) * 0.5f), new Vector2(thickness, max.y - min.y));
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
        }

        private static Text MakeText(string value, Transform parent, int size, Color color, TextAnchor alignment)
        {
            GameObject textObject = NewUi("Text", parent);
            Text label = textObject.AddComponent<Text>();
            label.font = CatCafeUiFontProvider.LegacyFont;
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

        private static void AnchorRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetHorizontalInsets(RectTransform rect, float left, float right)
        {
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-right, rect.offsetMax.y);
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
