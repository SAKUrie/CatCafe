using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// 猫咖局内流程控制器。
    /// 本类负责状态推进、通用规则解释和表现队列；玩法数据来自 CatCafeGameConfig.xlsx 导出的统一 JSON。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeGameController : MonoBehaviour
    {
        private Material imageButtonBrightnessMaterial;
        private int BoardColumns { get { return CatCafeConfigDatabase.GetInt("board_columns", 4); } }
        private int BoardRows { get { return CatCafeConfigDatabase.GetInt("board_rows", 4); } }
        private int BoardSize { get { return BoardColumns * BoardRows; } }
        private float BoardCellSize { get { return UiValue("ui_board_cell_size"); } }
        private float BoardCellSpacingX { get { return UiValue("ui_board_spacing_x"); } }
        private float BoardCellSpacingY { get { return UiValue("ui_board_spacing_y"); } }
        private float BoardPadding { get { return UiValue("ui_board_padding"); } }
        private float BoardColumnPitch { get { return BoardCellSize + BoardCellSpacingX; } }
        private float BoardRowPitch { get { return BoardCellSize + BoardCellSpacingY; } }
        private float BoardIconSize { get { return UiValue("ui_board_icon_size"); } }
        private float BoardLayoutWidth
        {
            get { return BoardColumns * BoardCellSize + (BoardColumns - 1) * BoardCellSpacingX + BoardPadding * 2f; }
        }
        private float BoardLayoutHeight
        {
            get { return BoardRows * BoardCellSize + (BoardRows - 1) * BoardCellSpacingY + BoardPadding * 2f; }
        }
        private float ReelViewportHeight
        {
            get { return BoardRows * BoardCellSize + (BoardRows - 1) * BoardCellSpacingY; }
        }
        private const int ReelBaseTravelSlots = 16;
        private const int ReelExtraTravelSlots = 3;
        private int ReelSymbolCapacity { get { return ReelBaseTravelSlots + (BoardColumns - 1) * ReelExtraTravelSlots + BoardRows; } }
        private const float ReelStartStagger = 0.045f;
        private const float ReelBaseDuration = 1.28f;
        private const float ReelStopDelay = 0.18f;
        private const float ReelAnticipationDistance = 10f;
        private const float ReelAnticipationDuration = 0.09f;
        private const float ReelStopOvershoot = 13f;
        private const float ReelBounceDuration = 0.24f;
        private float SettlementSpeedMultiplier
        {
            get
            {
                return CatCafeUserSettings.ScaleSpeed(
                    CatCafeConfigDatabase.GetFloat("settlement_speed_multiplier", 1.5f)) *
                    AutoSpeedMultiplier;
            }
        }

        /// <summary>
        /// 长局自动提速：回合数过了起始阈值后逐回合加速，封顶后不再变。
        /// 刻意不改玩家存的档位——玩家选的是"标准"，不该被游戏偷偷改成"快"；
        /// 这一层只叠在运行时，回合重置就回到基准。
        /// </summary>
        private float AutoSpeedMultiplier
        {
            get
            {
                if (CatCafeUserSettings.IsInstantSpeed) return 1f;

                int startRound = CatCafeConfigDatabase.GetRequiredInt(
                    "settlement_auto_speed_start_round");
                if (round < startRound) return 1f;

                float step = CatCafeConfigDatabase.GetRequiredFloat("settlement_auto_speed_step");
                float max = CatCafeConfigDatabase.GetRequiredFloat("settlement_auto_speed_max");
                return Mathf.Min(max, 1f + (round - startRound + 1) * step);
            }
        }
        private int BuffsPerPage { get { return CatCafeConfigDatabase.GetInt("buffs_per_page"); } }
        private int PiecesPerPage { get { return CatCafeConfigDatabase.GetInt("pieces_per_page", 8); } }
        private static float UiValue(string key)
        {
            return CatCafeConfigDatabase.GetRequiredFloat(key);
        }

        private static string UiString(string key)
        {
            string value = CatCafeConfigDatabase.GetString(key);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("[CatCafeConfig] 缺少必填 UI 字符串设置：" + key);
            return value;
        }

        private static Color UiColor(string key)
        {
            Color color;
            if (ColorUtility.TryParseHtmlString(UiString(key), out color)) return color;
            throw new InvalidOperationException("[CatCafeConfig] 无法解析 UI 颜色设置：" + key);
        }

        private static Vector2 UiTopLeft(string prefix)
        {
            return new Vector2(UiValue(prefix + "_x"), -UiValue(prefix + "_y"));
        }

        private static Vector2 UiSize(string prefix)
        {
            return new Vector2(UiValue(prefix + "_width"), UiValue(prefix + "_height"));
        }

        /// <summary>稀有度档数。权重表和抽取循环都以它为准，加档只需改这里和表。</summary>
        private const int RarityCount = 4;

        private enum Kind { Cat, Kitten, Guest, Prop, Staff }
        private enum Rarity { Common, Uncommon, Rare, Special }

        private sealed class StageConfig
        {
            public readonly string Name;
            public readonly int Rounds;
            public readonly int Target;
            public readonly string RarityContext;
            public readonly int ClearItemTier;
            public readonly Rarity ClearRewardMinimum;
            public readonly bool IsFinal;

            public StageConfig(string name, int rounds, int target, string rarityContext,
                int clearItemTier, Rarity clearRewardMinimum, bool isFinal)
            {
                Name = name;
                Rounds = rounds;
                Target = target;
                RarityContext = rarityContext;
                ClearItemTier = clearItemTier;
                ClearRewardMinimum = clearRewardMinimum;
                IsFinal = isFinal;
            }
        }

        private sealed class Definition
        {
            public string Key;
            public string Name;
            public string Type;
            public Kind Kind;
            public Rarity Rarity;
            public string Color;
            public string Asset;
            public string ShortIcon;
            public string Unlock;
            public string PoolRarity;
            public bool SpecialPresentation;
            public string[] Rules;

            public Definition(string key, string name, string type, Kind kind, Rarity rarity,
                string color, string asset, string shortIcon, string unlock, string poolRarity,
                bool specialPresentation, params string[] rules)
            {
                Key = key;
                Name = name;
                Type = type;
                Kind = kind;
                Rarity = rarity;
                Color = color;
                Asset = asset;
                ShortIcon = shortIcon;
                Unlock = unlock;
                PoolRarity = poolRarity;
                SpecialPresentation = specialPresentation;
                Rules = rules;
            }
        }

        private sealed class ItemDefinition
        {
            public string Key;
            public string Name;
            public Rarity Rarity;
            public string Asset;
            public string ShortIcon;
            public string Rule;

            public ItemDefinition(string key, string name, Rarity rarity, string asset, string shortIcon, string rule)
            {
                Key = key;
                Name = name;
                Rarity = rarity;
                Asset = asset;
                ShortIcon = shortIcon;
                Rule = rule;
            }
        }

        private sealed class SymbolLinkCandidate
        {
            public string Name;
            public string LinkId;
            /// <summary>图集里的 sprite 名，就是棋子／道具的 key。</summary>
            public string SpriteName;
        }

        /// <summary>
        /// A single card in the nested symbol-reference chain. Each card owns its full-screen
        /// click blocker, so blank-click dismissal naturally walks back one level at a time.
        /// </summary>
        private sealed class SymbolReferenceCardView
        {
            public GameObject Root;
            public RectTransform VisualRect;
            public RectTransform PanelRect;
            public TMP_Text Title;
            public TMP_Text Meta;
            public TMP_Text Rule;
            public Image Icon;
            public TMP_Text Fallback;
        }

        private sealed class Element
        {
            public int Id;
            public Definition Def;

            // 实例成长状态只保存运行结果，成长条件与成长值全部来自 Rules 表。
            public int LifetimeRounds;
            public int CycleReductionBonus;
            public int GrantedExternalBonuses;
            public int PermanentIncomeBonus;
            public readonly HashSet<string> AppliedPersistentRules = new HashSet<string>();

            // 这两个字段只保存“最近一次已经完成计算的转动”结果。
            // 详情弹窗只读取记录，不会为了展示而重新计算一次收益。
            public int LastRoundIncome;
            public readonly List<string> LastRoundIncomeBreakdown = new List<string>();

            public string Key { get { return Def.Key; } }
            public string Name { get { return Def.Name; } }
            public Kind Kind { get { return Def.Kind; } }
            public string Color { get { return Def.Color; } }
        }

        private sealed class RoundEvent
        {
            public Element Element;
            public int Index;
            public int Amount;
            public readonly List<string> Breakdown = new List<string>();
            public bool ConsumeSelf;
            public bool IsSpecial;
            public bool IsHighValue;
            public bool HasLink;
            public readonly List<int> LinkedIndices = new List<int>();

            // 先计算、后播放：规则动作只在这里排队，CalculateEvents 不直接改棋盘。
            public readonly List<int> RemovedTargetIds = new List<int>();
            public readonly List<string> GeneratedKeys = new List<string>();
            public readonly List<PersistentGain> PersistentGains = new List<PersistentGain>();
            public readonly List<string> ActionReasons = new List<string>();
            public int TransformTargetId;
            public string TransformResultKey;

        }

        private sealed class PersistentGain
        {
            public Element Target;
            public int Amount;
            public string RuleId;
            public string Reason;
        }

        private sealed class RoundEventGroup
        {
            public readonly List<RoundEvent> Events = new List<RoundEvent>();
            public bool IsLinked;
            public readonly List<int> LinkedIndices = new List<int>();

        }

        private sealed class RoundPayoutBatch
        {
            public readonly List<RoundEvent> Events = new List<RoundEvent>();
            public int UnitAmount;

            public int TotalAmount { get { return UnitAmount * Events.Count; } }
        }

        private sealed class ReelSymbolView
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Icon;
            public TMP_Text Fallback;
            public float VisualScale = 1f;
        }

        private sealed class ReelColumnView
        {
            public GameObject Root;
            public RectTransform Strip;
            public Image MotionTint;
            public Image BlurOverlay;
            public ReelSymbolView[] Symbols;
        }

        private sealed class ConfiguredChoicePhase
        {
            public readonly List<string> Candidates = new List<string>();
            public int Remaining;
            public string Reason;
        }

        private readonly Dictionary<string, Definition> defs = new Dictionary<string, Definition>();
        private readonly Dictionary<string, ItemDefinition> itemDefs = new Dictionary<string, ItemDefinition>();
        private readonly List<SymbolLinkCandidate> symbolLinkCandidates = new List<SymbolLinkCandidate>();
        private const string PieceIconAtlasResource = "CatCafe/UI/PieceIcons";
        private TMP_SpriteAsset pieceIconAtlas;
        private bool pieceIconAtlasResolved;
        private HashSet<string> pieceIconNames;
        private readonly List<Element> pool = new List<Element>();
        private readonly Dictionary<string, int> archetypeIncome = new Dictionary<string, int>();
        private readonly List<Element> board = new List<Element>();
        private readonly List<string> ownedItems = new List<string>();
        // 道具的持有轮数与运行时累计值。计数含义由 Rules 的 scope/operation 决定，
        // 字典只保存通用状态，不认识任何具体道具 key。
        private readonly Dictionary<string, int> ownedItemRounds = new Dictionary<string, int>();
        private readonly Dictionary<string, int> itemCounters = new Dictionary<string, int>();
        private readonly Dictionary<string, int> roundRuleTriggerCounts = new Dictionary<string, int>();
        // 真实离场历史供 generate_history_random 使用；只记录已经离开名册的对象 key。
        private readonly List<string> dismissedHistory = new List<string>();
        private readonly Dictionary<int, int> roundRandomIncomeResults = new Dictionary<int, int>();
        private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
        private readonly HashSet<string> missingSpriteWarnings = new HashSet<string>();
        private readonly List<StageConfig> stages = new List<StageConfig>();

        private List<string> RewardPool(int rarity)
        {
            string rarityKey = RarityKey((Rarity)rarity);
            List<string> result = new List<string>();
            foreach (KeyValuePair<string, Definition> pair in defs)
            {
                Definition definition = pair.Value;
                if (EffectivePoolRarity(definition) != rarityKey || !CanAddElement(definition.Key)) continue;
                // 局外只负责解锁奖励池资格：不改初始牌组、不加权，也不提供任何局内数值。
                if (definition.Unlock == "base" || CatCafeMeta.IsDiscovered(definition.Key))
                    result.Add(definition.Key);
            }
            return result;
        }

        private int nextId = 1;
        private int money;
        private int round;
        private int stageIndex;
        private int stageRound;
        private int stageBonusRounds;
        private int normalStageCount;
        private int runFirstDiscoveries;
        /// <summary>本局掉落的绒毛总数，只用于结算页文案；真正的账记在 CatCafeMeta 里。</summary>
        private int runFurGained;
        private int runCansGained;
        private bool runSettled;
        private bool normalRunCompleted;
        private bool endlessMode;
        private int rerollTokens;
        private int removalTokens;
        private int inspirationTokens;
        private int consumedElements;
        private int pendingDismissRemovalTokens;
        private int pendingDismissRerollTokens;
        private int pendingDismissInspirationTokens;
        private readonly List<string> pendingDismissGeneratedKeys = new List<string>();
        private bool locked;
        private string resultMode = string.Empty;
        private bool pendingForceSkipReward;
        private bool pendingForceChooseReward;
        private Rarity? pendingRewardMinimum;
        private Rarity? pendingItemRewardMinimum;
        private Rarity? pendingItemChoiceMinimum;
        private int pendingExtraItemChoices;
        private int currentItemChoiceTier;
        private int pendingExtraPieceChoices;
        private bool waiveNextStagePayment;
        private string boardActionMode;
        private int boardActionFirstIndex = -1;
        private bool boardActionResolved;
        private int boostedColumn = -1;
        private float boostedColumnMultiplier = 1f;

        private Canvas canvas;
        private Transform boardRoot;
        private GameObject reelOverlayRoot;
        private ReelColumnView[] reelColumns;
        private TMP_Text moneyText;
        private TMP_Text stageText;
        private TMP_Text goalText;
        private TMP_Text roundText;
        private TMP_Text toastText;
        private Button rollButton;
        private Button rerollButton;
        private GameObject choiceOverlay;
        private GameObject itemOverlay;
        private GameObject resultOverlay;
        private GameObject settingsOverlay;
        private GameObject confirmOverlay;
        private GameObject cardDetailOverlay;
        private CatCafeOverlay choiceOverlayView;
        private CatCafeOverlay itemOverlayView;
        private CatCafeOverlay resultOverlayView;
        private CatCafeOverlay settingsOverlayView;
        private CatCafeOverlay confirmOverlayView;
        private CatCafeOverlay cardDetailOverlayView;
        private TMP_Text choiceTicketText;
        // 三选一面板要按卡片数量伸缩（预约名册会让它变成四张），布局时要一起动的几个节点。
        private RectTransform choicePanelRect;
        private LayoutElement choiceTitleSize;
        private LayoutElement choiceContentSize;
        private LayoutElement choiceTicketSize;
        private TMP_Text itemTicketText;
        private TMP_Text confirmTitle;
        private TMP_Text confirmCopy;
        private TMP_Text confirmCancelText;
        private TMP_Text confirmAcceptText;
        private TMP_Text cardDetailTitle;
        private TMP_Text cardDetailMeta;
        private TMP_Text cardDetailIncome;
        private TMP_Text cardDetailRule;
        private Image cardDetailIcon;
        private TMP_Text cardDetailFallback;
        private RectTransform cardDetailPanelRect;
        private Button cardDetailRemoveButton;
        private TMP_Text cardDetailRemoveText;
        private Image cardDetailRemoveImage;
        private Color cardDetailRemoveTextColor = Color.white;
        private readonly List<SymbolReferenceCardView> symbolReferenceCards =
            new List<SymbolReferenceCardView>();
        /// <summary>小窗当前展示的棋子实例；送走按钮按实例移除，不误伤同名棋子。</summary>
        private Element cardDetailElement;
        private ItemDefinition cardDetailItem;

        /// <summary>ApplyRemovalRule 里攒下的离场收益，由调用方取走并播动画后清零。</summary>
        private int pendingDismissCoins;
        private Action confirmAction;
        private Action confirmCancelAction;
        private bool choiceResolving;
        private bool pieceBoxRefreshDeferred;
        private readonly List<Element> pieceBoxEntries = new List<Element>();
        private readonly List<int> pieceBoxCounts = new List<int>();
        private string pieceBoxFocusKey;
        private readonly List<Button> musicButtons = new List<Button>();
        private readonly List<Button> volumeButtons = new List<Button>();
        private readonly List<Button> speedButtons = new List<Button>();
        private readonly List<Button> tutorialButtons = new List<Button>();
        private readonly List<Button> activeChoiceCards = new List<Button>();
        private readonly List<string> currentChoiceKeys = new List<string>();
        private readonly List<string> skippedChoiceHistory = new List<string>();
        private readonly List<ConfiguredChoicePhase> configuredChoicePhases =
            new List<ConfiguredChoicePhase>();
        private string configuredChoiceItemKey;
        private int configuredChoicePage;
        private Transform choicesRoot;
        private Transform itemChoicesRoot;
        private TMP_Text choiceTitle;
        private TMP_Text itemTitle;
        private TMP_Text resultTitle;
        private TMP_Text resultCopy;
        private Button resultButton;
        private Button leaderboardButton;
        private bool leaderboardSubmitting;
        private CatCafePresentation presentation;
        private CatCafeDiscoveryReveal discoveryReveal;
        private CatCafeInteractionFeedback interactionFeedback;
        private RectTransform moneyHudRect;
        private RectTransform moneyCoinTarget;
        private GameObject chainOverlayRoot;

        private Transform chainMarkerRoot;
        private TMP_Text chainSequenceText;
        private Transform buffEntriesRoot;
        private TMP_Text buffPageText;
        private Button buffPreviousButton;
        private Button buffNextButton;
        private Transform pieceBoxRoot;
        private RectTransform pieceBoxViewportRect;
        private TMP_Text pieceBoxCountText;
        private TMP_Text pieceBoxTendencyText;
        private TMP_Text pieceBoxPageText;
        private Button pieceBoxPreviousButton;
        private Button pieceBoxNextButton;
        private CatCafeLandlordNote tutorialNotes;
        private bool tutorialFirstInspectPending;
        private bool tutorialCatDetailOpened;
        private RectTransform resultPanelRect;
        private TMP_Text goalCaption;
        private float noteHoldUntil;
        /// <summary>本局点开过几次棋子小窗。下班券那条要等第二次，见 ShowCardDetail。</summary>
        private int cardDetailOpenCount;
        private bool tutorialFirstBoardPending;
        private int buffPage;
        private string buffFocusKey;
        private int pieceBoxPage;


        private TMP_FontAsset uiFont;

        private string RarityLabel(Rarity rarity)
        {
            return CatCafeConfigDatabase.RarityLabel(RarityKey(rarity));
        }

        private void Awake()
        {
            spriteCache.Clear();
            missingSpriteWarnings.Clear();
            CatCafeConfigDatabase.EnsureLoaded();
            CatCatalog.EnsureLoaded();
            CatCafeMeta.EnsureLoaded();
            LoadDefinitionsFromConfig();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) CatCafeMeta.SaveNow();
        }

        private void OnApplicationQuit()
        {
            CatCafeMeta.SaveNow();
        }

        private void Start()
        {
            EnsureEventSystem();

            presentation = GetComponent<CatCafePresentation>();
            if (presentation == null)
            {
                presentation = gameObject.AddComponent<CatCafePresentation>();
            }

            presentation.Initialize();
            BuildUi();
            discoveryReveal = GetComponent<CatCafeDiscoveryReveal>();
            if (discoveryReveal == null) discoveryReveal = gameObject.AddComponent<CatCafeDiscoveryReveal>();
            discoveryReveal.Initialize(canvas, presentation);
            ResetGame();
            tutorialNotes = gameObject.AddComponent<CatCafeLandlordNote>();
            tutorialNotes.Initialize(canvas);
            tutorialNotes.SetGate(CanShowLandlordNote);
            tutorialNotes.RegisterTarget("run_spin_button", rollButton.transform as RectTransform);
            tutorialNotes.RegisterTarget("run_synergy_cells", boardRoot as RectTransform);
            tutorialNotes.RegisterTarget("run_reward_cards", choicesRoot as RectTransform);
            tutorialNotes.RegisterTarget("run_rent_progress", goalText.rectTransform);
            tutorialNotes.RegisterTarget("run_summary_cans", resultPanelRect);
            tutorialNotes.RegisterTarget("run_reroll_button", rerollButton.transform as RectTransform);
            // 名册弹窗已由右侧常驻店内名册取代，相关字条聚光直接打在名册区域上。
            if (pieceBoxViewportRect != null)
            {
                tutorialNotes.RegisterTarget("run_pool_button", pieceBoxViewportRect);
            }
            tutorialNotes.RegisterTarget("run_collection_summary", resultPanelRect);
            // 第一次进场先让玩家亲手点一只猫看收益；详情关闭后才提示拉杆。
            // 两张字条由真实操作隔开，不在同一时点排队连续弹出。
            BeginOpeningTutorial();
            ShowToast("\u7FFB\u5F00\u8425\u4E1A\u724C\uFF0C\u8FCE\u63A5\u4ECA\u5929\u7B2C\u4E00\u6CE2\u5BA2\u4EBA");
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

        /* ── 房东字条的插话时机 ── */

        /// <summary>
        /// 空闲字条的放行闸。弹层出入场、发牌、结算演出期间一律不插话——
        /// 字条盖在一半的动画上，看起来就像"随机蹦出来"。
        /// </summary>
        private bool CanShowLandlordNote()
        {
            return Time.unscaledTime >= noteHoldUntil;
        }

        /// <summary>刚开了一段演出，压住空闲字条到它播完为止。</summary>
        private void HoldLandlordNotes(float seconds)
        {
            noteHoldUntil = Mathf.Max(noteHoldUntil, Time.unscaledTime + seconds);
        }

        /// <summary>棋盘格子的 RectTransform，用作字条聚光目标；越界时退回整块棋盘。</summary>
        private RectTransform BoardCellRect(int index)
        {
            if (boardRoot == null) return null;
            if (index < 0 || index >= boardRoot.childCount) return boardRoot as RectTransform;
            return boardRoot.GetChild(index) as RectTransform;
        }

        private int FindBoardIndexOfKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            for (int i = 0; i < board.Count; i++) if (board[i] != null && board[i].Key == key) return i;
            return -1;
        }

        /// <summary>
        /// 玩家刚做完选择、棋盘重新可点的那一拍，是本局唯一稳定的空闲点。
        /// 同一个稳定点只报告一条字条。优先讲眼前即将发生的最后波次，
        /// 否则才补充名册溢出的情境说明，避免收完奖励后连续弹多张。
        /// </summary>
        private void NotifyPostChoiceBeats()
        {
            if (tutorialNotes == null) return;
            HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_choice_hold", 0.3f));
            // 目标营业额提示要赶在玩家按下"营业"之前出现。
            if (stageRound == CurrentStage.Rounds + stageBonusRounds - 1)
            {
                if (tutorialNotes.Notify("run_first_rent_countdown")) return;
            }
            if (pool.Count > BoardSize) tutorialNotes.Notify("pool_first_overflow");
        }

        /// <summary>把 Excel 导出的纯数据适配为当前 UI 使用的轻量视图模型。</summary>
        private void LoadDefinitionsFromConfig()
        {
            defs.Clear();
            itemDefs.Clear();
            stages.Clear();

            CatCafeConfigDatabase.Root config = CatCafeConfigDatabase.Data;
            for (int i = 0; i < config.elements.Length; i++)
            {
                CatCafeConfigDatabase.ElementRow row = config.elements[i];
                if (!row.enabled || string.IsNullOrEmpty(row.key)) continue;
                string[] rules = string.IsNullOrEmpty(row.rule_text)
                    ? new string[0]
                    : row.rule_text.Replace("\\n", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                defs[row.key] = new Definition(row.key, row.name, row.type_label, ParseKind(row.kind),
                    ParseRarity(row.rarity), row.color_gene, row.asset, row.short_icon, row.unlock,
                    row.pool_rarity, row.special_presentation, rules);
            }

            for (int i = 0; i < config.items.Length; i++)
            {
                CatCafeConfigDatabase.ItemRow row = config.items[i];
                if (!row.enabled || string.IsNullOrEmpty(row.key)) continue;
                itemDefs[row.key] = new ItemDefinition(row.key, row.name, ParseRarity(row.rarity),
                    row.asset, row.short_icon, (row.rule_text ?? string.Empty).Replace("\\n", "\n"));
            }

            RebuildSymbolLinkCandidates();

            int finalStageIndex = -1;
            for (int i = 0; i < config.stages.Length; i++)
            {
                CatCafeConfigDatabase.StageRow row = config.stages[i];
                if (!row.enabled) continue;
                stages.Add(new StageConfig(row.name, row.rounds, row.target, row.rarity_context,
                    row.clear_item_tier, ParseRarity(row.clear_reward_min_rarity), row.is_final));
                if (row.is_final)
                {
                    if (finalStageIndex >= 0)
                        throw new InvalidOperationException("Stages 只能配置一个最终关。");
                    finalStageIndex = stages.Count - 1;
                }
            }

            if (defs.Count == 0 || stages.Count == 0)
                throw new InvalidOperationException("CatCafe 配置为空，请重新导出 CatCafeGameConfig.xlsx。");
            if (finalStageIndex < 0 || finalStageIndex != stages.Count - 1)
                throw new InvalidOperationException("Stages 的最后一个启用阶段必须标记 is_final=true。");
            normalStageCount = finalStageIndex + 1;
        }

        /// <summary>
        /// Builds an unambiguous display-name index from the exported Pieces and Buffs data.
        /// Longer names are checked first so, for example, a full cat name wins over a shorter
        /// name contained inside it. Duplicate display names are deliberately not linked because
        /// a visible label must never open an arbitrary definition.
        /// </summary>
        private void RebuildSymbolLinkCandidates()
        {
            symbolLinkCandidates.Clear();
            Dictionary<string, string> linksByName = new Dictionary<string, string>();
            HashSet<string> ambiguousNames = new HashSet<string>();

            foreach (KeyValuePair<string, Definition> pair in defs)
            {
                RegisterSymbolLinkName(pair.Value.Name, "element|" + pair.Key,
                    linksByName, ambiguousNames);
            }
            foreach (KeyValuePair<string, ItemDefinition> pair in itemDefs)
            {
                RegisterSymbolLinkName(pair.Value.Name, "item|" + pair.Key,
                    linksByName, ambiguousNames);
            }

            foreach (KeyValuePair<string, string> pair in linksByName)
            {
                if (ambiguousNames.Contains(pair.Key)) continue;
                // LinkId 形如 "element|key" / "item|key"，竖线后面那截就是图集里的 sprite 名。
                int separator = pair.Value.IndexOf('|');
                symbolLinkCandidates.Add(new SymbolLinkCandidate
                {
                    Name = pair.Key,
                    LinkId = pair.Value,
                    SpriteName = separator >= 0 && separator + 1 < pair.Value.Length
                        ? pair.Value.Substring(separator + 1)
                        : pair.Value,
                });
            }
            symbolLinkCandidates.Sort(delegate(SymbolLinkCandidate left, SymbolLinkCandidate right)
            {
                int lengthOrder = right.Name.Length.CompareTo(left.Name.Length);
                return lengthOrder != 0
                    ? lengthOrder
                    : string.CompareOrdinal(left.Name, right.Name);
            });
        }

        private static void RegisterSymbolLinkName(string name, string linkId,
            Dictionary<string, string> linksByName, HashSet<string> ambiguousNames)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(linkId)) return;

            string existing;
            if (linksByName.TryGetValue(name, out existing))
            {
                if (!string.Equals(existing, linkId, StringComparison.Ordinal))
                    ambiguousNames.Add(name);
                return;
            }
            linksByName.Add(name, linkId);
        }

        private static Kind ParseKind(string value)
        {
            // Legacy workbooks used "item" for board pieces. Persistent items are
            // loaded from the separate Buffs table and never enter this parser.
            if (string.Equals(value, "item", StringComparison.OrdinalIgnoreCase))
                return Kind.Prop;

            Kind result;
            return Enum.TryParse(value, true, out result) ? result : Kind.Prop;
        }

        private static Rarity ParseRarity(string value)
        {
            Rarity result;
            return Enum.TryParse(value, true, out result) ? result : Rarity.Common;
        }

        private static string RarityKey(Rarity rarity)
        {
            return rarity.ToString().ToLowerInvariant();
        }


        private Element Instance(string key)
        {
            return new Element { Id = nextId++, Def = defs[key] };
        }

        private bool HasItem(string key) { return ownedItems.Contains(key); }

        private void RemoveOwnedItem(string key)
        {
            ownedItems.Remove(key);
            if (!ownedItems.Contains(key))
            {
                ownedItemRounds.Remove(key);
                itemCounters.Remove(key);
            }
            RefreshBuffPanel();
        }

        private int OwnedItemCount(string key)
        {
            int count = 0;
            for (int i = 0; i < ownedItems.Count; i++) if (ownedItems[i] == key) count += 1;
            return count;
        }

        private int ItemStackLimit(string key)
        {
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("item_stack_limit", "item");
            for (int i = 0; i < rules.Count; i++)
                if (rules[i].owner_key == key && rules[i].operation == "max_count")
                    return Mathf.Max(1, CalculateRuleValue(rules[i], 0, 0));
            return 1;
        }

        private bool CanAcquireItem(string key)
        {
            return OwnedItemCount(key) < ItemStackLimit(key);
        }

        private int ItemCounter(string key)
        {
            int value;
            return !string.IsNullOrEmpty(key) && itemCounters.TryGetValue(key, out value) ? value : 0;
        }

        private void AddItemCounter(string key, int amount)
        {
            if (string.IsNullOrEmpty(key) || amount == 0) return;
            itemCounters[key] = ItemCounter(key) + amount;
        }

        private int OwnedItemRoundCount(string key)
        {
            int value;
            return !string.IsNullOrEmpty(key) && ownedItemRounds.TryGetValue(key, out value) ? value : 0;
        }

        private int CycleReduction(Element element)
        {
            if (element == null) return 0;
            int reduction = Mathf.Max(0, element.CycleReductionBonus);
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("cycle", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "add") continue;
                if (!ContainsToken(rule.source_keys, element.Key)) continue;
                reduction += Mathf.Max(0, CalculateRuleValue(rule, 0, 0));
            }
            return reduction;
        }

        private bool IsRuleSuppressed(CatCafeConfigDatabase.RuleRow candidate)
        {
            if (candidate == null) return false;
            List<CatCafeConfigDatabase.RuleRow> suppressors = ConfiguredRules("suppress_rules", "item");
            for (int i = 0; i < suppressors.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow suppressor = suppressors[i];
                if (!HasItem(suppressor.owner_key) || suppressor.operation != "suppress") continue;
                if (!ContainsToken(suppressor.source_keys, candidate.owner_key)) continue;
                if (suppressor.target_value_mode == "negative_only" && RuleHasNegativeEffect(candidate))
                    return true;
                if (ContainsToken(suppressor.result_key, candidate.operation) ||
                    ContainsToken(suppressor.result_key, candidate.trigger)) return true;
            }
            return false;
        }

        private static bool RuleHasNegativeEffect(CatCafeConfigDatabase.RuleRow rule)
        {
            return rule != null && CatCafeMechanicMath.IsNegativeRule(
                rule.trigger, rule.operation, rule.base_value, rule.primary_factor,
                rule.secondary_factor, rule.cross_factor, rule.multiplier);
        }

        private int ModifiedTargetLimit(CatCafeConfigDatabase.RuleRow sourceRule)
        {
            int result = sourceRule.target_limit;
            List<CatCafeConfigDatabase.RuleRow> modifiers = ConfiguredRules("modify_target_limit", "item");
            for (int i = 0; i < modifiers.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow modifier = modifiers[i];
                if (!HasItem(modifier.owner_key) || modifier.operation != "add") continue;
                if (!ContainsToken(modifier.source_keys, sourceRule.owner_key)) continue;
                result += CalculateRuleValue(modifier, 0, 0);
            }
            return result;
        }

        private int RuleRepeatCount(CatCafeConfigDatabase.RuleRow sourceRule)
        {
            int result = 1;
            List<CatCafeConfigDatabase.RuleRow> modifiers =
                ConfiguredRules("modify_rule_triggers", "item");
            for (int i = 0; i < modifiers.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow modifier = modifiers[i];
                if (!HasItem(modifier.owner_key) || modifier.operation != "add_count") continue;
                if (!ContainsToken(modifier.source_keys, sourceRule.owner_key)) continue;
                result += Mathf.Max(0, CalculateRuleValue(modifier, 0, 0));
            }
            return Mathf.Max(1, result);
        }

        private string EffectivePoolRarity(Definition definition)
        {
            string result = definition == null ? string.Empty : definition.PoolRarity;
            if (definition == null) return result;
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("pool_rarity", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "set") continue;
                if (ContainsToken(rule.source_keys, definition.Key)) result = rule.result_key;
            }
            return result;
        }

        private Rarity EffectiveRarity(Definition definition)
        {
            return definition == null ? Rarity.Common : ParseRarity(EffectivePoolRarity(definition));
        }

        /// <summary>
        /// 棋子离开名册时的结算（「被移除时获得 N 金币」那一类）。
        ///
        /// 与 consume_self 的区别：consume_self 是棋子自己在结算里用掉自己（温牛奶被喝掉），
        /// 这里是被外力拿走——玩家用托付券送走，或被道具规则清走。两者互不重叠。
        /// 返回本次结算出的金币，由调用方决定怎么播。
        ///
        /// playerInitiated：只有玩家主动送走才结算 transfer_permanent（离场价值转移）。
        /// 盘面只有 16 格，名册涨过 16 之后加牌不再提高每波收入，只稀释出场率——
        /// 「送走＝把两张牌的价值压进一张」是唯一能突破这个天花板的成长通道。
        /// 道具规则清走棋子不走这条：那是自动触发的，玩家没做选择，不该给成长。
        /// </summary>
        private int EvaluateDismissRules(Element piece, bool playerInitiated = false)
        {
            if (piece == null) return 0;

            int coins = 0;
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("on_dismiss", "element");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!MatchesRuleSource(rule, piece)) continue;
                if (IsRuleSuppressed(rule)) continue;

                // 棋子已经不在盘上了，位置相关的范围一律取 0，只让全局条件参与判定。
                int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, piece, -1, null, 0, false);
                int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, piece, -1, null, 0, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                    !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                int value = CalculateRuleValue(rule, primary, secondary);
                int repeats = RuleRepeatCount(rule);
                if (rule.operation == "income") coins += value * repeats;
                else if (rule.operation == "add_removal") pendingDismissRemovalTokens += value * repeats;
                else if (rule.operation == "add_reroll") pendingDismissRerollTokens += value * repeats;
                else if (rule.operation == "add_inspiration") pendingDismissInspirationTokens += value * repeats;
                else if (rule.operation == "transfer_permanent")
                {
                    if (playerInitiated) TransferPermanentIncome(piece, value * repeats);
                }
                else if (rule.operation == "generate")
                {
                    int triggerCount = RollRuleTriggers(rule);
                    int copies = Mathf.Max(1, rule.result_count);
                    for (int trigger = 0; trigger < triggerCount; trigger++)
                        for (int copy = 0; copy < copies; copy++)
                            for (int repeat = 0; repeat < repeats; repeat++)
                                pendingDismissGeneratedKeys.Add(rule.result_key);
                }
                else if (rule.operation == "generate_random")
                {
                    int triggerCount = RollRuleTriggers(rule);
                    int copies = Mathf.Max(1, rule.result_count);
                    for (int trigger = 0; trigger < triggerCount; trigger++)
                        for (int copy = 0; copy < copies; copy++)
                            for (int repeat = 0; repeat < repeats; repeat++)
                                pendingDismissGeneratedKeys.Add(ChooseRuleResultKey(rule));
                }
                else if (rule.operation == "set_reward_minimum")
                {
                    pendingRewardMinimum = ParseRarity(rule.result_key);
                }
            }

            List<CatCafeConfigDatabase.RuleRow> dismissModifiers =
                ConfiguredRules("modify_dismiss_income", "item");
            for (int i = 0; i < dismissModifiers.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow modifier = dismissModifiers[i];
                if (!HasItem(modifier.owner_key) || modifier.operation != "multiply") continue;
                if (!MatchesRuleSource(modifier, piece)) continue;
                coins = Mathf.RoundToInt(coins * (modifier.multiplier == 0f ? 1f : modifier.multiplier));
            }

            dismissedHistory.Add(piece.Key);
            ApplyAnyDismissGrowth(piece);

            // 所有离场路径统一从这里派发道具的 on_consume 规则。这样玩家主动送走、
            // 棋子自我消耗以及道具批量清理都不会漏掉“每移除一个……”类效果。
            int itemRemoval;
            int itemReroll;
            string itemReason;
            List<string> itemGenerated = new List<string>();
            coins += EvaluateItemTrigger(
                "on_consume", 0, out itemRemoval, out itemReroll, out itemReason,
                piece, itemGenerated);
            pendingDismissRemovalTokens += itemRemoval;
            pendingDismissRerollTokens += itemReroll;
            pendingDismissGeneratedKeys.AddRange(itemGenerated);
            if (!string.IsNullOrEmpty(itemReason)) ShowToast(itemReason);

            return coins;
        }

        private void ApplyAnyDismissGrowth(Element dismissed)
        {
            if (dismissed == null) return;
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("on_any_dismiss", "element");
            for (int poolIndex = 0; poolIndex < pool.Count; poolIndex++)
            {
                Element owner = pool[poolIndex];
                if (owner == null || owner.Id == dismissed.Id) continue;
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    CatCafeConfigDatabase.RuleRow rule = rules[ruleIndex];
                    if (!MatchesRuleSource(rule, owner)) continue;
                    if (!ContainsToken(rule.source_keys, dismissed.Key)) continue;
                    if (rule.operation == "cycle_reduce")
                    {
                        owner.CycleReductionBonus += Mathf.Max(0, CalculateRuleValue(rule, 0, 0));
                        if (!string.IsNullOrEmpty(rule.reason)) ShowToast(rule.reason);
                    }
                    else if (rule.operation == "permanent_add")
                    {
                        ApplyPersistentGain(new PersistentGain
                        {
                            Target = owner,
                            Amount = CalculateRuleValue(rule, 0, 0),
                            Reason = rule.reason
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 离场价值转移：把送走的棋子的一部分身价永久记到名册里同类最强的那位身上。
        ///
        /// 「同类」是为了让转移读起来有因果（猫把客人交给猫、物件顶替物件）；名册里没有
        /// 同类时退回全名册，否则最后一只猫下班就白送了。挑选按「固定收益＋已有永久加成」
        /// 排序，并列时取名册靠前的一位，保证同一局重放结果一致。
        /// </summary>
        private void TransferPermanentIncome(Element leaving, int amount)
        {
            if (leaving == null || amount <= 0 || pool.Count == 0) return;

            Element heir = null;
            int best = int.MinValue;
            for (int i = 0; i < pool.Count; i++)
            {
                Element candidate = pool[i];
                if (candidate == null || candidate.Id == leaving.Id) continue;
                if (candidate.Kind != leaving.Kind) continue;
                int score = ConfiguredBaseIncome(candidate) + candidate.PermanentIncomeBonus;
                if (score > best) { best = score; heir = candidate; }
            }
            if (heir == null)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    Element candidate = pool[i];
                    if (candidate == null || candidate.Id == leaving.Id) continue;
                    int score = ConfiguredBaseIncome(candidate) + candidate.PermanentIncomeBonus;
                    if (score > best) { best = score; heir = candidate; }
                }
            }
            if (heir == null) return;

            heir.PermanentIncomeBonus += amount;
            string template = CatCafeConfigDatabase.GetString(
                "ui_dismiss_transfer_format", "{0}接过了{1}的活儿，每次营业永久 +{2}");
            ShowToast(string.Format(template, heir.Name, leaving.Name, amount));
        }

        /// <summary>
        /// 执行一条移除类规则，返回实际移除的数量。
        ///
        /// 这是规则引擎第一次获得写权限——以前 CalculateEvents 只读棋盘算钱，从不改名册。
        /// 移除数量会被当作 primary 代回收益公式，所以「每移除 1 个得 N 金币」不需要新语法，
        /// 把 N 填进 primary_factor 就行。
        /// </summary>
        private int ApplyRemovalRule(CatCafeConfigDatabase.RuleRow rule)
        {
            if (rule == null || string.IsNullOrEmpty(rule.remove_scope)) return 0;

            int limit = rule.remove_limit <= 0 ? int.MaxValue : rule.remove_limit;
            int removed = 0;

            if (rule.remove_scope == "pool_key" || rule.remove_scope == "pool_kind")
            {
                bool byKey = rule.remove_scope == "pool_key";
                // 倒着遍历：边删边走正序会跳过元素。
                for (int i = pool.Count - 1; i >= 0 && removed < limit; i--)
                {
                    Element piece = pool[i];
                    if (piece == null) continue;
                    string token = byKey ? piece.Key : piece.Kind.ToString();
                    if (!ContainsTokenStrict(rule.remove_filter, token)) continue;
                    if (UnityEngine.Random.value > Mathf.Clamp01(rule.chance)) continue;

                    PersistentGain preventedGain;
                    if (TryFindRemovalProtector(piece, out preventedGain))
                    {
                        ApplyPersistentGain(preventedGain);
                        continue;
                    }

                    pool.RemoveAt(i);
                    // 名册里没了，这一波已经上桌的那一份也要一起撤，否则棋盘上还留着幽灵。
                    int boardIndex = FindBoardIndex(piece.Id);
                    if (boardIndex >= 0) board[boardIndex] = null;
                    consumedElements += 1;
                    // 被道具清走也算「被移除」，该给的离场收益照给。
                    pendingDismissCoins += EvaluateDismissRules(piece);
                    removed += 1;
                }

                if (removed > 0)
                {
                    RenderBoard();
                    UpdateHud();
                }
            }
            else if (rule.remove_scope == "owned_item")
            {
                for (int i = ownedItems.Count - 1; i >= 0 && removed < limit; i--)
                {
                    // 自身走 consume_self，不在这里顺手删掉——否则一条规则里两种语义会打架。
                    if (ownedItems[i] == rule.owner_key) continue;
                    if (!ContainsTokenStrict(rule.remove_filter, ownedItems[i])) continue;

                    ownedItems.RemoveAt(i);
                    removed += 1;
                }

                if (removed > 0) RefreshBuffPanel();
            }

            return removed;
        }

        /// <summary>
        /// 移除筛选用的匹配。和 ContainsToken 的区别是：空值代表「不匹配任何东西」而不是「匹配全部」——
        /// 移除是破坏性操作，漏填一列就清空整个名册的风险不能留。
        /// </summary>
        private static bool ContainsTokenStrict(string tokens, string value)
        {
            if (string.IsNullOrEmpty(tokens)) return false;
            if (tokens == "*") return true;
            return ContainsToken(tokens, value);
        }

        private StageConfig CurrentStage { get { return stages[Mathf.Min(stageIndex, stages.Count - 1)]; } }

        private void ResetGame()
        {
            nextId = 1;
            pool.Clear();
            archetypeIncome.Clear();
            tutorialFirstBoardPending = false;
            cardDetailOpenCount = 0;
            ownedItems.Clear();
            ownedItemRounds.Clear();
            itemCounters.Clear();
            roundRuleTriggerCounts.Clear();
            dismissedHistory.Clear();
            roundRandomIncomeResults.Clear();
            currentChoiceKeys.Clear();
            skippedChoiceHistory.Clear();
            configuredChoicePhases.Clear();
            configuredChoiceItemKey = null;
            configuredChoicePage = 0;
            buffPage = 0;
            buffFocusKey = null;
            pieceBoxPage = 0;
            money = CatCafeConfigDatabase.GetInt("initial_money", 0);
            round = 0;
            stageIndex = 0;
            normalRunCompleted = false;
            endlessMode = false;
            rerollTokens = CatCafeConfigDatabase.GetInt("initial_reroll_tokens", 1);
            removalTokens = CatCafeConfigDatabase.GetInt("initial_removal_tokens", 1);
            inspirationTokens = 0;
            consumedElements = 0;
            pendingDismissCoins = 0;
            pendingDismissRemovalTokens = 0;
            pendingDismissRerollTokens = 0;
            pendingDismissInspirationTokens = 0;
            pendingDismissGeneratedKeys.Clear();
            runFirstDiscoveries = 0;
            runFurGained = 0;
            runCansGained = 0;
            CatCafeConfigDatabase.InitialDeckRow[] initialDeck = CatCafeConfigDatabase.Data.initialDeck;
            for (int i = 0; i < initialDeck.Length; i++)
            {
                CatCafeConfigDatabase.InitialDeckRow row = initialDeck[i];
                if (!row.enabled || !defs.ContainsKey(row.element_key)) continue;
                for (int copy = 0; copy < Mathf.Max(0, row.count); copy++) pool.Add(Instance(row.element_key));
            }
            tutorialFirstBoardPending = CatCafeUserSettings.TutorialEnabled && CatCafeMeta.Runs == 0 &&
                CatCafeConfigDatabase.GetBool("tutorial_first_roll_enabled", true);

            // 每局都从第一期目标和初始牌组重新开始；局外收集不恢复或改写局内构筑。
            stageRound = 0;
            stageBonusRounds = 0;
            board.Clear();
            board.AddRange(BuildBoard());
            runSettled = false;
            locked = false;
            resultMode = string.Empty;
            pendingForceSkipReward = false;
            pendingForceChooseReward = false;
            pendingItemChoiceMinimum = null;
            pendingExtraItemChoices = 0;
            currentItemChoiceTier = 0;
            pendingExtraPieceChoices = 0;
            waiveNextStagePayment = false;
            boardActionMode = null;
            boardActionFirstIndex = -1;
            boardActionResolved = false;
            boostedColumn = -1;
            boostedColumnMultiplier = 1f;
            pendingRewardMinimum = null;
            pendingItemRewardMinimum = null;
            HideAllOverlays();
            UpdateHud();
            RefreshBuffPanel();
            SetRollInteractable(true);
            RenderBoard();
        }

        private List<Element> BuildBoard()
        {
            List<Element> selected = BoardSelection(pool, Mathf.Min(pool.Count, BoardSize));
            // 开局静态预览和玩家按下的首转用同一套脚本布局：预览里能点到的那只猫和那只
            // 猫砂盆，拉杆之后还在原位，教学的三拍（点图标→拉杆→看联动）指的是同两格。
            // 标记由 RunRound 在首转建完盘面后清掉，之后全随机。
            if (tutorialFirstBoardPending) return BuildScriptedFirstBoard(selected);
            List<Element> result = new List<Element>(selected);
            while (result.Count < BoardSize) result.Add(null);
            return Shuffle(result);
        }

        /// <summary>
        /// 首局第一次转动的脚本化牌面：把 tutorial_first_roll_pair 那一对钉在相邻的两格，
        /// 保证首转当场出联动，房东奶奶那张"挨着猫砂盆还能多挣一份"才有东西可指。
        ///
        /// 这一对必须真的有相邻联动。默认配的是 cat,litterBox——猫砂盆每有一只相邻猫咪
        /// 就多挣一枚；早先钉的是猫＋客人，可客人是固定小费，挨着谁都一样，
        /// 字条讲的联动在盘面上根本不存在。
        ///
        /// 只做这一次，之后全随机。槽位来自 tutorial_first_roll_slots，前两个必须相邻——
        /// 配歪了就退回普通随机并报警，绝不硬塞出一个指不到东西的聚光框。
        /// </summary>
        private List<Element> BuildScriptedFirstBoard(List<Element> selected)
        {
            List<Element> board = new List<Element>();
            while (board.Count < BoardSize) board.Add(null);

            List<int> slots = new List<int>();
            string[] slotParts = CatCafeConfigDatabase.GetString("tutorial_first_roll_slots", "0,1,4,5").Split(',');
            for (int i = 0; i < slotParts.Length; i++)
            {
                int slot;
                if (!int.TryParse(slotParts[i].Trim(), out slot)) continue;
                if (slot < 0 || slot >= BoardSize || slots.Contains(slot)) continue;
                slots.Add(slot);
            }

            string[] pairTokens = TutorialPairTokens();
            Element first = pairTokens.Length > 0 ? FindForTutorialPair(selected, pairTokens[0], null) : null;
            Element second = pairTokens.Length > 1 ? FindForTutorialPair(selected, pairTokens[1], first) : null;
            bool pairPlaced = false;
            // 相邻只算上下左右：adjacent_cats 这类作用域默认就是正交的，
            // 把两个斜角槽位钉起来看着像挨着，猫砂盆却一分钱都不会多挣。
            if (first != null && second != null && slots.Count >= 2 &&
                OrthogonalIndexes(slots[0]).Contains(slots[1]))
            {
                board[slots[0]] = first;
                board[slots[1]] = second;
                pairPlaced = true;
            }
            else
            {
                Debug.LogWarning("[CatCafe] 首转脚本化失败：名册里凑不齐 tutorial_first_roll_pair 配的 \"" +
                    CatCafeConfigDatabase.GetString("tutorial_first_roll_pair", "cat,litterBox") +
                    "\"，或 tutorial_first_roll_slots 的前两个槽位不是上下左右相邻。本次按普通随机处理。");
            }

            // 剩下的按加权抽取的顺序补进保护槽，再补空位；已经钉住的那两个不再参与。
            List<Element> rest = new List<Element>();
            for (int i = 0; i < selected.Count; i++)
            {
                if (pairPlaced && (selected[i] == first || selected[i] == second)) continue;
                rest.Add(selected[i]);
            }
            int cursor = 0;
            for (int i = 0; i < slots.Count && cursor < rest.Count; i++)
                if (board[slots[i]] == null) board[slots[i]] = rest[cursor++];
            for (int i = 0; i < board.Count && cursor < rest.Count; i++)
                if (board[i] == null) board[i] = rest[cursor++];
            return board;
        }

        private static string[] TutorialPairTokens()
        {
            return CatCafeConfigDatabase.GetString("tutorial_first_roll_pair", "cat,litterBox").Split(',');
        }

        /// <summary>
        /// tutorial_first_roll_pair 的一项能不能匹配这枚棋子：先当具体棋子键比，再当种类比。
        /// 具体键优先，因为"猫砂盆"这种要教的是那一件东西，不是"随便一个道具"。
        /// </summary>
        private static bool MatchesTutorialToken(Element element, string token)
        {
            if (element == null || string.IsNullOrEmpty(token)) return false;
            token = token.Trim();
            return element.Key == token ||
                string.Equals(element.Kind.ToString(), token, StringComparison.OrdinalIgnoreCase);
        }

        private static Element FindForTutorialPair(List<Element> source, string token, Element exclude)
        {
            if (string.IsNullOrEmpty(token)) return null;
            token = token.Trim();
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null && source[i] != exclude && source[i].Key == token) return source[i];
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null && source[i] != exclude && MatchesTutorialToken(source[i], token))
                    return source[i];
            return null;
        }

        /// <summary>
        /// 联动字条该框住的那两格：当前盘面上真的上下左右挨在一起的「猫 + 猫砂盆」。
        ///
        /// 必须每次现扫盘面，不能只认首转钉住的槽位——老存档（Runs&gt;0）压根不走脚本布局，
        /// 槽位是空的；早先那份代码在这时会退化成"框住第一个结算事件"，
        /// 于是字条讲着猫砂盆，聚光却打在一位客人身上。
        ///
        /// 找不到就返回 null：宁可这一轮不弹，等到真出现这一对再讲。
        /// 字条是 once 的，每一轮都会再问一次，迟早等得到。
        /// </summary>
        private RectTransform[] FindTutorialPairSpotlight()
        {
            string[] tokens = TutorialPairTokens();
            if (tokens.Length < 2) return null;
            for (int i = 0; i < board.Count; i++)
            {
                if (!MatchesTutorialToken(board[i], tokens[0])) continue;
                List<int> nearby = OrthogonalIndexes(i);
                for (int n = 0; n < nearby.Count; n++)
                {
                    int j = nearby[n];
                    if (j < 0 || j >= board.Count || !MatchesTutorialToken(board[j], tokens[1])) continue;
                    return new[] { BoardCellRect(i), BoardCellRect(j) };
                }
            }
            return null;
        }

        /// <summary>局内名册无放回随机抽取；局外随行猫和亲密度不改变局内概率。</summary>
        private List<Element> BoardSelection(IList<Element> source, int count)
        {
            List<Element> result = Shuffle(source);
            if (result.Count > count) result.RemoveRange(count, result.Count - count);
            return result;
        }

        private List<T> Shuffle<T>(IList<T> source)
        {
            List<T> result = new List<T>(source);
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                T temp = result[i];
                result[i] = result[j];
                result[j] = temp;
            }
            return result;
        }

        private void UpdateHud()
        {
            if (moneyText == null) return;
            moneyText.text = money.ToString();
            stageText.text = endlessMode
                ? string.Format(CatCafeConfigDatabase.GetRequiredString("ui_run_endless_stage_label_format"),
                    stageIndex + 1)
                : string.Format(CatCafeConfigDatabase.GetRequiredString("ui_run_stage_label_format"),
                    stageIndex + 1, normalStageCount);
            goalText.text = money + " / " + CurrentStage.Target;
            int totalWaves = CurrentStage.Rounds + stageBonusRounds;
            int displayedWave = Mathf.Min(stageRound + 1, totalWaves);
            roundText.text = string.Format(
                CatCafeConfigDatabase.GetRequiredString("ui_run_wave_label_format"),
                displayedWave, totalWaves);
            if (goalCaption != null)
                goalCaption.text = CatCafeConfigDatabase.GetRequiredString("ui_run_goal_caption");
        }

        public void RollRound()
        {
            if (locked) return;
            StartCoroutine(RunRound());
        }

        private IEnumerator RunRound()
        {
            locked = true;
            SetRollInteractable(false);

            // 新一轮开始后，上一轮的来源说明立即失效。之后会在 CalculateEvents
            // 得出本轮真实结果时重新写回对应棋子实例。
            ClearLastRoundIncomeRecords();
            roundRuleTriggerCounts.Clear();
            roundRandomIncomeResults.Clear();
            pendingForceSkipReward = false;
            pendingForceChooseReward = false;

            // 营业前的道具结算要在洗名册之前跑：它可能从名册里拿掉对象，
            // 放到 BuildBoard 之后就会出现「刚被清掉的对象还是上了桌」。
            yield return StartCoroutine(SettleBeforeRoundItems());

            int moneyAtRoundStart = money;
            List<Element> previousBoard = new List<Element>(board);
            List<Element> nextBoard = BuildBoard();
            // 首转用掉教学布局，从第二转起完全随机。
            tutorialFirstBoardPending = false;
            board.Clear();
            board.AddRange(nextBoard);
            if (interactionFeedback != null) interactionFeedback.PlayRollStart();
            yield return StartCoroutine(AnimateBoardRoll(previousBoard, nextBoard));
            if (interactionFeedback != null) interactionFeedback.PlayRollStop();
            yield return new WaitForSecondsRealtime(CatCafeConfigDatabase.GetFloat("round_roll_stop_delay", 0.08f));
            boostedColumn = -1;
            boostedColumnMultiplier = 1f;
            yield return StartCoroutine(OfferBoardActions());

            // 育儿窝那条讲的是"看到小窝该怎么摆"，要在结算前、窝刚落到盘面上时说，
            // 而不是等 SettleBreeding 已经在判定有没有大猫的时候。
            if (tutorialNotes != null)
            {
                int nestIndex = FindBoardIndexOfKey(CatCafeConfigDatabase.GetString("breeding_source_element"));
                if (nestIndex >= 0) yield return StartCoroutine(tutorialNotes.Interject("nursery_first_seen", BoardCellRect(nestIndex)));
            }

            List<RoundEvent> events = CalculateEvents();
            RecordArchetypeIncome(events);
            RecordLastRoundIncome(events);
            if (events.Count > 0 && tutorialNotes != null)
            {
                // 字条讲的就是这一对挨在一起这件事，所以只在盘面上真有这一对时才弹。
                RectTransform[] focus = FindTutorialPairSpotlight();
                if (focus != null)
                    yield return StartCoroutine(tutorialNotes.Interject("run_first_synergy", focus));
            }

            if (events.Count > 0)
            {
                BeginChainSequence();
                List<RoundEventGroup> reactionGroups = BuildLinkedEventGroups(events);

                // 联动棋子优先，严格按最左列到最右列、每列从上到下逐个播放；
                // 每只联动棋子演出结束后立即播放金币特效并入账，不等普通批次。
                for (int groupIndex = 0; groupIndex < reactionGroups.Count; groupIndex++)
                {
                    RoundEventGroup group = reactionGroups[groupIndex];
                    yield return StartCoroutine(PlayEventGroup(group, groupIndex));

                    RoundEvent trigger = group.Events[0];
                    if (trigger.Amount != 0)
                        yield return StartCoroutine(PlayDeferredBoardReward(trigger, null));

                    yield return new WaitForSecondsRealtime(
                        CatCafeConfigDatabase.GetRequiredFloat("settlement_reaction_group_gap_seconds") /
                        SettlementSpeedMultiplier);
                }

                // 联动全部结算后，仅将剩余普通棋子按相同金额合成一批；同批同时
                // 发光、同时播放金币特效，并在整批动画结束时一次入账。
                List<RoundPayoutBatch> payoutBatches = BuildPayoutBatches(events);
                int collectedAfterBatch = money;
                for (int batchIndex = 0; batchIndex < payoutBatches.Count; batchIndex++)
                {
                    RoundPayoutBatch batch = payoutBatches[batchIndex];
                    collectedAfterBatch += batch.TotalAmount;
                    yield return StartCoroutine(
                        PlayPayoutBatch(batch, batchIndex, collectedAfterBatch));

                    if (batchIndex < payoutBatches.Count - 1)
                    {
                        yield return new WaitForSecondsRealtime(
                            CatCafeConfigDatabase.GetRequiredFloat(
                                "settlement_payout_batch_gap_seconds") /
                            SettlementSpeedMultiplier);
                    }
                }

                ClearChainVisuals();

                yield return StartCoroutine(SettleConsumedEvents(events));
            }

            yield return StartCoroutine(SettleBreeding());
            yield return StartCoroutine(SettleFurDrops());
            yield return StartCoroutine(SettleRoundItems(money - moneyAtRoundStart));
            ApplyRoundEndPersistentRules();
            round += 1;
            stageRound += 1;
            UpdateHud();

            if (stageRound >= CurrentStage.Rounds + stageBonusRounds)
            {
                yield return new WaitForSeconds(CatCafeConfigDatabase.GetFloat("stage_finish_delay", 0.2f));
                ContinueStageEnd(false);
                yield break;
            }

            yield return new WaitForSeconds(CatCafeConfigDatabase.GetFloat("reward_choice_delay", 0.2f));
            ShowOrResolveReward();
        }

        private void ShowOrResolveReward()
        {
            pendingExtraPieceChoices = ConfiguredExtraPieceChoices();
            if (pendingForceSkipReward)
            {
                pendingForceSkipReward = false;
                pendingForceChooseReward = false;
                locked = false;
                SetRollInteractable(true);
                return;
            }
            if (pendingForceChooseReward)
            {
                pendingForceChooseReward = false;
                List<string> options = RewardOptions(null);
                if (options.Count > 0)
                {
                    Choose(options[UnityEngine.Random.Range(0, options.Count)]);
                    return;
                }
            }
            ShowChoices(null);
        }

        private int ConfiguredExtraPieceChoices()
        {
            int result = 0;
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("reward_sequence", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "add_choice") continue;
                int primary = EvaluateScope(
                    rule.primary_scope, rule.primary_filter, null, -1, null, 0, false);
                if (Passes(rule.primary_comparator, primary, rule.primary_threshold))
                    result += Mathf.Max(0, CalculateRuleValue(rule, primary, 0));
            }
            return result;
        }

        private List<RoundEvent> CalculateEvents()
        {
            List<RoundEvent> events = new List<RoundEvent>();
            HashSet<string> usedOnceRules = new HashSet<string>();
            // 同一轮的多个规则不能把同一个目标重复移除；这里只做“预占”，实际修改在播放结束后统一执行。
            HashSet<int> claimedRemovalTargets = new HashSet<int>();

            // 棋盘数据按“行优先”存储，但结算必须按“列优先”读取：
            // 最左列从上到下，然后继续右侧下一列。
            for (int column = 0; column < BoardColumns; column++)
            {
                for (int row = 0; row < BoardRows; row++)
                {
                    int index = row * BoardColumns + column;
                    if (index < 0 || index >= board.Count) continue;

                    Element element = board[index];
                    if (element == null) continue;

                    bool seesDiagonals = UsesDiagonalAdjacency(element, index);
                    List<Element> nearby = Neighbors(index, seesDiagonals);
                    List<int> nearbyIndexes = AdjacentIndexes(index, seesDiagonals);
                    int amount = element.PermanentIncomeBonus;
                    bool consumeSelf = false;
                    bool hasLink = false;
                    bool hasAction = false;
                    int externalBonusTriggers = 0;
                    HashSet<int> linkedIndices = new HashSet<int>();
                    List<string> breakdown = new List<string>();
                    List<int> removedTargetIds = new List<int>();
                    List<string> generatedKeys = new List<string>();
                    List<PersistentGain> persistentGains = new List<PersistentGain>();
                    List<string> actionReasons = new List<string>();
                    int transformTargetId = 0;
                    string transformResultKey = null;

                    if (element.PermanentIncomeBonus != 0)
                    {
                        breakdown.Add(string.Format(
                            CatCafeConfigDatabase.GetRequiredString(
                                "ui_card_detail_permanent_income_format"),
                            element.PermanentIncomeBonus));
                    }

                    List<CatCafeConfigDatabase.RuleRow> incomeRules = ConfiguredRules("round", "element");
                    for (int i = 0; i < incomeRules.Count; i++)
                    {
                        CatCafeConfigDatabase.RuleRow rule = incomeRules[i];
                        if (!MatchesRuleSource(rule, element)) continue;
                        if (IsRuleSuppressed(rule)) continue;

                        int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, element, index, nearby, 0, consumeSelf);
                        int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, element, index, nearby, 0, consumeSelf);
                        if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                            !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                        int contribution = 0;
                        int repeats = RuleRepeatCount(rule);
                        if (rule.operation == "income")
                        {
                            contribution = CalculateRuleValue(rule, primary, secondary) * repeats;
                            amount += contribution;
                            if (rule.owner_key == "*" && contribution > 0)
                            {
                                externalBonusTriggers += CatCafeMechanicMath.ExternalBonusTriggerCount(
                                    rule.primary_scope, primary, rule.secondary_scope, secondary);
                                RecordExternalBonusProviders(rule, nearbyIndexes);
                            }
                            AddRuleBreakdown(breakdown, rule, primary, secondary, contribution);
                        }
                        else if (rule.operation == "chance_income")
                        {
                            int triggerCount = RollRuleTriggers(rule, element, index) * repeats;
                            if (triggerCount <= 0) continue;
                            contribution = CalculateRuleValue(rule, primary, secondary) * triggerCount;
                            amount += contribution;
                            if (rule.owner_key == "*" && contribution > 0)
                            {
                                externalBonusTriggers += CatCafeMechanicMath.ExternalBonusTriggerCount(
                                    rule.primary_scope, primary, rule.secondary_scope, secondary);
                                RecordExternalBonusProviders(rule, nearbyIndexes);
                            }
                            AddRuleBreakdown(breakdown, rule, primary, secondary, contribution);
                        }
                        else if (rule.operation == "random_income")
                        {
                            if (RollRuleTriggers(rule, element, index) <= 0) continue;
                            int minimum = Mathf.Min(rule.base_value, rule.primary_factor);
                            int maximum = Mathf.Max(rule.base_value, rule.primary_factor);
                            contribution = 0;
                            for (int repeat = 0; repeat < repeats; repeat++)
                                contribution += ApplyRandomIncomeModifiers(
                                    rule, UnityEngine.Random.Range(minimum, maximum + 1), minimum, maximum);
                            roundRandomIncomeResults[element.Id] = contribution;
                            amount += contribution;
                            if (rule.owner_key == "*" && contribution > 0)
                            {
                                externalBonusTriggers += CatCafeMechanicMath.ExternalBonusTriggerCount(
                                    rule.primary_scope, primary, rule.secondary_scope, secondary);
                                RecordExternalBonusProviders(rule, nearbyIndexes);
                            }
                            AddRuleBreakdown(breakdown, rule, primary, secondary, contribution);
                        }
                        else if (rule.operation == "multiply_income")
                        {
                            int beforeMultiplier = amount;
                            for (int repeat = 0; repeat < repeats; repeat++)
                                amount = Mathf.RoundToInt(
                                    amount * (rule.multiplier == 0f ? 1f : rule.multiplier));
                            contribution = amount - beforeMultiplier;
                            if (rule.owner_key == "*" && contribution > 0)
                            {
                                externalBonusTriggers += CatCafeMechanicMath.ExternalBonusTriggerCount(
                                    rule.primary_scope, primary, rule.secondary_scope, secondary);
                                RecordExternalBonusProviders(rule, nearbyIndexes);
                            }
                            AddRuleBreakdown(breakdown, rule, primary, secondary, contribution);
                        }
                        else if (rule.operation == "remove_targets")
                        {
                            if (RollRuleTriggers(rule, element, index) <= 0) continue;
                            List<Element> targets = ResolveActionTargets(
                                rule, element, index, claimedRemovalTargets);
                            int removedCount = 0;
                            int removedBaseIncome = 0;
                            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                            {
                                Element target = targets[targetIndex];
                                PersistentGain preventedGain;
                                if (TryFindRemovalProtector(target, out preventedGain))
                                {
                                    persistentGains.Add(preventedGain);
                                    int protectorIndex = FindBoardIndex(preventedGain.Target.Id);
                                    if (protectorIndex >= 0) linkedIndices.Add(protectorIndex);
                                }
                                else
                                {
                                    claimedRemovalTargets.Add(target.Id);
                                    removedTargetIds.Add(target.Id);
                                    removedCount += 1;
                                    removedBaseIncome += ConfiguredBaseIncome(target);
                                }

                                int targetBoardIndex = FindBoardIndex(target.Id);
                                if (targetBoardIndex >= 0) linkedIndices.Add(targetBoardIndex);
                            }

                            if (targets.Count > 0)
                            {
                                if (rule.target_value_mode == "permanent_per_removed")
                                {
                                    int permanentAmount =
                                        rule.base_value + removedCount * rule.primary_factor;
                                    if (permanentAmount != 0)
                                    {
                                        persistentGains.Add(new PersistentGain
                                        {
                                            Target = element,
                                            Amount = permanentAmount,
                                            // “每移除1个永久成长”可以跨多轮重复触发，不能像
                                            // round_end 的里程碑成长那样用 rule_id 锁成终身一次。
                                            RuleId = string.Empty,
                                            Reason = rule.reason
                                        });
                                    }
                                }
                                else
                                {
                                    contribution = rule.base_value + removedCount * rule.primary_factor;
                                    if (rule.target_value_mode == "base_income")
                                    {
                                        float factor = rule.multiplier == 0f ? 1f : rule.multiplier;
                                        contribution += Mathf.RoundToInt(removedBaseIncome * factor);
                                    }
                                    amount += contribution;
                                    AddRuleBreakdown(
                                        breakdown, rule, removedCount, removedBaseIncome, contribution);
                                }
                                hasAction = true;
                                hasLink = true;
                                if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                            }
                        }
                        else if (rule.operation == "generate")
                        {
                            int triggerCount = RollRuleTriggers(rule, element, index) * repeats;
                            int copies = Mathf.Max(1, rule.result_count);
                            for (int trigger = 0; trigger < triggerCount; trigger++)
                                for (int copy = 0; copy < copies; copy++)
                                    generatedKeys.Add(rule.result_key);
                            if (triggerCount > 0)
                            {
                                hasAction = true;
                                if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                            }
                        }
                        else if (rule.operation == "generate_random")
                        {
                            int triggerCount = RollRuleTriggers(rule, element, index) * repeats;
                            int copies = Mathf.Max(1, rule.result_count);
                            for (int trigger = 0; trigger < triggerCount; trigger++)
                                for (int copy = 0; copy < copies; copy++)
                                    generatedKeys.Add(ChooseRuleResultKey(rule));
                            if (triggerCount > 0)
                            {
                                hasAction = true;
                                if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                            }
                        }
                        else if (rule.operation == "generate_history_random")
                        {
                            string historyKey = ChooseDismissedHistoryKey(rule.target_filter);
                            if (!string.IsNullOrEmpty(historyKey))
                            {
                                generatedKeys.Add(historyKey);
                                hasAction = true;
                                if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                            }
                        }
                        else if (rule.operation == "transform")
                        {
                            if (RollRuleTriggers(rule, element, index) > 0)
                            {
                                transformTargetId = element.Id;
                                transformResultKey = ModifiedTransformResult(rule);
                                hasAction = true;
                                if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                            }
                        }
                        else if (rule.operation == "force_skip")
                        {
                            if (RollRuleTriggers(rule, element, index) <= 0) continue;
                            pendingForceSkipReward = true;
                            hasAction = true;
                            if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                        }
                        else if (rule.operation == "force_choose")
                        {
                            if (RollRuleTriggers(rule, element, index) <= 0) continue;
                            pendingForceChooseReward = true;
                            hasAction = true;
                            if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                        }

                        if (IsLinkedScope(rule.primary_scope) && primary > 0)
                        {
                            hasLink = true;
                            AddLinkedIndices(linkedIndices, nearbyIndexes, rule.primary_scope, rule.primary_filter, index);
                        }
                        if (IsLinkedScope(rule.secondary_scope) && secondary > 0)
                        {
                            hasLink = true;
                            AddLinkedIndices(linkedIndices, nearbyIndexes, rule.secondary_scope, rule.secondary_filter, index);
                        }
                        if (rule.consume_self) consumeSelf = true;
                    }

                    List<CatCafeConfigDatabase.RuleRow> itemActions =
                        ConfiguredRules("item_round_action", "item");
                    for (int i = 0; i < itemActions.Count; i++)
                    {
                        CatCafeConfigDatabase.RuleRow rule = itemActions[i];
                        if (!HasItem(rule.owner_key) || !MatchesRuleSource(rule, element)) continue;
                        int primary = EvaluateScope(
                            rule.primary_scope, rule.primary_filter, element, index, nearby, 0, consumeSelf);
                        int secondary = EvaluateScope(
                            rule.secondary_scope, rule.secondary_filter, element, index, nearby, 0, consumeSelf);
                        if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                            !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                        if (rule.operation == "remove_targets" &&
                            RollRuleTriggers(rule, element, index) > 0)
                        {
                            List<Element> targets = ResolveActionTargets(
                                rule, element, index, claimedRemovalTargets);
                            int removedCount = 0;
                            int removedBaseIncome = 0;
                            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                            {
                                Element target = targets[targetIndex];
                                PersistentGain preventedGain;
                                if (TryFindRemovalProtector(target, out preventedGain))
                                    persistentGains.Add(preventedGain);
                                else
                                {
                                    claimedRemovalTargets.Add(target.Id);
                                    removedTargetIds.Add(target.Id);
                                    removedCount += 1;
                                    removedBaseIncome += ConfiguredBaseIncome(target);
                                }
                                int targetBoardIndex = FindBoardIndex(target.Id);
                                if (targetBoardIndex >= 0) linkedIndices.Add(targetBoardIndex);
                            }
                            if (targets.Count > 0)
                            {
                                int contribution = rule.base_value + removedCount * rule.primary_factor;
                                if (rule.target_value_mode == "base_income")
                                    contribution += Mathf.RoundToInt(
                                        removedBaseIncome * (rule.multiplier == 0f ? 1f : rule.multiplier));
                                amount += contribution;
                                AddRuleBreakdown(
                                    breakdown, rule, removedCount, removedBaseIncome, contribution);
                                hasAction = true;
                                hasLink = true;
                                if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                            }
                        }
                        else if (rule.operation == "transform" &&
                                 RollRuleTriggers(rule, element, index) > 0)
                        {
                            transformTargetId = element.Id;
                            transformResultKey = ModifiedTransformResult(rule);
                            hasAction = true;
                            if (!string.IsNullOrEmpty(rule.reason)) actionReasons.Add(rule.reason);
                        }
                    }

                    if (amount == 0 && !hasAction && !consumeSelf) continue;

                    List<CatCafeConfigDatabase.RuleRow> modifiers = ConfiguredRules("modify_income", "item");
                    for (int i = 0; i < modifiers.Count; i++)
                    {
                        if (amount == 0) break;
                        CatCafeConfigDatabase.RuleRow rule = modifiers[i];
                        if (!HasItem(rule.owner_key) || !MatchesRuleSource(rule, element)) continue;
                        if (rule.once_per_round && usedOnceRules.Contains(rule.rule_id)) continue;

                        int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, element, index, nearby, 0, consumeSelf);
                        int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, element, index, nearby, 0, consumeSelf);
                        if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                            !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                        if (IsLinkedScope(rule.primary_scope) && primary > 0)
                        {
                            hasLink = true;
                            AddLinkedIndices(linkedIndices, nearbyIndexes, rule.primary_scope, rule.primary_filter, index);
                        }
                        if (IsLinkedScope(rule.secondary_scope) && secondary > 0)
                        {
                            hasLink = true;
                            AddLinkedIndices(linkedIndices, nearbyIndexes, rule.secondary_scope, rule.secondary_filter, index);
                        }

                        if (rule.operation == "add")
                        {
                            int contribution = CalculateRuleValue(rule, primary, secondary);
                            amount += contribution;
                            if (contribution > 0) externalBonusTriggers += 1;
                            AddRuleBreakdown(breakdown, rule, primary, secondary, contribution);
                        }
                        else if (rule.operation == "set_income")
                        {
                            int before = amount;
                            amount = CalculateRuleValue(rule, primary, secondary);
                            int contribution = amount - before;
                            AddRuleBreakdown(breakdown, rule, primary, secondary, contribution);
                        }
                        else if (rule.operation == "multiply")
                        {
                            int beforeMultiplier = amount;
                            amount = Mathf.RoundToInt(amount * (rule.multiplier == 0f ? 1f : rule.multiplier));
                            if (amount > beforeMultiplier) externalBonusTriggers += 1;
                            // 乘算规则也必须进入本轮收益明细。具体显示名称来自 Rules.reason，
                            // 例如“成套坐垫：1金币”，代码只负责记录本次乘算新增的金币。
                            AddRuleBreakdown(breakdown, rule, primary, secondary,
                                amount - beforeMultiplier);
                        }
                        else if (rule.operation == "set_max_adjacent")
                        {
                            int before = amount;
                            amount = primary;
                            AddRuleBreakdown(breakdown, rule, primary, secondary, amount - before);
                        }

                        if (rule.once_per_round) usedOnceRules.Add(rule.rule_id);
                    }

                    if (externalBonusTriggers > 0)
                    {
                        List<CatCafeConfigDatabase.RuleRow> growthRules =
                            ConfiguredRules("on_external_bonus", "element");
                        for (int i = 0; i < growthRules.Count; i++)
                        {
                            CatCafeConfigDatabase.RuleRow rule = growthRules[i];
                            if (rule.operation != "permanent_add" || !MatchesRuleSource(rule, element)) continue;
                            persistentGains.Add(new PersistentGain
                            {
                                Target = element,
                                Amount = CalculateRuleValue(rule, 0, 0) * externalBonusTriggers,
                                Reason = rule.reason
                            });
                        }
                    }

                    amount = AdjustMoneyLoss(amount);
                    if (boostedColumn >= 0 && index % BoardColumns == boostedColumn)
                        amount = Mathf.RoundToInt(amount * boostedColumnMultiplier);

                    int highValueThreshold = CatCafeConfigDatabase.GetInt("high_value_threshold", 8);
                    int chainThreshold = CatCafeConfigDatabase.GetInt("chain_connected_threshold", 3);
                    RoundEvent roundEvent = new RoundEvent
                    {
                        Element = element,
                        Index = index,
                        Amount = amount,
                        ConsumeSelf = consumeSelf,
                        IsSpecial = consumeSelf || element.Def.SpecialPresentation,
                        IsHighValue = amount >= highValueThreshold || ConnectedSameCount(index, element.Key) >= chainThreshold,
                        HasLink = hasLink
                    };
                    roundEvent.LinkedIndices.AddRange(linkedIndices);
                    roundEvent.Breakdown.AddRange(breakdown);
                    roundEvent.RemovedTargetIds.AddRange(removedTargetIds);
                    roundEvent.GeneratedKeys.AddRange(generatedKeys);
                    roundEvent.PersistentGains.AddRange(persistentGains);
                    roundEvent.ActionReasons.AddRange(actionReasons);
                    roundEvent.TransformTargetId = transformTargetId;
                    roundEvent.TransformResultKey = transformResultKey;
                    events.Add(roundEvent);
                }
            }

            ApplyRoundEventModifiers(events);
            ApplyExternalGrantedRules(events);
            ApplyRandomResultRules(events);
            return events;
        }

        private IEnumerator OfferBoardActions()
        {
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("before_settlement", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) ||
                    (rule.operation != "swap_two" && rule.operation != "shuffle_column")) continue;
                int primary = EvaluateScope(
                    rule.primary_scope, rule.primary_filter, null, -1, null, 0, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold)) continue;
                ItemDefinition item = itemDefs[rule.owner_key];
                boardActionResolved = false;
                bool accepted = false;
                ShowConfirm(
                    item.Name, item.Rule,
                    CatCafeConfigDatabase.GetRequiredString("ui_stage_item_accept_label"),
                    delegate
                    {
                        accepted = true;
                        boardActionMode = rule.operation;
                        boardActionFirstIndex = -1;
                        boostedColumnMultiplier = rule.multiplier == 0f ? 1f : rule.multiplier;
                        ShowToast(rule.reason);
                    }, null, delegate { boardActionResolved = true; });
                while (!boardActionResolved) yield return null;
                boardActionMode = null;
                boardActionFirstIndex = -1;
                if (accepted) RenderBoard();
            }
        }

        private bool HandleBoardActionClick(Element element)
        {
            if (string.IsNullOrEmpty(boardActionMode) || element == null) return false;
            int index = FindBoardIndex(element.Id);
            if (index < 0) return true;
            if (boardActionMode == "swap_two")
            {
                if (boardActionFirstIndex < 0)
                {
                    boardActionFirstIndex = index;
                    return true;
                }
                if (boardActionFirstIndex != index)
                {
                    Element first = board[boardActionFirstIndex];
                    board[boardActionFirstIndex] = board[index];
                    board[index] = first;
                }
                boardActionResolved = true;
                return true;
            }
            if (boardActionMode == "shuffle_column")
            {
                int column = index % BoardColumns;
                List<Element> values = new List<Element>();
                for (int row = 0; row < BoardRows; row++)
                    values.Add(board[row * BoardColumns + column]);
                values = Shuffle(values);
                for (int row = 0; row < BoardRows; row++)
                    board[row * BoardColumns + column] = values[row];
                boostedColumn = column;
                boardActionResolved = true;
                return true;
            }
            return false;
        }

        private int ApplyRandomIncomeModifiers(
            CatCafeConfigDatabase.RuleRow candidate, int value, int minimum, int maximum)
        {
            List<CatCafeConfigDatabase.RuleRow> modifiers =
                ConfiguredRules("modify_random_income", "item");
            for (int i = 0; i < modifiers.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow modifier = modifiers[i];
                if (!HasItem(modifier.owner_key) ||
                    !ContainsToken(modifier.source_keys, candidate.owner_key)) continue;
                if (modifier.operation == "set_max") value = maximum;
                else if (modifier.operation == "reroll" && value <= modifier.primary_threshold)
                    value = UnityEngine.Random.Range(minimum, maximum + 1);
            }
            return value;
        }

        private void RecordExternalBonusProviders(
            CatCafeConfigDatabase.RuleRow rule, List<int> nearbyIndexes)
        {
            if (rule == null || nearbyIndexes == null) return;
            string filter = rule.primary_scope == "adjacent_key"
                ? rule.primary_filter
                : rule.secondary_scope == "adjacent_key" ? rule.secondary_filter : string.Empty;
            if (string.IsNullOrEmpty(filter)) return;
            for (int i = 0; i < nearbyIndexes.Count; i++)
            {
                int index = nearbyIndexes[i];
                if (index < 0 || index >= board.Count || board[index] == null) continue;
                if (ContainsToken(filter, board[index].Key)) board[index].GrantedExternalBonuses += 1;
            }
        }

        private void ApplyExternalGrantedRules(List<RoundEvent> events)
        {
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("on_external_granted", "element");
            for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                RoundEvent ownerEvent = events[eventIndex];
                if (ownerEvent == null || ownerEvent.Element == null) continue;
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    CatCafeConfigDatabase.RuleRow rule = rules[ruleIndex];
                    if (rule.operation != "consume_at_count" ||
                        !MatchesRuleSource(rule, ownerEvent.Element)) continue;
                    if (!Passes(rule.primary_comparator,
                            ownerEvent.Element.GrantedExternalBonuses, rule.primary_threshold)) continue;
                    ownerEvent.ConsumeSelf = true;
                    ownerEvent.IsSpecial = true;
                    if (!string.IsNullOrEmpty(rule.reason)) ownerEvent.ActionReasons.Add(rule.reason);
                }
            }
        }

        private void ApplyRandomResultRules(List<RoundEvent> events)
        {
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("on_random_result", "element");
            for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                RoundEvent ownerEvent = events[eventIndex];
                if (ownerEvent == null || ownerEvent.Element == null) continue;
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    CatCafeConfigDatabase.RuleRow rule = rules[ruleIndex];
                    if (rule.operation != "consume_self" ||
                        !MatchesRuleSource(rule, ownerEvent.Element)) continue;
                    bool matched = false;
                    foreach (KeyValuePair<int, int> pair in roundRandomIncomeResults)
                    {
                        Element source = FindPoolElement(pair.Key);
                        if (source == null || !ContainsToken(rule.source_keys, source.Key)) continue;
                        if (!Passes(rule.primary_comparator, pair.Value, rule.primary_threshold)) continue;
                        matched = true;
                        break;
                    }
                    if (!matched) continue;
                    ownerEvent.ConsumeSelf = true;
                    ownerEvent.IsSpecial = true;
                    if (!string.IsNullOrEmpty(rule.reason)) ownerEvent.ActionReasons.Add(rule.reason);
                }
            }
        }

        private string ChooseDismissedHistoryKey(string excludedKeys)
        {
            List<string> candidates = new List<string>();
            for (int i = 0; i < dismissedHistory.Count; i++)
            {
                string key = dismissedHistory[i];
                if (!defs.ContainsKey(key) || ContainsTokenStrict(excludedKeys, key)) continue;
                candidates.Add(key);
            }
            return candidates.Count == 0
                ? string.Empty
                : candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private string ModifiedTransformResult(CatCafeConfigDatabase.RuleRow sourceRule)
        {
            string result = sourceRule.result_key;
            List<CatCafeConfigDatabase.RuleRow> modifiers =
                ConfiguredRules("modify_transform_result", "item");
            for (int i = 0; i < modifiers.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow modifier = modifiers[i];
                if (!HasItem(modifier.owner_key) || modifier.operation != "rarity_random" ||
                    !ContainsToken(modifier.source_keys, sourceRule.owner_key)) continue;
                List<string> candidates = new List<string>();
                for (int rarity = 0; rarity < RarityCount; rarity++)
                    if (ContainsToken(modifier.result_key, RarityKey((Rarity)rarity)))
                        candidates.AddRange(RewardPool(rarity));
                if (candidates.Count > 0)
                    result = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }
            return result;
        }

        private int AdjustMoneyLoss(int amount)
        {
            if (amount >= 0) return amount;
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("modify_money_loss", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "reduce_loss") continue;
                amount = Mathf.Min(0, amount + Mathf.Max(0, CalculateRuleValue(rule, 0, 0)));
            }
            return amount;
        }

        private void ApplyRoundEventModifiers(List<RoundEvent> events)
        {
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("modify_round_events", "element");
            for (int sourceIndex = 0; sourceIndex < board.Count; sourceIndex++)
            {
                Element source = board[sourceIndex];
                if (source == null) continue;
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    CatCafeConfigDatabase.RuleRow rule = rules[ruleIndex];
                    if (!MatchesRuleSource(rule, source) || IsRuleSuppressed(rule)) continue;
                    int primary = EvaluateScope(
                        rule.primary_scope, rule.primary_filter, source, sourceIndex,
                        Neighbors(sourceIndex, false), 0, false);
                    int secondary = EvaluateScope(
                        rule.secondary_scope, rule.secondary_filter, source, sourceIndex,
                        Neighbors(sourceIndex, false), 0, false);
                    if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                        !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;
                    if (RollRuleTriggers(rule, source, sourceIndex) <= 0) continue;

                    List<int> targets = new List<int>();
                    if (rule.target_scope == "random_direction")
                    {
                        List<int> directions = new List<int>();
                        for (int direction = 0; direction < 4; direction++)
                        {
                            List<int> candidates = new List<int>();
                            AddDirectionTargets(
                                candidates, sourceIndex, direction, rule.target_filter);
                            if (candidates.Count > 0) directions.Add(direction);
                        }
                        directions = Shuffle(directions);
                        if (directions.Count == 0) continue;
                        int directionCount = Mathf.Clamp(
                            ModifiedTargetLimit(rule), 1, directions.Count);
                        for (int directionIndex = 0; directionIndex < directionCount; directionIndex++)
                            AddDirectionTargets(targets, sourceIndex, directions[directionIndex], rule.target_filter);
                    }
                    else if (rule.target_scope == "adjacent_random")
                    {
                        List<int> candidates = Shuffle(AdjacentIndexes(sourceIndex, false));
                        int targetCount = ModifiedTargetLimit(rule);
                        if (targetCount <= 0) targetCount = candidates.Count;
                        for (int i = 0; i < candidates.Count && targets.Count < targetCount; i++)
                        {
                            int candidate = candidates[i];
                            if (candidate >= 0 && candidate < board.Count &&
                                MatchesActionFilter(board[candidate], rule.target_filter)) targets.Add(candidate);
                        }
                    }

                    for (int i = 0; i < targets.Count; i++)
                    {
                        RoundEvent targetEvent = FindRoundEvent(events, targets[i]);
                        if (targetEvent == null) continue;
                        int before = targetEvent.Amount;
                        if (rule.operation == "multiply_targets")
                            targetEvent.Amount = Mathf.RoundToInt(
                                targetEvent.Amount * (rule.multiplier == 0f ? 1f : rule.multiplier));
                        else if (rule.operation == "set_targets_zero")
                            targetEvent.Amount = 0;
                        targetEvent.Amount = AdjustMoneyLoss(targetEvent.Amount);
                        if (targetEvent.Amount != before)
                        {
                            targetEvent.HasLink = true;
                            targetEvent.LinkedIndices.Add(sourceIndex);
                            if (!string.IsNullOrEmpty(rule.reason))
                                targetEvent.Breakdown.Add(rule.reason + "：" + (targetEvent.Amount - before) + "金币");
                            if (targetEvent.Amount > before) AddExternalBonusGrowth(targetEvent);
                        }
                    }
                }
            }
        }

        private void AddDirectionTargets(List<int> target, int sourceIndex, int direction, string filter)
        {
            List<int> ray = CatCafeMechanicMath.CardinalRay(
                sourceIndex, direction, BoardRows, BoardColumns);
            for (int i = 0; i < ray.Count; i++)
            {
                int index = ray[i];
                if (index < board.Count && MatchesActionFilter(board[index], filter) && !target.Contains(index))
                    target.Add(index);
            }
        }

        private static RoundEvent FindRoundEvent(List<RoundEvent> events, int boardIndex)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i] != null && events[i].Index == boardIndex) return events[i];
            return null;
        }

        private void AddExternalBonusGrowth(RoundEvent targetEvent)
        {
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("on_external_bonus", "element");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (rule.operation != "permanent_add" ||
                    !MatchesRuleSource(rule, targetEvent.Element)) continue;
                targetEvent.PersistentGains.Add(new PersistentGain
                {
                    Target = targetEvent.Element,
                    Amount = CalculateRuleValue(rule, 0, 0),
                    Reason = rule.reason
                });
            }
        }

        /// <summary>
        /// 清掉名册中每个棋子实例保存的上轮收益。调用时机是新一次转动刚开始，
        /// 因此尚未完成结算的棋子详情不会显示旧数据。
        /// </summary>
        private void ClearLastRoundIncomeRecords()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                Element element = pool[i];
                if (element == null) continue;
                element.LastRoundIncome = 0;
                element.LastRoundIncomeBreakdown.Clear();
            }
        }

        /// <summary>
        /// 把本轮最终采用的 RoundEvent 写回产生收益的棋子实例。
        /// 金币播放队列和详情展示共用同一批事件，避免画面显示与实际入账不一致。
        /// </summary>
        private static void RecordLastRoundIncome(IList<RoundEvent> events)
        {
            if (events == null) return;

            for (int i = 0; i < events.Count; i++)
            {
                RoundEvent current = events[i];
                if (current == null || current.Element == null || current.Amount <= 0) continue;

                current.Element.LastRoundIncome += current.Amount;
                current.Element.LastRoundIncomeBreakdown.AddRange(current.Breakdown);
            }
        }

        private static bool IsAdjacencyScope(string scope)
        {
            return !string.IsNullOrEmpty(scope) && scope.StartsWith("adjacent", StringComparison.Ordinal);
        }

        private static bool IsLinkedScope(string scope)
        {
            return IsAdjacencyScope(scope) || scope == "same_row_key";
        }

        private void AddLinkedIndices(
            HashSet<int> linkedIndices, List<int> adjacentIndexes, string scope, string filter,
            int sourceIndex)
        {
            if (linkedIndices == null) return;

            if (scope == "same_row_key")
            {
                // 同行范围不用依赖 nearby；从触发棋子所在行读取，由调用方最后去掉自身。
                if (sourceIndex < 0) return;
                int row = sourceIndex / BoardColumns;
                for (int column = 0; column < BoardColumns; column++)
                {
                    int candidate = row * BoardColumns + column;
                    if (candidate != sourceIndex && candidate < board.Count &&
                        board[candidate] != null && board[candidate].Key == filter)
                        linkedIndices.Add(candidate);
                }
                return;
            }

            if (adjacentIndexes == null || !IsAdjacencyScope(scope)) return;

            for (int i = 0; i < adjacentIndexes.Count; i++)
            {
                int neighborIndex = adjacentIndexes[i];
                if (neighborIndex < 0 || neighborIndex >= board.Count) continue;
                Element neighbor = board[neighborIndex];
                if (MatchesAdjacencyFilter(neighbor, scope, filter))
                    linkedIndices.Add(neighborIndex);
            }
        }

        private static bool MatchesAdjacencyFilter(Element element, string scope, string filter)
        {
            if (element == null) return false;

            if (scope == "adjacent_key")
                return string.IsNullOrEmpty(filter) || ContainsToken(filter, element.Key);

            if (scope == "adjacent_kind")
                return string.IsNullOrEmpty(filter) ||
                    string.Equals(element.Kind.ToString(), filter, StringComparison.OrdinalIgnoreCase);

            if (scope == "adjacent_cats")
                return element.Kind == Kind.Cat || element.Kind == Kind.Kitten;

            return true;
        }

        /// <summary>
        /// 解析一条动作规则本轮命中的实例。这里只选择目标并按实例 ID 预占，绝不修改棋盘；
        /// 因此结算动画播放期间画面与快速计算出的事件队列始终一致。
        /// </summary>
        private List<Element> ResolveActionTargets(
            CatCafeConfigDatabase.RuleRow rule,
            Element source,
            int sourceIndex,
            HashSet<int> claimedTargets)
        {
            List<Element> result = new List<Element>();
            if (rule == null || source == null) return result;

            int configuredLimit = ModifiedTargetLimit(rule);
            int limit = configuredLimit <= 0 ? int.MaxValue : configuredLimit;
            List<int> candidates = new List<int>();
            if (rule.target_scope == "self")
            {
                candidates.Add(sourceIndex);
            }
            else if (rule.target_scope == "adjacent" || rule.target_scope == "adjacent_keys" ||
                     rule.target_scope == "adjacent_key" ||
                     rule.target_scope == "adjacent_random")
            {
                if (UsesGlobalAdjacency(source, sourceIndex, rule.target_filter))
                {
                    for (int i = 0; i < board.Count; i++)
                        if (i != sourceIndex && board[i] != null) candidates.Add(i);
                }
                else candidates.AddRange(AdjacentIndexes(sourceIndex, false));
            }
            else if (rule.target_scope == "adjacent_diagonal")
            {
                candidates.AddRange(AdjacentIndexes(sourceIndex, true));
            }
            else if (rule.target_scope == "board" || rule.target_scope == "board_keys" ||
                     rule.target_scope == "board_key")
            {
                for (int i = 0; i < board.Count; i++) if (board[i] != null) candidates.Add(i);
            }

            if (rule.target_scope == "adjacent_random")
                candidates = Shuffle(candidates);

            for (int i = 0; i < candidates.Count && result.Count < limit; i++)
            {
                int candidateIndex = candidates[i];
                if (candidateIndex < 0 || candidateIndex >= board.Count) continue;
                Element candidate = board[candidateIndex];
                if (candidate == null || claimedTargets.Contains(candidate.Id)) continue;
                if (!MatchesActionFilter(candidate, rule.target_filter)) continue;
                result.Add(candidate);
            }
            return result;
        }

        private static bool MatchesActionFilter(Element target, string filter)
        {
            if (target == null || string.IsNullOrEmpty(filter)) return false;
            if (filter == "*") return true;
            string[] tokens = filter.Split('|');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (string.Equals(token, target.Key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(token, target.Kind.ToString(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>读取目标棋子的“固定金币”规则，供按目标身价倍数结算的通用动作使用。</summary>
        private int ConfiguredBaseIncome(Element target)
        {
            if (target == null) return 0;
            int value = 0;
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("round", "element");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (rule.operation != "income" || !MatchesRuleSource(rule, target)) continue;
                if ((!string.IsNullOrEmpty(rule.primary_scope) && rule.primary_scope != "none") ||
                    (!string.IsNullOrEmpty(rule.secondary_scope) && rule.secondary_scope != "none")) continue;
                value += rule.base_value;
            }
            return value;
        }

        /// <summary>
        /// 概率原子。repeat_on_success=true 时每次成功后继续掷同一个概率，直到失败或达到表中上限。
        /// </summary>
        private int RollRuleTriggers(CatCafeConfigDatabase.RuleRow rule, Element source = null, int sourceIndex = -1)
        {
            if (rule == null) return 0;
            int limit = Mathf.Max(1, rule.max_triggers);
            if (rule.target_value_mode == "round_capped")
            {
                int used;
                roundRuleTriggerCounts.TryGetValue(rule.rule_id, out used);
                limit = Mathf.Max(0, limit - used);
                if (limit == 0) return 0;
            }
            float chance = Mathf.Clamp01(ModifiedRuleChance(rule, source, sourceIndex));
            int count = 0;
            while (count < limit && UnityEngine.Random.value < chance)
            {
                count += 1;
                if (!rule.repeat_on_success) break;
            }
            if (rule.target_value_mode == "round_capped" && count > 0)
            {
                int used;
                roundRuleTriggerCounts.TryGetValue(rule.rule_id, out used);
                roundRuleTriggerCounts[rule.rule_id] = used + count;
            }
            return count;
        }

        private float ModifiedRuleChance(CatCafeConfigDatabase.RuleRow candidate, Element source, int sourceIndex)
        {
            float chance = candidate.chance;
            List<CatCafeConfigDatabase.RuleRow> modifiers = ConfiguredRules("modify_rule_chance", "item");
            for (int i = 0; i < modifiers.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow modifier = modifiers[i];
                if (!HasItem(modifier.owner_key) || modifier.operation != "multiply") continue;
                if (!ContainsToken(modifier.target_value_mode, candidate.operation)) continue;
                if (!ContainsToken(modifier.source_keys, candidate.owner_key)) continue;
                chance *= modifier.multiplier == 0f ? 1f : modifier.multiplier;
            }

            modifiers = ConfiguredRules("modify_rule_chance", "element");
            for (int boardIndex = 0; boardIndex < board.Count; boardIndex++)
            {
                Element owner = board[boardIndex];
                if (owner == null) continue;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    CatCafeConfigDatabase.RuleRow modifier = modifiers[i];
                    if (modifier.operation != "multiply" || !MatchesRuleSource(modifier, owner)) continue;
                    if (source == null || !MatchesActionFilter(source, modifier.target_filter)) continue;
                    int liveSourceIndex = sourceIndex >= 0 ? sourceIndex : FindBoardIndex(source.Id);
                    if (liveSourceIndex < 0 || !AdjacentIndexes(boardIndex, false).Contains(liveSourceIndex)) continue;
                    chance *= modifier.multiplier == 0f ? 1f : modifier.multiplier;
                }
            }
            return chance;
        }

        /// <summary>
        /// 从 Rules.result_key 的竖线分隔候选中随机选择一个结果。
        /// 候选集合及具体对象键完全来自表格；代码只提供通用随机选择原子。
        /// </summary>
        private string ChooseRuleResultKey(CatCafeConfigDatabase.RuleRow sourceRule)
        {
            string configuredKeys = sourceRule == null ? string.Empty : sourceRule.result_key;
            if (string.IsNullOrWhiteSpace(configuredKeys))
                throw new InvalidOperationException("[CatCafeConfig] 随机生成规则缺少 result_key。");

            string[] raw = configuredKeys.Split('|');
            List<string> candidates = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                string key = raw[i].Trim();
                if (!string.IsNullOrEmpty(key)) candidates.Add(key);
            }
            if (candidates.Count == 0)
                throw new InvalidOperationException("[CatCafeConfig] 随机生成规则没有有效候选：" + configuredKeys);

            List<CatCafeConfigDatabase.RuleRow> modifiers =
                ConfiguredRules("modify_generated_result", "item");
            for (int i = 0; i < modifiers.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow modifier = modifiers[i];
                if (!HasItem(modifier.owner_key) || modifier.operation != "rarity_filter" ||
                    !ContainsToken(modifier.source_keys, sourceRule.owner_key)) continue;
                List<string> filtered = new List<string>();
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    Definition definition;
                    if (defs.TryGetValue(candidates[candidateIndex], out definition) &&
                        ContainsToken(modifier.result_key, RarityKey(EffectiveRarity(definition))))
                        filtered.Add(candidates[candidateIndex]);
                }
                if (filtered.Count > 0) candidates = filtered;
            }
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// 查找能阻止目标被移除的棋子。保护者、相邻范围、可保护目标和成长数值全部来自 Rules 表。
        /// 多个保护者同时满足时按棋盘顺序选择第一个，保证录像复现稳定。
        /// </summary>
        private bool TryFindRemovalProtector(Element target, out PersistentGain gain)
        {
            gain = null;
            if (target == null) return false;

            List<CatCafeConfigDatabase.RuleRow> selfRules = ConfiguredRules("prevent_remove", "element");
            for (int i = 0; i < selfRules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = selfRules[i];
                if (rule.operation == "immune" && MatchesRuleSource(rule, target))
                    return true;
            }
            List<CatCafeConfigDatabase.RuleRow> itemRules = ConfiguredRules("prevent_remove", "item");
            for (int i = 0; i < itemRules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = itemRules[i];
                if (rule.operation == "immune" && HasItem(rule.owner_key) && MatchesRuleSource(rule, target))
                    return true;
            }
            int targetIndex = FindBoardIndex(target.Id);
            if (targetIndex < 0) return false;

            List<CatCafeConfigDatabase.RuleRow> rules = selfRules;
            for (int index = 0; index < board.Count; index++)
            {
                Element protector = board[index];
                if (protector == null || protector.Id == target.Id) continue;
                for (int i = 0; i < rules.Count; i++)
                {
                    CatCafeConfigDatabase.RuleRow rule = rules[i];
                    if (rule.operation != "prevent_remove" || !MatchesRuleSource(rule, protector)) continue;
                    if (!MatchesActionFilter(target, rule.target_filter)) continue;
                    bool adjacent = rule.target_scope == "adjacent_diagonal"
                        ? AdjacentIndexes(index, true).Contains(targetIndex)
                        : AdjacentIndexes(index, false).Contains(targetIndex);
                    if (!adjacent) continue;

                    gain = new PersistentGain
                    {
                        Target = protector,
                        Amount = rule.base_value,
                        Reason = rule.reason
                    };
                    return true;
                }
            }
            return false;
        }

        private void ApplyPersistentGain(PersistentGain gain)
        {
            if (gain == null || gain.Target == null || gain.Amount == 0) return;
            if (!string.IsNullOrEmpty(gain.RuleId) &&
                !gain.Target.AppliedPersistentRules.Add(gain.RuleId)) return;
            gain.Target.PermanentIncomeBonus += gain.Amount;
            if (!string.IsNullOrEmpty(gain.Reason)) ShowToast(gain.Reason);
        }

        private List<int> AdjacentIndexes(int index, bool diagonals)
        {
            List<int> result = new List<int>();
            int row = index / BoardColumns;
            int column = index % BoardColumns;
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    if (!diagonals && Math.Abs(dr) + Math.Abs(dc) != 1) continue;

                    int nr = row + dr;
                    int nc = column + dc;
                    if (nr < 0 || nr >= BoardRows || nc < 0 || nc >= BoardColumns) continue;

                    int neighborIndex = nr * BoardColumns + nc;
                    if (neighborIndex >= 0 && neighborIndex < board.Count && board[neighborIndex] != null)
                        result.Add(neighborIndex);
                }
            }
            return result;
        }




        private List<RoundEventGroup> BuildLinkedEventGroups(List<RoundEvent> events)
        {
            List<RoundEventGroup> linkedGroups = new List<RoundEventGroup>();
            if (events == null || events.Count == 0) return linkedGroups;

            Dictionary<int, RoundEvent> eventsByIndex = new Dictionary<int, RoundEvent>();
            for (int i = 0; i < events.Count; i++)
            {
                RoundEvent current = events[i];
                if (current != null && current.HasLink &&
                    !eventsByIndex.ContainsKey(current.Index))
                    eventsByIndex.Add(current.Index, current);
            }

            HashSet<int> processed = new HashSet<int>();
            bool foundNewInteraction;
            do
            {
                foundNewInteraction = false;

                // 每一轮都从左上角重新扫描，保证新产生的联动不会跳过前面的列。
                for (int column = 0; column < BoardColumns; column++)
                {
                    for (int row = 0; row < BoardRows; row++)
                    {
                        int index = row * BoardColumns + column;
                        RoundEvent current;
                        if (!eventsByIndex.TryGetValue(index, out current) ||
                            processed.Contains(index)) continue;

                        RoundEventGroup group = new RoundEventGroup
                        {
                            IsLinked = true
                        };
                        group.Events.Add(current);
                        group.LinkedIndices.AddRange(current.LinkedIndices);
                        linkedGroups.Add(group);
                        processed.Add(index);
                        foundNewInteraction = true;
                    }
                }
            }
            while (foundNewInteraction && processed.Count < eventsByIndex.Count);

            return linkedGroups;
        }

        private static List<RoundPayoutBatch> BuildPayoutBatches(List<RoundEvent> events)
        {
            SortedDictionary<int, RoundPayoutBatch> batches =
                new SortedDictionary<int, RoundPayoutBatch>();
            for (int i = 0; i < events.Count; i++)
            {
                RoundEvent current = events[i];
                // 联动棋子已经在逐个联动演出后立即入账，不能再进入普通批次。
                if (current == null || current.HasLink || current.Amount == 0) continue;
                RoundPayoutBatch batch;
                if (!batches.TryGetValue(current.Amount, out batch))
                {
                    batch = new RoundPayoutBatch { UnitAmount = current.Amount };
                    batches.Add(current.Amount, batch);
                }
                batch.Events.Add(current);
            }
            return new List<RoundPayoutBatch>(batches.Values);
        }

        private static string PayoutBatchSourceLabel(RoundPayoutBatch batch)
        {
            if (batch == null || batch.Events.Count == 0) return string.Empty;

            int limit = CatCafeConfigDatabase.GetRequiredInt("settlement_source_name_limit");
            if (limit <= 0)
                throw new InvalidOperationException(
                    "[CatCafeConfig] settlement_source_name_limit 必须大于 0。");

            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            bool hasMore = false;
            for (int i = 0; i < batch.Events.Count; i++)
            {
                string name = batch.Events[i].Element.Name;
                if (!seen.Add(name)) continue;
                if (names.Count < limit) names.Add(name);
                else hasMore = true;
            }

            string joined = string.Join(
                CatCafeConfigDatabase.GetRequiredString("ui_settlement_source_separator"),
                names.ToArray());
            return hasMore
                ? string.Format(
                    CatCafeConfigDatabase.GetRequiredString(
                        "ui_settlement_more_sources_format"), joined)
                : joined;
        }

        private IEnumerator SettleConsumedEvents(IList<RoundEvent> events)
        {
            ClearPendingDismissRewards();
            for (int i = 0; i < events.Count; i++)
            {
                RoundEvent current = events[i];
                if (current == null || current.Element == null) continue;
                bool hasLifecycleAction = current.ConsumeSelf ||
                    current.RemovedTargetIds.Count > 0 ||
                    current.GeneratedKeys.Count > 0 ||
                    current.PersistentGains.Count > 0 ||
                    current.TransformTargetId > 0;
                if (!hasLifecycleAction) continue;
                Vector2 sourcePosition = GetBoardRewardPosition(current.Index);
                if (current.ActionReasons.Count > 0)
                    ShowToast(string.Join(
                        CatCafeConfigDatabase.GetRequiredString("ui_action_reason_separator"),
                        current.ActionReasons.ToArray()));

                for (int gainIndex = 0; gainIndex < current.PersistentGains.Count; gainIndex++)
                    ApplyPersistentGain(current.PersistentGains[gainIndex]);

                for (int targetIndex = 0; targetIndex < current.RemovedTargetIds.Count; targetIndex++)
                {
                    Element removedTarget = FindPoolElement(current.RemovedTargetIds[targetIndex]);
                    if (removedTarget == null) continue;
                    RemoveElementInstance(removedTarget);
                    pendingDismissCoins += EvaluateDismissRules(removedTarget);
                }

                if (current.TransformTargetId > 0 && !string.IsNullOrEmpty(current.TransformResultKey))
                    TransformElementInstance(current.TransformTargetId, current.TransformResultKey);

                for (int generatedIndex = 0; generatedIndex < current.GeneratedKeys.Count; generatedIndex++)
                    BringGeneratedElement(current.GeneratedKeys[generatedIndex]);

                if (current.ConsumeSelf)
                {
                    Element liveSource = FindPoolElement(current.Element.Id);
                    PersistentGain preventedGain;
                    if (liveSource != null && TryFindRemovalProtector(liveSource, out preventedGain))
                    {
                        ApplyPersistentGain(preventedGain);
                    }
                    else if (liveSource != null)
                    {
                        RemoveElementInstance(liveSource);
                        pendingDismissCoins += EvaluateDismissRules(liveSource);
                    }
                }

                // 被动作移除的棋子，其 on_dismiss 奖励与本条动作一并结算。
                int lifecycleCoins = pendingDismissCoins;
                int lifecycleRemoval = pendingDismissRemovalTokens;
                int lifecycleReroll = pendingDismissRerollTokens;
                int lifecycleInspiration = pendingDismissInspirationTokens;
                List<string> lifecycleGenerated = new List<string>(pendingDismissGeneratedKeys);
                ClearPendingDismissRewards();
                removalTokens += lifecycleRemoval;
                rerollTokens += lifecycleReroll;
                inspirationTokens += lifecycleInspiration;
                for (int generatedIndex = 0; generatedIndex < lifecycleGenerated.Count; generatedIndex++)
                    BringGeneratedElement(lifecycleGenerated[generatedIndex]);

                RenderBoard();
                RefreshPieceBox();
                UpdateHud();
                PlayTicketGainNotes(sourcePosition, lifecycleReroll, lifecycleRemoval);
                if (lifecycleCoins > 0)
                    yield return StartCoroutine(PlayCoinReward(sourcePosition, lifecycleCoins));
            }
        }

        private Element FindPoolElement(int instanceId)
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && pool[i].Id == instanceId) return pool[i];
            return null;
        }

        private void RemoveElementInstance(Element target)
        {
            if (target == null) return;
            int boardIndex = FindBoardIndex(target.Id);
            if (boardIndex >= 0) board[boardIndex] = null;
            pool.RemoveAll(element => element != null && element.Id == target.Id);
            consumedElements += 1;
        }

        private void BringGeneratedElement(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!defs.ContainsKey(key))
                throw new InvalidOperationException("[CatCafeConfig] Rules.result_key 未在 Elements 中配置：" + key);
            if (!CanAddElement(key)) return;

            Element generated = ApplyElementEnterRules(Instance(key));
            pool.Add(generated);
            for (int i = 0; i < board.Count; i++)
            {
                if (board[i] != null) continue;
                board[i] = generated;
                break;
            }
        }

        private Element ApplyElementEnterRules(Element entering)
        {
            if (entering == null) return null;
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("element_enter", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "transform" ||
                    !MatchesRuleSource(rule, entering)) continue;
                if (!defs.ContainsKey(rule.result_key))
                    throw new InvalidOperationException(
                        "[CatCafeConfig] element_enter.result_key 未在 Elements 中配置：" + rule.result_key);
                entering = Instance(rule.result_key);
            }
            return entering;
        }

        private void ApplyElementEnterRulesToPool()
        {
            List<Element> snapshot = new List<Element>(pool);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Element source = snapshot[i];
                Element replacement = ApplyElementEnterRules(source);
                if (replacement == null || replacement.Key == source.Key) continue;
                int boardIndex = FindBoardIndex(source.Id);
                int poolIndex = pool.FindIndex(delegate(Element value)
                {
                    return value != null && value.Id == source.Id;
                });
                if (poolIndex >= 0) pool[poolIndex] = replacement;
                if (boardIndex >= 0) board[boardIndex] = replacement;
            }
            RenderBoard();
            RefreshPieceBox();
        }

        private bool CanAddElement(string key)
        {
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("pool_limit", "element");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (rule.operation != "max_count" || rule.owner_key != key) continue;
                int count = 0;
                for (int poolIndex = 0; poolIndex < pool.Count; poolIndex++)
                    if (pool[poolIndex] != null && pool[poolIndex].Key == key) count += 1;
                if (count >= Mathf.Max(0, CalculateRuleValue(rule, 0, 0))) return false;
            }
            return true;
        }

        private void TransformElementInstance(int instanceId, string resultKey)
        {
            if (!defs.ContainsKey(resultKey))
                throw new InvalidOperationException("[CatCafeConfig] Rules.result_key 未在 Elements 中配置：" + resultKey);
            Element source = FindPoolElement(instanceId);
            if (source == null) return;

            Element replacement = Instance(resultKey);
            int boardIndex = FindBoardIndex(instanceId);
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null || pool[i].Id != instanceId) continue;
                pool[i] = replacement;
                break;
            }
            if (boardIndex >= 0) board[boardIndex] = replacement;
        }

        private void ClearPendingDismissRewards()
        {
            pendingDismissCoins = 0;
            pendingDismissRemovalTokens = 0;
            pendingDismissRerollTokens = 0;
            pendingDismissInspirationTokens = 0;
            pendingDismissGeneratedKeys.Clear();
        }

        private string EventGroupLabel(RoundEventGroup group)
        {
            if (!group.IsLinked)
                return group.Events.Count > 1
                    ? UiString("ui_settlement_plain_group_label")
                    : group.Events[0].Element.Name;
            if (group.Events.Count == 1)
                return string.Format(UiString("ui_settlement_single_link_label_format"),
                    group.Events[0].Element.Name);
            return string.Format(UiString("ui_settlement_multi_link_label_format"),
                group.Events[0].Element.Name, group.Events.Count);
        }

        private bool UsesDiagonalAdjacency(Element element, int index)
        {
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("adjacency", "item");
            List<Element> orthogonal = Neighbors(index, false);
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "include_diagonal" || !MatchesRuleSource(rule, element)) continue;
                int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, element, index, orthogonal, 0, false);
                int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, element, index, orthogonal, 0, false);
                if (Passes(rule.primary_comparator, primary, rule.primary_threshold) &&
                    Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) return true;
            }
            return false;
        }

        private List<CatCafeConfigDatabase.RuleRow> ConfiguredRules(string trigger, string ownerType)
        {
            List<CatCafeConfigDatabase.RuleRow> result = new List<CatCafeConfigDatabase.RuleRow>();
            CatCafeConfigDatabase.RuleRow[] rows = CatCafeConfigDatabase.Data.rules;
            for (int i = 0; i < rows.Length; i++)
            {
                CatCafeConfigDatabase.RuleRow row = rows[i];
                if (row.enabled && row.trigger == trigger && row.owner_type == ownerType) result.Add(row);
            }
            result.Sort(delegate(CatCafeConfigDatabase.RuleRow a, CatCafeConfigDatabase.RuleRow b)
            {
                return a.priority.CompareTo(b.priority);
            });
            return result;
        }

        private bool MatchesRuleSource(CatCafeConfigDatabase.RuleRow rule, Element element)
        {
            if (element == null) return string.IsNullOrEmpty(rule.source_kinds) && string.IsNullOrEmpty(rule.source_keys);
            if (rule.owner_type == "element" && rule.owner_key != "*" && rule.owner_key != element.Key) return false;
            if (!ContainsToken(rule.source_kinds, element.Kind.ToString())) return false;
            if (!ContainsToken(rule.source_keys, element.Key)) return false;
            return true;
        }

        private static bool ContainsToken(string tokens, string value)
        {
            if (string.IsNullOrEmpty(tokens) || tokens == "*") return true;
            string[] parts = tokens.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Trim(), value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private int EvaluateScope(string scope, string filter, Element element, int index,
            List<Element> nearby, int roundIncome, bool consumeSelf)
        {
            if (string.IsNullOrEmpty(scope) || scope == "none") return 0;
            bool globalAdjacency = UsesGlobalAdjacency(element, index, filter);
            if (scope == "adjacent_cats") return globalAdjacency ? CountCats() : CountCats(nearby);
            if (scope == "adjacent_kind")
                return globalAdjacency ? CountKind(ParseKind(filter)) : CountKind(nearby, ParseKind(filter));
            if (scope == "adjacent_key") return globalAdjacency ? CountBoardKey(filter) : CountKey(nearby, filter);
            if (scope == "board_cats") return CountCats();
            if (scope == "board_kind") return CountKind(ParseKind(filter));
            if (scope == "board_key") return CountBoardKey(filter);
            if (scope == "board_key_or_adjacent_key")
            {
                string[] keys = (filter ?? string.Empty).Split(';');
                int boardCount = keys.Length > 0 ? CountBoardKey(keys[0]) : 0;
                bool hasAdjacent = keys.Length > 1 && CountKey(nearby, keys[1]) > 0;
                // BoardSize 作为“条件已满足”的上界哨兵，使比较阈值继续完全来自表格。
                return hasAdjacent ? BoardSize : boardCount;
            }
            if (scope == "board_empty") return CountBoardEmpty();
            if (scope == "board_max_same") return CountBoardMaximumSame();
            if (scope == "board_max_connected_same") return CountBoardMaximumConnectedSame();
            if (scope == "board_all_unique") return BoardHasOnlyUniqueElements() ? 1 : 0;
            if (scope == "pool_duplicate_count") return CountPoolDuplicateElements();
            if (scope == "same_row_key") return CountSameRowKey(index, filter, element);
            if (scope == "board_distinct_cat_color") return CountDistinctCatColors();
            if (scope == "connected_same") return ConnectedSameCount(index, element.Key);
            // 相邻空位：斯芬克斯那类「越空越赚、坐满就走」的效果全靠它。
            // 棋盘上的空位是 board[i] == null，本身不是对象，所以数不进 nearby。
            if (scope == "adjacent_empty") return CountAdjacentEmpty(index);
            if (scope == "self_corner")
            {
                if (index < 0) return 0;
                int row = index / BoardColumns;
                int column = index % BoardColumns;
                return (row == 0 || row == BoardRows - 1) &&
                    (column == 0 || column == BoardColumns - 1) ? 1 : 0;
            }
            // 当前是第几波（从 1 起算）。配 modulo_zero 比较符就是「每 N 波触发一次」，
            // 不需要给每枚棋子挂独立计数器。
            if (scope == "round_number") return round + 1;
            if (scope == "waves_remaining")
                return Mathf.Max(0, CurrentStage.Rounds + stageBonusRounds - stageRound);
            if (scope == "instance_rounds")
                return element == null ? 0 : CatCafeMechanicMath.EffectiveCycleAge(
                    element.LifetimeRounds, CycleReduction(element));
            if (scope == "instance_round_number")
                return element == null ? 0 : CatCafeMechanicMath.EffectiveCycleAge(
                    element.LifetimeRounds + 1, CycleReduction(element));
            // 自身稀有度序号（普通0/少见1/稀有2/特殊3）。配 base+primary_factor 就能写出
            // 「越稀有给得越多」的一条通用规则，不必给每个稀有度各配一行。
            if (scope == "self_rarity") return element == null ? 0 : (int)EffectiveRarity(element.Def);
            if (scope == "pool_cats") return CountPoolCats();
            if (scope == "pool_key")
            {
                int count = 0;
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i] != null && ContainsToken(filter, pool[i].Key)) count += 1;
                return count;
            }
            if (scope == "board_key_left") return CountColumnKey(0, filter);
            if (scope == "board_key_right") return CountColumnKey(BoardColumns - 1, filter);
            if (scope == "board_key_cycle_ready")
            {
                string[] parts = (filter ?? string.Empty).Split('|');
                int threshold;
                if (parts.Length < 2 || !int.TryParse(parts[1], out threshold)) return 0;
                for (int i = 0; i < board.Count; i++)
                {
                    Element candidate = board[i];
                    if (candidate != null && candidate.Key == parts[0] &&
                        CatCafeMechanicMath.EffectiveCycleAge(
                            candidate.LifetimeRounds, CycleReduction(candidate)) >= threshold) return 1;
                }
                return 0;
            }
            if (scope == "owned_items") return ownedItems.Count;
            if (scope == "owned_item_key")
            {
                int count = 0;
                for (int i = 0; i < ownedItems.Count; i++)
                    if (ContainsToken(filter, ownedItems[i])) count += 1;
                return count;
            }
            if (scope == "owned_item_rounds") return OwnedItemRoundCount(filter);
            if (scope == "item_counter") return ItemCounter(filter);
            if (scope == "item_counter_capped")
            {
                string[] parts = (filter ?? string.Empty).Split('|');
                int cap;
                if (parts.Length < 2 || !int.TryParse(parts[1], out cap)) cap = int.MaxValue;
                return Mathf.Min(ItemCounter(parts.Length > 0 ? parts[0] : string.Empty), cap);
            }
            if (scope == "removal_tokens") return removalTokens;
            if (scope == "skipped_count") return skippedChoiceHistory.Count;
            if (scope == "inspiration_tokens") return inspirationTokens;
            if (scope == "round_income") return roundIncome;
            if (scope == "consumed_total") return consumedElements;
            if (scope == "consume_self") return consumeSelf ? 1 : 0;
            if (scope == "max_adjacent_base")
            {
                int maximum = 0;
                List<Element> candidates = UsesGlobalAdjacency(element, index, filter)
                    ? board
                    : nearby;
                if (candidates == null) return maximum;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] == null || candidates[i] == element) continue;
                    maximum = Mathf.Max(maximum, ConfiguredBaseIncome(candidates[i]));
                }
                return maximum;
            }
            if (scope == "self_left")
                return index >= 0 && index % BoardColumns == 0 ? 1 : 0;
            return 0;
        }

        private bool UsesGlobalAdjacency(Element element, int index, string requestedKeys)
        {
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("adjacency", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key)) continue;
                int value = rule.primary_scope == "round_number" ? round + 1 : 0;
                if (!Passes(rule.primary_comparator, value, rule.primary_threshold)) continue;
                if (rule.operation == "global_all") return true;
                if (rule.operation == "global_corners" && index >= 0)
                {
                    int row = index / BoardColumns;
                    int column = index % BoardColumns;
                    if ((row == 0 || row == BoardRows - 1) &&
                        (column == 0 || column == BoardColumns - 1)) return true;
                }
                if (rule.operation == "global_key" &&
                    !string.IsNullOrEmpty(requestedKeys))
                {
                    string[] keys = requestedKeys.Split('|');
                    for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                        if (ContainsToken(rule.source_keys, keys[keyIndex].Trim())) return true;
                }
            }
            return false;
        }

        private int CountBoardEmpty()
        {
            int count = 0;
            for (int i = 0; i < board.Count; i++) if (board[i] == null) count += 1;
            return count;
        }

        private int CountBoardMaximumSame()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            int maximum = 0;
            for (int i = 0; i < board.Count; i++)
            {
                Element element = board[i];
                if (element == null) continue;
                int value;
                counts.TryGetValue(element.Key, out value);
                value += 1;
                counts[element.Key] = value;
                maximum = Mathf.Max(maximum, value);
            }
            return maximum;
        }

        private int CountBoardMaximumConnectedSame()
        {
            int maximum = 0;
            for (int i = 0; i < board.Count; i++)
            {
                Element element = board[i];
                if (element == null) continue;
                maximum = Mathf.Max(maximum, ConnectedSameCount(i, element.Key));
            }
            return maximum;
        }

        private int CountColumnKey(int column, string filter)
        {
            if (column < 0 || column >= BoardColumns) return 0;
            int count = 0;
            for (int row = 0; row < BoardRows; row++)
            {
                int index = row * BoardColumns + column;
                if (index < board.Count && board[index] != null &&
                    ContainsToken(filter, board[index].Key)) count += 1;
            }
            return count;
        }

        private bool BoardHasOnlyUniqueElements()
        {
            HashSet<string> keys = new HashSet<string>();
            for (int i = 0; i < board.Count; i++)
            {
                Element element = board[i];
                if (element == null) continue;
                if (!keys.Add(element.Key)) return false;
            }
            return keys.Count > 0;
        }

        private int CountPoolDuplicateElements()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            int duplicates = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                Element element = pool[i];
                if (element == null) continue;
                int value;
                counts.TryGetValue(element.Key, out value);
                if (value > 0) duplicates += 1;
                counts[element.Key] = value + 1;
            }
            return duplicates;
        }

        private int CountSameRowKey(int index, string key, Element source)
        {
            if (index < 0 || string.IsNullOrEmpty(key)) return 0;
            int row = index / BoardColumns;
            int count = 0;
            for (int column = 0; column < BoardColumns; column++)
            {
                int candidateIndex = row * BoardColumns + column;
                if (candidateIndex < 0 || candidateIndex >= board.Count) continue;
                Element candidate = board[candidateIndex];
                if (candidate == null || candidate.Key != key) continue;
                if (source != null && candidate.Id == source.Id) continue;
                count += 1;
            }
            return count;
        }

        private static bool Passes(string comparator, int value, int threshold)
        {
            if (string.IsNullOrEmpty(comparator) || comparator == "always") return true;
            if (comparator == "eq") return value == threshold;
            if (comparator == "ne") return value != threshold;
            if (comparator == "ge") return value >= threshold;
            if (comparator == "gt") return value > threshold;
            if (comparator == "le") return value <= threshold;
            if (comparator == "lt") return value < threshold;
            if (comparator == "modulo_zero") return threshold > 0 && value % threshold == 0;
            return false;
        }

        private static int CalculateRuleValue(CatCafeConfigDatabase.RuleRow rule, int primary, int secondary)
        {
            int divisor = Mathf.Max(1, rule.divisor);
            return rule.base_value +
                (primary / divisor) * rule.primary_factor +
                secondary * rule.secondary_factor +
                primary * secondary * rule.cross_factor;
        }

        private static void AddRuleBreakdown(List<string> target,
            CatCafeConfigDatabase.RuleRow rule, int primary, int secondary, int contribution)
        {
            if (target == null || rule == null || contribution == 0 ||
                string.IsNullOrWhiteSpace(rule.reason)) return;

            try
            {
                int divisor = Mathf.Max(1, rule.divisor);
                int primaryTerm = (primary / divisor) * rule.primary_factor;
                int secondaryTerm = secondary * rule.secondary_factor;
                int crossTerm = primary * secondary * rule.cross_factor;
                string[] clauses = rule.reason.Split(
                    new[] { '，', '；', '|' }, StringSplitOptions.RemoveEmptyEntries);
                List<string> visibleClauses = new List<string>();
                for (int i = 0; i < clauses.Length; i++)
                {
                    string clause = clauses[i].Trim();
                    bool hasComponent = false;
                    bool hasValue = false;
                    bool primaryIncluded = false;
                    bool secondaryIncluded = false;
                    bool crossIncluded = false;
                    int sourceAmount = 0;
                    bool isCross = clause.Contains("{0}") && clause.Contains("{1}") &&
                        clause.Contains("{5}");
                    if (isCross)
                    {
                        hasComponent = true;
                        hasValue |= crossTerm != 0;
                        crossIncluded = true;
                        sourceAmount += crossTerm;
                    }
                    else
                    {
                        if (clause.Contains("{0}") && clause.Contains("{3}"))
                        {
                            hasComponent = true;
                            hasValue |= primaryTerm != 0;
                            primaryIncluded = true;
                            sourceAmount += primaryTerm;
                        }
                        if (clause.Contains("{1}") && clause.Contains("{4}"))
                        {
                            hasComponent = true;
                            hasValue |= secondaryTerm != 0;
                            secondaryIncluded = true;
                            sourceAmount += secondaryTerm;
                        }
                    }
                    if (clause.Contains("{6}"))
                    {
                        hasComponent = true;
                        hasValue |= rule.base_value != 0;
                        sourceAmount += rule.base_value;
                    }
                    // 表格可直接引用每个计算分项的最终金币值，展示为
                    // “客人：2金币”之类的来源明细，而不是把公式重新展示给玩家。
                    if (clause.Contains("{8}"))
                    {
                        hasComponent = true;
                        hasValue |= primaryTerm != 0;
                        if (!primaryIncluded) sourceAmount += primaryTerm;
                    }
                    if (clause.Contains("{9}"))
                    {
                        hasComponent = true;
                        hasValue |= secondaryTerm != 0;
                        if (!secondaryIncluded) sourceAmount += secondaryTerm;
                    }
                    if (clause.Contains("{10}"))
                    {
                        hasComponent = true;
                        hasValue |= crossTerm != 0;
                        if (!crossIncluded) sourceAmount += crossTerm;
                    }
                    if (hasComponent && !hasValue) continue;

                    bool hasExplicitAmount = clause.Contains("{8}") ||
                        clause.Contains("{9}") || clause.Contains("{10}");
                    string display;
                    if (hasExplicitAmount)
                    {
                        display = string.Format(clause,
                            primary,
                            secondary,
                            contribution,
                            rule.primary_factor,
                            rule.secondary_factor,
                            rule.cross_factor,
                            rule.base_value,
                            divisor,
                            primaryTerm,
                            secondaryTerm,
                            crossTerm);
                    }
                    else
                    {
                        string label = SourceLabel(clause);
                        if (label.Length == 0) continue;
                        display = string.Format(
                            CatCafeConfigDatabase.GetRequiredString(
                                "ui_card_detail_income_source_format"),
                            label,
                            hasComponent ? sourceAmount : contribution);
                    }

                    if (!visibleClauses.Contains(display)) visibleClauses.Add(display);
                }

                if (visibleClauses.Count > 0)
                {
                    target.Add(string.Join(
                        CatCafeConfigDatabase.GetRequiredString(
                            "ui_card_detail_income_separator").Replace("\\n", "\n"),
                        visibleClauses.ToArray()));
                }
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "[CatCafeConfig] Rules.reason 占位符无效：" + rule.rule_id, exception);
            }
        }

        /// <summary>
        /// 从规则来源说明中取出表格配置的标签部分。
        /// “相邻客人 {0}×{3}”会得到“相邻客人”；金额展示格式仍由 Settings 决定。
        /// </summary>
        private static string SourceLabel(string clause)
        {
            if (string.IsNullOrEmpty(clause)) return string.Empty;
            int cut = clause.Length;
            for (int i = 0; i < clause.Length; i++)
            {
                char c = clause[i];
                if (c == '{' || c == '+' || c == '×' || c == '÷' || c == '=' ||
                    (c >= '0' && c <= '9'))
                {
                    cut = i;
                    break;
                }
            }
            return clause.Substring(0, cut).Trim();
        }

        private int CountBoardKey(string key)
        {
            int count = 0;
            for (int i = 0; i < board.Count; i++)
                if (board[i] != null && ContainsToken(key, board[i].Key)) count++;
            return count;
        }

        private int CountDistinctCatColors()
        {
            HashSet<string> colors = new HashSet<string>();
            for (int i = 0; i < board.Count; i++)
            {
                Element element = board[i];
                if (element != null && (element.Kind == Kind.Cat || element.Kind == Kind.Kitten)) colors.Add(element.Color);
            }
            return colors.Count;
        }

        private IEnumerator SettleBreeding()
        {
            string breedingSourceKey = CatCafeConfigDatabase.GetRequiredString("breeding_source_element");
            bool includeDiagonals = CatCafeConfigDatabase.GetRequiredBool("breeding_include_diagonals");
            Kind parentKind = ParseKind(CatCafeConfigDatabase.GetRequiredString("breeding_parent_kind"));
            int minimumParents = CatCafeConfigDatabase.GetRequiredInt("breeding_minimum_parents");
            HashSet<int> used = new HashSet<int>();
            List<int> nests = new List<int>();
            for (int i = 0; i < board.Count; i++)
                if (board[i] != null && board[i].Key == breedingSourceKey) nests.Add(i);
            for (int n = 0; n < nests.Count; n++)
            {
                int index = nests[n];
                Element breedingSource = index >= 0 && index < board.Count ? board[index] : null;
                if (breedingSource == null || breedingSource.Key != breedingSourceKey) continue;
                List<Element> adults = new List<Element>();
                List<Element> nearby = Neighbors(index, includeDiagonals);
                for (int i = 0; i < nearby.Count; i++)
                {
                    if (nearby[i].Kind == parentKind && !used.Contains(nearby[i].Id)) adults.Add(nearby[i]);
                }
                if (adults.Count < minimumParents) continue;
                adults = Shuffle(adults);
                Element first = adults[0];
                Element second = adults[1];
                // 精确配方优先；没有精确配方时可由 Breeding 的通配行按稀有度权重产仔。
                CatCatalog.BreedRow recipe = CatCatalog.LookupBreed(first.Key, second.Key);
                if (recipe == null) continue;
                string kittenKey = ResolveBreedingChild(recipe);
                if (!defs.ContainsKey(kittenKey))
                    throw new InvalidOperationException("[CatCafeConfig] Breeding 产物未在 Elements 中配置：" + kittenKey);
                Element kitten = Instance(kittenKey);
                int birthIndex;
                if (!TryReplaceBoardElement(breedingSource, kitten, out birthIndex)) continue;
                used.Add(first.Id);
                used.Add(second.Id);
                string grownKey = CatCatalog.GrownForm(kittenKey);
                bool firstDiscovery;
                CatCafeMeta.Discover(grownKey, "breed", out firstDiscovery);
                if (firstDiscovery)
                {
                    runFirstDiscoveries += 1;
                    // 图鉴联动不能等局末结算：首次发现立刻落盘，
                    // 局中途杀进程回到猫咖，图鉴里也已经点亮这只猫。
                    CatCafeMeta.SaveNow();
                }
                RenderBoard();
                yield return StartCoroutine(PlayBirthFx(birthIndex, firstDiscovery));
                ShowToast(firstDiscovery && defs.ContainsKey(grownKey)
                    ? "新发现：" + defs[grownKey].Name + "！已加入图鉴"
                    : "诞生：" + kitten.Name);
                float delay = firstDiscovery
                    ? CatCafeConfigDatabase.GetRequiredFloat("breeding_first_discovery_delay")
                    : CatCafeConfigDatabase.GetRequiredFloat("breeding_normal_delay");
                yield return new WaitForSeconds(delay / SettlementSpeedMultiplier);
                if (firstDiscovery && discoveryReveal != null)
                {
                    string discoveredName = defs.ContainsKey(grownKey) ? defs[grownKey].Name : kitten.Name;
                    // 使用新生小猫本身的贴纸做揭晓图，标题则显示它成年后的图鉴名称。
                    yield return StartCoroutine(discoveryReveal.ShowAndWait(discoveredName, LoadElementSprite(kitten)));
                }
                // 诞生演出（破壳弹跳 + 光环）播完再说话，别把字条压在小猫还没长到位的那一帧上。
                if (tutorialNotes != null)
                    yield return StartCoroutine(tutorialNotes.Interject("breeding_first_birth", BoardCellRect(birthIndex)));
            }
        }

        private string ResolveBreedingChild(CatCatalog.BreedRow recipe)
        {
            // 突变只读取本局配方表；局外亲密度和升级不改变局内概率。
            if (!string.IsNullOrEmpty(recipe.MutationChild) &&
                UnityEngine.Random.value < recipe.MutationRate)
                return recipe.MutationChild;

            string mode = string.IsNullOrEmpty(recipe.ResultMode) ? "fixed" : recipe.ResultMode;
            if (mode == "fixed") return recipe.Child;
            if (mode == "rarity_random") return RollBreedingKitten(recipe.RarityContext);
            throw new InvalidOperationException("[CatCafeConfig] Breeding 未知 result_mode：" + mode);
        }

        private string RollBreedingKitten(string rarityContext)
        {
            if (string.IsNullOrEmpty(rarityContext))
                throw new InvalidOperationException("[CatCafeConfig] Breeding rarity_random 缺少 rarity_context");

            Dictionary<string, string> kittenByAdult = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Definition> pair in defs)
            {
                if (pair.Value.Kind != Kind.Kitten) continue;
                string adultKey = CatCatalog.GrownForm(pair.Key);
                Definition adult;
                if (!defs.TryGetValue(adultKey, out adult) || adult.Kind != Kind.Cat) continue;
                if (!kittenByAdult.ContainsKey(adultKey)) kittenByAdult[adultKey] = pair.Key;
            }

            List<string>[] candidates = new List<string>[RarityCount];
            for (int i = 0; i < RarityCount; i++) candidates[i] = new List<string>();
            foreach (KeyValuePair<string, string> pair in kittenByAdult)
            {
                Definition adult = defs[pair.Key];
                candidates[(int)adult.Rarity].Add(pair.Value);
            }

            if (CatCafeConfigDatabase.GetWeight(rarityContext) == null)
                throw new InvalidOperationException("[CatCafeConfig] Breeding rarity_random 找不到 Weights 上下文：" + rarityContext);
            int[] weights = WeightValues(rarityContext);
            int total = 0;
            for (int i = 0; i < RarityCount; i++)
                if (candidates[i].Count > 0 && weights[i] > 0) total += weights[i];
            if (total <= 0)
                throw new InvalidOperationException("[CatCafeConfig] Breeding rarity_random 没有可选幼猫：" + rarityContext);

            int roll = UnityEngine.Random.Range(0, total);
            for (int i = 0; i < RarityCount; i++)
            {
                if (candidates[i].Count == 0 || weights[i] <= 0) continue;
                roll -= weights[i];
                if (roll < 0)
                    return candidates[i][UnityEngine.Random.Range(0, candidates[i].Count)];
            }
            throw new InvalidOperationException("[CatCafeConfig] Breeding rarity_random 权重结算失败：" + rarityContext);
        }

        /// <summary>
        /// 绒毛掉落：每波次结束时，盘面上每只成年猫各按 meta_fur_drop_chance 判一次，
        /// 中了就给自己品种 +1 撮绒毛。这是主动游玩来源；局外还会按现实时间自然积攒，
        /// 所以每波次都立刻落盘——玩到一半杀进程，已经掉下来的绒毛不能白掉。
        ///
        /// 只算 Kind.Cat：幼崽还没长大，掉的毛算在它成年品种头上会让图鉴没点亮的猫先攒出材料。
        /// 掉毛只影响局外，不改任何局内数值，因此放在繁育结算之后、道具结算之前的静默点。
        /// </summary>
        private IEnumerator SettleFurDrops()
        {
            float chance = CatCafeConfigDatabase.GetRequiredFloat("meta_fur_drop_chance");
            int noteIndex = -1;
            int dropped = 0;
            for (int i = 0; i < board.Count; i++)
            {
                Element element = board[i];
                if (element == null || element.Kind != Kind.Cat) continue;
                if (UnityEngine.Random.value >= chance) continue;

                CatCafeMeta.AddFur(CatCatalog.GrownForm(element.Key), 1);
                dropped += 1;
                if (noteIndex < 0) noteIndex = i;
                PlayFurDropFx(i, 1);
            }

            if (dropped <= 0) yield break;
            runFurGained += dropped;
            CatCafeMeta.SaveNow();
            if (tutorialNotes != null)
                yield return StartCoroutine(tutorialNotes.Interject("fur_first_drop", BoardCellRect(noteIndex)));
        }

        /// <summary>绒毛只做局外记账；这里补一条原地反馈，让玩家知道是哪只猫掉了毛。</summary>
        private void PlayFurDropFx(int boardIndex, int amount)
        {
            if (interactionFeedback == null || amount <= 0) return;
            RectTransform source = GetBoardRewardSource(boardIndex);
            if (source == null) return;

            string format = CatCafeConfigDatabase.GetRequiredString("ui_note_fur_format");
            Color color = UiColor("ui_note_fur_color");
            interactionFeedback.PlayFurReward(
                source, amount, string.Format(format, amount), color);
        }

        /// <summary>
        /// 按实例 ID 同时替换棋盘格和店内清单中的同一枚棋子。
        /// 繁育时传入的是育儿窝实例，因此父猫只参与配方计算，绝不会成为替换目标。
        /// </summary>
        private bool TryReplaceBoardElement(Element source, Element replacement, out int boardIndex)
        {
            boardIndex = -1;
            if (source == null || replacement == null) return false;

            int poolIndex = -1;
            for (int i = 0; i < board.Count; i++)
            {
                if (board[i] != null && board[i].Id == source.Id)
                {
                    boardIndex = i;
                    break;
                }
            }
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && pool[i].Id == source.Id)
                {
                    poolIndex = i;
                    break;
                }
            }

            if (boardIndex < 0 || poolIndex < 0)
            {
                Debug.LogError("[CatCafe] 无法替换棋子实例：" + source.Id + " / " + source.Key);
                boardIndex = -1;
                return false;
            }

            board[boardIndex] = replacement;
            pool[poolIndex] = replacement;
            return true;
        }

        /// <summary>
        /// 育儿窝诞生演出：新生小猫从小弹到位，格子上绽开一圈光环。
        /// 首次发现用金色双重光环，普通诞生用暖粉单环。时长跟随结算速度档。
        /// </summary>
        private IEnumerator PlayBirthFx(int boardIndex, bool firstDiscovery)
        {
            RectTransform token = GetBoardRewardSource(boardIndex);
            if (token == null || canvas == null || interactionFeedback == null) yield break;

            Vector2 center = interactionFeedback.GetFxPosition(token);
            Color accent = firstDiscovery
                ? new Color(1f, 0.82f, 0.35f, 1f)
                : new Color(0.98f, 0.72f, 0.62f, 1f);

            // 小猫弹出：0.3 → 1.12 → 1，比直接刷出来多一口"破壳"的气。
            token.localScale = new Vector3(0.3f, 0.3f, 1f);

            int rings = firstDiscovery ? 2 : 1;
            for (int r = 0; r < rings; r++)
            {
                StartCoroutine(PlayBirthRing(center, accent, r * 0.12f / SettlementSpeedMultiplier));
            }

            float duration = 0.32f / SettlementSpeedMultiplier;
            float elapsed = 0f;
            while (elapsed < duration && token != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - normalized, 3f);
                float overshoot = Mathf.Sin(normalized * Mathf.PI) * 0.12f;
                float scale = Mathf.Lerp(0.3f, 1f, eased) + overshoot;
                token.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            // 后续 RenderBoard 可能已把 token 销毁重建，能扶正就扶正。
            if (token != null) token.localScale = Vector3.one;
        }

        private IEnumerator PlayBirthRing(Vector2 center, Color accent, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            if (canvas == null) yield break;

            GameObject ring = NewUi("Birth Ring", canvas.transform);
            ring.transform.SetAsLastSibling();
            RectTransform rect = ring.GetComponent<RectTransform>();
            AnchorRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), center, new Vector2(BoardCellSize, BoardCellSize));

            Image image = ring.AddComponent<Image>();
            PixelFrame(image, new Color(accent.r, accent.g, accent.b, 0.55f));
            image.raycastTarget = false;
            CanvasGroup group = ring.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            float duration = 0.42f / SettlementSpeedMultiplier;
            float elapsed = 0f;
            while (elapsed < duration && ring != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - normalized, 2f);
                float scale = Mathf.Lerp(0.55f, 1.45f, eased);
                rect.localScale = new Vector3(scale, scale, 1f);
                group.alpha = 1f - normalized;
                yield return null;
            }

            if (ring != null) Destroy(ring);
        }

        private IEnumerator SettleRoundItems(int roundIncome)
        {
            int bonus = 0;
            int removalBonus = 0;
            int rerollBonus = 0;
            int inspirationBonus = 0;
            List<string> generatedKeys = new List<string>();
            List<string> reasons = new List<string>();
            List<string> consumedItems = new List<string>();
            bool stateChanged = false;
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("round_end", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key)) continue;
                int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, null, -1, null, roundIncome, false);
                int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, null, -1, null, roundIncome, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                    !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                bool fired = false;
                int value = CalculateRuleValue(rule, primary, secondary);
                if (rule.operation == "income") { bonus += value; fired = value != 0; }
                else if (rule.operation == "add_removal") { removalBonus += value; fired = value != 0; }
                else if (rule.operation == "add_reroll") { rerollBonus += value; fired = value != 0; }
                else if (rule.operation == "add_inspiration")
                {
                    inspirationBonus += value;
                    fired = value != 0;
                }
                else if (rule.operation == "generate" || rule.operation == "generate_random")
                {
                    int attempts = rule.target_value_mode == "triggers_per_primary"
                        ? Mathf.Max(0, primary)
                        : 1;
                    int triggerCount = 0;
                    for (int attempt = 0; attempt < attempts; attempt++)
                        triggerCount += RollRuleTriggers(rule);
                    int copies = Mathf.Max(1, rule.result_count);
                    for (int trigger = 0; trigger < triggerCount; trigger++)
                        for (int copy = 0; copy < copies; copy++)
                            generatedKeys.Add(rule.operation == "generate"
                                ? rule.result_key
                                : ChooseRuleResultKey(rule));
                    fired = triggerCount > 0;
                }
                else if (rule.operation == "store_value")
                {
                    AddItemCounter(rule.owner_key, value);
                    fired = value != 0;
                    stateChanged |= fired;
                }
                if (fired)
                {
                    reasons.Add(string.IsNullOrEmpty(rule.reason) ? itemDefs[rule.owner_key].Name : rule.reason);
                    if (rule.consume_self && !consumedItems.Contains(rule.owner_key))
                        consumedItems.Add(rule.owner_key);
                }
            }
            if (bonus == 0 && removalBonus == 0 && rerollBonus == 0 && inspirationBonus == 0 &&
                generatedKeys.Count == 0 && !stateChanged)
                yield break;

            yield return new WaitForSecondsRealtime(
                CatCafeConfigDatabase.GetFloat("round_end_pre_delay", 0.16f) / SettlementSpeedMultiplier);
            removalTokens += removalBonus;
            rerollTokens += rerollBonus;
            inspirationTokens += inspirationBonus;
            for (int i = 0; i < generatedKeys.Count; i++) BringGeneratedElement(generatedKeys[i]);
            for (int i = 0; i < consumedItems.Count; i++) RemoveOwnedItem(consumedItems[i]);

            List<string> gains = new List<string>();
            if (bonus != 0) gains.Add("+" + bonus + "金币");
            if (removalBonus != 0) gains.Add("+" + removalBonus + "下班券");
            if (rerollBonus != 0) gains.Add("+" + rerollBonus + "招呼券");
            if (inspirationBonus != 0) gains.Add("+" + inspirationBonus + "灵感券");
            string reasonText = string.Join(
                CatCafeConfigDatabase.GetRequiredString("ui_action_reason_separator"),
                reasons.ToArray());
            ShowToast(reasonText + (gains.Count > 0 ? "：" + string.Join("、", gains.ToArray()) : string.Empty));
            PlayTicketGainNotes(GetBoardCenterRewardPosition(), rerollBonus, removalBonus);
            if (bonus > 0)
                yield return StartCoroutine(PlayCoinReward(GetBoardCenterRewardPosition(), bonus));
            RenderBoard();
            RefreshPieceBox();
            UpdateHud();
            yield return new WaitForSecondsRealtime(
                CatCafeConfigDatabase.GetFloat("round_end_post_delay", 0.12f) / SettlementSpeedMultiplier);
        }

        private int RollRarity(Rarity? minimum)
        {
            string context = CurrentStage.RarityContext;
            int[] weights = WeightValues(context);

            ApplyRarityWeightRules(weights, ConfiguredRules("rarity_weights", "element"), false);
            ApplyRarityWeightRules(weights, ConfiguredRules("rarity_weights", "item"), true);

            int start = minimum.HasValue ? Mathf.Clamp((int)minimum.Value, 0, RarityCount - 1) : 0;
            int total = 0;
            for (int i = start; i < RarityCount; i++) total += weights[i];
            int roll = UnityEngine.Random.Range(0, Mathf.Max(1, total));
            for (int i = start; i < RarityCount; i++)
            {
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return 2;
        }

        private void ApplyRarityWeightRules(
            int[] weights, List<CatCafeConfigDatabase.RuleRow> rules, bool requireOwnedItem)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (requireOwnedItem && !HasItem(rule.owner_key)) continue;
                int primary = EvaluateScope(
                    rule.primary_scope, rule.primary_filter, null, -1, null, 0, false);
                int secondary = EvaluateScope(
                    rule.secondary_scope, rule.secondary_filter, null, -1, null, 0, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                    !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                float factor;
                if (rule.operation == "scale") factor = 1f + primary * rule.multiplier;
                else if (rule.operation == "multiply")
                {
                    int exponent = string.IsNullOrEmpty(rule.primary_scope) || rule.primary_scope == "none"
                        ? 1
                        : Mathf.Max(1, primary);
                    factor = Mathf.Pow(rule.multiplier == 0f ? 1f : rule.multiplier, exponent);
                }
                else continue;

                for (int rarity = 0; rarity < RarityCount; rarity++)
                    if (ContainsToken(rule.source_keys, RarityKey((Rarity)rarity)))
                        weights[rarity] = Mathf.Max(0, Mathf.RoundToInt(weights[rarity] * factor));
            }
        }

        private int[] WeightValues(string context)
        {
            CatCafeConfigDatabase.WeightRow row = CatCafeConfigDatabase.GetWeight(context);
            if (row == null)
            {
                Debug.LogError("[CatCafeConfig] Weights 缺少 context=" + context);
                return new[] { 1, 0, 0, 0 };
            }
            return new[] { row.common, row.uncommon, row.rare, row.special };
        }

        private List<string> RewardOptions(Rarity? minimum)
        {
            List<string> keys = new List<string>();
            if (minimum.HasValue) AddRewardFromRarity(keys, RollRarity(minimum));

            int targetCount = CatCafeConfigDatabase.GetInt("base_reward_option_count", 3);
            List<CatCafeConfigDatabase.RuleRow> countRules = ConfiguredRules("reward_options", "item");
            for (int i = 0; i < countRules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = countRules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "add_count") continue;
                int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, null, -1, null, 0, false);
                int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, null, -1, null, 0, false);
                if (Passes(rule.primary_comparator, primary, rule.primary_threshold) &&
                    Passes(rule.secondary_comparator, secondary, rule.secondary_threshold))
                    targetCount += CalculateRuleValue(rule, primary, secondary);
            }

            while (keys.Count < targetCount)
            {
                if (!AddRewardFromRarity(keys, RollRarity(null)))
                {
                    List<string> fallback = AllRewardKeys();
                    RemoveKeys(fallback, keys);
                    if (fallback.Count == 0) break;
                    keys.Add(fallback[UnityEngine.Random.Range(0, fallback.Count)]);
                }
            }

            string forceKey = CatCafeConfigDatabase.GetString("first_round_force_reward_key");
            int forceRound = CatCafeConfigDatabase.GetInt("first_round_force_reward_round", 1);
            bool alreadyOwned = false;
            for (int i = 0; i < pool.Count; i++) if (pool[i].Key == forceKey) alreadyOwned = true;
            if (round == forceRound && !string.IsNullOrEmpty(forceKey) && !alreadyOwned &&
                defs.ContainsKey(forceKey) && !keys.Contains(forceKey) && keys.Count > 0) keys[0] = forceKey;
            return Shuffle(keys);
        }

        private bool AddRewardFromRarity(List<string> keys, int rarity)
        {
            List<string> candidates = RewardPool(rarity);
            RemoveKeys(candidates, keys);
            if (candidates.Count == 0) return false;
            keys.Add(candidates[UnityEngine.Random.Range(0, candidates.Count)]);
            return true;
        }

        private List<string> ItemOptions(int tier)
        {
            List<string> keys = new List<string>();
            int optionCount = CatCafeConfigDatabase.GetInt("base_item_option_count", 3);
            while (keys.Count < optionCount)
            {
                int rarity = RollItemRarity(tier);
                if (pendingItemChoiceMinimum.HasValue)
                    rarity = Mathf.Max(rarity, (int)pendingItemChoiceMinimum.Value);
                List<string> candidates = ItemPool(rarity);
                for (int i = candidates.Count - 1; i >= 0; i--)
                {
                    if (!CanAcquireItem(candidates[i]) || keys.Contains(candidates[i])) candidates.RemoveAt(i);
                }
                if (candidates.Count == 0)
                {
                    candidates = AllItemKeys();
                    for (int i = candidates.Count - 1; i >= 0; i--)
                        if (!CanAcquireItem(candidates[i])) candidates.RemoveAt(i);
                    RemoveKeys(candidates, keys);
                }
                if (candidates.Count == 0) break;
                keys.Add(candidates[UnityEngine.Random.Range(0, candidates.Count)]);
            }
            return Shuffle(keys);
        }

        private List<string> ItemPool(int rarity)
        {
            List<string> result = new List<string>();
            foreach (KeyValuePair<string, ItemDefinition> pair in itemDefs)
            {
                if ((int)pair.Value.Rarity == rarity) result.Add(pair.Key);
            }
            return result;
        }

        private int RollItemRarity(int tier)
        {
            int[] weights = WeightValues("item_tier" + tier);
            int total = 0;
            for (int i = 0; i < RarityCount; i++) if (HasAvailableItemRarity(i)) total += weights[i];

            int roll = UnityEngine.Random.Range(0, Mathf.Max(1, total));
            for (int i = 0; i < RarityCount; i++)
            {
                if (!HasAvailableItemRarity(i)) continue;
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return 0;
        }

        private bool HasAvailableItemRarity(int rarity)
        {
            List<string> candidates = ItemPool(rarity);
            for (int i = 0; i < candidates.Count; i++) if (CanAcquireItem(candidates[i])) return true;
            return false;
        }

        private void ShowChoices(Rarity? minimum)
        {
            PopulateChoices(minimum);
            choiceOverlayView.Show();
            PlayCardEntrance();
            if (tutorialNotes != null)
            {
                HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_cards_hold", 0.45f));
                // 换牌券要在"还能用"的时候教，不是等玩家自己摸索着用完之后；
                // 但第一次开奖励页已经有一条了，让它单独讲完，换牌券留到下一次开页再说。
                RectTransform[] cardTargets = ActiveChoiceCardRects();
                if (!tutorialNotes.Notify("run_first_reward", cardTargets) && rerollTokens > 0)
                    tutorialNotes.Notify("reroll_ticket_first");
            }
        }

        /// <summary>
        /// 建完卡片、知道张数之后再定宽。
        ///
        /// 三张时占宽 771 &lt; 916，走基准值，与改前像素一致；四张需要 1016，
        /// 把内容轨、标题、券行和面板一起加宽。真加到放不下（面板要超过
        /// ChoicePanelMaxWidth）时改为压缩卡宽，宁可卡片窄一点，也不让它挂到面板外面。
        /// </summary>
        private void ApplyChoicePanelWidth(int cardCount)
        {
            if (choicePanelRect == null || choiceContentSize == null || cardCount <= 0) return;

            float gaps = Mathf.Max(0, cardCount - 1) * ChoiceCardSpacing;
            float chrome = ChoiceRailPadding * 2f + gaps;
            float cardWidth = CardWidth;

            float maxContent = ChoicePanelMaxWidth - (ChoicePanelBaseWidth - ChoiceContentBaseWidth);
            if (chrome + cardCount * cardWidth > maxContent)
            {
                cardWidth = Mathf.Floor((maxContent - chrome) / cardCount);
                for (int i = 0; i < activeChoiceCards.Count; i++)
                {
                    LayoutElement layout = activeChoiceCards[i].GetComponent<LayoutElement>();
                    if (layout == null) continue;
                    layout.minWidth = cardWidth;
                    layout.preferredWidth = cardWidth;
                }
            }

            float content = Mathf.Max(ChoiceContentBaseWidth, chrome + cardCount * cardWidth);
            float panel = content + (ChoicePanelBaseWidth - ChoiceContentBaseWidth);

            choiceContentSize.minWidth = content;
            choiceContentSize.preferredWidth = content;
            if (choiceTitleSize != null)
            {
                choiceTitleSize.minWidth = content;
                choiceTitleSize.preferredWidth = content;
            }
            if (choiceTicketSize != null)
            {
                choiceTicketSize.minWidth = content;
                choiceTicketSize.preferredWidth = content;
            }
            choicePanelRect.sizeDelta = new Vector2(panel, choicePanelRect.sizeDelta.y);
            SyncChoiceOverlayShell(choiceOverlay, choicePanelRect);
        }

        /// <summary>
        /// 选择面板改变位置或尺寸时，书本阴影和两层纸页也要同步。
        /// 它们不属于 Panel 的子节点，单独移动 Panel 会留下空白纸页。
        /// </summary>
        private void SyncChoiceOverlayShell(GameObject overlay, RectTransform panel)
        {
            if (overlay == null || panel == null) return;
            ConfigureChoiceOverlayShellLayer(overlay.transform.Find("BookBacking"), panel, "backing");
            ConfigureChoiceOverlayShellLayer(overlay.transform.Find("RearPaperLayer"), panel, "rear");
            ConfigureChoiceOverlayShellLayer(overlay.transform.Find("MiddlePaperLayer"), panel, "middle");
            CatCafeOverlay view = overlay == choiceOverlay ? choiceOverlayView : itemOverlayView;
            if (view == null) return;
            view.RefreshPanelHome(panel);
            RefreshChoiceOverlayShellHome(view, overlay.transform.Find("BookBacking"));
            RefreshChoiceOverlayShellHome(view, overlay.transform.Find("RearPaperLayer"));
            RefreshChoiceOverlayShellHome(view, overlay.transform.Find("MiddlePaperLayer"));
        }

        private static void RefreshChoiceOverlayShellHome(CatCafeOverlay view, Transform layer)
        {
            if (view == null || layer == null) return;
            view.RefreshPanelHome(layer.GetComponent<RectTransform>());
        }

        private void ConfigureChoiceOverlayShellLayer(Transform layer, RectTransform panel, string key)
        {
            if (layer == null) return;
            RectTransform rect = layer.GetComponent<RectTransform>();
            if (rect == null) return;
            Vector2 position = panel.anchoredPosition + new Vector2(
                UiValue("ui_choice_" + key + "_offset_x"),
                UiValue("ui_choice_" + key + "_offset_y"));
            Vector2 size = panel.sizeDelta + new Vector2(
                UiValue("ui_choice_" + key + "_width_extra"),
                UiValue("ui_choice_" + key + "_height_extra"));
            AnchorRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), position, size);
        }

        /// <summary>
        /// 只重建三张卡和面板上的文案。换一批走这条，弹层本身不重新播出场动画——
        /// 面板和遮罩已经在屏幕上了，再淡入一次会让整个界面看起来闪一下。
        /// </summary>
        private void PopulateChoices(Rarity? minimum)
        {
            pendingRewardMinimum = minimum;
            choiceResolving = false;
            activeChoiceCards.Clear();
            currentChoiceKeys.Clear();
            ClearChildren(choicesRoot);
            choiceTitle.text = minimum.HasValue
                ? string.Format(CatCafeConfigDatabase.GetRequiredString(
                    "ui_reward_choice_special_title_format"), RarityLabel(minimum.Value))
                : CatCafeConfigDatabase.GetRequiredString("ui_reward_choice_title");
            List<string> options = RewardOptions(minimum);
            currentChoiceKeys.AddRange(options);
            for (int i = 0; i < options.Count; i++)
            {
                string key = options[i];
                Definition def = defs[key];
                Button card = CreateCard(
                    choicesRoot, def.Name, EffectiveRarity(def), Join(def.Rules, " "), def.Asset, key, true);
                Button captured = card;
                card.onClick.AddListener(delegate { BeginChoose(key, captured); });
                activeChoiceCards.Add(card);
            }

            ApplyChoicePanelWidth(options.Count);

            choiceTicketText.text = TicketSummary();
            rerollButton.GetComponentInChildren<TMP_Text>().text = rerollTokens > 0
                ? string.Format(CatCafeConfigDatabase.GetRequiredString(
                    "ui_reward_reroll_available_format"), rerollTokens)
                : CatCafeConfigDatabase.GetRequiredString("ui_reward_reroll_unavailable");
            rerollButton.interactable = rerollTokens > 0;
            SetButtonDimmed(rerollButton, rerollTokens <= 0);
        }

        private void ShowItemChoices(int tier, Rarity minimumForSymbol)
        {
            currentItemChoiceTier = tier;
            pendingItemRewardMinimum = minimumForSymbol;
            choiceResolving = false;
            activeChoiceCards.Clear();
            ClearChildren(itemChoicesRoot);
            itemTitle.text = CatCafeConfigDatabase.GetRequiredString("ui_item_choice_title");
            List<string> options = ItemOptions(tier);
            for (int i = 0; i < options.Count; i++)
            {
                string key = options[i];
                ItemDefinition def = itemDefs[key];
                Button card = CreateCard(itemChoicesRoot, def.Name, def.Rarity, def.Rule, def.Asset, key, false);
                Button captured = card;
                card.onClick.AddListener(delegate { BeginChooseItem(key, captured); });
                activeChoiceCards.Add(card);
            }

            // 营业道具不能使用招呼券；文案由表统一术语。
            itemTicketText.text = TicketSummary() + "  ·  " +
                CatCafeConfigDatabase.GetRequiredString("ui_item_reroll_unavailable");
            itemOverlayView.Show();
            PlayCardEntrance();
            if (tutorialNotes != null)
            {
                HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_cards_hold", 0.45f));
                tutorialNotes.Notify("item_choice_first", ActiveChoiceCardRects());
            }
        }

        /// <summary>首次查看收益只框一只成年猫，避免把猫砂盆或整块棋盘一起框进来。</summary>
        private RectTransform FindTutorialCatSpotlight()
        {
            for (int i = 0; i < board.Count; i++)
                if (board[i] != null && board[i].Kind == Kind.Cat) return BoardCellRect(i);
            return boardRoot as RectTransform;
        }

        private void BeginOpeningTutorial()
        {
            if (tutorialNotes == null) return;
            tutorialCatDetailOpened = false;
            tutorialFirstInspectPending = tutorialNotes.Notify(
                "run_first_inspect", FindTutorialCatSpotlight());
            if (tutorialFirstInspectPending)
            {
                SetRollInteractable(false);
                return;
            }
            if (!tutorialNotes.Notify("run_first_enter"))
                tutorialNotes.Notify("run_second_before_roll");
        }

        /// <summary>当前选择页真正可见的卡片，用于让教程聚焦框贴合内容而不是框住整块弹层。</summary>
        private RectTransform[] ActiveChoiceCardRects()
        {
            List<RectTransform> result = new List<RectTransform>();
            for (int i = 0; i < activeChoiceCards.Count; i++)
            {
                Button card = activeChoiceCards[i];
                if (card != null && card.gameObject.activeInHierarchy)
                    result.Add(card.transform as RectTransform);
            }
            return result.ToArray();
        }

        private string TicketSummary()
        {
            // 分隔用普通空格和中点：UI 字体没有 U+3000 全角空格，会渲染成豆腐块。
            return "招呼券 ×" + rerollTokens + "  ·  下班券 ×" + removalTokens;
        }

        /// <summary>
        /// 三张卡依次淡入弹出。只动 alpha 与 localScale，避免与横向布局组抢 anchoredPosition；
        /// 遍历本次新建的卡片列表而不是子节点，因为 ClearChildren 走的是 Destroy，
        /// 上一批卡本帧仍挂在父节点下。
        /// </summary>
        private void PlayCardEntrance()
        {
            const float stagger = 0.06f;
            const float duration = 0.18f;

            for (int i = 0; i < activeChoiceCards.Count; i++)
            {
                Button card = activeChoiceCards[i];
                if (card == null) continue;

                CanvasGroup group = card.GetComponent<CanvasGroup>();
                if (group == null) continue;
                group.alpha = 0f;
                card.transform.localScale = new Vector3(0.88f, 0.88f, 1f);
                StartCoroutine(FadeCardIn(group, i * stagger, duration));
            }
        }

        private IEnumerator FadeCardIn(CanvasGroup card, float delay, float duration)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            if (card == null) yield break;

            RectTransform rect = card.transform as RectTransform;
            float elapsed = 0f;
            // 玩家在入场动画播完前就点了牌时收手，把这张卡直接补到位，交给选中演出接管。
            while (elapsed < duration && card != null && !choiceResolving)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - normalized, 3f);
                card.alpha = eased;
                if (rect != null)
                {
                    float scale = Mathf.Lerp(0.88f, 1f, eased);
                    rect.localScale = new Vector3(scale, scale, 1f);
                }
                yield return null;
            }

            if (card == null) yield break;
            card.alpha = 1f;
            if (rect != null) rect.localScale = Vector3.one;
        }

        private void BeginChoose(string key, Button card)
        {
            if (choiceResolving) return;
            choiceResolving = true;

            // 卡还在原位时先取好起飞点与图；提交之后弹层就关了，取不到了。
            Vector2 origin = CardFxOrigin(card);
            Sprite icon = CardIconSprite(card);

            StartCoroutine(ResolveChoice(card, delegate
            {
                // 让棋子盒等飞行落袋再刷新，新棋子才是"飞进去"而不是提前冒出来。
                pieceBoxRefreshDeferred = true;
                Choose(key);
                StartCoroutine(PlayPieceFlight(origin, icon));
            }));
        }

        private Vector2 CardFxOrigin(Button card)
        {
            if (card == null || interactionFeedback == null) return Vector2.zero;

            Canvas.ForceUpdateCanvases();
            return interactionFeedback.GetFxPosition(card.transform as RectTransform);
        }

        private static Sprite CardIconSprite(Button card)
        {
            if (card == null) return null;

            Transform artwork = card.transform.Find("Icon/Artwork");
            if (artwork == null) return null;
            Image image = artwork.GetComponent<Image>();
            return image == null ? null : image.sprite;
        }

        /// <summary>选中的棋子从卡片位置抛向棋子盒，落袋时棋子盒弹一下再刷新。</summary>
        private IEnumerator PlayPieceFlight(Vector2 origin, Sprite icon)
        {
            RectTransform target = pieceBoxRoot as RectTransform;
            if (canvas == null || target == null || interactionFeedback == null)
            {
                pieceBoxRefreshDeferred = false;
                RefreshPieceBox();
                yield break;
            }

            Canvas.ForceUpdateCanvases();
            Vector2 destination = interactionFeedback.GetFxPosition(target);

            GameObject ghost = NewUi("Piece Flight", canvas.transform);
            ghost.transform.SetAsLastSibling();
            RectTransform rect = ghost.GetComponent<RectTransform>();
            AnchorRect(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), origin, new Vector2(150f, 150f));

            Image image = ghost.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            if (icon != null)
            {
                image.sprite = icon;
            }
            else
            {
                image.sprite = presentation.PanelSprite;
                image.type = Image.Type.Sliced;
                image.color = new Color(0.87f, 0.81f, 0.66f, 1f);
            }

            CanvasGroup group = ghost.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            // 中途抬高一点走个抛物线，比直线更像"被收进盒子"。
            Vector2 apex = new Vector2(
                (origin.x + destination.x) * 0.5f,
                Mathf.Max(origin.y, destination.y) + 110f);

            float duration = 0.46f / SettlementSpeedMultiplier;
            float elapsed = 0f;
            while (elapsed < duration && ghost != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = normalized * normalized * (3f - 2f * normalized);
                rect.anchoredPosition = Vector2.Lerp(
                    Vector2.Lerp(origin, apex, eased),
                    Vector2.Lerp(apex, destination, eased),
                    eased);
                float scale = Mathf.Lerp(1f, 0.38f, eased);
                rect.localScale = new Vector3(scale, scale, 1f);
                group.alpha = 1f - Mathf.Pow(eased, 4f) * 0.45f;
                yield return null;
            }

            if (ghost != null) Destroy(ghost);

            pieceBoxRefreshDeferred = false;
            RefreshPieceBox();
            yield return PunchPieceBox();
        }

        private IEnumerator PunchPieceBox()
        {
            RectTransform rect = pieceBoxRoot as RectTransform;
            if (rect == null) yield break;

            float duration = 0.2f / SettlementSpeedMultiplier;
            float elapsed = 0f;
            while (elapsed < duration && rect != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float punch = Mathf.Sin(Mathf.Clamp01(elapsed / duration) * Mathf.PI) * 0.1f;
                rect.localScale = new Vector3(1f + punch, 1f + punch, 1f);
                yield return null;
            }

            if (rect != null) rect.localScale = Vector3.one;
        }

        private void BeginChooseItem(string key, Button card)
        {
            if (choiceResolving) return;
            choiceResolving = true;
            StartCoroutine(ResolveChoice(card, delegate { ChooseItem(key); }));
        }

        /// <summary>
        /// 一击即选，但先把"选了哪张"演出来：选中卡放大，其余两张淡出收小，再提交。
        /// 三选一是高频操作，多加一次确认点击会累；不可逆操作（打烊）才走确认弹层。
        /// </summary>
        private IEnumerator ResolveChoice(Button chosen, Action commit)
        {
            const float duration = 0.26f;
            RectTransform chosenRect = chosen == null ? null : chosen.transform as RectTransform;
            List<RectTransform> others = new List<RectTransform>();
            List<CanvasGroup> otherGroups = new List<CanvasGroup>();
            for (int i = 0; i < activeChoiceCards.Count; i++)
            {
                Button card = activeChoiceCards[i];
                if (card == null || card == chosen) continue;

                RectTransform rect = card.transform as RectTransform;
                CanvasGroup group = card.GetComponent<CanvasGroup>();
                if (rect == null || group == null) continue;
                others.Add(rect);
                otherGroups.Add(group);
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - normalized, 3f);
                float overshoot = Mathf.Sin(normalized * Mathf.PI) * 0.03f;
                if (chosenRect != null)
                {
                    float scale = Mathf.Lerp(1f, 1.06f, eased) + overshoot;
                    chosenRect.localScale = new Vector3(scale, scale, 1f);
                }

                for (int i = 0; i < others.Count; i++)
                {
                    if (otherGroups[i] == null || others[i] == null) continue;
                    otherGroups[i].alpha = Mathf.Lerp(1f, 0.4f, eased);
                    float scale = Mathf.Lerp(1f, 0.94f, eased);
                    others[i].localScale = new Vector3(scale, scale, 1f);
                }
                yield return null;
            }

            if (commit != null) commit();
            choiceResolving = false;
        }

        private void Choose(string key)
        {
            AddChosenPiece(key);
            if (pendingExtraPieceChoices > 0)
            {
                pendingExtraPieceChoices -= 1;
                ShowChoices(null);
                return;
            }
            choiceOverlayView.Hide();
            pendingRewardMinimum = null;
            locked = false;
            SetRollInteractable(true);
            NotifyPostChoiceBeats();
        }

        private Element AddChosenPiece(string key)
        {
            if (!CanAddElement(key))
            {
                throw new InvalidOperationException(
                    "[CatCafeConfig] 奖励选项违反 pool_limit：" + key);
            }
            Element chosen = ApplyElementEnterRules(Instance(key));
            pool.Add(chosen);
            // 合并同类之后新棋子未必在最后一页，交给刷新时按 key 定位。
            pieceBoxFocusKey = chosen.Key;
            int empty = -1;
            for (int i = 0; i < board.Count; i++) if (board[i] == null) { empty = i; break; }
            if (empty < 0) empty = UnityEngine.Random.Range(0, BoardSize);
            board[empty] = chosen;
            RenderBoard();
            ShowToast(chosen.Name + "进店了");
            ApplyImmediateItemRules("on_choose", chosen);
            return chosen;
        }

        private bool BeginConfiguredChoices(
            ItemDefinition item, List<CatCafeConfigDatabase.RuleRow> rules)
        {
            configuredChoicePhases.Clear();
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (rule.owner_key != item.Key || rule.operation != "choose_generate") continue;
                int primary = EvaluateScope(
                    rule.primary_scope, rule.primary_filter, null, -1, null, 0, false);
                int secondary = EvaluateScope(
                    rule.secondary_scope, rule.secondary_filter, null, -1, null, 0, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                    !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;
                ConfiguredChoicePhase phase = new ConfiguredChoicePhase
                {
                    Remaining = Mathf.Max(1, rule.result_count),
                    Reason = string.IsNullOrEmpty(rule.reason) ? item.Name : rule.reason
                };
                phase.Candidates.AddRange(ConfiguredChoiceCandidates(rule));
                if (phase.Candidates.Count >= phase.Remaining) configuredChoicePhases.Add(phase);
            }
            if (configuredChoicePhases.Count == 0) return false;
            configuredChoiceItemKey = item.Key;
            configuredChoicePage = 0;
            CloseCardDetail();
            PopulateConfiguredChoicePage();
            choiceOverlayView.Show();
            PlayCardEntrance();
            return true;
        }

        private List<string> ConfiguredChoiceCandidates(CatCafeConfigDatabase.RuleRow rule)
        {
            List<string> result = new List<string>();
            if (rule.target_scope == "kind")
            {
                Kind kind = ParseKind(rule.target_filter);
                foreach (KeyValuePair<string, Definition> pair in defs)
                    if (pair.Value.Kind == kind && CanAddElement(pair.Key)) result.Add(pair.Key);
            }
            else if (rule.target_scope == "keys")
            {
                string[] keys = (rule.target_filter ?? string.Empty).Split('|');
                for (int i = 0; i < keys.Length; i++)
                    if (defs.ContainsKey(keys[i]) && CanAddElement(keys[i])) result.Add(keys[i]);
            }
            else if (rule.target_scope == "all_rewards") result.AddRange(AllConfiguredPoolKeys(null));
            else if (rule.target_scope == "skipped_history")
            {
                for (int i = 0; i < skippedChoiceHistory.Count; i++)
                    if (defs.ContainsKey(skippedChoiceHistory[i]) &&
                        CanAddElement(skippedChoiceHistory[i])) result.Add(skippedChoiceHistory[i]);
            }
            else if (rule.target_scope == "rarity")
                result.AddRange(AllConfiguredPoolKeys(ParseRarity(rule.target_filter)));
            return result;
        }

        private List<string> AllConfiguredPoolKeys(Rarity? rarity)
        {
            List<string> result = new List<string>();
            foreach (KeyValuePair<string, Definition> pair in defs)
            {
                Definition definition = pair.Value;
                if (string.IsNullOrEmpty(definition.PoolRarity) || !CanAddElement(pair.Key)) continue;
                if (rarity.HasValue && EffectiveRarity(definition) != rarity.Value) continue;
                result.Add(pair.Key);
            }
            return result;
        }

        private void PopulateConfiguredChoicePage()
        {
            while (configuredChoicePhases.Count > 0 && configuredChoicePhases[0].Remaining <= 0)
                configuredChoicePhases.RemoveAt(0);
            if (configuredChoicePhases.Count == 0)
            {
                CompleteConfiguredChoices();
                return;
            }
            ConfiguredChoicePhase phase = configuredChoicePhases[0];
            choiceResolving = false;
            activeChoiceCards.Clear();
            currentChoiceKeys.Clear();
            ClearChildren(choicesRoot);
            int pageSize = Mathf.Max(1, CatCafeConfigDatabase.GetInt("base_reward_option_count", 3));
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(phase.Candidates.Count / (float)pageSize));
            configuredChoicePage = ((configuredChoicePage % pageCount) + pageCount) % pageCount;
            int start = configuredChoicePage * pageSize;
            int end = Mathf.Min(start + pageSize, phase.Candidates.Count);
            choiceTitle.text = string.Format(
                CatCafeConfigDatabase.GetRequiredString("ui_configured_choice_title_format"),
                phase.Reason, phase.Remaining);
            for (int i = start; i < end; i++)
            {
                string key = phase.Candidates[i];
                Definition definition = defs[key];
                Button card = CreateCard(
                    choicesRoot, definition.Name, EffectiveRarity(definition),
                    Join(definition.Rules, " "), definition.Asset, key, true);
                Button captured = card;
                card.onClick.AddListener(delegate { BeginConfiguredChoose(key, captured); });
                activeChoiceCards.Add(card);
                currentChoiceKeys.Add(key);
            }
            ApplyChoicePanelWidth(activeChoiceCards.Count);
            choiceTicketText.text = string.Format(
                CatCafeConfigDatabase.GetRequiredString("ui_pool_page_format"),
                configuredChoicePage + 1, pageCount);
            rerollButton.GetComponentInChildren<TMP_Text>().text =
                CatCafeConfigDatabase.GetRequiredString("ui_pool_next_label");
            rerollButton.interactable = pageCount > 1;
            SetButtonDimmed(rerollButton, pageCount <= 1);
        }

        private void BeginConfiguredChoose(string key, Button card)
        {
            if (choiceResolving) return;
            choiceResolving = true;
            StartCoroutine(ResolveChoice(card, delegate { CommitConfiguredChoice(key); }));
        }

        private void CommitConfiguredChoice(string key)
        {
            if (configuredChoicePhases.Count == 0) return;
            ConfiguredChoicePhase phase = configuredChoicePhases[0];
            AddChosenPiece(key);
            phase.Candidates.Remove(key);
            phase.Remaining -= 1;
            configuredChoicePage = 0;
            PopulateConfiguredChoicePage();
            PlayCardEntrance();
        }

        private void CompleteConfiguredChoices()
        {
            string itemKey = configuredChoiceItemKey;
            configuredChoiceItemKey = null;
            configuredChoicePhases.Clear();
            choiceOverlayView.Hide();
            if (!string.IsNullOrEmpty(itemKey)) RemoveOwnedItem(itemKey);
            locked = false;
            SetRollInteractable(true);
            RefreshPieceBox();
            UpdateHud();
        }

        private void ApplyImmediateItemRules(string trigger, Element source = null)
        {
            int removalBonus;
            int rerollBonus;
            string reason;
            List<string> generatedKeys = new List<string>();
            int coinBonus = EvaluateItemTrigger(
                trigger, 0, out removalBonus, out rerollBonus, out reason,
                source, generatedKeys);
            money += coinBonus;
            removalTokens += removalBonus;
            rerollTokens += rerollBonus;
            for (int i = 0; i < generatedKeys.Count; i++) BringGeneratedElement(generatedKeys[i]);
            if (coinBonus != 0 || removalBonus != 0 || rerollBonus != 0 || generatedKeys.Count > 0)
            {
                UpdateHud();
                ShowToast(reason);
                // 这一批没有具体来源棋子（是持有道具触发的），就飘在画面正中。
                PlayTicketGainNotes(Vector2.zero, rerollBonus, removalBonus);
            }
        }

        /// <summary>这件道具有没有可主动使用的规则——有才在详情窗里露出「使用」按钮。</summary>
        private bool ItemHasClickRule(string itemKey)
        {
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("on_click", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].owner_key == itemKey) return true;
            }
            return false;
        }

        /// <summary>
        /// 玩家在详情窗点「使用」：跑这件道具的全部 on_click 规则。
        /// 条件不满足就什么也不做（也不消耗自身），避免点一下白白亏掉一件道具。
        /// </summary>
        private IEnumerator UseOwnedItem(ItemDefinition item)
        {
            if (item == null || !HasItem(item.Key)) yield break;
            ClearPendingDismissRewards();

            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("on_click", "item");
            bool hasConfiguredChoice = false;
            for (int i = 0; i < rules.Count; i++)
                if (rules[i].owner_key == item.Key && rules[i].operation == "choose_generate")
                    hasConfiguredChoice = true;
            if (hasConfiguredChoice)
            {
                BeginConfiguredChoices(item, rules);
                yield break;
            }
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow skipRule = rules[i];
                if (skipRule.owner_key != item.Key || skipRule.operation != "skip_last_round") continue;
                int primary = EvaluateScope(
                    skipRule.primary_scope, skipRule.primary_filter, null, -1, null, 0, false);
                if (!Passes(skipRule.primary_comparator, primary, skipRule.primary_threshold)) continue;
                RemoveOwnedItem(item.Key);
                pendingItemChoiceMinimum = ParseRarity(skipRule.result_key);
                stageRound = CurrentStage.Rounds + stageBonusRounds;
                CloseCardDetail();
                locked = true;
                ContinueStageEnd(false);
                yield break;
            }
            int coinGain = 0;
            int activeRemoval = 0;
            int activeReroll = 0;
            int removedTotal = 0;
            bool consumeSelf = false;
            bool fired = false;
            List<string> generatedKeys = new List<string>();
            List<string> reasons = new List<string>();

            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (rule.owner_key != item.Key) continue;

                int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, null, -1, null, 0, false);
                int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, null, -1, null, 0, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                    !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                int removed = ApplyRemovalRule(rule);
                removedTotal += removed;
                // 有移除动作时，移除数量取代 primary 参与结算：「每移除 1 个得 N」直接用 primary_factor 表达。
                int effectivePrimary = string.IsNullOrEmpty(rule.remove_scope) ? primary : removed;
                int value = rule.target_value_mode == "multiply_value"
                    ? Mathf.RoundToInt(effectivePrimary * rule.multiplier)
                    : CalculateRuleValue(rule, effectivePrimary, secondary);
                if (rule.operation == "income") coinGain += value;
                else if (rule.operation == "add_removal") activeRemoval += value;
                else if (rule.operation == "add_reroll") activeReroll += value;
                else if (rule.operation == "generate" || rule.operation == "generate_random")
                {
                    int triggerCount = RollRuleTriggers(rule);
                    int copies = Mathf.Max(1, rule.result_count);
                    for (int trigger = 0; trigger < triggerCount; trigger++)
                        for (int copy = 0; copy < copies; copy++)
                            generatedKeys.Add(rule.operation == "generate"
                                ? rule.result_key
                                : ChooseRuleResultKey(rule));
                }
                else if (rule.operation == "set_reward_minimum")
                    pendingRewardMinimum = ParseRarity(rule.result_key);
                if (rule.consume_self) consumeSelf = true;
                if (!string.IsNullOrEmpty(rule.reason) && !reasons.Contains(rule.reason)) reasons.Add(rule.reason);
                fired = true;
            }

            if (!fired) yield break;

            // 被清走的棋子自己的离场收益，并进这一次点击的总额里一起飞。
            coinGain += pendingDismissCoins;
            int dismissalRemoval = pendingDismissRemovalTokens;
            int dismissalReroll = pendingDismissRerollTokens;
            int dismissalInspiration = pendingDismissInspirationTokens;
            List<string> dismissalGenerated = new List<string>(pendingDismissGeneratedKeys);
            ClearPendingDismissRewards();
            removalTokens += dismissalRemoval + activeRemoval;
            rerollTokens += dismissalReroll + activeReroll;
            inspirationTokens += dismissalInspiration;
            for (int i = 0; i < dismissalGenerated.Count; i++)
                BringGeneratedElement(dismissalGenerated[i]);
            for (int i = 0; i < generatedKeys.Count; i++)
                BringGeneratedElement(generatedKeys[i]);

            CloseCardDetail();
            if (reasons.Count > 0)
                ShowToast(string.Join(
                    CatCafeConfigDatabase.GetRequiredString("ui_action_reason_separator"),
                    reasons.ToArray()));
            // 主动使用没有具体来源棋子，金币从画面正中飞出。
            if (coinGain > 0) yield return StartCoroutine(PlayCoinReward(Vector2.zero, coinGain));
            PlayTicketGainNotes(
                Vector2.zero, dismissalReroll + activeReroll, dismissalRemoval + activeRemoval);

            // 消耗自身放在最后：先把钱结完，再让这件道具从册子上消失。
            if (consumeSelf) RemoveOwnedItem(item.Key);
            RefreshPieceBox();
            UpdateHud();
        }

        /// <summary>营业开始前触发的道具规则（例如「营业前清掉名册里的某类对象并结算」）。</summary>
        private IEnumerator SettleBeforeRoundItems()
        {
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("before_round", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key)) continue;

                int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, null, -1, null, 0, false);
                int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, null, -1, null, 0, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                    !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                ClearPendingDismissRewards();
                int removed = ApplyRemovalRule(rule);
                if (removed <= 0) continue;

                int value = (rule.operation == "income"
                    ? CalculateRuleValue(rule, removed, secondary)
                    : 0) + pendingDismissCoins;
                if (rule.operation == "store_removed") AddItemCounter(rule.owner_key, removed);
                int dismissalRemoval = pendingDismissRemovalTokens;
                int dismissalReroll = pendingDismissRerollTokens;
                int dismissalInspiration = pendingDismissInspirationTokens;
                List<string> dismissalGenerated = new List<string>(pendingDismissGeneratedKeys);
                ClearPendingDismissRewards();
                removalTokens += dismissalRemoval;
                rerollTokens += dismissalReroll;
                inspirationTokens += dismissalInspiration;
                for (int generatedIndex = 0; generatedIndex < dismissalGenerated.Count; generatedIndex++)
                    BringGeneratedElement(dismissalGenerated[generatedIndex]);
                if (!string.IsNullOrEmpty(rule.reason)) ShowToast(rule.reason);
                if (value > 0) yield return StartCoroutine(PlayCoinReward(Vector2.zero, value));
                PlayTicketGainNotes(Vector2.zero, dismissalReroll, dismissalRemoval);
                if (rule.consume_self) RemoveOwnedItem(rule.owner_key);
            }
        }

        private int EvaluateItemTrigger(string trigger, int roundIncome,
            out int removalBonus, out int rerollBonus, out string message,
            Element source = null, List<string> generatedKeys = null)
        {
            int coinBonus = 0;
            removalBonus = 0;
            rerollBonus = 0;
            List<string> reasons = new List<string>();
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules(trigger, "item");
            if (trigger == "on_consume")
            {
                HashSet<string> countedOwners = new HashSet<string>();
                for (int i = 0; i < rules.Count; i++)
                {
                    CatCafeConfigDatabase.RuleRow counterRule = rules[i];
                    if (!HasItem(counterRule.owner_key) || !MatchesRuleSource(counterRule, source)) continue;
                    if (counterRule.primary_scope != "item_counter" ||
                        !countedOwners.Add(counterRule.owner_key)) continue;
                    AddItemCounter(counterRule.owner_key, 1);
                }
            }
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key)) continue;
                if (!MatchesRuleSource(rule, source)) continue;
                int sourceIndex = source == null ? -1 : FindBoardIndex(source.Id);
                int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, source, sourceIndex, null, roundIncome, false);
                int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, source, sourceIndex, null, roundIncome, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                    !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                int value = rule.target_value_mode == "multiply_value"
                    ? Mathf.RoundToInt(primary * rule.multiplier)
                    : CalculateRuleValue(rule, primary, secondary);
                if (rule.operation == "income") coinBonus += value;
                else if (rule.operation == "add_removal") removalBonus += value;
                else if (rule.operation == "add_reroll") rerollBonus += value;
                else if (rule.operation == "generate" || rule.operation == "generate_random")
                {
                    int triggerCount = RollRuleTriggers(rule);
                    int copies = Mathf.Max(1, rule.result_count);
                    if (generatedKeys != null)
                        for (int triggerIndex = 0; triggerIndex < triggerCount; triggerIndex++)
                            for (int copy = 0; copy < copies; copy++)
                                generatedKeys.Add(rule.operation == "generate"
                                    ? rule.result_key
                                    : ChooseRuleResultKey(rule));
                }
                else if (rule.operation == "generate_source")
                {
                    int triggerCount = RollRuleTriggers(rule, source, sourceIndex);
                    if (generatedKeys != null && source != null)
                        for (int triggerIndex = 0; triggerIndex < triggerCount; triggerIndex++)
                            for (int copy = 0; copy < Mathf.Max(1, rule.result_count); copy++)
                                generatedKeys.Add(source.Key);
                }
                if (!string.IsNullOrEmpty(rule.reason) && !reasons.Contains(rule.reason)) reasons.Add(rule.reason);
            }

            List<string> gains = new List<string>();
            if (coinBonus != 0) gains.Add("+" + coinBonus + "金币");
            if (removalBonus != 0) gains.Add("+" + removalBonus + "下班券");
            if (rerollBonus != 0) gains.Add("+" + rerollBonus + "招呼券");
            message = (reasons.Count > 0
                ? string.Join(
                    CatCafeConfigDatabase.GetRequiredString("ui_action_reason_separator"),
                    reasons.ToArray()) + "："
                : string.Empty) +
                string.Join("、", gains.ToArray());
            return coinBonus;
        }

        /// <summary>
        /// 每轮结束只推进实例年龄，并执行表中声明的永久成长规则。
        /// 代码不知道哪张卡第几轮成长，也不知道加多少；这些均由 Rules 的范围、阈值和值决定。
        /// </summary>
        private void ApplyRoundEndPersistentRules()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                Element element = pool[i];
                if (element == null) continue;
                element.LifetimeRounds += 1;
            }

            for (int i = 0; i < ownedItems.Count; i++)
            {
                string key = ownedItems[i];
                ownedItemRounds[key] = OwnedItemRoundCount(key) + 1;
            }

            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("round_end", "element");
            for (int i = 0; i < pool.Count; i++)
            {
                Element element = pool[i];
                if (element == null) continue;
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    CatCafeConfigDatabase.RuleRow rule = rules[ruleIndex];
                    if (rule.operation != "permanent_add" || !MatchesRuleSource(rule, element)) continue;
                    if (element.AppliedPersistentRules.Contains(rule.rule_id)) continue;

                    int primary = EvaluateScope(
                        rule.primary_scope, rule.primary_filter, element,
                        FindBoardIndex(element.Id), null, 0, false);
                    int secondary = EvaluateScope(
                        rule.secondary_scope, rule.secondary_filter, element,
                        FindBoardIndex(element.Id), null, 0, false);
                    if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                        !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;

                    ApplyPersistentGain(new PersistentGain
                    {
                        Target = element,
                        Amount = CalculateRuleValue(rule, primary, secondary),
                        RuleId = rule.rule_id,
                        Reason = rule.reason
                    });
                }
            }
        }

        private bool TryApplyStageDeadlineRule()
        {
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("stage_deadline", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "extra_round") continue;
                int primary = EvaluateScope(rule.primary_scope, rule.primary_filter, null, -1, null, 0, false);
                int secondary = EvaluateScope(rule.secondary_scope, rule.secondary_filter, null, -1, null, 0, false);
                if (!Passes(rule.primary_comparator, primary, rule.primary_threshold) ||
                    !Passes(rule.secondary_comparator, secondary, rule.secondary_threshold)) continue;
                RemoveOwnedItem(rule.owner_key);
                stageBonusRounds += Mathf.Max(1, rule.base_value);
                string name = itemDefs.ContainsKey(rule.owner_key) ? itemDefs[rule.owner_key].Name : rule.owner_key;
                ShowToast(name + "：今天多招呼 " + Mathf.Max(1, rule.base_value) + " 波客人");
                return true;
            }
            return false;
        }

        private void ContinueStageEnd(bool skipSettlementOffer)
        {
            if (!skipSettlementOffer && TryOfferStageSettlementChoice()) return;
            if (money < CurrentStage.Target && TryOfferExtraRoundChoice()) return;
            if (money < CurrentStage.Target && TryApplyStageDeadlineRule())
            {
                UpdateHud();
                locked = false;
                SetRollInteractable(true);
                return;
            }
            FinishStage();
        }

        private bool TryOfferStageSettlementChoice()
        {
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("stage_settlement", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "waive_payment_generate") continue;
                ItemDefinition item = itemDefs[rule.owner_key];
                ShowConfirm(
                    item.Name, item.Rule,
                    CatCafeConfigDatabase.GetRequiredString("ui_stage_item_accept_label"),
                    delegate
                    {
                        RemoveOwnedItem(rule.owner_key);
                        waiveNextStagePayment = true;
                        for (int copy = 0; copy < Mathf.Max(1, rule.result_count); copy++)
                            BringGeneratedElement(rule.result_key);
                        FinishStage();
                    }, null, delegate { ContinueStageEnd(true); });
                return true;
            }
            return false;
        }

        private bool TryOfferExtraRoundChoice()
        {
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("stage_settlement", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (!HasItem(rule.owner_key) || rule.operation != "extra_round") continue;
                ItemDefinition item = itemDefs[rule.owner_key];
                ShowConfirm(
                    item.Name, item.Rule,
                    CatCafeConfigDatabase.GetRequiredString("ui_stage_item_accept_label"),
                    delegate
                    {
                        RemoveOwnedItem(rule.owner_key);
                        stageBonusRounds += Mathf.Max(1, rule.base_value);
                        locked = false;
                        SetRollInteractable(true);
                        UpdateHud();
                    }, null, delegate
                    {
                        if (money < CurrentStage.Target && TryApplyStageDeadlineRule())
                        {
                            locked = false;
                            SetRollInteractable(true);
                            UpdateHud();
                        }
                        else FinishStage();
                    });
                return true;
            }
            return false;
        }

        private void ChooseItem(string key)
        {
            if (CanAcquireItem(key))
            {
                ownedItems.Add(key);
                if (!ownedItemRounds.ContainsKey(key)) ownedItemRounds[key] = 0;
                if (!itemCounters.ContainsKey(key)) itemCounters[key] = 0;
                buffFocusKey = key;
            }
            ApplyItemStackRules(key);
            ApplyElementEnterRulesToPool();
            RefreshBuffPanel();
            itemOverlayView.Hide();
            ShowToast(string.Format(CatCafeConfigDatabase.GetRequiredString(
                "ui_item_added_toast_format"), itemDefs[key].Name));
            Rarity? minimum = pendingItemRewardMinimum;
            if (pendingExtraItemChoices > 0)
            {
                pendingExtraItemChoices -= 1;
                ShowItemChoices(currentItemChoiceTier, minimum ?? Rarity.Common);
            }
            else
            {
                pendingItemRewardMinimum = null;
                pendingItemChoiceMinimum = null;
                StartCoroutine(ShowChoicesAfterHandoff(minimum));
            }
        }

        private void ApplyItemStackRules(string key)
        {
            List<CatCafeConfigDatabase.RuleRow> rules =
                ConfiguredRules("item_stack_resolution", "item");
            for (int i = 0; i < rules.Count; i++)
            {
                CatCafeConfigDatabase.RuleRow rule = rules[i];
                if (rule.owner_key != key || rule.operation != "cashout") continue;
                int count = OwnedItemCount(key);
                if (!Passes(rule.primary_comparator, count, rule.primary_threshold)) continue;
                while (ownedItems.Remove(key)) { }
                ownedItemRounds.Remove(key);
                itemCounters.Remove(key);
                money += CalculateRuleValue(rule, count, 0);
                if (!string.IsNullOrEmpty(rule.reason)) ShowToast(rule.reason);
                RefreshBuffPanel();
                UpdateHud();
            }
        }

        private void SkipChoice()
        {
            if (!string.IsNullOrEmpty(configuredChoiceItemKey)) return;
            skippedChoiceHistory.AddRange(currentChoiceKeys);
            choiceOverlayView.Hide();
            pendingRewardMinimum = null;
            ApplyImmediateItemRules("on_skip");
            locked = false;
            SetRollInteractable(true);
            NotifyPostChoiceBeats();
        }

        private void SkipItemChoice()
        {
            itemOverlayView.Hide();
            Rarity? minimum = pendingItemRewardMinimum;
            pendingItemRewardMinimum = null;
            StartCoroutine(ShowChoicesAfterHandoff(minimum));
        }

        /// <summary>物品页与棋子页背对背，留一拍让上一个弹层收完再开下一个，避免"闪一下换了个面板"。</summary>
        private IEnumerator ShowChoicesAfterHandoff(Rarity? minimum)
        {
            yield return new WaitForSecondsRealtime(0.16f);
            ShowChoices(minimum);
        }

        private void RerollChoices()
        {
            if (!string.IsNullOrEmpty(configuredChoiceItemKey))
            {
                if (choiceResolving) return;
                configuredChoicePage += 1;
                PopulateConfiguredChoicePage();
                PlayCardEntrance();
                return;
            }
            if (rerollTokens <= 0 || choiceResolving) return;
            rerollTokens -= 1;
            ApplyImmediateItemRules("on_reroll_spent");
            PopulateChoices(pendingRewardMinimum);
            PlayCardEntrance();
            HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_cards_hold", 0.45f));
        }

        private void FinishStage()
        {
            StageConfig config = CurrentStage;
            if (money < config.Target && !waiveNextStagePayment)
            {
                resultMode = "fail";
                if (endlessMode && normalRunCompleted)
                {
                    SettleRunResult(true, stageIndex);
                    resultTitle.text = CatCafeConfigDatabase.GetRequiredString("ui_endless_result_title");
                    resultCopy.text = RunCopyFormat("ui_endless_failure_copy_format",
                        stageIndex + 1, config.Target, CollectionSummary());
                }
                else
                {
                    SettleRunResult(false, stageIndex);
                    resultTitle.text = CatCafeConfigDatabase.GetRequiredString("ui_run_failure_title");
                    resultCopy.text = RunCopyFormat("ui_run_failure_copy_format",
                        config.Target, CollectionSummary());
                }
                AppendRunSettlementCopy();
                resultButton.GetComponentInChildren<TMP_Text>().text =
                    CatCafeConfigDatabase.GetRequiredString("ui_run_return_collection_label");
                if (leaderboardButton != null) leaderboardButton.gameObject.SetActive(CatCafeLeaderboard.Enabled);
                locked = true;
                // 失败要有明确的“这件事发生了”的一拍：先演出，再让房东奶奶说话，
                // 最后才摊开账本。三样一起上的话，玩家分不清哪个是结果。
                StartCoroutine(PlayFailureSequence());
                return;
            }

            if (waiveNextStagePayment) waiveNextStagePayment = false;
            else money += AdjustMoneyLoss(-config.Target);
            int stageRerollReward = CatCafeConfigDatabase.GetInt("stage_clear_reroll_reward", 1);
            int stageRemovalReward = CatCafeConfigDatabase.GetInt("stage_clear_removal_reward", 1);
            // 名册每超出盘面 N 张就多给一张下班券：加牌速度是每波一张，而盘面恒为 16 格，
            // 固定发券压不住膨胀。让瘦身能力跟膨胀速度同构，玩家才有得选。
            int perExcess = CatCafeConfigDatabase.GetInt("stage_clear_removal_per_excess", 0);
            if (perExcess > 0 && pool.Count > BoardSize)
            {
                stageRemovalReward += (pool.Count - BoardSize) / perExcess;
            }
            rerollTokens += stageRerollReward;
            removalTokens += stageRemovalReward;
            UpdateHud();
            // 过关这一拍没有来源棋子，飘在画面正中；结果面板紧接着才弹，不会被挡住。
            PlayTicketGainNotes(Vector2.zero, stageRerollReward, stageRemovalReward);
            locked = true;

            if (config.IsFinal && !endlessMode)
            {
                normalRunCompleted = true;
                if (CatCafeConfigDatabase.GetRequiredBool("endless_enabled"))
                    OfferEndlessChallenge(config);
                else
                    ShowVictoryResult(config);
                return;
            }

            ShowStageClearResult(config);
        }

        private void ShowStageClearResult(StageConfig config)
        {
            resultMode = "stage";
            resultTitle.text = string.Format(
                CatCafeConfigDatabase.GetRequiredString(endlessMode
                    ? "ui_endless_stage_clear_title_format"
                    : "ui_run_stage_clear_title_format"),
                stageIndex + 1);
            resultCopy.text = string.Format(
                CatCafeConfigDatabase.GetRequiredString("ui_run_stage_clear_copy_format"),
                config.Target, money);
            resultButton.GetComponentInChildren<TMP_Text>().text =
                CatCafeConfigDatabase.GetRequiredString("ui_run_stage_clear_button");
            if (leaderboardButton != null) leaderboardButton.gameObject.SetActive(false);
            resultOverlayView.Show();
        }

        private void ShowVictoryResult(StageConfig config)
        {
            resultMode = "victory";
            // 刚交完最后一期房租、stageIndex 还停在这一期上，所以要 +1。
            SettleRunResult(true, stageIndex + 1);
            resultTitle.text = CatCafeConfigDatabase.GetRequiredString("ui_run_victory_title");
            resultCopy.text = RunCopyFormat("ui_run_victory_copy_format",
                config.Target, CollectionSummary());
            AppendRunSettlementCopy();
            resultButton.GetComponentInChildren<TMP_Text>().text =
                CatCafeConfigDatabase.GetRequiredString("ui_run_return_collection_label");
            if (leaderboardButton != null) leaderboardButton.gameObject.SetActive(CatCafeLeaderboard.Enabled);
            resultOverlayView.Show();
            if (tutorialNotes != null) tutorialNotes.Notify("run_first_summary", resultPanelRect);
        }

        private void OfferEndlessChallenge(StageConfig completedStage)
        {
            int completedDay = stageIndex + 1;
            int nextDay = completedDay + 1;
            int nextTarget = CalculateNextEndlessTarget(completedStage.Target);
            ShowConfirm(
                CatCafeConfigDatabase.GetRequiredString("ui_endless_offer_title"),
                RunCopyFormat("ui_endless_offer_copy_format", completedDay, nextDay, nextTarget),
                CatCafeConfigDatabase.GetRequiredString("ui_endless_continue_label"),
                delegate { StartEndlessChallenge(completedStage, nextDay, nextTarget); },
                CatCafeConfigDatabase.GetRequiredString("ui_endless_finish_label"),
                delegate { ShowVictoryResult(completedStage); });
        }

        private void StartEndlessChallenge(StageConfig completedStage, int nextDay, int nextTarget)
        {
            endlessMode = true;
            stages.Add(BuildEndlessStage(completedStage, nextDay, nextTarget));
            stageIndex += 1;
            stageRound = 0;
            stageBonusRounds = 0;
            resultMode = string.Empty;
            UpdateHud();
            ShowItemChoices(completedStage.ClearItemTier, completedStage.ClearRewardMinimum);
        }

        private StageConfig BuildEndlessStage(StageConfig previousStage, int day, int target)
        {
            return new StageConfig(
                string.Format(CatCafeConfigDatabase.GetRequiredString("endless_stage_name_format"), day),
                Mathf.Max(1, CatCafeConfigDatabase.GetRequiredInt("endless_rounds")),
                target,
                CatCafeConfigDatabase.GetRequiredString("endless_rarity_context"),
                previousStage.ClearItemTier,
                previousStage.ClearRewardMinimum,
                false);
        }

        private static int CalculateNextEndlessTarget(int previousTarget)
        {
            double growthRate = Math.Max(0d,
                CatCafeConfigDatabase.GetRequiredFloat("endless_target_growth_rate"));
            int flatIncrement = Math.Max(0,
                CatCafeConfigDatabase.GetRequiredInt("endless_target_flat_increment"));
            int roundTo = Math.Max(1,
                CatCafeConfigDatabase.GetRequiredInt("endless_target_round_to"));
            double raw = previousTarget * (1d + growthRate) + flatIncrement;
            double rounded = Math.Ceiling(Math.Max(previousTarget + 1d, raw) / roundTo) * roundTo;
            return (int)Math.Min(int.MaxValue, rounded);
        }

        /// <summary>失败三拍：印章特效 → 房东字条（有才弹）→ 今日账本。</summary>
        private IEnumerator PlayFailureSequence()
        {
            yield return StartCoroutine(PlayFailureStamp());
            if (tutorialNotes != null)
            {
                yield return StartCoroutine(tutorialNotes.Interject("run_first_failure"));
            }
            resultOverlayView.Show();
        }

        /// <summary>
        /// 失败印章：全屏压一层暗红，正中砸下失败字样，停一拍再淡出。
        /// 纯程序化，不依赖美术资源；时长走表，手感不对就调表。
        /// </summary>
        private IEnumerator PlayFailureStamp()
        {
            if (canvas == null) yield break;
            float flash = CatCafeConfigDatabase.GetFloat("run_fail_fx_flash_seconds", 0.16f);
            float hold = CatCafeConfigDatabase.GetFloat("run_fail_fx_hold_seconds", 0.95f);
            float fade = CatCafeConfigDatabase.GetFloat("run_fail_fx_fade_seconds", 0.35f);

            GameObject layer = NewUi("FailureStamp", canvas.transform);
            layer.transform.SetAsLastSibling();
            RectTransform layerRect = layer.GetComponent<RectTransform>();
            Stretch(layerRect, 0f, 0f, 0f, 0f);
            CanvasGroup group = layer.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = true;

            Image tint = layer.AddComponent<Image>();
            tint.color = new Color(0.22f, 0.05f, 0.04f, 0.62f);

            TMP_Text stamp = MakeText(
                CatCafeConfigDatabase.GetString("run_fail_stamp_text", "没 达 到 营 业 额 ……"),
                layer.transform, 64,
                new Color(0.96f, 0.87f, 0.72f), TextAnchor.MiddleCenter);
            stamp.fontStyle = FontStyles.Bold;
            RectTransform stampRect = stamp.rectTransform;
            AnchorRect(stampRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 140f));

            if (interactionFeedback != null) interactionFeedback.PlayRollStop();

            // 砸下来：从大到正，同时整层淡入。
            float elapsed = 0f;
            while (elapsed < flash)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, flash));
                float eased = 1f - Mathf.Pow(1f - normalized, 3f);
                group.alpha = eased;
                float scale = Mathf.Lerp(1.9f, 1f, eased);
                stampRect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            group.alpha = 1f;
            stampRect.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(hold);

            elapsed = 0f;
            while (elapsed < fade)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, fade));
                yield return null;
            }
            Destroy(layer);
        }

        private void HandleResultAction()
        {
            resultOverlayView.Hide();
            if (resultMode != "stage")
            {
                // 局末停在局内结果页；只有玩家明确点击后才进入局外收集界面。
                SceneManager.LoadScene(
                    CatCafeConfigDatabase.GetRequiredString("scene_meta_collection"));
                return;
            }

            // 达成一期目标后仍在同一局内继续构筑，构筑不清空。
            resultMode = string.Empty;
            StageConfig clearedStage = CurrentStage;
            stageIndex += 1;
            if (endlessMode && stageIndex >= stages.Count)
            {
                int day = stageIndex + 1;
                stages.Add(BuildEndlessStage(clearedStage, day,
                    CalculateNextEndlessTarget(clearedStage.Target)));
            }
            stageRound = 0;
            stageBonusRounds = 0;
            UpdateHud();
            pendingExtraItemChoices = ConfiguredExtraItemChoices();
            ShowItemChoices(clearedStage.ClearItemTier, clearedStage.ClearRewardMinimum);
        }

        private int ConfiguredExtraItemChoices()
        {
            int result = 0;
            List<CatCafeConfigDatabase.RuleRow> rules = ConfiguredRules("stage_clear", "item");
            for (int i = 0; i < rules.Count; i++)
                if (HasItem(rules[i].owner_key) && rules[i].operation == "add_item_choice")
                    result += Mathf.Max(0, CalculateRuleValue(rules[i], 0, 0));
            return result;
        }

        private string CollectionSummary()
        {
            return runFirstDiscoveries > 0
                ? string.Format(CatCafeConfigDatabase.GetRequiredString("ui_run_collection_count_format"),
                    runFirstDiscoveries)
                : CatCafeConfigDatabase.GetRequiredString("ui_run_collection_none");
        }

        private static bool ArchetypeContains(CatCafeConfigDatabase.ArchetypeRow archetype, string elementKey)
        {
            return archetype != null && ContainsToken(archetype.element_keys, elementKey);
        }

        private int ArchetypePoolCount(CatCafeConfigDatabase.ArchetypeRow archetype)
        {
            int count = 0;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && ArchetypeContains(archetype, pool[i].Key)) count += 1;
            return count;
        }

        private int ArchetypeDefinitionCount(CatCafeConfigDatabase.ArchetypeRow archetype)
        {
            if (archetype == null || string.IsNullOrEmpty(archetype.element_keys)) return 0;
            return archetype.element_keys.Split('|').Length;
        }

        private CatCafeConfigDatabase.ArchetypeRow DominantArchetype(out int count)
        {
            count = 0;
            CatCafeConfigDatabase.ArchetypeRow selected = null;
            CatCafeConfigDatabase.ArchetypeRow[] rows = CatCafeConfigDatabase.Data.archetypes;
            for (int i = 0; i < rows.Length; i++)
            {
                CatCafeConfigDatabase.ArchetypeRow row = rows[i];
                if (!row.enabled) continue;
                int candidate = ArchetypePoolCount(row);
                if (candidate <= count) continue;
                count = candidate;
                selected = row;
            }
            return selected;
        }

        private void RecordArchetypeIncome(IList<RoundEvent> events)
        {
            if (events == null) return;
            CatCafeConfigDatabase.ArchetypeRow[] rows = CatCafeConfigDatabase.Data.archetypes;
            for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                RoundEvent current = events[eventIndex];
                if (current == null || current.Element == null || current.Amount == 0) continue;
                for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                {
                    CatCafeConfigDatabase.ArchetypeRow row = rows[rowIndex];
                    if (!row.enabled || !ArchetypeContains(row, current.Element.Key)) continue;
                    int previous;
                    archetypeIncome.TryGetValue(row.key, out previous);
                    archetypeIncome[row.key] = previous + current.Amount;
                }
            }
        }

        private string ArchetypeSummary()
        {
            System.Text.StringBuilder result = new System.Text.StringBuilder();
            result.Append(CatCafeConfigDatabase.GetRequiredString("ui_run_archetype_summary_header"));
            CatCafeConfigDatabase.ArchetypeRow[] rows = CatCafeConfigDatabase.Data.archetypes;
            for (int i = 0; i < rows.Length; i++)
            {
                CatCafeConfigDatabase.ArchetypeRow row = rows[i];
                if (!row.enabled) continue;
                int income;
                archetypeIncome.TryGetValue(row.key, out income);
                result.Append('\n').AppendFormat(
                    CatCafeConfigDatabase.GetRequiredString("ui_run_archetype_summary_row_format"),
                    row.label, ArchetypePoolCount(row), ArchetypeDefinitionCount(row), income);
            }
            return result.ToString();
        }

        private static string RunCopyFormat(string key, params object[] values)
        {
            string format = CatCafeConfigDatabase.GetRequiredString(key).Replace("\\n", "\n");
            return string.Format(format, values);
        }

        /// <summary>
        /// 局末结算：记录局数与图鉴收集，并把本局收益折算成罐头（MetaGameDesign §7）。
        ///
        /// 折算的是"这一局带回家的东西"，不是把局内经济搬到局外：金币只按
        /// meta_cans_coin_rate 折一小部分，其余按交上的房租期数与首次发现计。
        /// 一期房租都没交上时给回合数保底——空手而归也得有下一局的理由。
        /// 道具、构筑、金币本身仍然一律不带出局。
        /// </summary>
        /// <param name="clearedStages">
        /// 本局交上的房租期数。多数分支等于 <c>stageIndex</c>（它只在玩家点"继续"进入下一期时才 +1），
        /// 但通关分支是"刚交完最后一期、还没往下走"，必须显式 +1，否则最后一期白交。
        /// </param>
        private void SettleRunResult(bool won, int clearedStages)
        {
            if (runSettled) return;
            runSettled = true;

            runCansGained =
                Mathf.FloorToInt(money * CatCafeConfigDatabase.GetRequiredFloat("meta_cans_coin_rate")) +
                clearedStages * CatCafeConfigDatabase.GetRequiredInt("meta_cans_per_stage_clear") +
                runFirstDiscoveries * CatCafeConfigDatabase.GetRequiredInt("meta_cans_per_first_discovery");
            if (clearedStages <= 0)
            {
                int divisor = Mathf.Max(1, CatCafeConfigDatabase.GetRequiredInt("meta_cans_fail_divisor"));
                runCansGained += round / divisor;
            }

            CatCafeMeta.AddCans(runCansGained);
            CatCafeMeta.RecordRunEnd(won);
            CatCafeMeta.SaveNow();
        }

        /// <summary>
        /// 结算页末尾那一行局外收益。四个结局分支（通关/失败/无尽失败/提前打烊）各自的
        /// 正文文案都在表里，这一行统一追加，免得四份格式串各写一遍还写不一致。
        /// </summary>
        private void AppendRunSettlementCopy()
        {
            if (resultCopy == null) return;
            string line = string.Format(
                CatCafeConfigDatabase.GetRequiredString("ui_run_settle_gain_format").Replace("\\n", "\n"),
                runCansGained, runFurGained);
            resultCopy.text = resultCopy.text + "\n" + line + "\n\n" + ArchetypeSummary();
        }

        private void RequestEndRun()
        {
            if (runSettled || !string.IsNullOrEmpty(resultMode)) return;
            ShowConfirm(
                CatCafeConfigDatabase.GetRequiredString("ui_run_end_confirm_title"),
                CatCafeConfigDatabase.GetRequiredString("ui_run_end_confirm_copy"),
                CatCafeConfigDatabase.GetRequiredString("ui_run_end_confirm_accept"),
                EndRunEarly);
        }

        private void EndRunEarly()
        {
            CloseConfirm();
            CloseSettings();
            resultMode = "abandoned";
            SettleRunResult(normalRunCompleted, stageIndex);
            if (endlessMode && normalRunCompleted)
            {
                resultTitle.text = CatCafeConfigDatabase.GetRequiredString("ui_endless_result_title");
                resultCopy.text = RunCopyFormat("ui_endless_abandoned_copy_format",
                    stageIndex + 1, CollectionSummary());
            }
            else
            {
                resultTitle.text = CatCafeConfigDatabase.GetRequiredString("ui_run_failure_title");
                resultCopy.text = RunCopyFormat("ui_run_abandoned_copy_format", CollectionSummary());
            }
            AppendRunSettlementCopy();
            resultButton.GetComponentInChildren<TMP_Text>().text =
                CatCafeConfigDatabase.GetRequiredString("ui_run_return_collection_label");
            if (leaderboardButton != null) leaderboardButton.gameObject.SetActive(CatCafeLeaderboard.Enabled);
            locked = true;
            resultOverlayView.Show();
        }

        private void ShowConfirm(string title, string body, string acceptLabel, Action onAccept,
            string cancelLabel = null, Action onCancel = null)
        {
            if (confirmOverlayView == null)
            {
                // 没有确认弹层时不能把操作吞掉，直接执行。
                if (onAccept != null) onAccept();
                return;
            }

            confirmAction = onAccept;
            confirmCancelAction = onCancel;
            confirmTitle.text = title;
            confirmCopy.text = body;
            confirmAcceptText.text = acceptLabel;
            confirmCancelText.text = string.IsNullOrEmpty(cancelLabel)
                ? CatCafeConfigDatabase.GetRequiredString("ui_common_cancel_label")
                : cancelLabel;
            confirmOverlayView.Show();
        }

        private void CloseConfirm()
        {
            Action pending = confirmCancelAction;
            confirmAction = null;
            confirmCancelAction = null;
            if (confirmOverlayView != null) confirmOverlayView.Hide();
            if (pending != null) pending();
        }

        private void AcceptConfirm()
        {
            Action pending = confirmAction;
            confirmAction = null;
            confirmCancelAction = null;
            if (confirmOverlayView != null) confirmOverlayView.Hide();
            if (pending != null) pending();
        }

        /// <summary>棋子详情小窗里的"送走/下班"能否点：有券、非结算演出、店里不止一位。</summary>
        private bool CanDismissPiece()
        {
            return removalTokens > 0 && !locked && pool.Count > 1;
        }

        /// <summary>按实例送走小窗当前展示的棋子（消耗一张下班券）。</summary>
        /// <summary>
        /// 详情窗底部那颗按钮一物两用：看棋子时是「下班」，看可主动使用的道具时是「使用」。
        /// 复用同一颗按钮而不是新加一颗，是为了不动已锁定的小窗布局。
        /// </summary>
        private void DismissCardDetailPiece()
        {
            if (cardDetailItem != null)
            {
                ItemDefinition item = cardDetailItem;
                StartCoroutine(UseOwnedItem(item));
                return;
            }

            Element piece = cardDetailElement;
            if (piece == null || !CanDismissPiece()) return;

            int poolIndex = -1;
            for (int i = 0; i < pool.Count; i++) if (pool[i].Id == piece.Id) { poolIndex = i; break; }
            if (poolIndex < 0) return;

            // 下班券支付的是这次主动送走操作；即使被保护效果阻止，操作仍已结算。
            removalTokens -= 1;
            ApplyImmediateItemRules("on_removal_spent", piece);

            PersistentGain preventedGain;
            if (TryFindRemovalProtector(piece, out preventedGain))
            {
                ApplyPersistentGain(preventedGain);
                RenderBoard();
                UpdateHud();
                CloseCardDetail();
                return;
            }

            pool.RemoveAt(poolIndex);
            int boardIndex = FindBoardIndex(piece.Id);
            if (boardIndex >= 0) board[boardIndex] = null;
            RenderBoard();
            UpdateHud();
            ShowToast(string.Format(CatCafeConfigDatabase.GetString(
                "ui_card_detail_dismiss_toast_format", "{0}下班回家啦"), piece.Name));
            CloseCardDetail();

            // 「被移除时获得 N 金币」这一类在这里兑现：送走它才是它真正的收益时刻。
            ClearPendingDismissRewards();
            int dismissCoins = EvaluateDismissRules(piece, true);
            int dismissRemoval = pendingDismissRemovalTokens;
            int dismissReroll = pendingDismissRerollTokens;
            int dismissInspiration = pendingDismissInspirationTokens;
            List<string> dismissGenerated = new List<string>(pendingDismissGeneratedKeys);
            ClearPendingDismissRewards();
            removalTokens += dismissRemoval;
            rerollTokens += dismissReroll;
            inspirationTokens += dismissInspiration;
            for (int i = 0; i < dismissGenerated.Count; i++) BringGeneratedElement(dismissGenerated[i]);
            RenderBoard();
            RefreshPieceBox();
            UpdateHud();
            Vector2 origin = boardIndex >= 0 ? GetBoardRewardPosition(boardIndex) : Vector2.zero;
            PlayTicketGainNotes(origin, dismissReroll, dismissRemoval);
            if (dismissCoins > 0)
            {
                StartCoroutine(PlayCoinReward(origin, dismissCoins));
            }
        }

        private IEnumerator PlayCoinReward(Vector2 sourcePosition, int amount)
        {
            if (interactionFeedback == null)
            {
                ApplyCoinReward(amount);
                yield break;
            }

            yield return StartCoroutine(
                interactionFeedback.PlayReward(sourcePosition, amount, ApplyCoinReward));
        }

        private void ApplyCoinReward(int amount)
        {
            money += amount;
            UpdateHud();
        }

        /// <summary>
        /// 发券时在原地飘提示：有几种券就飘几条，第二条压在第一条下面。
        ///
        /// 刻意不做"飞进券栏"那种入库演出——券没有常驻显示位（顶栏只有金币/天数/
        /// 营业额/波次，券栏只活在三选一与物件弹层里），而三个发券时机弹层都不在场，
        /// 飞过去无处可落。所以只在获得的地方交代一句，随即淡出。
        /// </summary>
        private void PlayTicketGainNotes(Vector2 position, int rerollGain, int removalGain)
        {
            if (interactionFeedback == null) return;

            float lineGap = CatCafeConfigDatabase.GetFloat("ui_note_line_gap", 34f);
            Color rerollColor = UiColorOr("ui_note_reroll_color", new Color(1f, 0.84f, 0.36f, 1f));
            Color removalColor = UiColorOr("ui_note_removal_color", new Color(0.78f, 0.90f, 1f, 1f));

            int line = 0;
            if (rerollGain > 0)
            {
                interactionFeedback.PlayFloatingNote(position, string.Format(
                    CatCafeConfigDatabase.GetRequiredString("ui_note_reroll_format"),
                    rerollGain), rerollColor);
                line++;
            }
            if (removalGain > 0)
            {
                interactionFeedback.PlayFloatingNote(position + Vector2.down * (lineGap * line), string.Format(
                    CatCafeConfigDatabase.GetRequiredString("ui_note_removal_format"),
                    removalGain), removalColor);
            }
        }

        /// <summary>取表里的颜色；没配或配歪就用调用方给的兜底色，不静默变成黑色。</summary>
        private static Color UiColorOr(string key, Color fallback)
        {
            string raw = CatCafeConfigDatabase.GetString(key);
            Color parsed;
            return !string.IsNullOrEmpty(raw) && ColorUtility.TryParseHtmlString(raw, out parsed)
                ? parsed
                : fallback;
        }

        private void BeginChainSequence()
        {
            ClearChainVisuals();
            if (chainOverlayRoot != null) chainOverlayRoot.SetActive(true);
            if (chainSequenceText != null) chainSequenceText.gameObject.SetActive(false);
        }

        /// <summary>
        /// 每个联动棋子单独占一拍，并同时显示被它联动到的棋子反馈。
        /// 该棋子的金币动画与入账紧随这一拍完成，不进入后续普通批次。
        /// </summary>
        private IEnumerator PlayEventGroup(RoundEventGroup eventGroup, int groupOrder)
        {
            if (eventGroup == null || eventGroup.Events.Count == 0 ||
                chainOverlayRoot == null || chainMarkerRoot == null) yield break;

            Canvas.ForceUpdateCanvases();

            // 每个结算组拥有独立辉光。下一组开始前立即隐藏并清理上一组，
            // 避免 Destroy 延迟到帧末时旧辉光仍参与渲染。
            for (int i = chainMarkerRoot.childCount - 1; i >= 0; i--)
            {
                chainMarkerRoot.GetChild(i).gameObject.SetActive(false);
            }
            presentation.ClearChildren(chainMarkerRoot);

            RoundEvent trigger = eventGroup.Events[0];
            bool high = trigger.IsHighValue;
            string reactionLevel = high ? "high" : eventGroup.IsLinked ? "linked" : "plain";
            float restingAlpha = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_reaction_" + reactionLevel + "_marker_alpha");
            float duration = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_reaction_" + reactionLevel + "_seconds") /
                SettlementSpeedMultiplier;
            float peakScale = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_reaction_" + reactionLevel + "_scale");

            // 当前结算棋子使用主辉光色；被规则命中的邻接棋子使用其浅色变体。
            Color triggerColor = UiColor("ui_settlement_high_color");
            Color linkedColor = UiColor("ui_settlement_linked_color");
            float triggerAlpha = Mathf.Max(restingAlpha, 0.34f);
            float linkedAlpha = Mathf.Max(
                CatCafeConfigDatabase.GetRequiredFloat("settlement_reaction_linked_marker_alpha") * 0.82f,
                0.14f);

            List<GameObject> markers = new List<GameObject>();
            List<CatCafeLinkedPieceShake> linkedShakes =
                new List<CatCafeLinkedPieceShake>();
            markers.Add(CreatePieceChainMarker(
                trigger.Index, groupOrder + 1, triggerColor, triggerAlpha, true));

            HashSet<int> linkedSeen = new HashSet<int>();
            for (int i = 0; i < eventGroup.LinkedIndices.Count; i++)
            {
                int linkedIndex = eventGroup.LinkedIndices[i];
                if (linkedIndex == trigger.Index || !linkedSeen.Add(linkedIndex)) continue;

                GameObject linkedMarker = CreatePieceChainMarker(
                    linkedIndex, groupOrder + 1, linkedColor, linkedAlpha, false);
                markers.Add(linkedMarker);

                RectTransform linkedGlow = linkedMarker == null
                    ? null
                    : linkedMarker.GetComponent<RectTransform>();
                CatCafeLinkedPieceShake shake =
                    BeginLinkedPieceShake(linkedIndex, duration, linkedGlow);
                if (shake != null)
                {
                    linkedShakes.Add(shake);
                }
            }

            // 每个标记自己负责淡入、呼吸和淡出；结算协程只等待这一拍完成。
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i] == null) continue;
                CatCafeGlowPulse pulse = markers[i].GetComponent<CatCafeGlowPulse>();
                if (pulse != null) pulse.Begin(duration, peakScale);
            }

            yield return new WaitForSecondsRealtime(duration);

            // 抖动和辉光都只属于当前结算拍，结束时强制恢复棋子原始姿态。
            for (int i = 0; i < linkedShakes.Count; i++)
            {
                if (linkedShakes[i] != null)
                {
                    linkedShakes[i].StopImmediately();
                }
            }

            for (int i = 0; i < markers.Count; i++)
            {
                GameObject marker = markers[i];
                if (marker == null) continue;
                marker.SetActive(false);
                Destroy(marker);
            }
        }

        private IEnumerator PlayPayoutBatch(
            RoundPayoutBatch batch, int batchOrder, int collectedAfterBatch)
        {
            if (batch == null || batch.Events.Count == 0) yield break;

            Color accent = UiColor("ui_settlement_payout_color");
            float restingAlpha = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_payout_marker_alpha");
            float peakScale = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_payout_peak_scale");
            float pulseDuration = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_payout_batch_pulse_seconds") / SettlementSpeedMultiplier;
            float holdDuration = CatCafeConfigDatabase.GetRequiredFloat(
                "settlement_payout_batch_hold_seconds") / SettlementSpeedMultiplier;

            if (chainMarkerRoot != null) presentation.ClearChildren(chainMarkerRoot);
            List<GameObject> markers = new List<GameObject>();
            for (int i = 0; i < batch.Events.Count; i++)
            {
                markers.Add(CreatePieceChainMarker(
                    batch.Events[i].Index, batchOrder + 1, accent, restingAlpha, false));
            }

            float visibleDuration = pulseDuration + holdDuration + 0.35f;
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i] == null) continue;
                CatCafeGlowPulse pulse = markers[i].GetComponent<CatCafeGlowPulse>();
                if (pulse != null) pulse.Begin(visibleDuration, peakScale);
            }

            if (chainSequenceText != null)
            {
                chainSequenceText.gameObject.SetActive(true);
                chainSequenceText.color = UiColor("ui_settlement_batch_text_color");
                chainSequenceText.text = string.Format(
                    UiString("ui_settlement_batch_format"),
                    PayoutBatchSourceLabel(batch), batch.UnitAmount,
                    batch.Events.Count, batch.TotalAmount,
                    collectedAfterBatch - batch.TotalAmount);
            }

            yield return new WaitForSecondsRealtime(pulseDuration);

            yield return StartCoroutine(PlayDeferredPayoutRewards(batch.Events));
            // 同一金额批次只在全部金币动画完成后统一入账，避免随机飞行时长
            // 造成同批棋子逐个跳钱。
            ApplyCoinReward(batch.TotalAmount);
            if (chainSequenceText != null)
            {
                chainSequenceText.text = string.Format(
                    UiString("ui_settlement_batch_format"),
                    PayoutBatchSourceLabel(batch), batch.UnitAmount,
                    batch.Events.Count, batch.TotalAmount, collectedAfterBatch);
            }

            yield return new WaitForSecondsRealtime(holdDuration);
        }

        private IEnumerator PlayDeferredPayoutRewards(IList<RoundEvent> events)
        {
            if (events == null || events.Count == 0) yield break;

            int pending = events.Count;
            for (int i = 0; i < events.Count; i++)
            {
                RoundEvent current = events[i];
                StartCoroutine(PlayBoardRewardAnimation(
                    current, delegate { pending -= 1; }));
            }
            while (pending > 0) yield return null;
        }

        private IEnumerator PlayDeferredBoardReward(RoundEvent roundEvent, Action onComplete)
        {
            yield return StartCoroutine(PlayBoardRewardAnimation(roundEvent, null));
            if (roundEvent != null) ApplyCoinReward(roundEvent.Amount);
            if (onComplete != null) onComplete();
        }

        private IEnumerator PlayBoardRewardAnimation(RoundEvent roundEvent, Action onComplete)
        {
            if (roundEvent == null)
            {
                if (onComplete != null) onComplete();
                yield break;
            }

            if (interactionFeedback != null && roundEvent.HasLink)
            {
                string reactionLevel = roundEvent.IsHighValue
                    ? "high"
                    : "linked";
                float burstDuration = CatCafeConfigDatabase.GetRequiredFloat(
                    "settlement_reaction_" + reactionLevel + "_seconds") /
                    SettlementSpeedMultiplier;
                interactionFeedback.PlayRareChainCoinBurst(
                    GetBoardRewardPosition(roundEvent.Index), burstDuration);
            }

            RectTransform source = GetBoardRewardSource(roundEvent.Index);
            if (interactionFeedback != null)
            {
                yield return StartCoroutine(
                    interactionFeedback.PlayReward(
                        source, roundEvent.Amount, roundEvent.Element.Name, null));
            }

            if (onComplete != null) onComplete();
        }

private void ClearChainVisuals()
        {
            if (chainMarkerRoot != null) presentation.ClearChildren(chainMarkerRoot);
            if (chainSequenceText != null)
            {
                chainSequenceText.text = string.Empty;
                chainSequenceText.gameObject.SetActive(false);
            }
            if (chainOverlayRoot != null) chainOverlayRoot.SetActive(false);
        }




private GameObject CreatePieceChainMarker(
            int boardIndex, int order, Color accent, float restingAlpha, bool primary)
        {
            if (chainMarkerRoot == null) return null;

            // A board cell owns exactly one settlement glow. Re-triggering the
            // same cell replaces its previous marker instead of stacking another
            // HDR source at the same position.
            RemoveExistingPieceChainMarker(boardIndex);

            GameObject marker = presentation.NewUi(
                "Chain Highlight Cell " + boardIndex,
                chainMarkerRoot);
            RectTransform markerRect = marker.GetComponent<RectTransform>();
            AnchorRect(markerRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                GetChainCellCenter(boardIndex),
                new Vector2(BoardIconSize, BoardIconSize));

            RectTransform sourcePieceRect =
                BoardPieceRect(boardIndex);
            if (sourcePieceRect != null)
            {
                // Keep the generated alpha mask in the same orientation as
                // the rendered pawn. Rotation remains centered because both
                // RectTransforms use a 0.5 pivot.
                markerRect.rotation = sourcePieceRect.rotation;
            }

            Sprite pieceSprite = BoardPieceSprite(boardIndex);
            float strength = primary ? 1f : 0.72f;
            Image glowImage = null;
            if (pieceSprite != null)
            {
                // The original pawn and its shadow remain untouched. Keep the
                // glow graphic at full alpha: CatCafeGlowPulse owns the single
                // fade envelope through CanvasGroup. Applying restingAlpha to
                // both layers squared the opacity and hid the soft outer glow.
                glowImage = AddPieceGlowLayer(
                    marker.transform,
                    pieceSprite,
                    accent,
                    1f,
                    1f);

                // 软辉光依赖专用图集和 Shader；任一运行时资源不可用时，
                // 退回透明中心的金色框线，保证联动反馈仍然可见。
                if (glowImage == null || !glowImage.enabled || glowImage.material == null)
                {
                    if (glowImage == null)
                    {
                        GameObject fallback = presentation.NewUi(
                            "Piece Glow Fallback", marker.transform);
                        glowImage = fallback.AddComponent<Image>();
                        RectTransform fallbackRect = fallback.GetComponent<RectTransform>();
                        AnchorRect(fallbackRect,
                            new Vector2(0.5f, 0.5f),
                            new Vector2(0.5f, 0.5f),
                            new Vector2(0.5f, 0.5f),
                            Vector2.zero,
                            new Vector2(BoardIconSize, BoardIconSize));
                    }

                    glowImage.enabled = true;
                    presentation.GlowFrame(
                        glowImage,
                        accent,
                        restingAlpha * (primary ? 0.90f : 0.76f),
                        primary ? 4f : 2.5f);
                }
            }
            else
            {
                Image fallback = marker.AddComponent<Image>();
                presentation.GlowFrame(
                    fallback,
                    accent,
                    restingAlpha * (primary ? 0.48f : 0.30f),
                    primary ? 4f : 2.5f);
                fallback.raycastTarget = false;
                glowImage = fallback;
            }

            marker.AddComponent<CanvasGroup>();
            CatCafeGlowPulse pulse = marker.AddComponent<CatCafeGlowPulse>();
            pulse.Initialize(glowImage, restingAlpha * strength, primary);
            return marker;
        }

private CatCafeLinkedPieceShake BeginLinkedPieceShake(
            int boardIndex,
            float availableSeconds,
            RectTransform synchronizedGlow)
        {
            RectTransform piece = BoardPieceRect(boardIndex);
            if (piece == null)
            {
                return null;
            }

            CatCafeLinkedPieceShake shake =
                piece.GetComponent<CatCafeLinkedPieceShake>();
            if (shake == null)
            {
                shake = piece.gameObject.AddComponent<CatCafeLinkedPieceShake>();
            }

            shake.Begin(availableSeconds, synchronizedGlow);
            return shake;
        }


private void RemoveExistingPieceChainMarker(int boardIndex)
        {
            if (chainMarkerRoot == null) return;

            Transform existing = chainMarkerRoot.Find(
                "Chain Highlight Cell " + boardIndex);
            if (existing == null) return;

            existing.gameObject.SetActive(false);
            Destroy(existing.gameObject);
        }


private Image AddPieceGlowLayer(
            Transform parent, Sprite pieceSprite, Color accent, float alpha, float scale)
        {
            if (parent == null || pieceSprite == null) return null;

            GameObject layer = presentation.NewUi("Piece Shape Glow", parent);
            RectTransform rect = layer.GetComponent<RectTransform>();
            AnchorRect(rect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(BoardIconSize * scale, BoardIconSize * scale));

            Image image = layer.AddComponent<Image>();
            presentation.ShapeGlow(image, pieceSprite, accent, alpha);
            return image;
        }









private void AddPieceShapeEdgeLayer(
            Transform parent,
            Sprite pieceSprite,
            Color accent,
            float alpha,
            float distance,
            float scale)
        {
            if (parent == null || pieceSprite == null) return;

            GameObject layer = presentation.NewUi("Piece Shape Edge", parent);
            RectTransform rect = layer.GetComponent<RectTransform>();
            AnchorRect(rect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(BoardIconSize * scale, BoardIconSize * scale));

            Image image = layer.AddComponent<Image>();
            presentation.ShapeEdgeGlow(image, pieceSprite, accent, alpha, distance);
        }


private Sprite BoardPieceSprite(int boardIndex)
        {
            RectTransform piece = BoardPieceRect(boardIndex);
            if (piece == null)
            {
                return null;
            }

            // CreateToken keeps the Image on its Artwork child; querying only
            // the token root made every settlement marker lose its source sprite
            // and silently fall back to UI/Default.
            Image pieceImage = piece.GetComponentInChildren<Image>(true);
            return pieceImage == null
                ? null
                : pieceImage.sprite;
        }

private RectTransform BoardPieceRect(
            int boardIndex)
        {
            if (boardRoot == null ||
                boardIndex < 0 ||
                boardIndex >= boardRoot.childCount)
            {
                return null;
            }

            Transform cell = boardRoot.GetChild(boardIndex);
            if (cell == null || cell.childCount == 0)
            {
                return null;
            }

            return cell.GetChild(0) as RectTransform;
        }











private Vector2 GetChainCellCenter(int boardIndex)
        {
            if (chainOverlayRoot == null || boardRoot == null ||
                boardIndex < 0 || boardIndex >= boardRoot.childCount)
            {
                return Vector2.zero;
            }

            Canvas.ForceUpdateCanvases();

            RectTransform cell = boardRoot.GetChild(boardIndex) as RectTransform;
            RectTransform overlayRect = chainOverlayRoot.GetComponent<RectTransform>();
            if (cell == null || overlayRect == null) return Vector2.zero;

            // The cell is a layout slot whose pivot is top-left. Resolve the
            // rendered pawn itself so the glow and pawn share exactly the same center.
            RectTransform source = cell;
            if (cell.childCount > 0)
            {
                RectTransform piece = cell.GetChild(0) as RectTransform;
                if (piece != null) source = piece;
            }

            Vector3 worldCenter = source.TransformPoint(source.rect.center);
            return overlayRect.InverseTransformPoint(worldCenter);
        }

private RectTransform GetBoardRewardSource(int boardIndex)
        {
            if (boardRoot == null || boardIndex < 0 || boardIndex >= boardRoot.childCount)
            {
                return boardRoot == null ? null : boardRoot.GetComponent<RectTransform>();
            }

            Canvas.ForceUpdateCanvases();
            RectTransform cell = boardRoot.GetChild(boardIndex) as RectTransform;
            if (cell == null) return boardRoot.GetComponent<RectTransform>();
            return cell.childCount > 0 ? cell.GetChild(0) as RectTransform : cell;
        }

        private Vector2 GetBoardRewardPosition(int boardIndex)
        {
            RectTransform source = GetBoardRewardSource(boardIndex);
            // Vector-based reward playback expects the raw source center; the reward FX
            // component applies the shared above-piece label layout exactly once.
            return interactionFeedback == null
                ? Vector2.zero
                : interactionFeedback.GetFxPosition(source);
        }

        private Vector2 GetBoardCenterRewardPosition()
        {
            if (boardRoot == null || interactionFeedback == null) return Vector2.zero;

            Canvas.ForceUpdateCanvases();
            return interactionFeedback.GetFxPosition(boardRoot.GetComponent<RectTransform>());
        }

        private void RenderBoard()
        {
            if (boardRoot == null) return;
            // Destroy 要到帧末才真正移除对象。若旧格子继续留在层级中，紧接着播放的
            // 出生特效会按 child index 命中旧棋盘节点，看起来像别的猫变成了幼崽。
            // 先移出布局再销毁，保证 child index 立刻与 board[index] 一一对应。
            for (int i = boardRoot.childCount - 1; i >= 0; i--)
            {
                Transform oldCell = boardRoot.GetChild(i);
                oldCell.gameObject.SetActive(false);
                oldCell.SetParent(null, false);
                Destroy(oldCell.gameObject);
            }
            for (int i = 0; i < BoardSize; i++)
            {
                GameObject cell = NewUi("Cell " + i, boardRoot);
                Image cellImage = cell.AddComponent<Image>();
                // 格子边框由纸板美术底图提供；运行时层只负责摆放可交互符号。
                cellImage.color = Color.clear;
                cellImage.raycastTarget = false;
                Element element = i < board.Count ? board[i] : null;
                if (element != null) CreateToken(cell.transform, element);
            }

            RefreshPieceBox();
        }

        private IEnumerator AnimateBoardRoll(IList<Element> previousBoard, IList<Element> finalBoard)
        {
            if (reelOverlayRoot == null || reelColumns == null || reelColumns.Length != BoardColumns)
            {
                RenderBoard();
                yield break;
            }

            PrepareReels(previousBoard, finalBoard);
            reelOverlayRoot.SetActive(true);
            boardRoot.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();

            bool[] finished = new bool[BoardColumns];
            for (int column = 0; column < BoardColumns; column++)
            {
                int reelIndex = column;
                StartCoroutine(AnimateColumnRoll(reelIndex, delegate { finished[reelIndex] = true; }));
                if (column < BoardColumns - 1)
                {
                    yield return new WaitForSecondsRealtime(ReelStartStagger);
                }
            }

            while (AnyReelRunning(finished))
            {
                yield return null;
            }

            RenderBoard();
            boardRoot.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            reelOverlayRoot.SetActive(false);
        }

        private IEnumerator AnimateColumnRoll(int column, Action onComplete)
        {
            ReelColumnView reel = reelColumns[column];
            int travelSlots = ReelBaseTravelSlots + column * ReelExtraTravelSlots;
            float targetY = travelSlots * BoardRowPitch;
            reel.Strip.anchoredPosition = Vector2.zero;
            reel.Root.transform.localScale = Vector3.one;
            SetReelMotion(reel, 0f);

            float elapsed = 0f;
            while (elapsed < ReelAnticipationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / ReelAnticipationDuration);
                float eased = 1f - Mathf.Pow(1f - normalized, 3f);
                SetStripY(reel.Strip, Mathf.Lerp(0f, -ReelAnticipationDistance, eased));
                SetReelMotion(reel, normalized * 0.08f);
                yield return null;
            }

            float duration = ReelBaseDuration + column * ReelStopDelay;
            float spinStartY = -ReelAnticipationDistance;
            float spinEndY = targetY + ReelStopOvershoot;
            elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float progress = ReelTravelProgress(normalized);
                SetStripY(reel.Strip, Mathf.LerpUnclamped(spinStartY, spinEndY, progress));
                SetReelMotion(reel, ReelTravelVelocity(normalized));
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < ReelBounceDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / ReelBounceDuration);
                float spring = Mathf.Exp(-4.6f * normalized) *
                    Mathf.Cos(normalized * Mathf.PI * 2.5f);
                SetStripY(reel.Strip, targetY + ReelStopOvershoot * spring);
                SetReelMotion(reel, (1f - normalized) * 0.14f);
                float punch = 1f + Mathf.Sin(normalized * Mathf.PI) * 0.012f;
                reel.Root.transform.localScale = new Vector3(1f, punch, 1f);
                yield return null;
            }

            SetStripY(reel.Strip, targetY);
            reel.Root.transform.localScale = Vector3.one;
            SetReelMotion(reel, 0f);
            if (onComplete != null) onComplete();
        }

        private Element PreviewReelElement(System.Random random)
        {
            int blankSlots = Mathf.Max(0, BoardSize - pool.Count);
            int visualPoolSize = pool.Count + blankSlots;
            if (visualPoolSize == 0) return null;
            int slot = random.Next(visualPoolSize);
            return slot < pool.Count ? pool[slot] : null;
        }

        private void BuildChainOverlay(Transform boardContainer)
        {
            chainOverlayRoot = presentation.NewUi("Chain Highlight Overlay", boardContainer);
            presentation.Stretch(chainOverlayRoot.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            // 光层位于 Cells 之上；Shader 只输出棋子轮廓外的柔光，不覆盖棋子本体。
            chainOverlayRoot.transform.SetSiblingIndex(1);

            GameObject markers = presentation.NewUi("Trigger Order Markers", chainOverlayRoot.transform);
            presentation.Stretch(markers.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            chainMarkerRoot = markers.transform;

            chainOverlayRoot.SetActive(false);
        }

private void BuildReelOverlay(Transform boardContainer)
        {
            reelOverlayRoot = NewUi("ReelOverlay", boardContainer);
            Stretch(reelOverlayRoot.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            reelColumns = new ReelColumnView[BoardColumns];

            BuildReelGridOverlay();

            for (int column = 0; column < BoardColumns; column++)
            {
                GameObject viewport = NewUi("Reel " + column, reelOverlayRoot.transform);
                RectTransform viewportRect = viewport.GetComponent<RectTransform>();
                AnchorRect(viewportRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(BoardPadding + column * BoardColumnPitch, -BoardPadding),
                    new Vector2(BoardCellSize, ReelViewportHeight));

                Image viewportBackground = viewport.AddComponent<Image>();
                viewportBackground.color = Color.clear;
                viewportBackground.raycastTarget = false;
                viewport.AddComponent<RectMask2D>();

                GameObject stripObject = NewUi("Strip", viewport.transform);
                RectTransform stripRect = stripObject.GetComponent<RectTransform>();
                AnchorRect(stripRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 1f), Vector2.zero,
                    new Vector2(BoardCellSize, ReelSymbolCapacity * BoardRowPitch));

                ReelColumnView reel = new ReelColumnView
                {
                    Root = viewport,
                    Strip = stripRect,
                    Symbols = new ReelSymbolView[ReelSymbolCapacity]
                };

                for (int index = 0; index < ReelSymbolCapacity; index++)
                {
                    GameObject slot = NewUi("Reel Symbol " + index, stripObject.transform);
                    RectTransform slotRect = slot.GetComponent<RectTransform>();
                    AnchorRect(slotRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(0f, 1f), new Vector2(0f, -index * BoardRowPitch),
                        new Vector2(BoardCellSize, BoardCellSize));

                    Image slotBackground = slot.AddComponent<Image>();
                    slotBackground.color = Color.clear;
                    slotBackground.raycastTarget = false;

                    GameObject iconObject = NewUi("Icon", slot.transform);
                    RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                    Stretch(iconRect, 10f, 10f, 10f, 10f);
                    Image icon = iconObject.AddComponent<Image>();
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    ApplyPawnIconEffects(icon, true);

                    TMP_Text fallback = MakeText(string.Empty, slot.transform, 28, Color.white,
                        TextAnchor.MiddleCenter);
                    fallback.fontStyle = FontStyles.Bold;
                    Stretch(fallback.rectTransform, 10f, 10f, 10f, 10f);

                    reel.Symbols[index] = new ReelSymbolView
                    {
                        Root = slot,
                        Rect = slotRect,
                        Icon = icon,
                        Fallback = fallback
                    };
                }

                GameObject motionTintObject = NewUi("Motion Tint", viewport.transform);
                Stretch(motionTintObject.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
                reel.MotionTint = motionTintObject.AddComponent<Image>();
                reel.MotionTint.color = new Color(0.95f, 0.84f, 0.62f, 0f);
                reel.MotionTint.raycastTarget = false;

                GameObject blurObject = NewUi("Motion Blur", viewport.transform);
                Stretch(blurObject.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
                reel.BlurOverlay = blurObject.AddComponent<Image>();
                reel.BlurOverlay.sprite = presentation.VerticalFadeSprite;
                reel.BlurOverlay.type = Image.Type.Simple;
                reel.BlurOverlay.color = new Color(1f, 0.88f, 0.66f, 0f);
                reel.BlurOverlay.raycastTarget = false;

                GameObject topShade = NewUi("Top Shade", viewport.transform);
                AnchorRect(topShade.GetComponent<RectTransform>(),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, new Vector2(BoardCellSize, 84f));
                Image topShadeImage = topShade.AddComponent<Image>();
                topShadeImage.sprite = presentation.VerticalFadeSprite;
                topShadeImage.type = Image.Type.Simple;
                topShadeImage.color = new Color(0.06f, 0.04f, 0.03f, 0.48f);
                topShadeImage.raycastTarget = false;

                GameObject bottomShade = NewUi("Bottom Shade", viewport.transform);
                AnchorRect(bottomShade.GetComponent<RectTransform>(),
                    Vector2.zero, Vector2.zero, Vector2.zero,
                    Vector2.zero, new Vector2(BoardCellSize, 84f));
                Image bottomShadeImage = bottomShade.AddComponent<Image>();
                bottomShadeImage.sprite = presentation.VerticalFadeSprite;
                bottomShadeImage.type = Image.Type.Simple;
                bottomShadeImage.rectTransform.localScale = new Vector3(1f, -1f, 1f);
                bottomShadeImage.color = new Color(0.06f, 0.04f, 0.03f, 0.48f);
                bottomShadeImage.raycastTarget = false;

                reelColumns[column] = reel;
            }

            reelOverlayRoot.SetActive(false);
        }

        private void BuildReelGridOverlay()
        {
            GameObject gridOverlay = NewUi("Fixed Reel Grid", reelOverlayRoot.transform);
            Stretch(gridOverlay.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            for (int index = 0; index < BoardSize; index++)
            {
                int row = index / BoardColumns;
                int column = index % BoardColumns;
                GameObject cell = NewUi("Fixed Reel Cell " + index, gridOverlay.transform);
                RectTransform cellRect = cell.GetComponent<RectTransform>();
                AnchorRect(cellRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(
                        BoardPadding + column * BoardColumnPitch,
                        -BoardPadding - row * BoardRowPitch),
                    new Vector2(BoardCellSize, BoardCellSize));

                Image cellImage = cell.AddComponent<Image>();
                cellImage.color = Color.clear;
                cellImage.raycastTarget = false;
            }
        }



        private void PrepareReels(IList<Element> previousBoard, IList<Element> finalBoard)
        {
            for (int column = 0; column < BoardColumns; column++)
            {
                ReelColumnView reel = reelColumns[column];
                int travelSlots = ReelBaseTravelSlots + column * ReelExtraTravelSlots;
                int sequenceCount = travelSlots + BoardRows;
                System.Random random = new System.Random(unchecked(
                    (round + 1) * 48611 + column * 7919 + pool.Count * 101));

                for (int index = 0; index < ReelSymbolCapacity; index++)
                {
                    ReelSymbolView symbol = reel.Symbols[index];
                    bool active = index < sequenceCount;
                    symbol.Root.SetActive(active);
                    if (!active) continue;

                    Element element;
                    if (index < BoardRows)
                    {
                        element = BoardElementAt(previousBoard, index * BoardColumns + column);
                    }
                    else if (index >= travelSlots)
                    {
                        int finalRow = index - travelSlots;
                        element = BoardElementAt(finalBoard, finalRow * BoardColumns + column);
                    }
                    else
                    {
                        element = PreviewReelElement(random);
                    }

                    SetReelSymbol(symbol, element);
                }

                SetStripY(reel.Strip, 0f);
                reel.Root.transform.localScale = Vector3.one;
                SetReelMotion(reel, 0f);
            }
        }

        private static Element BoardElementAt(IList<Element> source, int index)
        {
            return source != null && index >= 0 && index < source.Count ? source[index] : null;
        }

        private static bool AnyReelRunning(IList<bool> finished)
        {
            for (int i = 0; i < finished.Count; i++)
            {
                if (!finished[i]) return true;
            }
            return false;
        }

        private void SetReelSymbol(ReelSymbolView view, Element element)
        {
            view.Root.name = element == null ? "空位" : element.Name;
            Sprite sprite = LoadElementSprite(element);
            bool hasSprite = sprite != null;
            view.Icon.enabled = hasSprite;
            view.Icon.sprite = sprite;
            view.Icon.color = Color.white;
            view.Icon.rectTransform.localScale = Vector3.one;
            view.Icon.rectTransform.anchoredPosition = Vector2.zero;
            view.VisualScale = hasSprite
                ? NormalizePawnVisual(view.Icon,
                    Vector2.one * (BoardCellSize - 20f),
                    BoardIconSize * UiValue("ui_pawn_visual_fill_ratio"),
                    Vector2.zero)
                : 1f;
            view.Fallback.gameObject.SetActive(element != null && !hasSprite);
            view.Fallback.text = element == null ? string.Empty : ShortIcon(element.Key);
            view.Fallback.color = Color.white;
        }

        private Sprite LoadElementSprite(Element element)
        {
            if (element == null || element.Def == null || string.IsNullOrEmpty(element.Def.Asset))
            {
                return null;
            }

            return LoadConfiguredSprite(element.Def.Asset, element.Key);
        }

        private Sprite LoadConfiguredSprite(string asset, string ownerKey)
        {
            if (string.IsNullOrWhiteSpace(asset)) return null;

            Sprite sprite;
            if (spriteCache.TryGetValue(asset, out sprite) && sprite != null)
            {
                return sprite;
            }

            sprite = Resources.Load<Sprite>("CatCafe/" + asset);
            if (sprite != null)
            {
                spriteCache[asset] = sprite;
                missingSpriteWarnings.Remove(asset);
                return sprite;
            }

            // Do not cache a miss: the Editor may still be importing a newly replaced asset.
            spriteCache.Remove(asset);
            if (missingSpriteWarnings.Add(asset))
            {
                Debug.LogError("[CatCafeUI] 配置资源加载失败：" + ownerKey + " → Resources/CatCafe/" + asset);
            }
            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器中替换棋子图片后，Resources 已导入新纹理，但当前 Play 会话仍可能持有旧
        /// Sprite 和旧的 Tight Mesh 归一化结果。由 CatCafePawnImporter 在导入完成后调用，
        /// 只刷新表现，不改变棋盘、配置或玩法状态。
        /// </summary>
        public void RefreshPawnSpritesAfterAssetImport()
        {
            spriteCache.Clear();
            missingSpriteWarnings.Clear();
            if (!isActiveAndEnabled || boardRoot == null) return;
            RenderBoard();
        }
#endif

        private static void SetStripY(RectTransform strip, float y)
        {
            Vector2 position = strip.anchoredPosition;
            position.y = y;
            strip.anchoredPosition = position;
        }

        private void SetReelMotion(ReelColumnView reel, float speed)
        {
            float normalizedSpeed = Mathf.Clamp01(speed);
            float motionStretch = 1f + normalizedSpeed * 0.18f;
            float motionAlpha = Mathf.Lerp(1f, 0.72f, normalizedSpeed);
            float viewportCenterY = -ReelViewportHeight * 0.5f;
            float viewportRadius = ReelViewportHeight * 0.5f;

            for (int i = 0; i < reel.Symbols.Length; i++)
            {
                ReelSymbolView symbol = reel.Symbols[i];
                if (!symbol.Root.activeSelf) continue;

                float symbolCenterY = reel.Strip.anchoredPosition.y +
                    symbol.Rect.anchoredPosition.y - BoardCellSize * 0.5f;
                float edge = Mathf.Clamp01(
                    Mathf.Abs(symbolCenterY - viewportCenterY) / viewportRadius);
                float edgeCurve = edge * edge;
                float cylinderScale = Mathf.Lerp(1f, 0.90f, edgeCurve);
                float cylinderAlpha = Mathf.Lerp(1f, 0.78f, edgeCurve);
                Vector3 scale = new Vector3(
                    symbol.VisualScale * cylinderScale,
                    symbol.VisualScale * cylinderScale * motionStretch, 1f);

                symbol.Icon.rectTransform.localScale = scale;
                symbol.Fallback.rectTransform.localScale = scale;

                Color iconColor = symbol.Icon.color;
                iconColor.a = motionAlpha * cylinderAlpha;
                symbol.Icon.color = iconColor;

                Color textColor = symbol.Fallback.color;
                textColor.a = motionAlpha * cylinderAlpha;
                symbol.Fallback.color = textColor;
            }

            Color tint = reel.MotionTint.color;
            tint.a = normalizedSpeed * 0.10f;
            reel.MotionTint.color = tint;

            if (reel.BlurOverlay != null)
            {
                Color blur = reel.BlurOverlay.color;
                blur.a = normalizedSpeed * 0.26f;
                reel.BlurOverlay.color = blur;
            }
        }

        private static float ReelTravelProgress(float normalizedTime)
        {
            const float acceleration = 0.20f;
            const float deceleration = 0.34f;
            const float cruise = 1f - acceleration - deceleration;
            float area = acceleration * 0.5f + cruise + deceleration * 0.5f;
            float time = Mathf.Clamp01(normalizedTime);

            if (time < acceleration)
            {
                float t = time / acceleration;
                float integral = t * t * t - 0.5f * t * t * t * t;
                return acceleration * integral / area;
            }

            if (time < acceleration + cruise)
            {
                return (acceleration * 0.5f + time - acceleration) / area;
            }

            float decelerationTime = (time - acceleration - cruise) / deceleration;
            float decelerationIntegral = decelerationTime
                - decelerationTime * decelerationTime * decelerationTime
                + 0.5f * decelerationTime * decelerationTime *
                decelerationTime * decelerationTime;
            return (acceleration * 0.5f + cruise +
                deceleration * decelerationIntegral) / area;
        }

        private static float ReelTravelVelocity(float normalizedTime)
        {
            const float acceleration = 0.20f;
            const float deceleration = 0.34f;
            const float cruiseEnd = 1f - deceleration;
            float time = Mathf.Clamp01(normalizedTime);

            if (time < acceleration)
            {
                return SmoothStep01(time / acceleration);
            }

            if (time < cruiseEnd)
            {
                return 1f;
            }

            return 1f - SmoothStep01((time - cruiseEnd) / deceleration);
        }

        private static float SmoothStep01(float value)
        {
            float time = Mathf.Clamp01(value);
            return time * time * (3f - 2f * time);
        }

        private GameObject CreateToken(Transform parent, Element element)
        {
            GameObject token = NewUi(element.Name, parent);
            RectTransform tokenRect = token.GetComponent<RectTransform>();
            AnchorRect(tokenRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(BoardIconSize, BoardIconSize));
            GameObject artworkObject = NewUi("Artwork", token.transform);
            RectTransform artworkRect = artworkObject.GetComponent<RectTransform>();
            Stretch(artworkRect, 0f, 0f, 0f, 0f);
            Image image = artworkObject.AddComponent<Image>();
            ApplyPawnIconEffects(image, true);
            Sprite sprite = LoadElementSprite(element);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.preserveAspect = true;
                NormalizePawnVisual(image, Vector2.one * BoardIconSize,
                    BoardIconSize * UiValue("ui_pawn_visual_fill_ratio"), Vector2.zero);
            }
            else
            {
                image.color = TokenColor(element.Key);
                TMP_Text icon = MakeText(ShortIcon(element.Key), token.transform, 24, Color.white, TextAnchor.MiddleCenter);
                Stretch(icon.rectTransform, 0, 0, 0, 0);
            }
            Button button = token.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(delegate { ShowCardDetail(element, true); });
            LayoutElement layout = token.AddComponent<LayoutElement>();
            layout.minWidth = 54;
            layout.minHeight = 54;
            return token;
        }

        /// <summary>
        /// Keeps the source artwork's own silhouette. Board pawns also receive a restrained
        /// left/down cast shadow so they sit above the paper slots instead of looking printed
        /// directly onto them.
        /// </summary>
        private static void ApplyPawnIconEffects(Image image, bool onBoard)
        {
            if (image == null) return;

            // 棋子原图已经包含自己的边缘处理；再叠一层 Unity Outline 会在缩小时形成毛边。
            // 旧实例若已有该组件则明确关闭，避免对象复用时残留描边。
            Outline outline = image.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;

            Shadow shadow = null;
            Shadow[] effects = image.GetComponents<Shadow>();
            for (int i = 0; i < effects.Length; i++)
            {
                // Outline derives from Shadow, so GetComponent<Shadow>() can return the outline.
                // Keep a distinct base Shadow component or the shadow settings overwrite the white edge.
                if (effects[i] != null && effects[i].GetType() == typeof(Shadow))
                {
                    shadow = effects[i];
                    break;
                }
            }
            if (shadow == null) shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = onBoard
                ? new Color(0.18f, 0.10f, 0.055f, 0.30f)
                : new Color(0.18f, 0.10f, 0.055f, 0.24f);
            shadow.effectDistance = onBoard
                ? new Vector2(-3f, -2f)
                : new Vector2(-2f, -1.5f);
            shadow.useGraphicAlpha = true;
            shadow.enabled = true;
        }

        /// <summary>
        /// 按 Sprite 的 Tight Mesh 可见轮廓统一棋子尺寸，并把透明留白造成的视觉偏心校正回容器中心。
        /// 源图不裁切、不缩放；这里只调整运行时 Image 子节点。
        /// </summary>
        private static float NormalizePawnVisual(Image image, Vector2 containerSize,
            float targetVisibleSize, Vector2 baseAnchoredPosition)
        {
            if (image == null || image.sprite == null) return 1f;

            Sprite sprite = image.sprite;
            image.useSpriteMesh = true;
            image.preserveAspect = true;

            float pixelsPerUnit = Mathf.Max(0.0001f, sprite.pixelsPerUnit);
            Vector2[] vertices = sprite.vertices;
            Vector2 visibleMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 visibleMaxVertex = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; vertices != null && i < vertices.Length; i++)
            {
                visibleMin = Vector2.Min(visibleMin, vertices[i]);
                visibleMaxVertex = Vector2.Max(visibleMaxVertex, vertices[i]);
            }
            bool hasTightVertices = vertices != null && vertices.Length >= 3;
            Vector2 visibleSizeUnits = hasTightVertices
                ? visibleMaxVertex - visibleMin
                : new Vector2(sprite.bounds.size.x, sprite.bounds.size.y);
            Vector2 visibleCenterUnits = hasTightVertices
                ? (visibleMin + visibleMaxVertex) * 0.5f
                : new Vector2(sprite.bounds.center.x, sprite.bounds.center.y);
            Vector2 visiblePixels = visibleSizeUnits * pixelsPerUnit;
            float sourceWidth = Mathf.Max(1f, sprite.rect.width);
            float sourceHeight = Mathf.Max(1f, sprite.rect.height);
            float fitScale = Mathf.Min(containerSize.x / sourceWidth, containerSize.y / sourceHeight);
            float visibleExtent = Mathf.Max(visiblePixels.x, visiblePixels.y) * fitScale;
            if (visibleExtent <= 0.001f) return 1f;

            float scale = Mathf.Clamp(targetVisibleSize / visibleExtent,
                UiValue("ui_pawn_visual_scale_min"), UiValue("ui_pawn_visual_scale_max"));
            image.rectTransform.localScale = Vector3.one * scale;

            Vector2 fullCenterFromPivotPixels = new Vector2(
                sourceWidth * 0.5f - sprite.pivot.x,
                sourceHeight * 0.5f - sprite.pivot.y);
            Vector2 visibleCenterFromPivotPixels = visibleCenterUnits * pixelsPerUnit;
            Vector2 visualOffset =
                (visibleCenterFromPivotPixels - fullCenterFromPivotPixels) * fitScale * scale;
            image.rectTransform.anchoredPosition = baseAnchoredPosition - visualOffset;
            return scale;
        }

        // reward-card 美术是 375×600（宽高比 0.625）。卡片按同一比例摆，
        // 否则整张卡面会被横向拉伸，槽位也跟着错位。
        /// <summary>
        /// 三选一面板的基准尺寸。三张卡时的实际占宽是
        /// 28×2 + 225×3 + 20×2 = 771，小于内容轨 916，所以三张时按基准值走、像素不变；
        /// 持有「预约名册」变成四张时需要 1016，必须把轨道和面板一起加宽。
        /// </summary>
        private static float ChoiceContentBaseWidth { get { return UiValue("ui_choice_rail_base_width"); } }
        private static float ChoicePanelSidePadding { get { return UiValue("ui_choice_panel_side_padding"); } }
        private static float ChoicePanelBaseWidth { get { return ChoiceContentBaseWidth + ChoicePanelSidePadding; } }
        private static float ChoiceRailPadding { get { return UiValue("ui_choice_rail_padding"); } }
        private static float ChoiceCardSpacing { get { return UiValue("ui_choice_card_spacing"); } }
        /// <summary>面板最宽不超过它，再多的卡片改为压缩卡宽，避免顶出 1536 设计区。</summary>
        private static float ChoicePanelMaxWidth { get { return UiValue("ui_choice_panel_max_width"); } }

        private static float CardWidth { get { return UiValue("ui_choice_card_width"); } }
        private static float CardHeight { get { return UiValue("ui_choice_card_height"); } }

        /// <summary>按卡面美术的归一化坐标摆放子元素，y 自上而下。</summary>
        private static void PlaceOnCard(RectTransform rect, float left, float top, float right, float bottom)
        {
            if (rect == null) return;

            rect.anchorMin = new Vector2(left, 1f - bottom);
            rect.anchorMax = new Vector2(right, 1f - top);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

private Button CreateCard(Transform parent, string title, Rarity rarity, string rule, string asset, string key,
            bool useChoiceLedgerSkin)
        {
            GameObject cardObject = NewUi(title, parent);
            Color cardFill = UiColor("ui_choice_card_" + RarityKey(rarity) + "_fill");
            Color rarityColor = BuffRarityColor(rarity);
            Image background = cardObject.AddComponent<Image>();
            if (useChoiceLedgerSkin)
            {
                presentation.ApplyNamedSkin(background,
                    CatCafeConfigDatabase.GetRequiredString("ui_choice_card_skin"), Color.white);
            }
            else
            {
                ApplySurface(background, PaperSurface.RewardCard, cardFill);
            }
            background.raycastTarget = true;

            // 稀有度用配置表 rarities.color 上到描边，卡片一眼能分级，不必去读那行小字。
            Outline cardOutline = cardObject.GetComponent<Outline>();
            if (cardOutline != null)
            {
                cardOutline.effectColor = new Color(
                    rarityColor.r * 0.55f, rarityColor.g * 0.55f, rarityColor.b * 0.55f, 0.95f);
                cardOutline.effectDistance = new Vector2(3f, -3f);
            }

            // 入场错开与选中演出都靠它，布局组不会碰 alpha 和 localScale。
            cardObject.AddComponent<CanvasGroup>();

            Button button = cardObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            LayoutElement layout = cardObject.AddComponent<LayoutElement>();
            layout.minWidth = CardWidth;
            layout.preferredWidth = CardWidth;
            layout.minHeight = CardHeight;
            layout.preferredHeight = CardHeight;

            // 卡面各槽位由 reward-card 美术决定，直接按归一化坐标贴上去。
            // 之前用 VerticalLayoutGroup 顺序堆叠，元素整体错了一格：名字挤在图框下面，
            // 规则文字压在名字槽上。改成对位摆放后，换卡片尺寸也不会再错位。
            GameObject iconObject = NewUi("Icon", cardObject.transform);
            PlaceOnCard(iconObject.GetComponent<RectTransform>(), 0.10f, 0.085f, 0.90f, 0.545f);
            Image iconFrame = iconObject.AddComponent<Image>();
            // 稀有度颜色只落在图标展示框，文字和下半部仍用纸卡承载；
            // 与参考卡的分层一致，颜色更醒目也不会影响规则阅读。
            PixelFrame(iconFrame, cardFill);
            iconFrame.raycastTarget = false;

            Sprite sprite = LoadConfiguredSprite(asset, key);
            if (sprite != null)
            {
                GameObject artworkObject = NewUi("Artwork", iconObject.transform);
                Image artwork = artworkObject.AddComponent<Image>();
                artwork.sprite = sprite;
                artwork.type = Image.Type.Simple;
                artwork.preserveAspect = true;
                artwork.raycastTarget = false;
                ApplyPawnIconEffects(artwork, false);
                Stretch(artwork.rectTransform, 6, 6, 6, 6);
                Vector2 artworkSize = new Vector2(
                    CardWidth * 0.80f - 12f,
                    CardHeight * (0.545f - 0.085f) - 12f);
                NormalizePawnVisual(artwork, artworkSize,
                    Mathf.Min(artworkSize.x, artworkSize.y) * UiValue("ui_pawn_visual_fill_ratio"),
                    Vector2.zero);
            }
            else
            {
                TMP_Text iconText = MakeText(ShortIcon(key), iconObject.transform,
                    CatCafeConfigDatabase.GetRequiredInt("ui_choice_card_icon_fallback_font_size"),
                    Color.white, TextAnchor.MiddleCenter);
                Stretch(iconText.rectTransform, 0, 0, 0, 0);
            }

            // 稀有度徽章素材由 Rarities 表的 badge 列指定；没有素材就退回配置表颜色的色带。
            GameObject rarityBand = NewUi("RarityBand", cardObject.transform);
            PlaceOnCard(rarityBand.GetComponent<RectTransform>(), 0.16f,
                UiValue("ui_choice_rarity_top"), 0.84f, UiValue("ui_choice_rarity_bottom"));
            Image bandImage = rarityBand.AddComponent<Image>();
            Sprite badge = presentation.RarityBadgeSprite(RarityKey(rarity));
            bool hasBadge = badge != null;
            if (hasBadge)
            {
                bandImage.sprite = badge;
                bandImage.type = badge.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                bandImage.color = Color.white;
            }
            else
            {
                bandImage.sprite = presentation.PanelSprite;
                bandImage.type = Image.Type.Sliced;
                bandImage.color = rarityColor;
            }
            bandImage.raycastTarget = false;
            TMP_Text rarityText = MakeText(RarityLabel(rarity), rarityBand.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_choice_card_rarity_font_size"),
                hasBadge ? new Color(1f, 0.95f, 0.86f, 1f) : ReadableInk(rarityColor),
                TextAnchor.MiddleCenter);
            rarityText.fontStyle = FontStyles.Bold;
            Stretch(rarityText.rectTransform, 12, 7, 12, 7);

            // 道具名放在原来那块紫色牌子的位置——牌子已从卡面美术里去掉，
            // 两侧的装饰线正好把名字框住。
            TMP_Text nameText = MakeText(title, cardObject.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_choice_card_name_font_size"),
                new Color(0.28f, 0.17f, 0.11f), TextAnchor.MiddleCenter);
            nameText.fontStyle = FontStyles.Bold;
            PlaceOnCard(nameText.rectTransform, 0.10f, UiValue("ui_choice_name_top"),
                0.90f, UiValue("ui_choice_name_bottom"));

            TMP_Text ruleText = MakeText(FormatRuleWithSymbolLinks(rule), cardObject.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_choice_card_rule_font_size"),
                new Color(0.34f, 0.26f, 0.19f), TextAnchor.UpperCenter);
            ApplyPieceIconAtlas(ruleText);
            PlaceOnCard(ruleText.rectTransform, 0.09f, UiValue("ui_choice_rule_top"),
                0.91f, UiValue("ui_choice_rule_bottom"));
            CatCafeTextLinkHandler ruleLinks =
                ruleText.gameObject.AddComponent<CatCafeTextLinkHandler>();
            // 只有链接文字会生成独立按钮；普通规则文字不拦截射线，仍由整张卡确认选择。
            ruleLinks.Initialize(ruleText, HandleSymbolReferenceLink);
            return button;
        }

        private void BuildUi()
        {
            // v2 分层皮肤（游戏界面更新2）：背景/账本/目标横幅/金币挂件都是
            // 1536×864 全画布对齐图层，直接整层铺，不再走单张合成图 + 裁剪定位。
            bool hasLayeredSkin =
                Resources.Load<Sprite>("CatCafe/InGameUI/background-v2") != null &&
                Resources.Load<Sprite>("CatCafe/InGameUI/book-v2") != null;
            if (!hasLayeredSkin)
            {
                Debug.LogWarning("CatCafe paper UI skin is missing; using the legacy runtime UI.");
                BuildLegacyUi();
                return;
            }

            if (presentation == null)
            {
                presentation = GetComponent<CatCafePresentation>();
                if (presentation == null)
                {
                    presentation = gameObject.AddComponent<CatCafePresentation>();
                }

                presentation.Initialize();
            }

            uiFont = presentation.UiFont;

            GameObject canvasObject = NewUi("CatCafePaperCanvas", transform);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;
            canvas.pixelPerfect = false;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1536f, 864f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject fallbackBackground = NewUi("Fallback Background", canvas.transform);
            Image fallbackImage = fallbackBackground.AddComponent<Image>();
            fallbackImage.color = new Color(0.17f, 0.13f, 0.11f, 1f);
            fallbackImage.raycastTarget = false;
            Stretch(fallbackBackground.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            // 设计分辨率根：美术整图铺满画布时会跟屏幕比例一起拉伸，而棋盘等元素用的
            // 是 1536×864 的固定坐标——全屏/改分辨率后两者就会错位（棋子看着从格子上
            // 移开）。把两层都挂进恒为 1536×864、整体等比缩放的根节点，坐标系就锁死了。
            GameObject designRootObject = NewUi("Design Root", canvas.transform);
            CatCafeDesignRootFitter designFitter =
                designRootObject.AddComponent<CatCafeDesignRootFitter>();
            designFitter.Configure(scaler.referenceResolution);
            Transform designRoot = designRootObject.transform;

            GameObject artLayer = NewUi("Paper Art Layer", designRoot);
            Stretch(artLayer.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            CreateSkinArt(artLayer.transform, "Background", "background-v3");
            CreateSkinArt(artLayer.transform, "Book", "book-v3");
            CreateSkinArt(artLayer.transform, "Wave Round Sign", "round-sign-v3");
            CreateSkinArt(artLayer.transform, "Business Goal Banner", "goal-banner-v2");
            CreateSkinArt(artLayer.transform, "Paw Coin", "coin-hud-v2");
            // v2 皮肤没画开始营业按钮与设置齿轮，沿用旧版独立贴纸层补位。
            CreateSkinArt(artLayer.transform, "Start Business Button", "start-button");
            CreateSkinArt(artLayer.transform, "Settings", "settings-v3");

            // 旧偏移是为了对齐合成图里"画得略歪"的按钮；v2 皮肤下可见层就是同一张
            // 贴纸，反馈层必须零偏移完全重叠，否则按下高亮会错位。
            RectTransform startButtonVisual = CreateInteractiveSkinArt(artLayer.transform,
                "Start Business Button Feedback", "start-button", Vector2.zero);
            RectTransform settingsVisual = CreateInteractiveSkinArt(artLayer.transform,
                "Settings Feedback", "settings", Vector2.zero);

            GameObject gameplayLayer = NewUi("Gameplay Layer", designRoot);
            Stretch(gameplayLayer.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            BuildPaperHud(gameplayLayer.transform);
            BuildBuffPanel(gameplayLayer.transform);
            BuildPieceBox(gameplayLayer.transform);

            GameObject boardObject = NewUi("Board", gameplayLayer.transform);
            RectTransform boardRect = boardObject.GetComponent<RectTransform>();
            PlaceTopLeft(boardRect, UiValue("ui_board_x"), UiValue("ui_board_y"),
                BoardLayoutWidth, BoardLayoutHeight);

            GameObject boardCellsObject = NewUi("Cells", boardObject.transform);
            Stretch(boardCellsObject.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            boardRoot = boardCellsObject.transform;
            GridLayoutGroup grid = boardCellsObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset((int)BoardPadding, (int)BoardPadding,
                (int)BoardPadding, (int)BoardPadding);
            grid.spacing = new Vector2(BoardCellSpacingX, BoardCellSpacingY);
            grid.cellSize = new Vector2(BoardCellSize, BoardCellSize);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = BoardColumns;
            grid.childAlignment = TextAnchor.MiddleCenter;
            BuildReelOverlay(boardObject.transform);
            BuildChainOverlay(boardObject.transform);

            // 美术按钮本身是整屏透明图层；单独铺设命中区域，避免透明部分拦截点击。
            rollButton = CreateHotspot(gameplayLayer.transform, "开始营业", RollRound,
                UiTopLeft("ui_start_hotspot"), UiSize("ui_start_hotspot"));
            CreateHotspot(gameplayLayer.transform, "营业帮助", ShowHelpPlaceholder,
                UiTopLeft("ui_help_hotspot"), UiSize("ui_help_hotspot"));
            Button settingsButton = CreateHotspot(gameplayLayer.transform, "营业菜单", ShowSettings,
                new Vector2(1390f, 0f), new Vector2(146f, 132f));
            AddImageButtonFeedback(rollButton, startButtonVisual);
            AddImageButtonFeedback(settingsButton, settingsVisual);

            toastText = MakeText(string.Empty, gameplayLayer.transform,
                Mathf.RoundToInt(UiValue("ui_toast_font_size")),
                UiColor("ui_toast_color"), TextAnchor.MiddleCenter);
            toastText.fontStyle = FontStyles.Bold;
            // v2 皮肤底部没有提示缎带，走"亮色文字直接浮在画面上"，描边保证木桌上可读。
            Outline toastOutline = toastText.gameObject.AddComponent<Outline>();
            toastOutline.effectColor = new Color(0.10f, 0.07f, 0.05f, 0.92f);
            toastOutline.effectDistance = new Vector2(2f, -2f);
            PlaceConfigured(toastText.rectTransform, "ui_toast");

            chainSequenceText = MakeText(string.Empty, gameplayLayer.transform, 15,
                new Color(0.45f, 0.25f, 0.12f), TextAnchor.MiddleCenter);
            chainSequenceText.fontStyle = FontStyles.Bold;
            Outline chainOutline = chainSequenceText.gameObject.AddComponent<Outline>();
            chainOutline.effectColor = new Color(1f, 0.88f, 0.62f, 0.95f);
            chainOutline.effectDistance = new Vector2(2f, -2f);
            PlaceTopLeft(chainSequenceText.rectTransform, 460f, 711f, 560f, 36f);
            chainSequenceText.gameObject.SetActive(false);

            interactionFeedback = GetComponent<CatCafeInteractionFeedback>();
            if (interactionFeedback == null)
            {
                interactionFeedback = gameObject.AddComponent<CatCafeInteractionFeedback>();
            }

            interactionFeedback.Initialize(canvas, moneyHudRect, moneyCoinTarget,
                goalText, uiFont, toastText);
            BuildChoiceOverlay();
            BuildItemOverlay();
            BuildResultOverlay();
            BuildSettingsOverlay();
            BuildConfirmOverlay();
            BuildCardDetailOverlay();
            interactionFeedback.RegisterButtons(canvas.GetComponentsInChildren<Button>(true));
        }

        private void BuildLegacyUi()
        {
            if (presentation == null)
            {
                presentation = GetComponent<CatCafePresentation>();
                if (presentation == null)
                {
                    presentation = gameObject.AddComponent<CatCafePresentation>();
                }

                presentation.Initialize();
            }

            uiFont = presentation.UiFont;

            GameObject canvasObject = NewUi("CatCafeHtmlPortCanvas", transform);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject background = NewUi("Background", canvas.transform);
            Image bgImage = background.AddComponent<Image>();
            bgImage.color = new Color(0.17f, 0.13f, 0.11f, 1f);
            bgImage.raycastTarget = false;
            CatCafeBackdrop backdrop = background.AddComponent<CatCafeBackdrop>();
            backdrop.Initialize(bgImage);
            Stretch(background.GetComponent<RectTransform>(), 0, 0, 0, 0);

            GameObject hud = NewUi("HUD", canvas.transform);
            RectTransform hudRect = hud.GetComponent<RectTransform>();
            AnchorRect(hudRect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(-24f, 54f));
            HorizontalLayoutGroup hudLayout = hud.AddComponent<HorizontalLayoutGroup>();
            hudLayout.spacing = 8;
            hudLayout.padding = new RectOffset(0, 0, 0, 0);
            hudLayout.childAlignment = TextAnchor.MiddleCenter;
            hudLayout.childControlWidth = false;
            hudLayout.childControlHeight = true;
            hudLayout.childForceExpandWidth = false;
            hudLayout.childForceExpandHeight = true;
            AddHudBlock(hud.transform, "金币", out moneyText);
            AddHudBlock(hud.transform, "天数", out stageText);
            AddHudBlock(hud.transform,
                CatCafeConfigDatabase.GetRequiredString("ui_run_goal_caption"),
                out goalText, out goalCaption);
            AddHudBlock(hud.transform, "波次", out roundText);

            GameObject machine = NewUi("Machine", canvas.transform);
            RectTransform machineRect = machine.GetComponent<RectTransform>();
            AnchorRect(machineRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(790f, 690f));
            Image machineImage = machine.AddComponent<Image>();
            PixelFrame(machineImage, new Color(0.43f, 0.30f, 0.22f, 1f));
            machineImage.raycastTarget = false;

            GameObject titlePlate = NewUi("MachineTitle", machine.transform);
            RectTransform titleRect = titlePlate.GetComponent<RectTransform>();
            AnchorRect(titleRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(205f, 38f));
            Image titleImage = titlePlate.AddComponent<Image>();
            PixelFrame(titleImage, new Color(0.94f, 0.88f, 0.75f, 1f));
            titleImage.raycastTarget = false;
            TMP_Text titleText = MakeText("猫 咖 营 业 台", titlePlate.transform, 14,
                new Color(0.29f, 0.20f, 0.15f), TextAnchor.MiddleCenter);
            titleText.fontStyle = FontStyles.Bold;
            Stretch(titleText.rectTransform, 0, 0, 0, 0);

            GameObject boardObject = NewUi("Board", machine.transform);
            RectTransform boardRect = boardObject.GetComponent<RectTransform>();
            AnchorRect(boardRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -70f),
                new Vector2(BoardLayoutWidth, BoardLayoutHeight));
            Image boardImage = boardObject.AddComponent<Image>();
            PixelFrame(boardImage, new Color(0.71f, 0.54f, 0.38f, 1f));
            boardImage.raycastTarget = false;
            GameObject boardCellsObject = NewUi("Cells", boardObject.transform);
            Stretch(boardCellsObject.GetComponent<RectTransform>(), 0, 0, 0, 0);
            boardRoot = boardCellsObject.transform;
            GridLayoutGroup grid = boardCellsObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset((int)BoardPadding, (int)BoardPadding, (int)BoardPadding, (int)BoardPadding);
            grid.spacing = new Vector2(BoardCellSpacingX, BoardCellSpacingY);
            grid.cellSize = new Vector2(BoardCellSize, BoardCellSize);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = BoardColumns;
            grid.childAlignment = TextAnchor.MiddleCenter;
            BuildReelOverlay(boardObject.transform);
            BuildChainOverlay(boardObject.transform);


            GameObject controls = NewUi("Controls", machine.transform);
            RectTransform controlsRect = controls.GetComponent<RectTransform>();
            AnchorRect(controlsRect, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-28f, 54f));
            HorizontalLayoutGroup controlsLayout = controls.AddComponent<HorizontalLayoutGroup>();
            controlsLayout.spacing = 8;
            controlsLayout.padding = new RectOffset(0, 0, 0, 0);
            controlsLayout.childAlignment = TextAnchor.MiddleCenter;
            controlsLayout.childControlWidth = true;
            controlsLayout.childControlHeight = true;
            controlsLayout.childForceExpandWidth = false;
            controlsLayout.childForceExpandHeight = true;
            rollButton = CreateButton(controls.transform, "营业", RollRound, 0f, 54f);
            rollButton.GetComponent<LayoutElement>().flexibleWidth = 1f;
            CreateButton(controls.transform,
                CatCafeConfigDatabase.GetRequiredString("ui_settings_end_run_label"),
                RequestEndRun, 110f, 54f);

            toastText = MakeText(string.Empty, canvas.transform, 16,
                new Color(0.94f, 0.84f, 0.55f), TextAnchor.MiddleCenter);
            toastText.fontStyle = FontStyles.Bold;
            Outline toastOutline = toastText.gameObject.AddComponent<Outline>();
            toastOutline.effectColor = new Color(0.08f, 0.06f, 0.05f, 0.9f);
            toastOutline.effectDistance = new Vector2(2f, -2f);
            RectTransform toastRect = toastText.rectTransform;
            AnchorRect(toastRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -244f), new Vector2(720f, 36f));
            chainSequenceText = MakeText(string.Empty, canvas.transform, 15,
                new Color(1f, 0.78f, 0.30f), TextAnchor.MiddleCenter);
            chainSequenceText.fontStyle = FontStyles.Bold;
            Outline chainOutline = chainSequenceText.gameObject.AddComponent<Outline>();
            chainOutline.effectColor = new Color(0.08f, 0.045f, 0.025f, 0.95f);
            chainOutline.effectDistance = new Vector2(2f, -2f);
            RectTransform chainTextRect = chainSequenceText.rectTransform;
            AnchorRect(chainTextRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(600f, 32f));
            chainSequenceText.gameObject.SetActive(false);


            interactionFeedback = GetComponent<CatCafeInteractionFeedback>();
            if (interactionFeedback == null)
            {
                interactionFeedback = gameObject.AddComponent<CatCafeInteractionFeedback>();
            }

            interactionFeedback.Initialize(canvas, moneyHudRect, moneyCoinTarget, moneyText, uiFont, toastText);
            BuildChoiceOverlay();
            BuildItemOverlay();
            BuildResultOverlay();
            BuildConfirmOverlay();
            BuildCardDetailOverlay();
            interactionFeedback.RegisterButtons(canvas.GetComponentsInChildren<Button>(true));
        }

        private GameObject CreateSkinArt(Transform parent, string objectName, string resourceName)
        {
            GameObject layer = NewUi(objectName, parent);
            Image image = layer.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("CatCafe/InGameUI/" + resourceName);
            image.color = Color.white;
            image.preserveAspect = false;
            image.raycastTarget = false;
            Stretch(layer.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            if (image.sprite == null)
            {
                Debug.LogWarning("Missing CatCafe in-game UI sprite: " + resourceName);
            }

            return layer;
        }

        private RectTransform CreateInteractiveSkinArt(Transform parent, string objectName,
            string resourceName, Vector2 authoredOffset)
        {
            GameObject layer = NewUi(objectName, parent);
            Image image = layer.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("CatCafe/InGameUI/" + resourceName);
            image.color = new Color(1f, 1f, 1f, 0f);
            image.preserveAspect = false;
            image.raycastTarget = false;

            Shader brightnessShader = Shader.Find(CatCafeImageButtonFeedback.BrightnessShaderName);
            if (brightnessShader != null)
            {
                if (imageButtonBrightnessMaterial == null)
                {
                    imageButtonBrightnessMaterial = new Material(brightnessShader)
                    {
                        name = "CatCafe Button Brightness (Runtime)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                image.material = imageButtonBrightnessMaterial;
            }
            else
            {
                Debug.LogWarning("[CatCafe] Missing UI/CatCafe Brightness shader.");
            }

            RectTransform rect = layer.GetComponent<RectTransform>();
            Stretch(rect, authoredOffset.x, authoredOffset.y, -authoredOffset.x, -authoredOffset.y);

            if (image.sprite == null)
            {
                Debug.LogWarning("[CatCafe] Missing interactive skin art: " + resourceName);
            }

            return rect;
        }

        private static void AddImageButtonFeedback(Button button, RectTransform visual)
        {
            if (button == null || visual == null) return;

            Graphic graphic = visual.GetComponent<Graphic>();
            if (graphic == null) return;

            CatCafeImageButtonFeedback feedback = button.gameObject.AddComponent<CatCafeImageButtonFeedback>();
            feedback.Initialize(visual, graphic);
        }

        private void BuildPaperHud(Transform parent)
        {
            Color ink = new Color(0.30f, 0.19f, 0.13f, 1f);

            // v2 横幅（goal-banner-v2）：左段空白纸面放"第 N 天"和目标标题，
            // 金币挂件画在 (1176,62)，金额进度落在挂件右侧的胶带区上。
            stageText = MakeText("第 1 天", parent, 24, ink, TextAnchor.MiddleCenter);
            stageText.fontStyle = FontStyles.Bold;
            PlaceTopLeft(stageText.rectTransform, 915f, 36f, 112f, 54f);

            goalCaption = MakeText(
                CatCafeConfigDatabase.GetRequiredString("ui_run_goal_caption"),
                parent, 21, ink, TextAnchor.MiddleCenter);
            goalCaption.fontStyle = FontStyles.Bold;
            PlaceTopLeft(goalCaption.rectTransform, 1018f, 38f, 118f, 50f);

            goalText = MakeText("0 / 35", parent, 23, ink, TextAnchor.MiddleCenter);
            goalText.fontStyle = FontStyles.Bold;
            PlaceTopLeft(goalText.rectTransform, 1206f, 36f, 130f, 54f);

            // moneyText remains the gameplay value source. The visible amount is folded into
            // goalText, leaving the left paper stack available for the paged Buff handbook.
            moneyText = MakeText("0", parent, 1, Color.clear, TextAnchor.MiddleCenter);
            PlaceTopLeft(moneyText.rectTransform, 1206f, 36f, 1f, 1f);
            moneyHudRect = goalText.rectTransform;
            moneyCoinTarget = goalText.rectTransform;

            // 回合信息放在画面左上角。v3 美术层的 round-sign-v3 已经画好了吊牌衬底，
            // 这里只摆字：ui_round 对准牌面上那片干净纸区（躲开顶部肉垫、左下毛线球、
            // 右下爪印），不再自己垫程序化纸签。
            roundText = MakeText("波次 0 / 5", parent, 21, ink, TextAnchor.MiddleCenter);
            roundText.fontStyle = FontStyles.Bold;
            PlaceConfigured(roundText.rectTransform, "ui_round");
        }

        private void BuildBuffPanel(Transform parent)
        {
            // v2 皮肤左页自带卷轴横幅，标题文字直接落在横幅上。
            TMP_Text buffTitle = MakeText(
                CatCafeConfigDatabase.GetString("ui_buff_panel_title", "营业道具"),
                parent, 20, new Color(0.30f, 0.19f, 0.13f, 1f), TextAnchor.MiddleCenter);
            buffTitle.fontStyle = FontStyles.Bold;
            // 卷轴缎带面（两个卷筒之间）实测 x 153-330、y 180-225，按其中心摆。
            PlaceTopLeft(buffTitle.rectTransform, 151f, 178f, 180f, 48f);

            GameObject list = NewUi("Buff Sticker List", parent);
            PlaceConfigured(list.GetComponent<RectTransform>(), "ui_buff_list");
            TiltBuff(list.GetComponent<RectTransform>());

            GameObject entries = NewUi("Buff Sticker Entries", list.transform);
            Stretch(entries.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            buffEntriesRoot = entries.transform;

            buffPageText = MakeText(UiString("ui_buff_page_empty"), list.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_buff_page_font_size"),
                UiColor("ui_buff_page_color"), TextAnchor.MiddleCenter);
            buffPageText.fontStyle = FontStyles.Bold;
            PlaceConfigured(buffPageText.rectTransform, "ui_buff_page");

            buffPreviousButton = CreateBuffPageButton(list.transform, "物件册上一页",
                UiString("ui_buff_previous_label"), "ui_buff_previous",
                delegate { ChangeBuffPage(-1); });
            buffNextButton = CreateBuffPageButton(list.transform, "物件册下一页",
                UiString("ui_buff_next_label"), "ui_buff_next",
                delegate { ChangeBuffPage(1); });

            RefreshBuffPanel();
        }

        private Button CreateBuffPageButton(Transform parent, string objectName, string label,
            string layoutPrefix, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = NewUi(objectName, parent);
            PlaceConfigured(buttonObject.GetComponent<RectTransform>(), layoutPrefix);
            Image background = buttonObject.AddComponent<Image>();
            background.color = UiColor("ui_buff_button_color");

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(1f, 1f, 1f,
                UiValue("ui_buff_button_disabled_alpha"));
            button.colors = colors;

            TMP_Text text = MakeText(label, buttonObject.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_buff_button_font_size"),
                Color.white, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
            Stretch(text.rectTransform, 0f, 0f, 0f, 2f);
            return button;
        }

        private void ChangeBuffPage(int direction)
        {
            int pageCount = Mathf.Max(1,
                Mathf.CeilToInt(ownedItems.Count / (float)BuffsPerPage));
            buffPage = Mathf.Clamp(buffPage + direction, 0, pageCount - 1);
            RefreshBuffPanel();
        }

        private void RefreshBuffPanel()
        {
            if (buffEntriesRoot == null) return;

            ClearChildren(buffEntriesRoot);
            int pageCount = Mathf.Max(1,
                Mathf.CeilToInt(ownedItems.Count / (float)BuffsPerPage));
            if (!string.IsNullOrEmpty(buffFocusKey))
            {
                int focusIndex = ownedItems.IndexOf(buffFocusKey);
                if (focusIndex >= 0) buffPage = focusIndex / BuffsPerPage;
                buffFocusKey = null;
            }

            buffPage = Mathf.Clamp(buffPage, 0, pageCount - 1);
            int first = buffPage * BuffsPerPage;
            int count = Mathf.Min(BuffsPerPage, Mathf.Max(0, ownedItems.Count - first));
            for (int slot = 0; slot < count; slot++)
            {
                string key = ownedItems[first + slot];
                ItemDefinition item;
                if (!itemDefs.TryGetValue(key, out item)) continue;
                CreateBuffEntry(item, slot, count);
            }

            bool hasMultiplePages = pageCount > 1;
            if (buffPageText != null)
            {
                buffPageText.text = ownedItems.Count == 0
                    ? UiString("ui_buff_page_empty")
                    : string.Format(UiString("ui_buff_page_format"), buffPage + 1, pageCount);
                buffPageText.gameObject.SetActive(hasMultiplePages);
            }
            if (buffPreviousButton != null)
            {
                buffPreviousButton.gameObject.SetActive(hasMultiplePages);
                buffPreviousButton.interactable = buffPage > 0;
            }
            if (buffNextButton != null)
            {
                buffNextButton.gameObject.SetActive(hasMultiplePages);
                buffNextButton.interactable = buffPage < pageCount - 1;
            }
        }

        private void CreateBuffEntry(ItemDefinition item, int index, int pageItemCount)
        {
            int column = index % 2;
            int row = index / 2;
            bool centeredLast = pageItemCount % 2 == 1 && index == pageItemCount - 1;
            float x = centeredLast
                ? UiValue("ui_buff_sticker_x_single")
                : column == 0
                    ? UiValue("ui_buff_sticker_x_even")
                    : UiValue("ui_buff_sticker_x_odd");
            float y = UiValue("ui_buff_sticker_y") + row * UiValue("ui_buff_sticker_row_pitch");
            float size = UiValue("ui_buff_sticker_size");

            GameObject stickerObject = NewUi("Buff Sticker " + index, buffEntriesRoot);
            RectTransform stickerRect = stickerObject.GetComponent<RectTransform>();
            PlaceTopLeft(stickerRect, x, y, size, size);

            GameObject artworkObject = NewUi("Artwork", stickerObject.transform);
            RectTransform artworkRect = artworkObject.GetComponent<RectTransform>();
            Stretch(artworkRect, 0f, 0f, 0f, 0f);
            Image icon = artworkObject.AddComponent<Image>();
            icon.color = Color.white;
            icon.preserveAspect = true;
            icon.raycastTarget = true;
            Sprite sprite = LoadConfiguredSprite(item.Asset, item.Key);
            if (sprite != null)
            {
                icon.sprite = sprite;
                ApplyPawnIconEffects(icon, true);
                NormalizePawnVisual(icon, Vector2.one * size,
                    UiValue("ui_sidebar_pawn_visual_size"), Vector2.zero);
            }
            else
            {
                icon.color = Color.clear;
                string shortIcon = string.IsNullOrEmpty(item.ShortIcon)
                    ? CatCafeConfigDatabase.GetString("default_short_icon")
                    : item.ShortIcon;
                TMP_Text fallback = MakeText(shortIcon, stickerObject.transform, 28,
                    new Color(0.30f, 0.19f, 0.13f, 1f), TextAnchor.MiddleCenter);
                fallback.fontStyle = FontStyles.Bold;
                fallback.raycastTarget = false;
                Stretch(fallback.rectTransform, 0f, 0f, 0f, 0f);
            }

            CreateBuffTape(stickerObject.transform, size, index, centeredLast);

            float tabWidth = UiValue("ui_buff_rarity_tab_width");
            float tabHeight = UiValue("ui_buff_rarity_tab_height");
            GameObject rarityTab = NewUi("品质色签", stickerObject.transform);
            Image rarityTabImage = rarityTab.AddComponent<Image>();
            Color rarityColor = BuffRarityColor(item.Rarity);
            rarityColor.a = UiValue("ui_buff_rarity_tab_alpha");
            rarityTabImage.color = rarityColor;
            rarityTabImage.raycastTarget = false;
            PlaceTopLeft(rarityTab.GetComponent<RectTransform>(),
                (size - tabWidth) * 0.5f, UiValue("ui_buff_rarity_tab_y"),
                tabWidth, tabHeight);

            Button button = stickerObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = icon;
            button.onClick.AddListener(delegate { ShowItemDetail(item); });
            AddImageButtonFeedback(button, stickerRect);
        }

        private void CreateBuffTape(Transform parent, float stickerSize, int index, bool centeredLast)
        {
            float width = UiValue("ui_buff_tape_width");
            float height = UiValue("ui_buff_tape_height");
            GameObject tapeObject = NewUi("物件贴纸胶带", parent);
            RectTransform tapeRect = tapeObject.GetComponent<RectTransform>();
            PlaceTopLeft(tapeRect, (stickerSize - width) * 0.5f, UiValue("ui_buff_tape_y"), width, height);
            float rotation = centeredLast
                ? UiValue("ui_buff_tape_rotation_single")
                : index % 2 == 0
                    ? UiValue("ui_buff_tape_rotation_even")
                    : UiValue("ui_buff_tape_rotation_odd");
            tapeRect.localRotation = Quaternion.Euler(0f, 0f, rotation);

            Image tape = tapeObject.AddComponent<Image>();
            Color tapeColor = UiColor("ui_buff_tape_color");
            tapeColor.a = UiValue("ui_buff_tape_alpha");
            tape.color = tapeColor;
            tape.raycastTarget = false;

            Shadow shadow = tapeObject.AddComponent<Shadow>();
            Color shadowColor = UiColor("ui_buff_tape_shadow_color");
            shadowColor.a = UiValue("ui_buff_tape_shadow_alpha");
            shadow.effectColor = shadowColor;
            shadow.effectDistance = new Vector2(
                UiValue("ui_buff_tape_shadow_x"), UiValue("ui_buff_tape_shadow_y"));
            shadow.useGraphicAlpha = true;
        }

        private void BuildPieceBox(Transform parent)
        {
            Color ink = new Color(0.30f, 0.19f, 0.13f, 1f);

            // 原美术层写着“卡池预览”，这里用一张同色纸签覆盖并改成世界观内称呼。
            // 数量指名册里实际登记的棋子总份数（含同名复制），不是合并后的种类数。
            GameObject rosterHeading = NewUi("店内名册标题", parent);
            RectTransform headingRect = rosterHeading.GetComponent<RectTransform>();
            // v2 皮肤右页自带卷轴横幅（缎带面实测 y 200-245），标题不再垫程序化纸签，
            // 文字直接落在横幅上。
            PlaceTopLeft(headingRect, UiValue("ui_piece_viewport_x"),
                UiValue("ui_piece_viewport_y") - 56f, UiValue("ui_piece_viewport_width"), 54f);

            pieceBoxCountText = MakeText(string.Empty, rosterHeading.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_piece_roster_font_size"),
                ink, TextAnchor.MiddleCenter);
            pieceBoxCountText.fontStyle = FontStyles.Bold;
            Stretch(pieceBoxCountText.rectTransform, 5f, 25f, 5f, 2f);
            pieceBoxTendencyText = MakeText(string.Empty, rosterHeading.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_piece_tendency_font_size"),
                ink, TextAnchor.MiddleCenter);
            Stretch(pieceBoxTendencyText.rectTransform, 5f, 2f, 5f, 27f);

            GameObject viewport = NewUi("店内名册列表", parent);
            pieceBoxViewportRect = viewport.GetComponent<RectTransform>();
            PlaceConfigured(pieceBoxViewportRect, "ui_piece_viewport");
            viewport.AddComponent<RectMask2D>();

            GameObject entries = NewUi("Piece Box Entries", viewport.transform);
            Stretch(entries.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            pieceBoxRoot = entries.transform;

            pieceBoxPageText = MakeText("0 / 0", viewport.transform, 14, ink, TextAnchor.MiddleCenter);
            pieceBoxPageText.fontStyle = FontStyles.Bold;
            PlaceTopLeft(pieceBoxPageText.rectTransform,
                UiValue("ui_piece_page_x"), UiValue("ui_piece_page_y") + 6f,
                UiValue("ui_piece_page_width"), UiValue("ui_piece_page_height"));

            string previousLabel = CatCafeConfigDatabase.GetString("ui_piece_previous_label");
            string nextLabel = CatCafeConfigDatabase.GetString("ui_piece_next_label");
            if (string.IsNullOrEmpty(previousLabel) || string.IsNullOrEmpty(nextLabel))
            {
                throw new InvalidOperationException(
                    "[CatCafeUI] Settings 表缺少已启用的棋子盒翻页字符配置。");
            }

            pieceBoxPreviousButton = CreateTextPageButton(viewport.transform, "棋子盒上一页", previousLabel,
                UiTopLeft("ui_piece_previous"), UiSize("ui_piece_previous"),
                delegate { ChangePieceBoxPage(-1); });
            pieceBoxNextButton = CreateTextPageButton(viewport.transform, "棋子盒下一页", nextLabel,
                UiTopLeft("ui_piece_next"), UiSize("ui_piece_next"),
                delegate { ChangePieceBoxPage(1); });

            RefreshPieceBox();
        }

        private Button CreateTextPageButton(Transform parent, string objectName, string label,
            Vector2 topLeft, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = NewUi(objectName, parent);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            AnchorRect(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), topLeft, size);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.52f, 0.35f, 0.24f, 0.90f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(action);
            TMP_Text text = MakeText(label, buttonObject.transform, 26, Color.white, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
            Stretch(text.rectTransform, 0f, 0f, 0f, 2f);
            return button;
        }

        private void ChangePieceBoxPage(int direction)
        {
            // 分页按合并后的种类数算，不是清单条目数——同种棋子只占一格。
            RebuildPieceBoxGroups();
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(pieceBoxEntries.Count / (float)PiecesPerPage));
            pieceBoxPage = Mathf.Clamp(pieceBoxPage + direction, 0, pageCount - 1);
            RefreshPieceBox();
        }

        /// <summary>
        /// 把清单按棋子种类合并，同种只占一格、旁边标数量。
        /// 顺序按第一次获得的先后，新棋子不会因为重名被塞回队伍前面。
        /// </summary>
        private void RebuildPieceBoxGroups()
        {
            pieceBoxEntries.Clear();
            pieceBoxCounts.Clear();

            Dictionary<string, int> indexByKey = new Dictionary<string, int>();
            for (int i = 0; i < pool.Count; i++)
            {
                Element element = pool[i];
                int index;
                if (indexByKey.TryGetValue(element.Key, out index))
                {
                    pieceBoxCounts[index] += 1;
                    continue;
                }

                indexByKey[element.Key] = pieceBoxEntries.Count;
                pieceBoxEntries.Add(element);
                pieceBoxCounts.Add(1);
            }
        }

        private void RefreshPieceBox()
        {
            if (pieceBoxRoot == null) return;
            // 新棋子正在飞向盒子，等落袋再重建，否则它会提前出现在列表里。
            if (pieceBoxRefreshDeferred) return;

            ClearChildren(pieceBoxRoot);
            RebuildPieceBoxGroups();

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(pieceBoxEntries.Count / (float)PiecesPerPage));

            // 刚入袋的棋子要能被看到：翻到它所在那一页再夹取。
            if (!string.IsNullOrEmpty(pieceBoxFocusKey))
            {
                for (int i = 0; i < pieceBoxEntries.Count; i++)
                {
                    if (pieceBoxEntries[i].Key != pieceBoxFocusKey) continue;
                    pieceBoxPage = i / PiecesPerPage;
                    break;
                }

                pieceBoxFocusKey = null;
            }

            pieceBoxPage = Mathf.Clamp(pieceBoxPage, 0, pageCount - 1);

            int first = pieceBoxPage * PiecesPerPage;
            int count = Mathf.Min(PiecesPerPage, pieceBoxEntries.Count - first);
            for (int i = 0; i < count; i++)
            {
                CreatePieceBoxEntry(pieceBoxEntries[first + i], pieceBoxCounts[first + i], i);
            }

            if (pieceBoxPageText != null)
            {
                pieceBoxPageText.text = pieceBoxEntries.Count == 0
                    ? "0 / 0"
                    : (pieceBoxPage + 1) + " / " + pageCount;
            }
            int tendencyCount;
            CatCafeConfigDatabase.ArchetypeRow tendency = DominantArchetype(out tendencyCount);
            if (pieceBoxCountText != null)
            {
                pieceBoxCountText.text = string.Format(
                    CatCafeConfigDatabase.GetRequiredString("ui_piece_box_roster_format"), pool.Count);
            }
            if (pieceBoxTendencyText != null)
            {
                pieceBoxTendencyText.text = tendency == null
                    ? CatCafeConfigDatabase.GetRequiredString("ui_build_tendency_none")
                    : string.Format(CatCafeConfigDatabase.GetRequiredString("ui_build_tendency_format"),
                        tendency.label, tendencyCount);
            }
            bool hasMultiplePages = pageCount > 1;
            if (pieceBoxPreviousButton != null)
            {
                pieceBoxPreviousButton.gameObject.SetActive(hasMultiplePages);
                pieceBoxPreviousButton.interactable = pieceBoxPage > 0;
            }
            if (pieceBoxNextButton != null)
            {
                pieceBoxNextButton.gameObject.SetActive(hasMultiplePages);
                pieceBoxNextButton.interactable = pieceBoxPage < pageCount - 1;
            }
        }

        private void CreatePieceBoxEntry(Element element, int copies, int slot)
        {
            int column = slot % 2;
            int row = slot / 2;
            float x = UiValue("ui_piece_entry_x") + column * UiValue("ui_piece_entry_column_pitch");
            float y = UiValue("ui_piece_entry_y") + row * UiValue("ui_piece_entry_row_pitch");

            GameObject entry = NewUi("棋子盒 " + element.Name + " " + slot, pieceBoxRoot);
            RectTransform entryRect = entry.GetComponent<RectTransform>();
            Image frame = entry.AddComponent<Image>();
            // 名册仍可打开棋子详情，但详情入口会明确告知弹窗不要显示本轮结算。
            frame.color = new Color(1f, 1f, 1f, 0.002f);
            frame.raycastTarget = true;
            PlaceTopLeft(entryRect, x, y,
                UiValue("ui_piece_entry_width"), UiValue("ui_piece_entry_height"));

            Sprite sprite = LoadElementSprite(element);
            if (sprite != null)
            {
                GameObject iconObject = NewUi("Icon", entry.transform);
                Image icon = iconObject.AddComponent<Image>();
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                ApplyPawnIconEffects(icon, true);
                // Use the full entry height so the visible cat silhouette is close to the
                // proportions of the original preview stickers behind this runtime layer.
                Stretch(iconObject.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
                NormalizePawnVisual(icon,
                    new Vector2(UiValue("ui_piece_entry_width"), UiValue("ui_piece_entry_height")),
                    UiValue("ui_sidebar_pawn_visual_size"), Vector2.zero);
            }
            else
            {
                TMP_Text fallback = MakeText(ShortIcon(element.Key), entry.transform, 23,
                    new Color(0.28f, 0.18f, 0.13f), TextAnchor.MiddleCenter);
                fallback.raycastTarget = false;
                Stretch(fallback.rectTransform, 4f, 4f, 4f, 4f);
            }

            if (copies > 1)
            {
                // 数量贴在格子右下角，压在图上也能看清。
                TMP_Text countText = MakeText("×" + copies, entry.transform, 19,
                    new Color(0.30f, 0.19f, 0.13f, 1f), TextAnchor.MiddleRight);
                countText.fontStyle = FontStyles.Bold;
                countText.raycastTarget = false;
                countText.enableAutoSizing = false;

                Outline countOutline = countText.gameObject.AddComponent<Outline>();
                countOutline.effectColor = new Color(1f, 0.95f, 0.84f, 0.95f);
                countOutline.effectDistance = new Vector2(2f, -2f);

                RectTransform countRect = countText.rectTransform;
                countRect.anchorMin = new Vector2(1f, 0f);
                countRect.anchorMax = new Vector2(1f, 0f);
                countRect.pivot = new Vector2(1f, 0f);
                countRect.anchoredPosition = new Vector2(-2f, 1f);
                countRect.sizeDelta = new Vector2(46f, 24f);
            }

            Button button = entry.AddComponent<Button>();
            button.targetGraphic = frame;
            button.onClick.AddListener(delegate { ShowCardDetail(element, false); });
        }

        private Color BuffRarityColor(Rarity rarity)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(
                CatCafeConfigDatabase.RarityColor(RarityKey(rarity), "#9C7350"), out color)
                ? color : Color.white;
        }

        private Button CreateHotspot(Transform parent, string name,
            UnityEngine.Events.UnityAction action, Vector2 topLeft, Vector2 size)
        {
            GameObject hotspot = NewUi(name, parent);
            RectTransform rect = hotspot.GetComponent<RectTransform>();
            AnchorRect(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), topLeft, size);
            Image image = hotspot.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.002f);
            image.raycastTarget = true;
            Button button = hotspot.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        private void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            AnchorRect(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));
        }

        private void PlaceConfigured(RectTransform rect, string prefix)
        {
            PlaceTopLeft(rect, UiValue(prefix + "_x"), UiValue(prefix + "_y"),
                UiValue(prefix + "_width"), UiValue(prefix + "_height"));
        }

        private static void TiltBuff(RectTransform rect)
        {
            rect.localEulerAngles = new Vector3(0f, 0f, UiValue("ui_buff_tilt_degrees"));
        }

        private void ShowSettings()
        {
            if (settingsOverlayView == null) return;
            RefreshSettingsToggles();
            settingsOverlayView.Show();
        }

        /// <summary>
        /// 问号：小帮手还没做，先给一条"建设中"的提示。
        ///
        /// 之前这里接的是"重放全部教程"，有两个问题：一是 tutorialNotes 为空或字条被
        /// 收起时会直接 return，点下去毫无反应，看起来像按钮坏了；二是它调的
        /// ReplayAll 会清掉当前存档的**全部**已读位，玩家只想查个规则，
        /// 结果之后图鉴、猫窝、绒毛那些早读过的字条会重新弹一遍，代价太大。
        ///
        /// 现在无论什么状态点下去都有反馈。等正式帮助目录做好再换掉这里，
        /// 按钮位置照旧由 Settings 的 ui_help_hotspot_* 维护。
        /// </summary>
        private void ShowHelpPlaceholder()
        {
            ShowToast(CatCafeConfigDatabase.GetString(
                "ui_help_wip_toast", "小帮手还在建设中，先自己摸索一下吧"));
        }

        private void AddHudBlock(Transform parent, string label, out TMP_Text value)
        {
            TMP_Text ignored;
            AddHudBlock(parent, label, out value, out ignored);
        }

        private void AddHudBlock(Transform parent, string label, out TMP_Text value, out TMP_Text caption)
        {
            GameObject block = NewUi(label, parent);
            LayoutElement blockLayout = block.AddComponent<LayoutElement>();
            blockLayout.minHeight = 54f;
            blockLayout.preferredHeight = 54f;
            if (label == "金币")
            {
                blockLayout.minWidth = 150f;
                blockLayout.flexibleWidth = 1f;
            }
            else
            {
                blockLayout.minWidth = 96f;
                blockLayout.preferredWidth = 96f;
            }

            Image image = block.AddComponent<Image>();
            PixelFrame(image, new Color(0.87f, 0.81f, 0.66f, 1f));
            image.raycastTarget = false;
            HorizontalLayoutGroup layout = block.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.spacing = 7;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            if (label == "金币")
            {
                GameObject coin = NewUi("Coin", block.transform);
                LayoutElement coinLayout = coin.AddComponent<LayoutElement>();
                coinLayout.minWidth = 18f;
                coinLayout.preferredWidth = 18f;
                coinLayout.minHeight = 18f;
                coinLayout.preferredHeight = 18f;
                moneyHudRect = block.GetComponent<RectTransform>();
                moneyCoinTarget = coin.GetComponent<RectTransform>();
                Image coinImage = coin.AddComponent<Image>();
                coinImage.sprite = Resources.Load<Sprite>("CatCafe/InGameUI/coin");
                coinImage.preserveAspect = true;
                if (coinImage.sprite == null)
                {
                    PixelFrame(coinImage, new Color(0.79f, 0.64f, 0.34f, 1f));
                }
                coinImage.raycastTarget = false;
            }

            TMP_Text labelText = MakeText(label, block.transform, 12,
                new Color(0.37f, 0.32f, 0.26f), TextAnchor.MiddleCenter);
            labelText.fontStyle = FontStyles.Bold;
            caption = labelText;
            TMP_Text labelValue = MakeText("—", block.transform, 25,
                new Color(0.20f, 0.15f, 0.12f), TextAnchor.MiddleCenter);
            labelValue.fontStyle = FontStyles.Bold;
            LayoutElement valueLayout = labelValue.gameObject.AddComponent<LayoutElement>();
            valueLayout.minWidth = 28f;
            valueLayout.flexibleWidth = 1f;
            value = labelValue;
        }

        private void BuildChoiceOverlay()
        {
            choiceOverlay = NewOverlay("ChoiceOverlay", "门口来的伙伴", out choiceTitle, out choicesRoot);
            // 这四个节点要按卡片张数一起伸缩，所以存成字段而不是局部变量——
            // 之前这里用同名局部变量把字段遮蔽了，字段一直是 null，伸缩也就无从谈起。
            choicePanelRect = choicesRoot.parent.GetComponent<RectTransform>();
            Image choicePanelImage = choicePanelRect.GetComponent<Image>();
            presentation.ApplyNamedSkin(choicePanelImage,
                CatCafeConfigDatabase.GetRequiredString("ui_choice_panel_skin"), Color.white);
            Image choiceTitleImage = choiceTitle.transform.parent.GetComponent<Image>();
            presentation.ApplyNamedSkin(choiceTitleImage,
                CatCafeConfigDatabase.GetRequiredString("ui_choice_title_skin"), Color.white);
            choicePanelRect.anchoredPosition += new Vector2(UiValue("ui_choice_piece_offset_x"), 0f);
            choicePanelRect.sizeDelta = new Vector2(ChoicePanelBaseWidth,
                UiValue("ui_choice_panel_height"));
            SyncChoiceOverlayShell(choiceOverlay, choicePanelRect);
            choiceTitleSize = choiceTitle.transform.parent.GetComponent<LayoutElement>();
            choiceTitleSize.minWidth = ChoiceContentBaseWidth;
            choiceTitleSize.preferredWidth = ChoiceContentBaseWidth;
            choiceTitleSize.flexibleWidth = 0f;
            choiceContentSize = choicesRoot.gameObject.GetComponent<LayoutElement>();
            choiceContentSize.minWidth = ChoiceContentBaseWidth;
            choiceContentSize.preferredWidth = ChoiceContentBaseWidth;
            choiceContentSize.minHeight = UiValue("ui_choice_content_height");
            choiceContentSize.preferredHeight = UiValue("ui_choice_content_height");
            choiceContentSize.flexibleWidth = 0f;
            choiceContentSize.flexibleHeight = 0f;
            choicesRoot.parent.GetComponent<VerticalLayoutGroup>().spacing = 14f;
            Image choiceRail = choicesRoot.gameObject.AddComponent<Image>();
            choiceRail.color = Color.clear;
            choiceRail.raycastTarget = false;
            HorizontalLayoutGroup choicesLayout = choicesRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            choicesLayout.padding = new RectOffset(
                (int)ChoiceRailPadding, (int)ChoiceRailPadding, 6, 6);
            choicesLayout.spacing = ChoiceCardSpacing;
            choicesLayout.childAlignment = TextAnchor.MiddleCenter;
            choicesLayout.childControlWidth = true;
            choicesLayout.childControlHeight = true;
            choicesLayout.childForceExpandWidth = false;
            choicesLayout.childForceExpandHeight = false;
            GameObject actions = NewUi("Actions", choicesRoot.parent);
            LayoutElement actionSize = actions.AddComponent<LayoutElement>();
            actionSize.minWidth = UiValue("ui_choice_actions_width");
            actionSize.preferredWidth = UiValue("ui_choice_actions_width");
            actionSize.flexibleWidth = 0f;
            actionSize.minHeight = UiValue("ui_choice_actions_height");
            actionSize.preferredHeight = UiValue("ui_choice_actions_height");
            actionSize.flexibleHeight = 0f;
            HorizontalLayoutGroup row = actions.AddComponent<HorizontalLayoutGroup>();
            row.spacing = UiValue("ui_choice_action_spacing");
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            rerollButton = CreateButton(actions.transform, "换一批", RerollChoices,
                UiValue("ui_choice_reroll_width"), UiValue("ui_choice_reroll_height"),
                PaperButtonRole.Secondary);
            CreateButton(actions.transform, "跳过", SkipChoice,
                UiValue("ui_choice_skip_width"), UiValue("ui_choice_skip_height"),
                PaperButtonRole.Secondary);

            // 强制决策界面：不允许点空白或 Esc 关掉。
            choiceOverlayView = AttachOverlay(choiceOverlay, choicePanelRect, false, null);
            choiceTicketText = CreateTicketRow(choicesRoot.parent, choiceContentSize.preferredWidth);
            choiceTicketSize = choiceTicketText.GetComponent<LayoutElement>();
        }

        private void BuildItemOverlay()
        {
            itemOverlay = NewOverlay("ItemOverlay", "店里的物件", out itemTitle, out itemChoicesRoot);
            RectTransform itemPanelRect = itemChoicesRoot.parent.GetComponent<RectTransform>();
            itemPanelRect.anchoredPosition += new Vector2(UiValue("ui_choice_item_offset_x"), 0f);
            itemPanelRect.sizeDelta = new Vector2(960f, 680f);
            SyncChoiceOverlayShell(itemOverlay, itemPanelRect);
            LayoutElement itemTitleSize = itemTitle.transform.parent.GetComponent<LayoutElement>();
            itemTitleSize.minWidth = 916f;
            itemTitleSize.preferredWidth = 916f;
            itemTitleSize.flexibleWidth = 0f;
            LayoutElement itemContentSize = itemChoicesRoot.gameObject.GetComponent<LayoutElement>();
            itemContentSize.minWidth = 916f;
            itemContentSize.preferredWidth = 916f;
            itemContentSize.minHeight = 380f;
            itemContentSize.preferredHeight = 380f;
            itemContentSize.flexibleWidth = 0f;
            itemContentSize.flexibleHeight = 0f;
            itemChoicesRoot.parent.GetComponent<VerticalLayoutGroup>().spacing = 14f;
            Image itemRail = itemChoicesRoot.gameObject.AddComponent<Image>();
            itemRail.color = Color.clear;
            itemRail.raycastTarget = false;
            HorizontalLayoutGroup itemChoicesLayout = itemChoicesRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            itemChoicesLayout.padding = new RectOffset(28, 28, 6, 6);
            itemChoicesLayout.spacing = 20;
            itemChoicesLayout.childAlignment = TextAnchor.MiddleCenter;
            itemChoicesLayout.childControlWidth = true;
            itemChoicesLayout.childControlHeight = true;
            itemChoicesLayout.childForceExpandWidth = false;
            itemChoicesLayout.childForceExpandHeight = false;
            GameObject actions = NewUi("Actions", itemChoicesRoot.parent);
            LayoutElement actionSize = actions.AddComponent<LayoutElement>();
            actionSize.minWidth = 330f;
            actionSize.preferredWidth = 330f;
            actionSize.flexibleWidth = 0f;
            actionSize.minHeight = 76f;
            actionSize.preferredHeight = 76f;
            actionSize.flexibleHeight = 0f;
            HorizontalLayoutGroup row = actions.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            CreateButton(actions.transform, "跳过", SkipItemChoice, 140f, 46f, PaperButtonRole.Secondary);

            itemOverlayView = AttachOverlay(itemOverlay, itemPanelRect, false, null);
            itemTicketText = CreateTicketRow(itemChoicesRoot.parent, itemContentSize.preferredWidth);
        }

        private void BuildResultOverlay()
        {
            resultOverlay = NewOverlay("ResultOverlay", "今日账本", out resultTitle, out Transform content);
            RectTransform panelRect = content.parent.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(
                UiValue("ui_result_panel_width"), UiValue("ui_result_panel_height"));
            FitOverlayWidths(resultTitle, content, UiValue("ui_result_content_width"));

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(38, 38, 18, 22);
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            resultCopy = CreateLabelFrame(content, "ResultCopy", string.Empty, 18,
                new Color(0.25f, 0.17f, 0.11f), TextAnchor.MiddleCenter,
                UiValue("ui_result_copy_height"), 1f,
                PaperSurface.Transparent);

            // 只留一个出口。主按钮在三种结果下都导向回猫咖，再摆一个"返回猫咖"
            // 就是同一件事说两遍，玩家只会犹豫该点哪个。
            resultButton = CreateButton(content, "继续营业", HandleResultAction, 360f, 62f, PaperButtonRole.Primary);
            leaderboardButton = CreateButton(content,
                CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_result_submit_button"),
                OpenLeaderboardSubmission, 420f, 58f, PaperButtonRole.Primary);
            leaderboardButton.gameObject.SetActive(false);
            resultPanelRect = panelRect;
            resultOverlay.SetActive(false);
            // 结果页必须做出选择，不给点空白关闭。
            resultOverlayView = AttachOverlay(resultOverlay, panelRect, false, null);
        }

        private void OpenLeaderboardSubmission()
        {
            if (leaderboardSubmitting || !CatCafeLeaderboard.Enabled) return;
            GameObject overlay = NewOverlay("LeaderboardSubmissionOverlay",
                CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_submit_title"),
                out TMP_Text title, out Transform content);
            RectTransform panel = content.parent.GetComponent<RectTransform>();
            panel.sizeDelta = new Vector2(
                UiValue("ui_leaderboard_submit_panel_width"),
                UiValue("ui_leaderboard_submit_panel_height"));
            FitOverlayWidths(title, content, UiValue("ui_leaderboard_submit_content_width"));

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(38, 38, 24, 22);
            layout.spacing = 18;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            TMP_Text label = CreateLabelFrame(content, "PlayerNameLabel",
                CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_name_label"),
                CatCafeConfigDatabase.GetRequiredInt("ui_leaderboard_name_label_font_size"),
                UiColor("ui_leaderboard_name_input_text_color"), TextAnchor.MiddleCenter,
                34f, 0f, PaperSurface.Transparent);
            label.fontStyle = FontStyles.Bold;
            TMP_InputField nameInput = CreateLeaderboardNameInput(content);

            GameObject actions = NewUi("Actions", content);
            HorizontalLayoutGroup actionLayout = actions.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 14;
            actionLayout.childAlignment = TextAnchor.MiddleCenter;
            actionLayout.childControlWidth = false;
            actionLayout.childControlHeight = false;
            actionLayout.childForceExpandWidth = false;
            actionLayout.childForceExpandHeight = false;
            LayoutElement actionSize = actions.AddComponent<LayoutElement>();
            actionSize.minHeight = 58f;
            actionSize.preferredHeight = 58f;
            actionSize.flexibleHeight = 0f;

            CatCafeOverlay view = AttachOverlay(overlay, panel, false, null);
            CreateButton(actions.transform, CatCafeConfigDatabase.GetRequiredString("ui_common_cancel_label"),
                delegate { view.Hide(); Destroy(overlay); }, UiValue("ui_leaderboard_name_button_width"),
                52f, PaperButtonRole.Secondary);
            Button submitButton = null;
            submitButton = CreateButton(actions.transform,
                CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_submit_button"),
                delegate { SubmitLeaderboardScore(nameInput.text, submitButton, overlay, view); },
                UiValue("ui_leaderboard_name_button_width"), 52f, PaperButtonRole.Primary);
            view.Show();
        }

        private TMP_InputField CreateLeaderboardNameInput(Transform parent)
        {
            GameObject root = NewUi("PlayerNameInput", parent);
            Image background = root.AddComponent<Image>();
            background.color = UiColor("ui_leaderboard_name_input_fill");
            Outline border = root.AddComponent<Outline>();
            border.effectColor = UiColor("ui_leaderboard_name_input_border_color");
            border.effectDistance = new Vector2(2f, -2f);
            LayoutElement size = root.AddComponent<LayoutElement>();
            size.minWidth = UiValue("ui_leaderboard_name_input_width");
            size.preferredWidth = UiValue("ui_leaderboard_name_input_width");
            size.minHeight = UiValue("ui_leaderboard_name_input_height");
            size.preferredHeight = UiValue("ui_leaderboard_name_input_height");
            size.flexibleWidth = 0f;
            size.flexibleHeight = 0f;

            TMP_InputField input = root.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.characterLimit = CatCafeConfigDatabase.GetRequiredInt("ui_leaderboard_name_max_length");
            input.textViewport = root.GetComponent<RectTransform>();
            input.text = CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_default_name");

            TMP_Text text = MakeText(string.Empty, root.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_leaderboard_name_input_font_size"),
                UiColor("ui_leaderboard_name_input_text_color"), TextAnchor.MiddleLeft);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(text.rectTransform, 16f, 7f, 16f, 7f);
            input.textComponent = text;

            TMP_Text placeholder = MakeText(
                CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_name_placeholder"), root.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_leaderboard_name_input_font_size"),
                UiColor("ui_leaderboard_name_placeholder_color"), TextAnchor.MiddleLeft);
            placeholder.enableWordWrapping = false;
            placeholder.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(placeholder.rectTransform, 16f, 7f, 16f, 7f);
            input.placeholder = placeholder;
            return input;
        }

        private void SubmitLeaderboardScore(string playerName, Button submitButton,
            GameObject overlay, CatCafeOverlay view)
        {
            if (leaderboardSubmitting || !CatCafeLeaderboard.Enabled) return;
            leaderboardSubmitting = true;
            submitButton.interactable = false;
            StartCoroutine(CatCafeLeaderboard.Submit(
                playerName,
                money, stageIndex + 1, endlessMode,
                delegate(bool success, string error)
                {
                    leaderboardSubmitting = false;
                    if (!success)
                    {
                        ShowToast(CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_submit_failure"));
                        submitButton.interactable = true;
                        return;
                    }
                    view.Hide();
                    Destroy(overlay);
                    StartCoroutine(ShowLeaderboard(money));
                }));
        }

        private IEnumerator ShowLeaderboard(int? submittedScore = null)
        {
            GameObject overlay = NewOverlay("LeaderboardOverlay",
                CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_title"),
                out TMP_Text title, out Transform content);
            RectTransform panel = content.parent.GetComponent<RectTransform>();
            panel.sizeDelta = new Vector2(UiValue("ui_leaderboard_panel_width"),
                UiValue("ui_leaderboard_panel_height"));
            FitOverlayWidths(title, content, UiValue("ui_leaderboard_content_width"));
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(38, 38, 18, 22);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            TMP_Text rank = CreateLabelFrame(content, "CurrentRank",
                submittedScore.HasValue
                    ? CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_rank_loading")
                    : string.Empty,
                18, new Color(0.25f, 0.17f, 0.11f), TextAnchor.MiddleCenter,
                submittedScore.HasValue ? 34f : 0f, 0f, PaperSurface.Transparent);

            GameObject viewportObject = NewUi("LeaderboardViewport", content);
            // 正文只在列表窗口内显示，不能滚到下方的“加载更多”和“关闭”按钮区域。
            viewportObject.AddComponent<RectMask2D>();
            LayoutElement viewportSize = viewportObject.AddComponent<LayoutElement>();
            // VerticalLayoutGroup 在 childForceExpandWidth=false 时会按子项的首选宽度布局。
            // ScrollRect 自身报告的首选宽度为 0，必须由这个布局项明确提供正文列宽。
            float leaderboardListWidth = UiValue("ui_leaderboard_content_width") - layout.padding.horizontal;
            viewportSize.minWidth = leaderboardListWidth;
            viewportSize.preferredWidth = leaderboardListWidth;
            viewportSize.minHeight = UiValue("ui_leaderboard_list_height");
            viewportSize.preferredHeight = UiValue("ui_leaderboard_list_height");
            viewportSize.flexibleHeight = 0f;
            // VerticalLayoutGroup 的 childForceExpandWidth=false 时子物体默认按 0 宽处理，
            // 不显式声明 flexibleWidth 就撑不满整行——正文框会被压成 0 宽，逐字换行。
            viewportSize.flexibleWidth = 1f;
            ScrollRect scroll = viewportObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            scroll.viewport = viewportRect;
            TMP_Text body = MakeText(CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_loading"),
                viewportObject.transform, 18, new Color(0.25f, 0.17f, 0.11f), TextAnchor.UpperLeft);
            body.raycastTarget = false;
            RectTransform bodyRect = body.rectTransform;
            AnchorRect(bodyRect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(0f, UiValue("ui_leaderboard_list_height")));
            scroll.content = bodyRect;

            // ScrollRect 的 content 不能依赖 ContentSizeFitter 在首帧回填高度；那会让
            // RectMask2D 以 0 高度裁掉全部排行文字。每次文案更新后按实际首选高度显式扩展。
            Action refreshScrollContent = delegate
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewportRect);
                float width = Mathf.Max(1f, viewportRect.rect.width - 12f);
                float textHeight = body.GetPreferredValues(body.text, width, 0f).y;
                float height = Mathf.Max(UiValue("ui_leaderboard_list_height"), textHeight + 16f);
                bodyRect.sizeDelta = new Vector2(0f, height);
                scroll.verticalNormalizedPosition = 1f;
            };

            List<CatCafeLeaderboard.ScoreRow> rowsShown = new List<CatCafeLeaderboard.ScoreRow>();
            int nextOffset = 0;
            bool loading = false;
            bool reachedEnd = false;
            Button loadMoreButton = null;
            Action loadPage = null;
            loadPage = delegate
            {
                if (loading || reachedEnd) return;
                loading = true;
                loadMoreButton.interactable = false;
                StartCoroutine(CatCafeLeaderboard.Fetch(nextOffset,
                    delegate(CatCafeLeaderboard.ScoreRow[] rows, string error)
                    {
                        loading = false;
                        if (!string.IsNullOrEmpty(error))
                        {
                            body.text = rowsShown.Count == 0
                                ? CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_load_failure")
                                : body.text;
                            refreshScrollContent();
                            loadMoreButton.interactable = true;
                            return;
                        }
                        if (rows != null) rowsShown.AddRange(rows);
                        nextOffset += rows == null ? 0 : rows.Length;
                        reachedEnd = rows == null || rows.Length <
                            CatCafeConfigDatabase.GetRequiredInt("leaderboard_limit");
                        if (rowsShown.Count == 0)
                            body.text = CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_empty");
                        else
                        {
                            StringBuilder text = new StringBuilder();
                            for (int i = 0; i < rowsShown.Count; i++)
                            {
                                if (i > 0) text.Append('\n');
                                CatCafeLeaderboard.ScoreRow row = rowsShown[i];
                                text.AppendFormat(CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_row_format"),
                                    i + 1, row.name, row.score, row.days);
                            }
                            body.text = text.ToString();
                        }
                        refreshScrollContent();
                        loadMoreButton.gameObject.SetActive(!reachedEnd);
                        if (!reachedEnd) loadMoreButton.interactable = true;
                    }));
            };
            CatCafeOverlay view = AttachOverlay(overlay, panel, false, null);
            loadMoreButton = CreateButton(content,
                CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_load_more"),
                delegate { loadPage(); }, UiValue("ui_leaderboard_load_more_width"), 48f,
                PaperButtonRole.Secondary);
            CreateButton(content, CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_close"),
                delegate { view.Hide(); Destroy(overlay); }, UiValue("ui_leaderboard_close_width"), 52f,
                PaperButtonRole.Primary);
            view.Show();
            refreshScrollContent();
            if (submittedScore.HasValue)
            {
                yield return StartCoroutine(CatCafeLeaderboard.FetchRank(submittedScore.Value,
                    delegate(int position, string error)
                {
                    rank.text = string.IsNullOrEmpty(error)
                        ? string.Format(CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_rank_format"), position, submittedScore.Value)
                        : CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_rank_failure");
                }));
            }
            loadPage();
        }

        private void BuildSettingsOverlay()
        {
            settingsOverlay = NewOverlay("SettingsOverlay", "营业菜单",
                out TMP_Text title, out Transform content);
            RectTransform panelRect = content.parent.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(570f, 700f);
            FitOverlayWidths(title, content, 514f);

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(34, 34, 16, 16);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            Image menuPaper = content.gameObject.AddComponent<Image>();
            menuPaper.color = Color.clear;
            menuPaper.raycastTarget = false;

            Color sectionInk = new Color(0.30f, 0.19f, 0.13f, 1f);

            // ── 设置区 ──
            // 显示模式不放这里：这块面板已经排满，再加一行会把"结束本局"顶到纸面外面。
            // 切窗口/全屏是开局前决定的事，开始界面和大厅的设置里都有。
            CreateLabelFrame(content, "MusicTitle", "音乐音量", 20,
                sectionInk, TextAnchor.MiddleCenter, 28f, 0f, PaperSurface.Transparent);
            presentation.BuildToggleRow(content, CatCafeUserSettings.VolumeLabels, musicButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.MusicVolume = CatCafeUserSettings.VolumeSteps[index];
                    RefreshSettingsToggles();
                });

            CreateLabelFrame(content, "AudioTitle", "音效音量", 20,
                sectionInk, TextAnchor.MiddleCenter, 28f, 0f, PaperSurface.Transparent);
            presentation.BuildToggleRow(content, CatCafeUserSettings.VolumeLabels, volumeButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.SfxVolume = CatCafeUserSettings.VolumeSteps[index];
                    RefreshSettingsToggles();
                });

            CreateLabelFrame(content, "SpeedTitle", "忙碌演出速度", 20,
                sectionInk, TextAnchor.MiddleCenter, 28f, 0f, PaperSurface.Transparent);
            presentation.BuildToggleRow(content, CatCafeUserSettings.SpeedLabels, speedButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.SpeedTierIndex = index;
                    RefreshSettingsToggles();
                });

            CreateLabelFrame(content, "TutorialTitle",
                CatCafeConfigDatabase.GetRequiredString("ui_settings_tutorial_title"), 20,
                sectionInk, TextAnchor.MiddleCenter, 28f, 0f, PaperSurface.Transparent);
            presentation.BuildToggleRow(content, new[]
                {
                    CatCafeConfigDatabase.GetRequiredString("ui_settings_tutorial_off_label"),
                    CatCafeConfigDatabase.GetRequiredString("ui_settings_tutorial_on_label")
                }, tutorialButtons,
                delegate (int index)
                {
                    CatCafeUserSettings.TutorialEnabled = index == 1;
                    if (tutorialNotes != null)
                    {
                        tutorialNotes.ApplyEnabledPreference();
                        if (CatCafeUserSettings.TutorialEnabled)
                        {
                            BeginOpeningTutorial();
                        }
                        else if (tutorialFirstInspectPending)
                        {
                            tutorialFirstInspectPending = false;
                            tutorialCatDetailOpened = false;
                            SetRollInteractable(true);
                        }
                    }
                    RefreshSettingsToggles();
                });

            // ── 操作区 ──
            CreateLabelFrame(content, "MenuDivider",
                CatCafeConfigDatabase.GetRequiredString("ui_settings_operation_title"), 18,
                new Color(0.46f, 0.34f, 0.24f, 1f), TextAnchor.MiddleCenter, 26f, 0f, PaperSurface.Transparent);
            // 名册弹窗已删：店内伙伴直接看右侧常驻店内名册，送走改在棋子详情小窗操作。
            CreateButton(content, "继续营业", CloseSettings, 350f, 58f, PaperButtonRole.Primary);
            // 确认层盖在设置面板之上；取消后玩家回到营业菜单，而不是被丢回棋盘。
            CreateButton(content,
                CatCafeConfigDatabase.GetRequiredString("ui_settings_end_run_label"),
                RequestEndRun, 350f, 58f, PaperButtonRole.Leave);

            settingsOverlayView = AttachOverlay(settingsOverlay, panelRect, true, CloseSettings);
            RefreshSettingsToggles();
        }

        private void CloseSettings()
        {
            if (settingsOverlayView != null) settingsOverlayView.Hide();
        }

        private void RefreshSettingsToggles()
        {
            presentation.MarkToggleGroup(musicButtons,
                CatCafeUserSettings.NearestVolumeStep(CatCafeUserSettings.MusicVolume));
            presentation.MarkToggleGroup(volumeButtons,
                CatCafeUserSettings.NearestVolumeStep(CatCafeUserSettings.SfxVolume));
            presentation.MarkToggleGroup(speedButtons, CatCafeUserSettings.SpeedTierIndex);
            presentation.MarkToggleGroup(tutorialButtons, CatCafeUserSettings.TutorialEnabled ? 1 : 0);
        }


        private void BuildConfirmOverlay()
        {
            confirmOverlay = NewOverlay("ConfirmOverlay", "确认", out confirmTitle, out Transform content);
            RectTransform panelRect = content.parent.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(650f, 430f);
            FitOverlayWidths(confirmTitle, content, 594f);

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(38, 38, 24, 24);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            confirmCopy = CreateLabelFrame(content, "ConfirmCopy", string.Empty, 18,
                new Color(0.25f, 0.17f, 0.11f), TextAnchor.MiddleCenter, 150f, 1f,
                PaperSurface.Transparent);

            GameObject actions = NewUi("Actions", content);
            LayoutElement actionSize = actions.AddComponent<LayoutElement>();
            actionSize.minWidth = 460f;
            actionSize.preferredWidth = 460f;
            actionSize.flexibleWidth = 0f;
            actionSize.minHeight = 60f;
            actionSize.preferredHeight = 60f;
            actionSize.flexibleHeight = 0f;

            HorizontalLayoutGroup row = actions.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            Button cancel = CreateButton(actions.transform,
                CatCafeConfigDatabase.GetRequiredString("ui_common_cancel_label"),
                CloseConfirm, 190f, 54f, PaperButtonRole.Secondary);
            confirmCancelText = cancel.GetComponentInChildren<TMP_Text>();
            Button accept = CreateButton(actions.transform,
                CatCafeConfigDatabase.GetRequiredString("ui_common_confirm_label"),
                AcceptConfirm, 230f, 54f, PaperButtonRole.Primary);
            confirmAcceptText = accept.GetComponentInChildren<TMP_Text>();

            // 确认层最后建，天然盖在设置面板之上；取消后设置面板仍在原处。
            confirmOverlayView = AttachOverlay(confirmOverlay, panelRect, true, CloseConfirm);
        }

        /// <summary>
        /// 局内棋子详情只展示配置表里的名称、类型、品质和规则说明。
        /// 棋盘与右侧名册共用同一个小窗，避免两套展示产生内容差异。
        /// </summary>
        private void BuildCardDetailOverlay()
        {
            cardDetailOverlay = NewOverlay("CardDetailOverlay",
                UiString("ui_card_detail_header"), out cardDetailTitle, out Transform content);
            Image backdrop = cardDetailOverlay.GetComponent<Image>();
            if (backdrop != null) backdrop.color = UiColor("ui_card_detail_backdrop_color");

            RectTransform panelRect = content.parent.GetComponent<RectTransform>();
            cardDetailPanelRect = panelRect;
            panelRect.sizeDelta = new Vector2(
                UiValue("ui_card_detail_width"), UiValue("ui_card_detail_height"));
            float contentWidth = UiValue("ui_card_detail_content_width");
            float contentHeight = UiValue("ui_card_detail_content_height");
            FitOverlayWidths(cardDetailTitle, content, contentWidth);

            cardDetailTitle.fontSize = CatCafeUiFontProvider.ScaleSize(
                CatCafeConfigDatabase.GetRequiredInt("ui_card_detail_title_font_size"));
            LayoutElement titleSize = cardDetailTitle.transform.parent.GetComponent<LayoutElement>();
            titleSize.minHeight = UiValue("ui_card_detail_title_height");
            titleSize.preferredHeight = titleSize.minHeight;
            titleSize.flexibleHeight = 0f;

            LayoutElement contentSize = content.GetComponent<LayoutElement>();
            contentSize.minHeight = contentHeight;
            contentSize.preferredHeight = contentHeight;
            contentSize.flexibleHeight = 0f;

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UiValue("ui_card_detail_content_spacing");
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            float iconSize = UiValue("ui_card_detail_icon_size");
            GameObject iconSlot = NewUi("CardIconSlot", content);
            LayoutElement iconSlotSize = iconSlot.AddComponent<LayoutElement>();
            iconSlotSize.minWidth = contentWidth;
            iconSlotSize.preferredWidth = contentWidth;
            iconSlotSize.minHeight = iconSize;
            iconSlotSize.preferredHeight = iconSize;
            iconSlotSize.flexibleWidth = 0f;
            iconSlotSize.flexibleHeight = 0f;

            GameObject iconObject = NewUi("CardIcon", iconSlot.transform);
            cardDetailIcon = iconObject.AddComponent<Image>();
            cardDetailIcon.preserveAspect = true;
            cardDetailIcon.raycastTarget = false;
            ApplyPawnIconEffects(cardDetailIcon, true);
            AnchorRect(iconObject.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));

            cardDetailFallback = MakeText(string.Empty, iconSlot.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_card_detail_fallback_font_size"),
                UiColor("ui_card_detail_fallback_color"),
                TextAnchor.MiddleCenter);
            cardDetailFallback.raycastTarget = false;
            AnchorRect(cardDetailFallback.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));

            cardDetailMeta = MakeText(string.Empty, content,
                CatCafeConfigDatabase.GetRequiredInt("ui_card_detail_meta_font_size"),
                UiColor("ui_card_detail_rule_color"), TextAnchor.MiddleCenter);
            cardDetailMeta.fontStyle = FontStyles.Bold;
            LayoutElement metaSize = cardDetailMeta.gameObject.AddComponent<LayoutElement>();
            metaSize.minWidth = contentWidth;
            metaSize.preferredWidth = contentWidth;
            metaSize.minHeight = UiValue("ui_card_detail_meta_height");
            metaSize.preferredHeight = metaSize.minHeight;
            metaSize.flexibleWidth = 0f;
            metaSize.flexibleHeight = 0f;

            cardDetailIncome = MakeText(string.Empty, content,
                CatCafeConfigDatabase.GetRequiredInt("ui_card_detail_income_font_size"),
                UiColor("ui_card_detail_rule_color"), TextAnchor.MiddleCenter);
            cardDetailIncome.fontStyle = FontStyles.Bold;
            cardDetailIncome.textWrappingMode = TextWrappingModes.Normal;
            LayoutElement incomeSize = cardDetailIncome.gameObject.AddComponent<LayoutElement>();
            incomeSize.minWidth = contentWidth;
            incomeSize.preferredWidth = contentWidth;
            incomeSize.minHeight = UiValue("ui_card_detail_income_height");
            incomeSize.preferredHeight = incomeSize.minHeight;
            incomeSize.flexibleWidth = 0f;
            incomeSize.flexibleHeight = 0f;

            cardDetailRule = CreateLabelFrame(content, "CardRule", string.Empty,
                CatCafeConfigDatabase.GetRequiredInt("ui_card_detail_rule_font_size"),
                UiColor("ui_card_detail_rule_color"), TextAnchor.MiddleCenter,
                UiValue("ui_card_detail_rule_height"), 1f, PaperSurface.Transparent);
            LayoutElement ruleSize = cardDetailRule.transform.parent.GetComponent<LayoutElement>();
            ruleSize.minWidth = contentWidth;
            ruleSize.preferredWidth = contentWidth;
            ruleSize.flexibleWidth = 0f;
            CatCafeTextLinkHandler ruleLinks = cardDetailRule.gameObject.AddComponent<CatCafeTextLinkHandler>();
            ruleLinks.Initialize(cardDetailRule, HandleSymbolReferenceLink);

            // 下班按钮：名册弹窗删掉后，让伙伴下班从这里操作——点棋子直接送走。
            cardDetailRemoveButton = CreateButton(content, string.Empty,
                DismissCardDetailPiece, contentWidth, 48f, PaperButtonRole.Leave);
            cardDetailRemoveText = cardDetailRemoveButton.GetComponentInChildren<TMP_Text>();
            cardDetailRemoveImage = cardDetailRemoveButton.GetComponent<Image>();
            if (cardDetailRemoveText != null)
            {
                cardDetailRemoveTextColor = cardDetailRemoveText.color;
            }

            AlignCardDetailPaperLayers();
            Transform detailBacking = cardDetailOverlay.transform.Find("BookBacking");
            if (detailBacking != null)
            {
                detailBacking.gameObject.SetActive(
                    CatCafeConfigDatabase.GetRequiredBool("ui_card_detail_show_backing"));
            }
            cardDetailOverlayView = AttachOverlay(
                cardDetailOverlay, panelRect, true, CloseCardDetail);
        }

        /// <summary>
        /// Builds one compact reference card with the same layered paper, border and title ribbon
        /// as the main detail card. A fresh card is created for every link click, allowing the
        /// player to follow references recursively without losing the previous cards.
        /// </summary>
        private SymbolReferenceCardView CreateSymbolReferenceCard()
        {
            TMP_Text title;
            Transform content;
            GameObject root = NewOverlay(
                "SymbolReferenceRoot_" + (symbolReferenceCards.Count + 1),
                string.Empty, out title, out content);
            // NewOverlay 已经把引用卡挂到 Canvas 顶层。不能再放到 CardDetailOverlay
            // 下面：招募/物件选择界面里详情层是关闭的，子节点即使 SetActive(true)
            // 也不会显示。保持顶层还能让递归引用始终盖在当前弹窗之上。
            Image rootImage = root.GetComponent<Image>();
            if (rootImage != null)
            {
                rootImage.color = Color.clear;
                rootImage.raycastTarget = false;
            }

            SymbolReferenceCardView view = new SymbolReferenceCardView
            {
                Root = root,
                Title = title,
                PanelRect = content.parent.GetComponent<RectTransform>()
            };

            GameObject blockerObject = NewUi("SymbolReferenceBlocker", root.transform);
            Image blockerImage = blockerObject.AddComponent<Image>();
            blockerImage.color = Color.clear;
            blockerImage.raycastTarget = true;
            Stretch(blockerObject.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            Button blocker = blockerObject.AddComponent<Button>();
            blocker.targetGraphic = blockerImage;
            blocker.transition = Selectable.Transition.None;
            blocker.onClick.AddListener(CloseTopSymbolReference);
            blockerObject.transform.SetAsFirstSibling();

            GameObject visualObject = NewUi("SymbolReferenceVisual", root.transform);
            RectTransform visualRect = visualObject.GetComponent<RectTransform>();
            AnchorRect(visualRect,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            view.VisualRect = visualRect;

            string[] paperLayerNames =
                { "BookBacking", "RearPaperLayer", "MiddlePaperLayer", "Panel" };
            for (int i = 0; i < paperLayerNames.Length; i++)
            {
                Transform layer = root.transform.Find(paperLayerNames[i]);
                if (layer != null) layer.SetParent(visualObject.transform, false);
            }

            float padding = UiValue("ui_symbol_reference_panel_padding");
            float spacing = UiValue("ui_symbol_reference_content_spacing");
            float contentWidth = UiValue("ui_symbol_reference_width") - padding * 2f;
            float titleHeight = UiValue("ui_symbol_reference_title_height");
            view.PanelRect.sizeDelta = new Vector2(
                UiValue("ui_symbol_reference_width"), UiValue("ui_symbol_reference_height"));
            Image panelImage = view.PanelRect.GetComponent<Image>();
            if (panelImage != null)
            {
                ApplySurface(panelImage, PaperSurface.Modal,
                    UiColor("ui_symbol_reference_panel_color"));
                panelImage.raycastTarget = true;
            }

            FitOverlayWidths(title, content, contentWidth);
            title.fontSize = CatCafeUiFontProvider.ScaleSize(
                CatCafeConfigDatabase.GetRequiredInt("ui_symbol_reference_title_font_size"));
            LayoutElement titleSize = title.transform.parent.GetComponent<LayoutElement>();
            titleSize.minHeight = titleHeight;
            titleSize.preferredHeight = titleHeight;
            titleSize.flexibleHeight = 0f;

            VerticalLayoutGroup panelLayout = view.PanelRect.GetComponent<VerticalLayoutGroup>();
            int roundedPadding = Mathf.RoundToInt(padding);
            panelLayout.padding = new RectOffset(
                roundedPadding, roundedPadding, roundedPadding, roundedPadding);
            panelLayout.spacing = spacing;

            LayoutElement contentSize = content.GetComponent<LayoutElement>();
            float contentHeight = UiValue("ui_symbol_reference_height") - padding * 2f -
                                  titleHeight - spacing;
            contentSize.minHeight = contentHeight;
            contentSize.preferredHeight = contentHeight;
            contentSize.flexibleHeight = 0f;

            VerticalLayoutGroup contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = spacing;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = false;
            contentLayout.childForceExpandHeight = false;

            float iconSize = UiValue("ui_symbol_reference_icon_size");
            GameObject iconSlot = NewUi("SymbolReferenceIconSlot", content);
            LayoutElement iconSlotSize = iconSlot.AddComponent<LayoutElement>();
            iconSlotSize.minWidth = contentWidth;
            iconSlotSize.preferredWidth = contentWidth;
            iconSlotSize.minHeight = iconSize;
            iconSlotSize.preferredHeight = iconSize;
            iconSlotSize.flexibleWidth = 0f;
            iconSlotSize.flexibleHeight = 0f;

            GameObject iconObject = NewUi("SymbolReferenceIcon", iconSlot.transform);
            view.Icon = iconObject.AddComponent<Image>();
            view.Icon.preserveAspect = true;
            view.Icon.raycastTarget = false;
            ApplyPawnIconEffects(view.Icon, true);
            AnchorRect(iconObject.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));

            view.Fallback = MakeText(string.Empty, iconSlot.transform,
                CatCafeConfigDatabase.GetRequiredInt("ui_card_detail_fallback_font_size"),
                UiColor("ui_card_detail_fallback_color"), TextAnchor.MiddleCenter);
            view.Fallback.raycastTarget = false;
            AnchorRect(view.Fallback.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));

            view.Meta = MakeText(string.Empty, content,
                CatCafeConfigDatabase.GetRequiredInt("ui_symbol_reference_meta_font_size"),
                UiColor("ui_symbol_reference_text_color"), TextAnchor.MiddleCenter);
            view.Meta.fontStyle = FontStyles.Bold;
            LayoutElement metaSize = view.Meta.gameObject.AddComponent<LayoutElement>();
            metaSize.minWidth = contentWidth;
            metaSize.preferredWidth = contentWidth;
            metaSize.minHeight = UiValue("ui_symbol_reference_meta_height");
            metaSize.preferredHeight = metaSize.minHeight;
            metaSize.flexibleWidth = 0f;
            metaSize.flexibleHeight = 0f;

            view.Rule = CreateLabelFrame(content, "SymbolReferenceRule",
                string.Empty, CatCafeConfigDatabase.GetRequiredInt(
                    "ui_symbol_reference_rule_font_size"),
                UiColor("ui_symbol_reference_text_color"), TextAnchor.MiddleCenter,
                UiValue("ui_symbol_reference_rule_height"), 0f, PaperSurface.Transparent);
            view.Rule.textWrappingMode = TextWrappingModes.Normal;
            LayoutElement referenceRuleSize =
                view.Rule.transform.parent.GetComponent<LayoutElement>();
            referenceRuleSize.minWidth = contentWidth;
            referenceRuleSize.preferredWidth = contentWidth;
            referenceRuleSize.flexibleWidth = 0f;
            CatCafeTextLinkHandler referenceLinks =
                view.Rule.gameObject.AddComponent<CatCafeTextLinkHandler>();
            referenceLinks.Initialize(view.Rule, HandleSymbolReferenceLink);

            TMP_Text closeHint = MakeText(UiString("ui_symbol_reference_close_hint"),
                content,
                CatCafeConfigDatabase.GetRequiredInt("ui_symbol_reference_hint_font_size"),
                UiColor("ui_symbol_reference_hint_color"), TextAnchor.MiddleCenter);
            closeHint.raycastTarget = false;
            LayoutElement hintSize = closeHint.gameObject.AddComponent<LayoutElement>();
            hintSize.minWidth = contentWidth;
            hintSize.preferredWidth = contentWidth;
            hintSize.minHeight = UiValue("ui_symbol_reference_hint_height");
            hintSize.preferredHeight = hintSize.minHeight;
            hintSize.flexibleWidth = 0f;
            hintSize.flexibleHeight = 0f;

            float paperLayerX = UiValue("ui_card_detail_paper_layer_x");
            AlignOverlayLayerX(
                visualObject.transform.Find("RearPaperLayer") as RectTransform, paperLayerX);
            AlignOverlayLayerX(
                visualObject.transform.Find("MiddlePaperLayer") as RectTransform, paperLayerX);
            Transform backing = visualObject.transform.Find("BookBacking");
            if (backing != null)
            {
                backing.gameObject.SetActive(
                    CatCafeConfigDatabase.GetRequiredBool("ui_card_detail_show_backing"));
            }
            FitOverlayPaperLayers(visualObject, view.PanelRect, null);
            root.SetActive(false);
            return view;
        }

        /// <summary>
        /// 棋子介绍是窄面板，通用弹层的左右错页在这里会显得不对称。
        /// 只调整介绍小窗的两层底衬，并由 Settings 决定水平位置。
        /// </summary>
        private void AlignCardDetailPaperLayers()
        {
            if (cardDetailOverlay == null) return;

            float x = UiValue("ui_card_detail_paper_layer_x");
            AlignOverlayLayerX(cardDetailOverlay.transform.Find("RearPaperLayer") as RectTransform, x);
            AlignOverlayLayerX(cardDetailOverlay.transform.Find("MiddlePaperLayer") as RectTransform, x);
        }

        private static void AlignOverlayLayerX(RectTransform rect, float x)
        {
            if (rect == null) return;
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
        }

        private void HandleSymbolReferenceLink(string linkId, PointerEventData eventData)
        {
            if (eventData == null) return;
            ShowSymbolReference(linkId, eventData.position, eventData.pressEventCamera);
        }

        private void ShowSymbolReference(string linkId, Vector2 screenPosition, Camera eventCamera)
        {
            Definition definition = null;
            ItemDefinition item = null;
            const string elementPrefix = "element|";
            const string itemPrefix = "item|";
            if (linkId.StartsWith(elementPrefix, StringComparison.Ordinal))
            {
                defs.TryGetValue(linkId.Substring(elementPrefix.Length), out definition);
            }
            else if (linkId.StartsWith(itemPrefix, StringComparison.Ordinal))
            {
                itemDefs.TryGetValue(linkId.Substring(itemPrefix.Length), out item);
            }
            if (definition == null && item == null) return;

            SymbolReferenceCardView view = CreateSymbolReferenceCard();

            string asset;
            string key;
            string fallback;
            string rule;
            Rarity rarity;
            if (definition != null)
            {
                view.Title.text = definition.Name;
                view.Meta.text = string.Format(UiString("ui_card_detail_meta_format"),
                    definition.Type, RarityLabel(EffectiveRarity(definition)));
                asset = definition.Asset;
                key = definition.Key;
                fallback = string.IsNullOrEmpty(definition.ShortIcon)
                    ? UiString("default_short_icon")
                    : definition.ShortIcon;
                rule = Join(definition.Rules, "\n");
                rarity = EffectiveRarity(definition);
            }
            else
            {
                view.Title.text = item.Name;
                view.Meta.text = string.Format(UiString("ui_card_detail_meta_format"),
                    UiString("ui_item_detail_type_label"), RarityLabel(item.Rarity));
                asset = item.Asset;
                key = item.Key;
                fallback = string.IsNullOrEmpty(item.ShortIcon)
                    ? UiString("default_short_icon")
                    : item.ShortIcon;
                rule = item.Rule;
                rarity = item.Rarity;
            }

            view.Meta.color = BuffRarityColor(rarity);
            ApplyPieceIconAtlas(view.Rule);
            view.Rule.text = string.IsNullOrWhiteSpace(rule)
                ? EscapeRichText(UiString("ui_card_detail_no_effect"))
                : FormatRuleWithSymbolLinks(rule);

            Sprite sprite = LoadConfiguredSprite(asset, key);
            view.Icon.gameObject.SetActive(sprite != null);
            view.Fallback.gameObject.SetActive(sprite == null);
            if (sprite != null)
            {
                view.Icon.sprite = sprite;
                view.Icon.color = Color.white;
                float iconSize = UiValue("ui_symbol_reference_icon_size");
                NormalizePawnVisual(view.Icon, Vector2.one * iconSize,
                    iconSize * UiValue("ui_pawn_visual_fill_ratio"), Vector2.zero);
            }
            else
            {
                view.Fallback.text = fallback;
            }

            symbolReferenceCards.Add(view);
            PositionSymbolReference(view, screenPosition, eventCamera,
                symbolReferenceCards.Count - 1);
            view.Root.transform.SetAsLastSibling();
            view.Root.SetActive(true);
        }

        private void PositionSymbolReference(SymbolReferenceCardView view,
            Vector2 screenPosition, Camera eventCamera, int depth)
        {
            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (canvasRect == null || view == null || view.VisualRect == null) return;

            Vector2 pointer;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPosition, eventCamera, out pointer)) return;

            float width = UiValue("ui_symbol_reference_width");
            float height = UiValue("ui_symbol_reference_height");
            float padding = UiValue("ui_symbol_reference_screen_padding");
            float gap = UiValue("ui_symbol_reference_horizontal_gap");
            Rect bounds = canvasRect.rect;

            float x = pointer.x + width * 0.5f + gap;
            if (x + width * 0.5f > bounds.xMax - padding)
                x = pointer.x - width * 0.5f - gap;
            float y = pointer.y + UiValue("ui_symbol_reference_vertical_offset");
            if (depth > 0 && depth - 1 < symbolReferenceCards.Count)
            {
                Vector2 previous = symbolReferenceCards[depth - 1].VisualRect.anchoredPosition;
                float cascade = UiValue("ui_symbol_reference_title_height");
                x = previous.x + cascade;
                if (x + width * 0.5f > bounds.xMax - padding) x = previous.x - cascade;
                y = previous.y - cascade;
                if (y - height * 0.5f < bounds.yMin + padding) y = previous.y + cascade;
            }
            x = Mathf.Clamp(x,
                bounds.xMin + width * 0.5f + padding,
                bounds.xMax - width * 0.5f - padding);
            y = Mathf.Clamp(y,
                bounds.yMin + height * 0.5f + padding,
                bounds.yMax - height * 0.5f - padding);
            view.VisualRect.anchoredPosition = new Vector2(x, y);
        }

        private void CloseTopSymbolReference()
        {
            int last = symbolReferenceCards.Count - 1;
            if (last < 0) return;

            SymbolReferenceCardView view = symbolReferenceCards[last];
            symbolReferenceCards.RemoveAt(last);
            if (view.Root != null)
            {
                view.Root.SetActive(false);
                Destroy(view.Root);
            }
        }

        private void HideSymbolReferences()
        {
            for (int i = symbolReferenceCards.Count - 1; i >= 0; i--)
            {
                GameObject root = symbolReferenceCards[i].Root;
                if (root == null) continue;
                root.SetActive(false);
                Destroy(root);
            }
            symbolReferenceCards.Clear();
        }

        /// <summary>
        /// 规则文案里内联棋子图标用的 TMP 图集，由「Tools/Cat Cafe/生成棋子图标图集」产出。
        /// 图集缺席时整套机制自动退回原来的「带下划线的名字」，不会显示成方框或报错。
        /// </summary>
        private TMP_SpriteAsset PieceIconAtlas
        {
            get
            {
                if (pieceIconAtlasResolved) return pieceIconAtlas;
                pieceIconAtlasResolved = true;
                pieceIconAtlas = Resources.Load<TMP_SpriteAsset>(PieceIconAtlasResource);
                if (pieceIconAtlas == null)
                {
                    Debug.LogWarning("[CatCafe] 缺少棋子图标图集 Resources/" + PieceIconAtlasResource
                        + "，规则文案退回文字显示。跑一次「Tools/Cat Cafe/生成棋子图标图集」即可。");
                }
                return pieceIconAtlas;
            }
        }

        private bool HasPieceIcon(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName) || PieceIconAtlas == null) return false;
            if (pieceIconNames == null)
            {
                pieceIconNames = new HashSet<string>();
                List<TMP_SpriteCharacter> table = PieceIconAtlas.spriteCharacterTable;
                for (int i = 0; table != null && i < table.Count; i++)
                {
                    if (!string.IsNullOrEmpty(table[i].name)) pieceIconNames.Add(table[i].name);
                }
            }
            return pieceIconNames.Contains(spriteName);
        }

        /// <summary>
        /// 给要渲染规则文案的 TMP_Text 挂上图集，否则 &lt;sprite&gt; 标签会原样吐出来。
        ///
        /// 换 spriteAsset 不会让 TMP 自动重新解析已经设好的文本，所以这里补一次
        /// 赋值触发重排——CreateCard 是先 MakeText 再挂图集的，不补就只有下一帧
        /// 有别的改动时才会显示出来。
        /// </summary>
        private void ApplyPieceIconAtlas(TMP_Text target)
        {
            if (target == null || PieceIconAtlas == null) return;
            if (target.spriteAsset == PieceIconAtlas) return;
            target.spriteAsset = PieceIconAtlas;
            if (!string.IsNullOrEmpty(target.text)) target.SetText(target.text);
        }

        private string FormatRuleWithSymbolLinks(string rule)
        {
            if (string.IsNullOrEmpty(rule)) return string.Empty;

            string linkColor = UiString("ui_symbol_link_color");
            StringBuilder result = new StringBuilder(rule.Length + 64);
            int cursor = 0;
            while (cursor < rule.Length)
            {
                SymbolLinkCandidate match = null;
                for (int i = 0; i < symbolLinkCandidates.Count; i++)
                {
                    SymbolLinkCandidate candidate = symbolLinkCandidates[i];
                    if (cursor + candidate.Name.Length > rule.Length) continue;
                    if (string.CompareOrdinal(rule, cursor, candidate.Name, 0,
                            candidate.Name.Length) == 0)
                    {
                        match = candidate;
                        break;
                    }
                }

                if (match != null)
                {
                    // 图集里有这枚棋子就在名字前面插一个图标。图标负责「一眼认出」，
                    // 名字负责消歧——六种会员徽章缩到内联尺寸后几乎一样，光有图标
                    // 读者分不出是哪一种。<link> 包住两者，点哪边都能翻出详情卡。
                    result.Append("<link=\"");
                    result.Append(EscapeRichText(match.LinkId));
                    result.Append("\">");
                    if (HasPieceIcon(match.SpriteName))
                    {
                        result.Append("<sprite name=\"");
                        result.Append(match.SpriteName);
                        result.Append("\">");
                    }
                    result.Append("<color=");
                    result.Append(linkColor);
                    result.Append("><u>");
                    result.Append(EscapeRichText(match.Name));
                    result.Append("</u></color></link>");
                    cursor += match.Name.Length;
                }
                else
                {
                    AppendEscapedCharacter(result, rule[cursor]);
                    cursor++;
                }
            }
            return result.ToString();
        }

        private static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++) AppendEscapedCharacter(result, value[i]);
            return result.ToString();
        }

        private static void AppendEscapedCharacter(StringBuilder target, char value)
        {
            switch (value)
            {
                case '&': target.Append("&amp;"); break;
                case '<': target.Append("&lt;"); break;
                case '>': target.Append("&gt;"); break;
                case '"': target.Append("&quot;"); break;
                default: target.Append(value); break;
            }
        }

        /// <param name="showRoundSettlement">
        /// 棋盘入口传 true；店内名册入口传 false。两处共用棋子详情，只有棋盘入口
        /// 可以展示该实例最近一次转动的实际结算。
        /// </param>
        private void ShowCardDetail(Element element, bool showRoundSettlement)
        {
            if (HandleBoardActionClick(element)) return;
            if (element == null || cardDetailOverlayView == null) return;

            HideSymbolReferences();
            cardDetailElement = element;
            cardDetailItem = null;
            if (tutorialFirstInspectPending && showRoundSettlement && element.Kind == Kind.Cat)
                tutorialCatDetailOpened = true;
            cardDetailTitle.text = element.Name;
            cardDetailMeta.text = string.Format(UiString("ui_card_detail_meta_format"),
                element.Def.Type, RarityLabel(EffectiveRarity(element.Def)));
            cardDetailMeta.color = BuffRarityColor(EffectiveRarity(element.Def));
            bool showActualIncome = showRoundSettlement && round > 0;
            bool showIncome = showRoundSettlement;
            cardDetailIncome.gameObject.SetActive(showIncome);
            cardDetailIncome.text = string.Empty;
            if (showIncome)
            {
                if (!showActualIncome)
                {
                    cardDetailIncome.text = string.Format(
                        UiString("ui_card_detail_preview_income_format").Replace("\\n", "\n"),
                        ConfiguredBaseIncome(element));
                }
                else if (element.LastRoundIncomeBreakdown.Count == 0)
                {
                    cardDetailIncome.text = string.Format(
                        UiString("ui_card_detail_income_format").Replace("\\n", "\n"),
                        element.LastRoundIncome);
                }
                else
                {
                    cardDetailIncome.text = string.Format(
                        UiString("ui_card_detail_income_breakdown_format").Replace("\\n", "\n"),
                        element.LastRoundIncome,
                        string.Join(UiString("ui_card_detail_income_separator").Replace("\\n", "\n"),
                            element.LastRoundIncomeBreakdown.ToArray()));
                }
            }

            string rule = Join(element.Def.Rules, "\n");
            ApplyPieceIconAtlas(cardDetailRule);
            cardDetailRule.text = string.IsNullOrWhiteSpace(rule)
                ? EscapeRichText(UiString("ui_card_detail_no_effect"))
                : FormatRuleWithSymbolLinks(rule);

            Sprite sprite = LoadElementSprite(element);
            cardDetailIcon.gameObject.SetActive(sprite != null);
            cardDetailFallback.gameObject.SetActive(sprite == null);
            if (sprite != null)
            {
                cardDetailIcon.sprite = sprite;
                cardDetailIcon.color = Color.white;
                float iconSize = UiValue("ui_card_detail_icon_size");
                NormalizePawnVisual(cardDetailIcon, Vector2.one * iconSize,
                    iconSize * UiValue("ui_pawn_visual_fill_ratio"), Vector2.zero);
            }
            else
            {
                cardDetailFallback.text = ShortIcon(element.Key);
            }

            // 送走按钮：文案随下班券数量刷新。不可用时不做半透明压暗（糊成一团看不清），
            // 而是把皮肤染灰——外框依然完整可见，文字保持浅色可读。
            bool canDismiss = CanDismissPiece();
            if (cardDetailRemoveButton != null)
            {
                cardDetailRemoveButton.gameObject.SetActive(true);
                cardDetailRemoveButton.interactable = canDismiss;
                if (cardDetailRemoveImage != null)
                {
                    cardDetailRemoveImage.color = canDismiss
                        ? Color.white
                        : new Color(0.60f, 0.58f, 0.55f, 1f);
                }
                if (cardDetailRemoveText != null)
                {
                    cardDetailRemoveText.text = string.Format(
                        CatCafeConfigDatabase.GetRequiredString("ui_card_detail_dismiss_format"),
                        removalTokens);
                    cardDetailRemoveText.color = canDismiss
                        ? cardDetailRemoveTextColor
                        : new Color(0.90f, 0.88f, 0.84f, 1f);
                }
            }
            SetCardDetailPanelHeight(true);

            cardDetailOverlay.transform.SetAsLastSibling();
            PositionCardDetail();
            cardDetailOverlayView.Show();

            // 下班券教学要等第二次点开。
            //
            // 第一次点开小窗，玩家多半是照着"点一下图标就能看清它挣多少"那条字条来的——
            // 那一拍该讲的是收益，紧接着再蹦一条讲送走，两件事挤在一起谁也没记住。
            // 等玩家自己又点开一次，说明小窗是干什么的已经懂了，这时候再讲。
            cardDetailOpenCount += 1;
            int dismissNoteAfter = Mathf.Max(1,
                CatCafeConfigDatabase.GetInt("tutorial_dismiss_note_after_opens", 2));
            if (tutorialNotes != null && canDismiss && cardDetailOpenCount >= dismissNoteAfter)
            {
                HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_overlay_hold", 0.25f));
                tutorialNotes.Notify("removal_ticket_first", cardDetailPanelRect);
            }
        }

        private void ShowItemDetail(ItemDefinition item)
        {
            if (item == null || cardDetailOverlayView == null) return;

            HideSymbolReferences();
            cardDetailElement = null;
            cardDetailItem = item;
            cardDetailTitle.text = item.Name;
            cardDetailMeta.text = string.Format(UiString("ui_card_detail_meta_format"),
                UiString("ui_item_detail_type_label"), RarityLabel(item.Rarity));
            cardDetailMeta.color = BuffRarityColor(item.Rarity);
            cardDetailIncome.gameObject.SetActive(false);
            ApplyPieceIconAtlas(cardDetailRule);
            cardDetailRule.text = string.IsNullOrWhiteSpace(item.Rule)
                ? EscapeRichText(UiString("ui_item_detail_no_effect"))
                : FormatRuleWithSymbolLinks(item.Rule);

            Sprite sprite = LoadConfiguredSprite(item.Asset, item.Key);
            cardDetailIcon.gameObject.SetActive(sprite != null);
            cardDetailFallback.gameObject.SetActive(sprite == null);
            if (sprite != null)
            {
                cardDetailIcon.sprite = sprite;
                cardDetailIcon.color = Color.white;
                float iconSize = UiValue("ui_card_detail_icon_size");
                NormalizePawnVisual(cardDetailIcon, Vector2.one * iconSize,
                    iconSize * UiValue("ui_pawn_visual_fill_ratio"), Vector2.zero);
            }
            else
            {
                cardDetailFallback.text = string.IsNullOrEmpty(item.ShortIcon)
                    ? UiString("default_short_icon")
                    : item.ShortIcon;
            }

            // 物件不能送走；但可主动使用的物件把这颗按钮借过来当「使用」。
            bool usable = ItemHasClickRule(item.Key);
            if (cardDetailRemoveButton != null)
            {
                cardDetailRemoveButton.gameObject.SetActive(usable);
                cardDetailRemoveButton.interactable = usable;
                if (usable && cardDetailRemoveText != null)
                {
                    cardDetailRemoveText.text = UiString("ui_item_detail_use_label");
                    cardDetailRemoveText.color = cardDetailRemoveTextColor;
                }
            }
            SetCardDetailPanelHeight(usable);

            cardDetailOverlay.transform.SetAsLastSibling();
            PositionCardDetail();
            cardDetailOverlayView.Show();
        }

        /// <summary>小窗高度按是否带送走按钮增减，配置表里的基准高度不动。</summary>
        private void SetCardDetailPanelHeight(bool withDismissButton)
        {
            if (cardDetailPanelRect == null) return;
            float height = UiValue("ui_card_detail_height");
            if (withDismissButton)
            {
                height += 48f + UiValue("ui_card_detail_content_spacing");
            }

            cardDetailPanelRect.sizeDelta = new Vector2(
                UiValue("ui_card_detail_width"), height);
        }

        private void PositionCardDetail()
        {
            if (cardDetailOverlayView == null || cardDetailPanelRect == null) return;

            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            float panelWidth = UiValue("ui_card_detail_width");
            float panelHeight = cardDetailPanelRect.sizeDelta.y;
            float padding = UiValue("ui_card_detail_screen_padding");
            Vector2 target = new Vector2(
                UiValue("ui_card_detail_horizontal_offset"),
                UiValue("ui_card_detail_vertical_offset"));

            Rect canvasBounds = canvasRect.rect;
            target.x = Mathf.Clamp(target.x,
                canvasBounds.xMin + panelWidth * 0.5f + padding,
                canvasBounds.xMax - panelWidth * 0.5f - padding);
            target.y = Mathf.Clamp(target.y,
                canvasBounds.yMin + panelHeight * 0.5f + padding,
                canvasBounds.yMax - panelHeight * 0.5f - padding);
            cardDetailOverlayView.SetPanelHomeOffset(target);
        }

        private void CloseCardDetail()
        {
            HideSymbolReferences();
            if (cardDetailOverlayView != null) cardDetailOverlayView.Hide();
            if (!tutorialFirstInspectPending || !tutorialCatDetailOpened) return;

            tutorialFirstInspectPending = false;
            tutorialCatDetailOpened = false;
            SetRollInteractable(true);
            HoldLandlordNotes(CatCafeConfigDatabase.GetFloat("tutorial_note_after_overlay_hold", 0.25f));
            if (tutorialNotes != null) tutorialNotes.Notify("run_first_enter");
        }

        /// <summary>
        /// NewOverlay 的标题与内容区宽度是按默认 760 面板写死的。窄面板必须改窄，
        /// 否则标题条会顶出面板边框。
        /// </summary>
        private void FitOverlayWidths(TMP_Text titleText, Transform content, float width)
        {
            LayoutElement titleLayout = titleText.transform.parent.GetComponent<LayoutElement>();
            if (titleLayout != null)
            {
                titleLayout.minWidth = width;
                titleLayout.preferredWidth = width;
                titleLayout.flexibleWidth = 0f;
            }

            LayoutElement contentLayout = content.GetComponent<LayoutElement>();
            if (contentLayout != null)
            {
                contentLayout.minWidth = width;
                contentLayout.preferredWidth = width;
                contentLayout.flexibleWidth = 0f;
            }
        }

        private CatCafeOverlay AttachOverlay(GameObject overlay, RectTransform panel,
            bool casualClose, Action onClose)
        {
            if (overlay == null) return null;

            CatCafeOverlay view = overlay.GetComponent<CatCafeOverlay>();
            if (view == null) view = overlay.AddComponent<CatCafeOverlay>();
            view.Initialize(panel, casualClose, onClose);

            FitOverlayPaperLayers(overlay, panel, view);
            return view;
        }

        private void FitOverlayPaperLayers(GameObject overlay, RectTransform panel,
            CatCafeOverlay animatedView)
        {
            if (overlay == null) return;
            if (overlay == choiceOverlay || overlay == itemOverlay)
            {
                SyncChoiceOverlayShell(overlay, panel);
                if (animatedView != null)
                {
                    animatedView.AddPanel(overlay.transform.Find("BookBacking")?.GetComponent<RectTransform>());
                    animatedView.AddPanel(overlay.transform.Find("RearPaperLayer")?.GetComponent<RectTransform>());
                    animatedView.AddPanel(overlay.transform.Find("MiddlePaperLayer")?.GetComponent<RectTransform>());
                }
                return;
            }
            // NewOverlay 在面板后面垫了一层书页底衬。它是按默认面板尺寸写死的，
            // 各弹层改过 sizeDelta 之后就对不上（窄的会整片藏进面板里），这里跟着面板重新贴合，
            // 正常弹层登记进出场动画；引用卡则把同一套纸页直接放进自己的视觉根节点。
            Transform backing = overlay.transform.Find("BookBacking");
            if (backing != null)
            {
                RectTransform backingRect = backing as RectTransform;
                if (backingRect != null && panel != null)
                {
                    backingRect.anchoredPosition = new Vector2(0f, -3f);
                    backingRect.sizeDelta = panel.sizeDelta + new Vector2(14f, 12f);
                }

                if (animatedView != null) animatedView.AddPanel(backingRect);
            }

            Transform rearPage = overlay.transform.Find("RearPaperLayer");
            if (rearPage != null)
            {
                RectTransform rearRect = rearPage as RectTransform;
                if (rearRect != null && panel != null)
                {
                    rearRect.sizeDelta = panel.sizeDelta + new Vector2(22f, 18f);
                }
                if (animatedView != null) animatedView.AddPanel(rearRect);
            }

            Transform middlePage = overlay.transform.Find("MiddlePaperLayer");
            if (middlePage != null)
            {
                RectTransform middleRect = middlePage as RectTransform;
                if (middleRect != null && panel != null)
                {
                    middleRect.sizeDelta = panel.sizeDelta + new Vector2(12f, 10f);
                }
                if (animatedView != null) animatedView.AddPanel(middleRect);
            }
        }

        /// <summary>券数常驻在标题下方，不再只藏在"换一批（N）"的按钮文案里。</summary>
        private TMP_Text CreateTicketRow(Transform panel, float width)
        {
            TMP_Text text = MakeText(string.Empty, panel, 17,
                new Color(0.40f, 0.29f, 0.19f, 1f), TextAnchor.MiddleCenter);
            text.fontStyle = FontStyles.Bold;
            LayoutElement layout = text.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
            layout.minHeight = 30f;
            layout.preferredHeight = 30f;
            layout.flexibleHeight = 0f;
            // 排在标题之后、卡片之前。
            text.transform.SetSiblingIndex(1);
            return text;
        }

        private void SetButtonDimmed(Button button, bool dimmed)
        {
            if (button == null) return;

            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = dimmed ? 0.45f : 1f;
        }

        /// <summary>按亮度给稀有度色块挑深浅字，避免浅色稀有度上出现白字。</summary>
        private static Color ReadableInk(Color background)
        {
            float luminance = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
            return luminance > 0.6f
                ? new Color(0.20f, 0.14f, 0.09f, 1f)
                : new Color(1f, 0.95f, 0.86f, 1f);
        }

        private GameObject NewOverlay(string name, string title, out TMP_Text titleText, out Transform content)
        {
            GameObject overlay = NewUi(name, canvas.transform);
            Image image = overlay.AddComponent<Image>();
            image.color = new Color(0.10f, 0.065f, 0.04f, 0.78f);
            Stretch(overlay.GetComponent<RectTransform>(), 0, 0, 0, 0);

            GameObject backing = NewUi("BookBacking", overlay.transform);
            Image backingImage = backing.AddComponent<Image>();
            PixelFrame(backingImage, new Color(0.30f, 0.17f, 0.11f, 1f));
            RectTransform backingRect = backing.GetComponent<RectTransform>();
            AnchorRect(backingRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -3f), new Vector2(774f, 712f));
            backingImage.raycastTarget = false;
            LayoutElement backingLayout = backing.AddComponent<LayoutElement>();
            backingLayout.ignoreLayout = true;

            // 分层纸页：后层只负责书页厚度和错位轮廓，不能与前景主纸板烘焙成一张图。
            GameObject rearPage = NewUi("RearPaperLayer", overlay.transform);
            Image rearPageImage = rearPage.AddComponent<Image>();
            presentation.ApplyNamedSkin(rearPageImage, "modal-main-v2",
                new Color(0.60f, 0.43f, 0.30f, 1f));
            RectTransform rearPageRect = rearPage.GetComponent<RectTransform>();
            AnchorRect(rearPageRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-18f, 10f), new Vector2(778f, 712f));
            rearPageImage.raycastTarget = false;
            LayoutElement rearPageLayout = rearPage.AddComponent<LayoutElement>();
            rearPageLayout.ignoreLayout = true;

            GameObject middlePage = NewUi("MiddlePaperLayer", overlay.transform);
            Image middlePageImage = middlePage.AddComponent<Image>();
            presentation.ApplyNamedSkin(middlePageImage, "modal-main-v2",
                new Color(0.80f, 0.68f, 0.52f, 1f));
            RectTransform middlePageRect = middlePage.GetComponent<RectTransform>();
            AnchorRect(middlePageRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(14f, -5f), new Vector2(770f, 706f));
            middlePageImage.raycastTarget = false;
            LayoutElement middlePageLayout = middlePage.AddComponent<LayoutElement>();
            middlePageLayout.ignoreLayout = true;

            GameObject panel = NewUi("Panel", overlay.transform);
            Image panelImage = panel.AddComponent<Image>();
            ApplySurface(panelImage, PaperSurface.Modal, new Color(0.95f, 0.87f, 0.71f, 1f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            AnchorRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 700f));

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            titleText = CreateLabelFrame(panel.transform, "DialogTitle", title, 27,
                new Color(1f, 0.92f, 0.76f), TextAnchor.MiddleCenter, 88f, 0f,
                PaperSurface.TitleRibbon);
            LayoutElement dialogTitleLayout = titleText.transform.parent.GetComponent<LayoutElement>();
            dialogTitleLayout.minWidth = 724f;
            dialogTitleLayout.preferredWidth = 724f;
            dialogTitleLayout.flexibleWidth = 0f;

            GameObject contentObject = NewUi("Content", panel.transform);
            LayoutElement contentLayout = contentObject.AddComponent<LayoutElement>();
            contentLayout.minWidth = 0f;
            contentLayout.preferredWidth = 724f;
            contentLayout.flexibleWidth = 0f;
            contentLayout.minHeight = 260f;
            contentLayout.preferredHeight = 360f;
            contentLayout.flexibleHeight = 1f;
            content = contentObject.transform;

            overlay.SetActive(false);
            return overlay;
        }

        private Button CreateButton(Transform parent, string label,
            UnityEngine.Events.UnityAction action, float width, float height,
            PaperButtonRole role = PaperButtonRole.Procedural)
        {
            return presentation.CreateButton(parent, label, action, width, height, role);
        }

        private TMP_Text MakeText(string text, Transform parent, int size, Color color, TextAnchor alignment)
        {
            return presentation.MakeText(text, parent, size, color, alignment);
        }

        private TMP_Text CreateLabelFrame(Transform parent, string name, string value, int size,
            Color color, TextAnchor alignment, float height, float flexibleHeight = 0f,
            PaperSurface surface = PaperSurface.Procedural)
        {
            return presentation.CreateLabelFrame(
                parent, name, value, size, color, alignment, height, flexibleHeight, surface);
        }

        private GameObject NewUi(string name, Transform parent)
        {
            return presentation.NewUi(name, parent);
        }

        private void PixelFrame(Image image, Color fill)
        {
            presentation.PixelFrame(image, fill);
        }

        private void ApplySurface(Image image, PaperSurface surface, Color proceduralFill)
        {
            presentation.ApplySurface(image, surface, proceduralFill);
        }

        private void AnchorRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            presentation.AnchorRect(rect, anchorMin, anchorMax, pivot, position, size);
        }

        private void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            presentation.Stretch(rect, left, bottom, right, top);
        }

        private void ClearChildren(Transform parent)
        {
            presentation.ClearChildren(parent);
        }

        private void HideAllOverlays()
        {
            choiceResolving = false;
            confirmAction = null;
            confirmCancelAction = null;
            // 飞行中途重开一局时别把棋子盒卡在"等落袋"状态。
            pieceBoxRefreshDeferred = false;
            activeChoiceCards.Clear();
            if (choiceOverlayView != null) choiceOverlayView.HideImmediate();
            if (itemOverlayView != null) itemOverlayView.HideImmediate();
            if (resultOverlayView != null) resultOverlayView.HideImmediate();
            if (settingsOverlayView != null) settingsOverlayView.HideImmediate();
            if (confirmOverlayView != null) confirmOverlayView.HideImmediate();
            HideSymbolReferences();
            if (cardDetailOverlayView != null) cardDetailOverlayView.HideImmediate();
        }

        private void SetRollInteractable(bool interactable)
        {
            if (rollButton != null) rollButton.interactable = interactable;
        }

        private void ShowToast(string message)
        {
            if (interactionFeedback != null)
            {
                interactionFeedback.ShowToast(message);
                return;
            }

            if (toastText != null)
            {
                toastText.text = message;
            }
        }



        private List<Element> Neighbors(int index, bool diagonals)
        {
            List<Element> result = new List<Element>();
            List<int> indexes = AdjacentIndexes(index, diagonals);
            for (int i = 0; i < indexes.Count; i++)
            {
                int neighborIndex = indexes[i];
                if (neighborIndex >= 0 && neighborIndex < board.Count && board[neighborIndex] != null)
                    result.Add(board[neighborIndex]);
            }
            return result;
        }

        /// <summary>数这一格上下左右有几个空位。越界不算空位，只算盘内的空格。</summary>
        private int CountAdjacentEmpty(int index)
        {
            if (index < 0) return 0;

            List<int> neighbors = OrthogonalIndexes(index);
            int empty = 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (board[neighbors[i]] == null) empty += 1;
            }
            return empty;
        }

        private List<int> OrthogonalIndexes(int index)
        {
            List<int> result = new List<int>();
            int row = index / BoardColumns;
            int column = index % BoardColumns;
            if (row > 0) result.Add(index - BoardColumns);
            if (row < BoardRows - 1) result.Add(index + BoardColumns);
            if (column > 0) result.Add(index - 1);
            if (column < BoardColumns - 1) result.Add(index + 1);
            return result;
        }

        private int ConnectedSameCount(int index, string key)
        {
            HashSet<int> seen = new HashSet<int> { index };
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(index);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                List<int> neighbors = OrthogonalIndexes(current);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int next = neighbors[i];
                    if (!seen.Contains(next) && board[next] != null && board[next].Key == key)
                    {
                        seen.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }
            return seen.Count;
        }

        private int FindBoardIndex(int id)
        {
            for (int i = 0; i < board.Count; i++) if (board[i] != null && board[i].Id == id) return i;
            return -1;
        }

        private int CountKind(Kind kind)
        {
            int count = 0;
            for (int i = 0; i < board.Count; i++) if (board[i] != null && board[i].Kind == kind) count++;
            return count;
        }

        private int CountKind(List<Element> elements, Kind kind)
        {
            int count = 0;
            for (int i = 0; i < elements.Count; i++) if (elements[i].Kind == kind) count++;
            return count;
        }

        private int CountKey(List<Element> elements, string key)
        {
            int count = 0;
            if (elements == null) return count;
            for (int i = 0; i < elements.Count; i++)
                if (ContainsToken(key, elements[i].Key)) count++;
            return count;
        }

        private int CountCats()
        {
            int count = 0;
            for (int i = 0; i < board.Count; i++) if (board[i] != null && (board[i].Kind == Kind.Cat || board[i].Kind == Kind.Kitten)) count++;
            return count;
        }

        private int CountCats(List<Element> elements)
        {
            int count = 0;
            for (int i = 0; i < elements.Count; i++) if (elements[i].Kind == Kind.Cat || elements[i].Kind == Kind.Kitten) count++;
            return count;
        }

        private int CountPoolCats()
        {
            int count = 0;
            for (int i = 0; i < pool.Count; i++) if (pool[i].Kind == Kind.Cat || pool[i].Kind == Kind.Kitten) count++;
            return count;
        }

        private List<string> AllRewardKeys()
        {
            List<string> result = new List<string>();
            for (int i = 0; i < RarityCount; i++) result.AddRange(RewardPool(i));
            return result;
        }

        private List<string> AllItemKeys()
        {
            return new List<string>(itemDefs.Keys);
        }

        private static void RemoveKeys(List<string> values, List<string> toRemove)
        {
            for (int i = values.Count - 1; i >= 0; i--) if (toRemove.Contains(values[i])) values.RemoveAt(i);
        }

        private static string Join(string[] values, string separator)
        {
            return string.Join(separator, values);
        }

        private string ShortIcon(string key)
        {
            Definition element;
            if (defs.TryGetValue(key, out element) && !string.IsNullOrEmpty(element.ShortIcon)) return element.ShortIcon;
            ItemDefinition item;
            if (itemDefs.TryGetValue(key, out item) && !string.IsNullOrEmpty(item.ShortIcon)) return item.ShortIcon;
            return CatCafeConfigDatabase.GetString("default_short_icon", "物");
        }

        private static Color TokenColor(string key)
        {
            unchecked
            {
                int hash = key.GetHashCode();
                float hue = (hash & 0x7fffffff) % 360 / 360f;
                return Color.HSVToRGB(hue, 0.38f, 0.72f);
            }
        }

        private void OnDestroy()
        {
            if (imageButtonBrightnessMaterial != null)
            {
                Destroy(imageButtonBrightnessMaterial);
                imageButtonBrightnessMaterial = null;
            }
        }
}
}
