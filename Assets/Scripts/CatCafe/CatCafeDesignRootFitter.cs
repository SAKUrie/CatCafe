using UnityEngine;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 设计分辨率根节点：保持自身 rect 恒为设计尺寸（如 1536×864），
    /// 按父节点（画布）实际大小做等比缩放居中（contain / 信箱式）。
    /// 美术整图与固定坐标的棋盘都挂在它下面，屏幕比例怎么变（全屏、改分辨率）
    /// 两者都同步缩放，不会再互相错位。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeDesignRootFitter : MonoBehaviour
    {
        private RectTransform rectTransform;
        private RectTransform parentRect;
        private Vector2 designSize;
        private Vector2 lastParentSize = new Vector2(float.NaN, float.NaN);

        public void Configure(Vector2 size)
        {
            designSize = size;
            rectTransform = (RectTransform)transform;
            parentRect = transform.parent as RectTransform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = designSize;
            lastParentSize = new Vector2(float.NaN, float.NaN);
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            if (parentRect == null || designSize.x <= 0f || designSize.y <= 0f) return;
            Vector2 parentSize = parentRect.rect.size;
            if (parentSize == lastParentSize) return;
            lastParentSize = parentSize;
            float scale = Mathf.Min(
                parentSize.x / designSize.x, parentSize.y / designSize.y);
            rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
