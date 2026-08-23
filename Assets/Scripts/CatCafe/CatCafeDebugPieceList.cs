using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 【临时调试面板】局内按 L 打开棋子与物件总览，再按一次关闭；滚轮翻页。
    ///
    /// 直接读配置表，不碰任何玩法状态，也不改控制器——通过 RuntimeInitializeOnLoadMethod
    /// 自己挂到一个常驻对象上。**删掉这个文件就等于完全移除**，没有任何别处的引用要清。
    ///
    /// 正式版本不该保留它：面板是程序化拼的，没走纸艺皮肤，也没走 16:9 布局约束。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeDebugPieceList : MonoBehaviour
    {
        private const int SortingOrder = 32000;

        private GameObject panel;
        private TMP_Text body;
        private bool built;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindObjectOfType<CatCafeDebugPieceList>() != null) return;

            GameObject host = new GameObject("CatCafe Debug Piece List");
            host.AddComponent<CatCafeDebugPieceList>();
            DontDestroyOnLoad(host);
        }

        /// <summary>
        /// 工程的 activeInputHandler 只开了 Input System 包，旧版 UnityEngine.Input
        /// 一调用就抛 InvalidOperationException，而且是每帧抛。两套 API 都留着，
        /// 谁被启用就走谁，改回旧输入也不会再炸。
        /// </summary>
        private static bool ToggleRequested()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.lKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.L);
#endif
        }

        private void Update()
        {
            if (!ToggleRequested()) return;

            if (!built) Build();
            if (panel == null) return;

            bool show = !panel.activeSelf;
            if (show) body.text = Compose();
            panel.SetActive(show);
        }

        private void Build()
        {
            built = true;

            GameObject root = new GameObject("Debug Overview", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1536f, 864f);
            root.AddComponent<GraphicRaycaster>();

            panel = NewUi("Panel", root.transform);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1400f, 800f);
            panelRect.anchoredPosition = Vector2.zero;
            Image backdrop = panel.AddComponent<Image>();
            backdrop.color = new Color(0.06f, 0.05f, 0.04f, 0.96f);

            GameObject viewport = NewUi("Viewport", panel.transform);
            RectTransform viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(16f, 16f);
            viewportRect.offsetMax = new Vector2(-16f, -16f);
            Image mask = viewport.AddComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.01f);
            viewport.AddComponent<RectMask2D>();

            GameObject content = NewUi("Content", viewport.transform);
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);

            body = content.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = CatCafeUiFontProvider.TmpFont;
            if (font != null) body.font = font;
            body.fontSize = 15f;
            body.color = new Color(0.94f, 0.90f, 0.84f, 1f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.richText = true;
            body.raycastTarget = false;

            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = panel.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            panel.SetActive(false);
        }

        /// <summary>把配置表整理成一段可直接读的文本：按种类分组，标出来源与奖励池档位。</summary>
        private static string Compose()
        {
            CatCafeConfigDatabase.EnsureLoaded();
            CatCafeConfigDatabase.Root config = CatCafeConfigDatabase.Data;

            StringBuilder text = new StringBuilder();
            text.AppendLine("<b>棋子与物件总览</b>（L 关闭 · 滚轮翻页 · 数据直接来自配置表）");
            text.AppendLine();

            Dictionary<string, List<CatCafeConfigDatabase.ElementRow>> byKind =
                new Dictionary<string, List<CatCafeConfigDatabase.ElementRow>>();
            List<string> kindOrder = new List<string>();
            int enabledCount = 0;
            for (int i = 0; i < config.elements.Length; i++)
            {
                CatCafeConfigDatabase.ElementRow row = config.elements[i];
                if (!row.enabled) continue;
                enabledCount += 1;
                string kind = string.IsNullOrEmpty(row.kind) ? "(未填)" : row.kind;
                if (!byKind.ContainsKey(kind))
                {
                    byKind[kind] = new List<CatCafeConfigDatabase.ElementRow>();
                    kindOrder.Add(kind);
                }
                byKind[kind].Add(row);
            }

            text.AppendLine("棋子 " + enabledCount + " 种 ｜ 物件 " + CountEnabledItems(config) + " 种");
            text.AppendLine();

            for (int k = 0; k < kindOrder.Count; k++)
            {
                string kind = kindOrder[k];
                List<CatCafeConfigDatabase.ElementRow> rows = byKind[kind];
                text.AppendLine("<b>── " + kind + "（" + rows.Count + "）──</b>");
                for (int i = 0; i < rows.Count; i++)
                {
                    CatCafeConfigDatabase.ElementRow row = rows[i];
                    string pool = string.IsNullOrEmpty(row.pool_rarity) ? "不进池" : row.pool_rarity;
                    text.AppendLine(
                        "  " + row.name +
                        "  <color=#8C8C8C>" + row.key + "</color>" +
                        "  [" + row.rarity + "]" +
                        "  池:" + pool +
                        "  来源:" + row.unlock);
                    if (!string.IsNullOrWhiteSpace(row.rule_text))
                    {
                        text.AppendLine("      <color=#C9B79C>" +
                            row.rule_text.Replace("\\n", " ") + "</color>");
                    }
                }
                text.AppendLine();
            }

            text.AppendLine("<b>── 经营物件 ──</b>");
            for (int i = 0; i < config.items.Length; i++)
            {
                CatCafeConfigDatabase.ItemRow row = config.items[i];
                if (!row.enabled) continue;
                text.AppendLine(
                    "  " + row.name +
                    "  <color=#8C8C8C>" + row.key + "</color>" +
                    "  [" + row.rarity + "]");
                if (!string.IsNullOrWhiteSpace(row.rule_text))
                {
                    text.AppendLine("      <color=#C9B79C>" +
                        row.rule_text.Replace("\\n", " ") + "</color>");
                }
            }

            return text.ToString();
        }

        private static int CountEnabledItems(CatCafeConfigDatabase.Root config)
        {
            int count = 0;
            for (int i = 0; i < config.items.Length; i++) if (config.items[i].enabled) count += 1;
            return count;
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }
    }
}
