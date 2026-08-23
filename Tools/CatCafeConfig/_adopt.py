"""送养并入棋子详情页，拆掉独立的名册弹层。跑完删掉本脚本。"""
import io
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / 'Assets' / 'Scripts' / 'CatCafe' / 'CatCafeGameController.cs'
s = io.open(P, encoding='utf-8').read()


def cut(text, sig):
    i = text.index(sig)
    head = text.rfind('\n\n', 0, i)
    start = head + 1 if head != -1 else i
    j = text.index('{', i)
    d, k = 0, j
    while True:
        if text[k] == '{':
            d += 1
        elif text[k] == '}':
            d -= 1
            if d == 0:
                break
        k += 1
    return text[:start] + text[text.index('\n', k) + 1:]


# ── 1. 详情页底部加送养按钮 ──
a = '''            cardDetailOverlay.transform.SetAsLastSibling();
            PositionCardDetail(source);
            cardDetailOverlayView.Show();
        }

        private void ShowItemDetail(ItemDefinition item, RectTransform source)'''
b = '''            RefreshAdoptButton(element);

            cardDetailOverlay.transform.SetAsLastSibling();
            PositionCardDetail(source);
            cardDetailOverlayView.Show();
        }

        /// <summary>
        /// 送养按钮只在"看某一枚棋子"时出现，物品详情页没有这一栏。
        /// 券不够、结算中、名册只剩一枚时按钮变灰而不是消失——玩家要能看到条件。
        /// </summary>
        private void RefreshAdoptButton(Element element)
        {
            adoptTarget = element;
            if (adoptButton == null) return;

            bool isPoolPiece = element != null && pool.Contains(element);
            adoptButton.gameObject.SetActive(isPoolPiece);
            if (!isPoolPiece) return;

            bool canAdopt = removalTokens > 0 && !locked && pool.Count > 1;
            adoptButton.interactable = canAdopt;
            TMP_Text label = adoptButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = string.Format(UiString("ui_card_detail_adopt_format"), removalTokens);
            }
        }

        /// <summary>详情页里的送养：走原来的 RemoveOne，收完就把详情页关掉。</summary>
        private void AdoptCurrentPiece()
        {
            if (adoptTarget == null) return;
            string key = adoptTarget.Key;
            adoptTarget = null;
            if (cardDetailOverlayView != null) cardDetailOverlayView.Hide();
            RemoveOne(key);
        }

        private void ShowItemDetail(ItemDefinition item, RectTransform source)'''
assert s.count(a) == 1, '详情页锚点没命中'
s = s.replace(a, b)

# 物品详情页要藏掉送养按钮
a2 = '''            cardDetailMeta.color = BuffRarityColor(item.Rarity);
            cardDetailIncome.gameObject.SetActive(false);'''
b2 = '''            cardDetailMeta.color = BuffRarityColor(item.Rarity);
            cardDetailIncome.gameObject.SetActive(false);
            RefreshAdoptButton(null);'''
assert s.count(a2) == 1, '物品详情锚点没命中'
s = s.replace(a2, b2)

# ── 2. 构建按钮 ──
a3 = '''            cardDetailRule = CreateLabelFrame(content, "CardRule", string.Empty,'''
b3 = '''            adoptButton = CreateButton(content, string.Empty, AdoptCurrentPiece,
                UiValue("ui_card_detail_adopt_width"), UiValue("ui_card_detail_adopt_height"),
                PaperButtonRole.Leave);
            adoptButton.gameObject.SetActive(false);

            cardDetailRule = CreateLabelFrame(content, "CardRule", string.Empty,'''
assert s.count(a3) == 1, '按钮插入锚点没命中'
s = s.replace(a3, b3)

# ── 3. 字段 ──
a4 = '        private Transform pieceBoxRoot;'
b4 = ('        private Transform pieceBoxRoot;\n'
      '        private Button adoptButton;\n'
      '        private Element adoptTarget;')
assert s.count(a4) == 1, '字段锚点没命中'
s = s.replace(a4, b4)

# ── 4. RemoveOne 收尾：不再刷新已删除的名册弹层 ──
a5 = '''            ShowToast(removed.Name + "被好心人领养了");
            ShowPool();
        }'''
b5 = '''            ShowToast(string.Format(UiString("ui_adopt_done_format"), removed.Name));
            RefreshPieceBox();
        }'''
assert s.count(a5) == 1, 'RemoveOne 锚点没命中'
s = s.replace(a5, b5)

# ── 5. 拆掉名册弹层 ──
for sig in ['private void ShowPool()',
            'private void BuildPoolOverlay()',
            'private void ClosePool()',
            'private Button CreatePoolEntry(']:
    try:
        s = cut(s, sig)
    except ValueError:
        print('（跳过，不存在）' + sig)

io.open(P, 'w', encoding='utf-8', newline='').write(s)
print('送养已并入详情页；名册弹层已拆')
