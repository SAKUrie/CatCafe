using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 统一的弹层出入场表现。所有猫咖弹层用 Show/Hide 代替裸 SetActive，
    /// 让遮罩淡入、面板弹入、点空白关闭和 Esc 关闭在各界面保持一致。
    /// 缓动曲线与 <see cref="CatCafeImageButtonFeedback"/> 相同，保证整体手感统一。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeOverlay : MonoBehaviour, IPointerClickHandler
    {
        private const float ShowDuration = 0.14f;
        private const float HideDuration = 0.10f;
        private const float EnterScale = 0.94f;
        private const float EnterRise = 12f;

        private CanvasGroup group;
        private readonly List<RectTransform> panels = new List<RectTransform>();
        private readonly List<Vector2> panelBaseHomes = new List<Vector2>();
        private readonly List<Vector2> panelHomes = new List<Vector2>();
        private bool allowCasualClose;
        private Action closeRequested;
        private Coroutine transition;
        private bool isOpening;

        public bool IsOpen { get { return gameObject.activeSelf; } }

        /// <param name="panelRect">弹层内的主面板，做缩放与上浮。</param>
        /// <param name="casualClose">是否允许点遮罩空白处或按 Esc 关闭。三选一等强制决策界面传 false。</param>
        /// <param name="onCloseRequested">玩家请求关闭时的回调；为 null 时直接 Hide。</param>
        public void Initialize(RectTransform panelRect, bool casualClose, Action onCloseRequested)
        {
            allowCasualClose = casualClose;
            closeRequested = onCloseRequested;

            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();

            panels.Clear();
            panelBaseHomes.Clear();
            panelHomes.Clear();
            AddPanel(panelRect);
        }

        /// <summary>登记与主面板同步运动的附加层，例如面板背后的书页底衬。</summary>
        public void AddPanel(RectTransform rect)
        {
            if (rect == null || panels.Contains(rect)) return;
            panels.Add(rect);
            panelBaseHomes.Add(rect.anchoredPosition);
            panelHomes.Add(rect.anchoredPosition);
        }

        /// <summary>布局在弹层创建后发生变化时，更新该视觉层的动画落点。</summary>
        public void RefreshPanelHome(RectTransform rect)
        {
            int index = panels.IndexOf(rect);
            if (index < 0 || rect == null) return;
            panelBaseHomes[index] = rect.anchoredPosition;
            panelHomes[index] = rect.anchoredPosition;
        }

        /// <summary>
        /// Repositions every visual layer belonging to this overlay while preserving the
        /// paper-stack offsets registered by <see cref="AddPanel"/>. Used by compact
        /// symbol tooltips that open beside the clicked symbol instead of at screen centre.
        /// </summary>
        public void SetPanelHomeOffset(Vector2 offset)
        {
            for (int i = 0; i < panels.Count; i++)
            {
                RectTransform rect = panels[i];
                Vector2 home = panelBaseHomes[i] + offset;
                panelHomes[i] = home;
                if (rect != null) rect.anchoredPosition = home;
            }
        }

        public void Show()
        {
            // 已经开着（或正在开）就别重播入场：面板和遮罩已经在屏幕上，
            // 再淡入一次整屏会闪一下。刷新内容的调用方只该重建内容。
            if (isOpening && gameObject.activeSelf) return;

            isOpening = true;
            gameObject.SetActive(true);
            StopTransition();
            transition = StartCoroutine(Animate(true));
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;

            isOpening = false;
            StopTransition();
            transition = StartCoroutine(Animate(false));
        }

        /// <summary>不播出场动画的立即关闭，用于重开一局这类批量收尾。</summary>
        public void HideImmediate()
        {
            isOpening = false;
            StopTransition();
            ApplyFrame(0f);
            gameObject.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!allowCasualClose || eventData == null) return;

            // 只有按在遮罩本体上才算点空白；按在面板及其子节点上会被这里挡掉。
            if (eventData.pointerPressRaycast.gameObject != gameObject) return;
            RaiseClose();
        }

        private void Update()
        {
            if (!allowCasualClose || !IsTopMost()) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
            RaiseClose();
        }

        /// <summary>Esc 只关最上面那层，避免设置层和它上面的确认层被同一次按键一起收掉。</summary>
        private bool IsTopMost()
        {
            Transform parent = transform.parent;
            if (parent == null) return true;

            for (int i = transform.GetSiblingIndex() + 1; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (!sibling.gameObject.activeSelf) continue;
                if (sibling.GetComponent<CatCafeOverlay>() != null) return false;
            }

            return true;
        }

        private void RaiseClose()
        {
            if (closeRequested != null) closeRequested();
            else Hide();
        }

        private IEnumerator Animate(bool opening)
        {
            float duration = opening ? ShowDuration : HideDuration;
            float from = opening ? 0f : 1f;
            float to = opening ? 1f : 0f;
            float elapsed = 0f;

            ApplyFrame(from);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = normalized * normalized * (3f - 2f * normalized);
                ApplyFrame(Mathf.Lerp(from, to, eased));
                yield return null;
            }

            ApplyFrame(to);
            transition = null;
            if (!opening) gameObject.SetActive(false);
        }

        private void ApplyFrame(float progress)
        {
            if (group != null) group.alpha = progress;

            float scale = Mathf.Lerp(EnterScale, 1f, progress);
            float rise = Mathf.Lerp(-EnterRise, 0f, progress);
            for (int i = 0; i < panels.Count; i++)
            {
                RectTransform rect = panels[i];
                if (rect == null) continue;
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = panelHomes[i] + new Vector2(0f, rise);
            }
        }

        private void StopTransition()
        {
            if (transition == null) return;
            StopCoroutine(transition);
            transition = null;
        }

        private void OnDisable()
        {
            StopTransition();
        }
    }
}
