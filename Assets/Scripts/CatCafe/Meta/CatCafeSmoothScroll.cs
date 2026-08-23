using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 平滑滚轮：接管 ScrollRect 的滚轮输入（ScrollRect 自身灵敏度置 0），
    /// 每格滚轮累加一个目标位移，逐帧指数缓动逼近，目标始终钳在内容范围内，
    /// 因此滚到顶/底就停住，不会滑出内容露白。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeSmoothScroll : MonoBehaviour, IScrollHandler, IBeginDragHandler
    {
        private ScrollRect scrollRect;
        private float stepPixels = 110f;   // 每格滚轮的目标位移（像素）
        private float smoothingRate = 12f; // 指数缓动速率，越大跟手越快

        private float targetY;
        private bool animating;

        public void Configure(ScrollRect scroll, float step, float smoothing)
        {
            scrollRect = scroll;
            if (step > 0f) stepPixels = step;
            if (smoothing > 0f) smoothingRate = smoothing;
        }

        private float MaxY
        {
            get
            {
                if (scrollRect == null || scrollRect.content == null) return 0f;
                RectTransform viewport = scrollRect.viewport != null
                    ? scrollRect.viewport
                    : (RectTransform)scrollRect.transform;
                return Mathf.Max(0f, scrollRect.content.rect.height - viewport.rect.height);
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (scrollRect == null || scrollRect.content == null) return;
            if (!animating) targetY = scrollRect.content.anchoredPosition.y;
            // 滚轮向上（delta.y > 0）回到顶部方向；目标直接钳制在 [0, MaxY]。
            targetY = Mathf.Clamp(targetY - eventData.scrollDelta.y * stepPixels, 0f, MaxY);
            animating = true;
            scrollRect.StopMovement();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // 手动拖拽时让位给 ScrollRect 本体，避免两边抢内容位置。
            animating = false;
        }

        private void OnDisable()
        {
            animating = false;
        }

        private void LateUpdate()
        {
            if (!animating || scrollRect == null || scrollRect.content == null) return;
            // 内容高度可能变化（图鉴条目增减），每帧重新钳制目标。
            targetY = Mathf.Clamp(targetY, 0f, MaxY);
            Vector2 position = scrollRect.content.anchoredPosition;
            float next = Mathf.Lerp(position.y, targetY,
                1f - Mathf.Exp(-smoothingRate * Time.unscaledDeltaTime));
            if (Mathf.Abs(next - targetY) < 0.5f)
            {
                next = targetY;
                animating = false;
            }

            position.y = next;
            scrollRect.content.anchoredPosition = position;
        }
    }
}
