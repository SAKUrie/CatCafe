#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""猫咖局内数值模拟器：按 cat_cafe_config.json 复刻结算引擎，蒙特卡洛跑多局。

复刻范围以「决定收益」的部分为准：
  - 每轮从名册无放回抽 16 格铺满棋盘（BuildBoard / BoardSelection）
  - 逐格结算 trigger=round 的 element 规则（income / generate / transform /
    remove_targets），再套 modify_income 的道具规则（add / multiply）
  - 回合末结算 trigger=round_end 的道具规则
  - 奖励：按关卡稀有度权重掷稀有度，再在该稀有度池里等概率取；每轮 3 选 1
  - 送走：花 1 张下班券，兑现 trigger=on_dismiss 规则（income / add_removal /
    add_reroll / generate）

刻意简化（会在 coverage 里报出来）：
  - 育儿窝繁殖、幼崽成长按 breeding 表近似，不模拟局外图鉴解锁
  - 亲密度、罐头、绒毛等局外经济不参与（它们不进局内数值）
  - prevent_remove 保护只在送走路径上生效

用法：
    python Tools/CatCafeConfig/balance_sim.py --runs 100
    python Tools/CatCafeConfig/balance_sim.py --runs 100 --variant proposed
"""

import argparse
import json
import os
import random
import statistics
import sys
from collections import Counter, defaultdict

CONFIG = os.path.join('Assets', 'Resources', 'GameData', 'cat_cafe_config.json')
BOARD_COLUMNS = 4
BOARD_ROWS = 4
BOARD_SIZE = BOARD_COLUMNS * BOARD_ROWS
RARITY_ORDER = ['common', 'uncommon', 'rare', 'special']
SIM_SUPPORTED_OPERATIONS = {
    # 原有
    'income', 'generate', 'transform', 'remove_targets', 'add', 'multiply',
    'permanent_add', 'add_removal', 'add_reroll', 'transfer_permanent',
    'scale', 'add_count', 'extra_round', 'include_diagonal',
    # 收益算子
    'chance_income', 'random_income', 'multiply_income', 'set_income',
    'set_max_adjacent', 'multiply_targets', 'set_targets_zero', 'cashout',
    'reduce_loss',
    # 生成 / 变形
    'generate_random', 'generate_history_random', 'generate_source',
    'choose_generate', 'waive_payment_generate', 'rarity_random', 'rarity_filter',
    # 资源票据
    'add_inspiration', 'reroll', 'set_reward_minimum', 'add_choice',
    'add_item_choice',
    # 保护 / 抑制 / 计数
    'immune', 'prevent_remove', 'suppress', 'max_count', 'consume_at_count',
    'consume_self', 'store_removed', 'store_value', 'cycle_reduce',
    'set', 'set_max',
    # 全局邻接与盘面动作
    'global_all', 'global_corners', 'global_key', 'shuffle_column', 'swap_two',
    'force_choose', 'force_skip', 'skip_last_round',
}
SIM_SUPPORTED_SCOPES = {
    # 原有
    '', 'none', 'adjacent_cats', 'adjacent_kind', 'adjacent_key', 'board_cats',
    'board_kind', 'board_key', 'same_row_key', 'board_distinct_cat_color',
    'connected_same', 'adjacent_empty', 'round_number', 'instance_rounds',
    'self_rarity', 'pool_cats', 'owned_items', 'round_income', 'consumed_total',
    'consume_self',
    # 盘面形态
    'board_empty', 'board_max_same', 'board_max_connected_same',
    'board_all_unique', 'board_key_left', 'board_key_right',
    'board_key_or_adjacent_key', 'board_key_cycle_ready', 'max_adjacent_base',
    'self_corner', 'self_left',
    # 名册 / 道具 / 票据
    'pool_key', 'pool_duplicate_count', 'owned_item_key', 'owned_item_rounds',
    'item_counter', 'item_counter_capped', 'removal_tokens',
    'inspiration_tokens', 'skipped_count',
    # 进度
    'instance_round_number', 'waves_remaining',
}


# ────────────────────────── 配置加载 ──────────────────────────

class Config:
    def __init__(self, path=CONFIG, variant=None):
        with open(path, encoding='utf-8') as handle:
            raw = json.load(handle)
        self.settings = {r['key']: r['value'] for r in raw['settings'] if r.get('enabled', True)}
        self.elements = {e['key']: e for e in raw['elements'] if e.get('enabled', True)}
        self.items = {i['key']: i for i in raw['items'] if i.get('enabled', True)}
        self.stages = [s for s in raw['stages'] if s.get('enabled', True)]
        self.weights = {w['context']: w for w in raw['weights'] if w.get('enabled', True)}
        self.initial_deck = [d for d in raw['initialDeck'] if d.get('enabled', True)]
        self.breeding = [b for b in raw['breeding'] if b.get('enabled', True)]
        rules = [r for r in raw['rules'] if r.get('enabled', True)]
        self.unsupported_protocol = {
            'operations': sorted({r.get('operation') for r in rules} - SIM_SUPPORTED_OPERATIONS),
            'scopes': sorted({r.get(field) or '' for r in rules
                              for field in ('primary_scope', 'secondary_scope')} - SIM_SUPPORTED_SCOPES),
        }

        self.round_rules = defaultdict(list)      # owner_key -> rules
        self.dismiss_rules = defaultdict(list)
        self.round_end_rules = []
        self.modify_rules = []
        self.other_rules = defaultdict(list)
        for rule in rules:
            trigger = rule['trigger']
            if trigger == 'round':
                self.round_rules[rule['owner_key']].append(rule)
            elif trigger == 'on_dismiss':
                self.dismiss_rules[rule['owner_key']].append(rule)
            elif trigger == 'round_end':
                self.round_end_rules.append(rule)
            elif trigger == 'modify_income':
                self.modify_rules.append(rule)
            else:
                self.other_rules[trigger].append(rule)

        if variant:
            variant(self)

        # 配置里出现 transfer_permanent 就说明「送走＝浓缩」已接入
        self.model_v2 = getattr(self, 'model_v2', False) or any(
            r.get('operation') == 'transfer_permanent'
            for rules in self.dismiss_rules.values() for r in rules)

        self.reward_pool = defaultdict(list)      # rarity key -> element keys
        for key, element in self.elements.items():
            pool_rarity = element.get('pool_rarity') or ''
            if not pool_rarity:
                continue
            if element.get('unlock') != 'base':   # 新账号只有 base 进奖励池
                continue
            self.reward_pool[pool_rarity].append(key)

    def num(self, key, fallback=0.0):
        try:
            return float(self.settings.get(key, fallback))
        except (TypeError, ValueError):
            return fallback

    def by_trigger(self, trigger, owner_type=None):
        """复刻 C# 的 ConfiguredRules(trigger, ownerType)：两个维度同时过滤。

        只对上 trigger 不够——历史上的静默失效就是规则写成 owner_type=element
        而调用点只取 item。这里保持同样的双重过滤，避免模拟器比实机宽松。
        """
        if trigger == 'round':
            rules = [r for rs in self.round_rules.values() for r in rs]
        elif trigger == 'on_dismiss':
            rules = [r for rs in self.dismiss_rules.values() for r in rs]
        elif trigger == 'round_end':
            rules = self.round_end_rules
        elif trigger == 'modify_income':
            rules = self.modify_rules
        else:
            rules = self.other_rules.get(trigger, [])
        if owner_type is None:
            return rules
        return [r for r in rules if r.get('owner_type') == owner_type]


# ────────────────────────── 规则引擎 ──────────────────────────

KIND_ALIAS = {'item': 'prop'}          # 旧表把棋盘物件写作 item，C# ParseKind 映射到 Prop


def contains_token(tokens, value):
    if not tokens or tokens == '*':
        return True
    value = str(value).lower()
    for part in tokens.split('|'):
        token = part.strip().lower()
        if token == value or KIND_ALIAS.get(token) == value:
            return True
    return False


def passes(comparator, value, threshold):
    if not comparator or comparator == 'always':
        return True
    if comparator == 'eq':
        return value == threshold
    if comparator == 'ne':
        return value != threshold
    if comparator == 'ge':
        return value >= threshold
    if comparator == 'gt':
        return value > threshold
    if comparator == 'le':
        return value <= threshold
    if comparator == 'lt':
        return value < threshold
    if comparator == 'modulo_zero':
        return threshold > 0 and value % threshold == 0
    return False


def rule_value(rule, primary, secondary):
    divisor = max(1, int(rule.get('divisor') or 1))
    return (int(rule.get('base_value') or 0)
            + (primary // divisor) * int(rule.get('primary_factor') or 0)
            + secondary * int(rule.get('secondary_factor') or 0)
            + primary * secondary * int(rule.get('cross_factor') or 0))


class Piece:
    __slots__ = ('key', 'kind', 'uid', 'rounds', 'permanent')

    def __init__(self, key, kind, uid):
        self.key = key
        self.kind = kind
        self.uid = uid
        self.rounds = 0
        self.permanent = 0


class Run:
    """一局：6 天，每天若干轮。"""

    def __init__(self, cfg, rng, policy):
        self.cfg = cfg
        self.rng = rng
        self.policy = policy
        self.uid = 0
        self.pool = []
        for entry in cfg.initial_deck:
            for _ in range(int(entry['count'])):
                self.add_piece(entry['element_key'])
        self.money = int(cfg.num('initial_money', 0))
        self.reroll = int(cfg.num('initial_reroll_tokens', 1))
        self.removal = int(cfg.num('initial_removal_tokens', 1))
        self.items = []
        self.consumed = 0
        self.round_index = 0
        self.unsupported = Counter()
        # 记账
        self.day_income = []
        self.day_dismiss_income = []
        self.round_income = []
        self.cleared_days = 0
        self.failed_day = None
        self.dismissals = 0
        self.day = 0                      # 当前第几天（1 起），供按天缩放用
        self.transfers = 0                # v2：永久转移次数
        self.deadline_saves = 0           # 用掉保底道具续命的次数
        self.removal_granted = 0          # 全程发出的下班券
        self.removal_peak = 0             # 手里同时攥着的最多张数
        # ── 补齐实机协议所需的状态 ──
        self.inspiration = 0              # 灵感值（add_inspiration）
        self.item_counters = Counter()    # store_removed / store_value / on_consume 计数
        self.owned_item_rounds = Counter()  # 每件道具在手上过了几轮
        self.skipped = 0                  # 跳过奖励的次数（skipped_count）
        self.removed_history = []         # 本局被移除过的棋子 key（generate_history_random）
        self.round_trigger_counts = Counter()   # round_capped 用的每轮触发次数
        self.stage_rounds_done = 0        # 当天已经打完的轮数（waves_remaining）
        self.stage_bonus_rounds = 0       # extra_round 追加的轮数
        self.stage_rounds_total = 0       # 当天计划轮数
        self.pending_reward_minimum = None  # set_reward_minimum 留给下次挑选
        self.skip_last_round = False      # skip_last_round 生效标记
        self.item_uses = 0                # 主动使用道具的次数（统计用）
        self.force_skip_choice = False    # force_skip：本轮不给挑
        self.force_choice_key = None      # force_choose：本轮被指定拿哪个

    # ── 名册 ──
    def add_piece(self, key):
        element = self.cfg.elements.get(key)
        if not element:
            return None
        self.uid += 1
        piece = Piece(key, element['kind'], self.uid)
        self.pool.append(piece)
        return piece

    def remove_piece(self, piece):
        if piece in self.pool:
            self.pool.remove(piece)

    # ── 棋盘 ──
    def build_board(self):
        picks = self.rng.sample(self.pool, min(BOARD_SIZE, len(self.pool)))
        board = list(picks) + [None] * (BOARD_SIZE - len(picks))
        self.rng.shuffle(board)
        return board

    def neighbors(self, board, index, diagonal=False):
        row, col = divmod(index, BOARD_COLUMNS)
        deltas = [(-1, 0), (1, 0), (0, -1), (0, 1)]
        if diagonal:
            deltas += [(-1, -1), (-1, 1), (1, -1), (1, 1)]
        out = []
        for dr, dc in deltas:
            r, c = row + dr, col + dc
            if 0 <= r < BOARD_ROWS and 0 <= c < BOARD_COLUMNS:
                out.append(board[r * BOARD_COLUMNS + c])
        return out

    def adjacent_empty(self, board, index):
        return sum(1 for n in self.neighbors(board, index) if n is None)

    def connected_same(self, board, index, key):
        seen, stack, count = set(), [index], 0
        while stack:
            i = stack.pop()
            if i in seen:
                continue
            seen.add(i)
            piece = board[i]
            if piece is None or piece.key != key:
                continue
            count += 1
            row, col = divmod(i, BOARD_COLUMNS)
            for dr, dc in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                r, c = row + dr, col + dc
                if 0 <= r < BOARD_ROWS and 0 <= c < BOARD_COLUMNS:
                    stack.append(r * BOARD_COLUMNS + c)
        return count

    # ── 通用原子（对齐 CatCafeMechanicMath / GameController） ──

    def effective_cycle_age(self, piece):
        """EffectiveCycleAge：寿命轮数 + 缩短量，缩短只改阈值一次，不逐轮叠加。"""
        return max(0, piece.rounds if piece else 0) + max(0, self.cycle_reduction(piece))

    def cycle_reduction(self, piece):
        if piece is None:
            return 0
        reduction = 0
        for rule in self.cfg.by_trigger('cycle', 'item'):
            if rule.get('owner_key') not in self.items or rule.get('operation') != 'add':
                continue
            if not contains_token(rule.get('source_keys'), piece.key):
                continue
            reduction += max(0, rule_value(rule, 0, 0))
        return reduction

    def rule_repeat_count(self, rule):
        """modify_rule_triggers.add_count：某些道具让指定规则整条多跑几次。"""
        result = 1
        for mod in self.cfg.by_trigger('modify_rule_triggers', 'item'):
            if mod.get('owner_key') not in self.items or mod.get('operation') != 'add_count':
                continue
            if not contains_token(mod.get('source_keys'), rule.get('owner_key')):
                continue
            result += max(0, rule_value(mod, 0, 0))
        return max(1, result)

    def modified_rule_chance(self, rule):
        """modify_rule_chance：道具/棋子按算子类型改写某条规则的触发概率。"""
        chance = float(rule.get('chance') or 0.0)
        for mod in self.cfg.by_trigger('modify_rule_chance', 'item'):
            if mod.get('owner_key') not in self.items or mod.get('operation') != 'multiply':
                continue
            if not contains_token(mod.get('target_value_mode'), rule.get('operation')):
                continue
            if not contains_token(mod.get('source_keys'), rule.get('owner_key')):
                continue
            chance *= float(mod.get('multiplier') or 1.0) or 1.0
        owned_keys = {p.key for p in self.pool}
        for mod in self.cfg.by_trigger('modify_rule_chance', 'element'):
            if mod.get('operation') != 'multiply' or mod.get('owner_key') not in owned_keys:
                continue
            if not contains_token(mod.get('target_value_mode'), rule.get('operation')):
                continue
            if not contains_token(mod.get('source_keys'), rule.get('owner_key')):
                continue
            chance *= float(mod.get('multiplier') or 1.0) or 1.0
        return min(1.0, max(0.0, chance))

    def roll_rule_triggers(self, rule):
        """概率原子：repeat_on_success 时连掷同一概率，直到失败或撞上限。"""
        limit = max(1, int(rule.get('max_triggers') or 1))
        capped = rule.get('target_value_mode') == 'round_capped'
        if capped:
            used = self.round_trigger_counts[rule.get('rule_id')]
            limit = max(0, limit - used)
            if limit == 0:
                return 0
        chance = self.modified_rule_chance(rule)
        count = 0
        while count < limit and self.rng.random() < chance:
            count += 1
            if not rule.get('repeat_on_success'):
                break
        if capped and count > 0:
            self.round_trigger_counts[rule.get('rule_id')] += count
        return count

    def apply_random_income_modifiers(self, rule, value, minimum, maximum):
        for mod in self.cfg.by_trigger('modify_random_income', 'item'):
            if mod.get('owner_key') not in self.items:
                continue
            if not contains_token(mod.get('source_keys'), rule.get('owner_key')):
                continue
            if mod.get('operation') == 'set_max':
                value = maximum
            elif (mod.get('operation') == 'reroll'
                  and value <= int(mod.get('primary_threshold') or 0)):
                value = self.rng.randint(minimum, maximum)
        return value

    def is_rule_suppressed(self, rule):
        for sup in self.cfg.by_trigger('suppress_rules', 'item'):
            if sup.get('owner_key') not in self.items or sup.get('operation') != 'suppress':
                continue
            if contains_token(sup.get('source_keys'), rule.get('owner_key')):
                return True
        return False

    def is_immune(self, piece):
        """immune / prevent_remove：棋子或道具让目标免于被清走。"""
        if piece is None:
            return False
        for rule in self.cfg.by_trigger('prevent_remove', 'element'):
            if rule.get('operation') == 'immune' and self.matches_source(rule, piece):
                return True
        for rule in self.cfg.by_trigger('prevent_remove', 'item'):
            if (rule.get('operation') == 'immune' and rule.get('owner_key') in self.items
                    and self.matches_source(rule, piece)):
                return True
        return False

    def uses_global_adjacency(self, piece, index, requested):
        """adjacency 道具把「相邻」放大成全盘 / 四角 / 指定 key。"""
        for rule in self.cfg.by_trigger('adjacency', 'item'):
            if rule.get('owner_key') not in self.items:
                continue
            value = self.round_index + 1 if rule.get('primary_scope') == 'round_number' else 0
            if not passes(rule.get('primary_comparator'), value,
                          int(rule.get('primary_threshold') or 0)):
                continue
            op = rule.get('operation')
            if op == 'global_all':
                return True
            if op == 'global_corners' and index >= 0:
                row, col = divmod(index, BOARD_COLUMNS)
                if row in (0, BOARD_ROWS - 1) and col in (0, BOARD_COLUMNS - 1):
                    return True
            if op == 'global_key' and requested:
                for key in str(requested).split('|'):
                    if contains_token(rule.get('source_keys'), key.strip()):
                        return True
        return False

    def configured_base_income(self, piece):
        """棋子的无条件底薪，供 max_adjacent_base 取最大值。"""
        if piece is None:
            return 0
        value = 0
        for rule in self.cfg.round_rules.get(piece.key, []) + self.cfg.round_rules.get('*', []):
            if rule.get('owner_type') != 'element' or rule.get('operation') != 'income':
                continue
            if not self.matches_source(rule, piece):
                continue
            if (rule.get('primary_scope') or 'none') != 'none':
                continue
            if (rule.get('secondary_scope') or 'none') != 'none':
                continue
            value += int(rule.get('base_value') or 0)
        return value

    def choose_result_key(self, rule):
        """generate_random / rarity_random：按 result_key 里的候选或稀有度池取一个。"""
        candidates = [k.strip() for k in str(rule.get('result_key') or '').split('|') if k.strip()]
        candidates = [k for k in candidates if k in self.cfg.elements]
        if candidates:
            return self.rng.choice(candidates)
        rarity = rule.get('result_rarity') or rule.get('primary_filter') or ''
        pool = self.cfg.reward_pool.get(rarity) or []
        return self.rng.choice(pool) if pool else None

    def item_stack_limit(self, key):
        for rule in self.cfg.by_trigger('item_stack_limit', 'item'):
            if rule.get('owner_key') == key and rule.get('operation') == 'max_count':
                return max(1, rule_value(rule, 0, 0))
        return 1

    def column_key(self, board, column, filt):
        return sum(1 for row in range(BOARD_ROWS)
                   for n in [board[row * BOARD_COLUMNS + column]]
                   if n is not None and contains_token(filt, n.key))

    def scope(self, name, filt, piece, index, board, nearby, round_income):
        if not name or name == 'none':
            return 0
        # 全局邻接道具在场时，「相邻」按全盘算——和实机 UsesGlobalAdjacency 一致
        wide = self.uses_global_adjacency(piece, index, filt)
        if name == 'adjacent_cats':
            source = board if wide else nearby
            return sum(1 for n in source if n is not None and n.kind == 'cat')
        if name == 'adjacent_kind':
            source = board if wide else nearby
            return sum(1 for n in source if n is not None and contains_token(filt, n.kind))
        if name == 'adjacent_key':
            source = board if wide else nearby
            return sum(1 for n in source if n is not None and contains_token(filt, n.key))
        if name == 'board_cats':
            return sum(1 for n in board if n is not None and n.kind == 'cat')
        if name == 'board_kind':
            return sum(1 for n in board if n is not None and contains_token(filt, n.kind))
        if name == 'board_key':
            return sum(1 for n in board if n is not None and contains_token(filt, n.key))
        if name == 'same_row_key':
            row = index // BOARD_COLUMNS
            return sum(1 for c in range(BOARD_COLUMNS)
                       for n in [board[row * BOARD_COLUMNS + c]]
                       if n is not None and n.key == filt and n.uid != piece.uid)
        if name == 'board_distinct_cat_color':
            return len({self.cfg.elements[n.key].get('color_gene')
                        for n in board if n is not None and n.kind == 'cat'
                        and self.cfg.elements[n.key].get('color_gene')})
        if name == 'connected_same':
            return self.connected_same(board, index, piece.key)
        if name == 'adjacent_empty':
            return self.adjacent_empty(board, index)
        if name == 'round_number':
            return self.round_index + 1
        if name == 'instance_rounds':
            # 实机走 EffectiveCycleAge：cycle_reduce 道具是改阈值，不是每轮加速
            return self.effective_cycle_age(piece) if piece else 0
        if name == 'self_rarity':
            if piece is None:
                return 0
            rarity = self.cfg.elements.get(piece.key, {}).get('rarity', 'common')
            return RARITY_ORDER.index(rarity) if rarity in RARITY_ORDER else 0
        if name == 'pool_cats':
            return sum(1 for p in self.pool if p.kind == 'cat')
        if name == 'owned_items':
            return len(self.items)
        if name == 'round_income':
            return round_income
        if name == 'consumed_total':
            return self.consumed
        if name == 'consume_self':
            return 0
        # ── 盘面形态 ──
        if name == 'board_empty':
            return sum(1 for n in board if n is None)
        if name == 'board_max_same':
            counts = Counter(n.key for n in board if n is not None)
            return max(counts.values()) if counts else 0
        if name == 'board_max_connected_same':
            best = 0
            for i, n in enumerate(board):
                if n is not None:
                    best = max(best, self.connected_same(board, i, n.key))
            return best
        if name == 'board_all_unique':
            keys = [n.key for n in board if n is not None]
            return 1 if keys and len(keys) == len(set(keys)) else 0
        if name == 'board_key_left':
            return self.column_key(board, 0, filt)
        if name == 'board_key_right':
            return self.column_key(board, BOARD_COLUMNS - 1, filt)
        if name == 'board_key_or_adjacent_key':
            # filter 用 ';' 分两段：前段数全盘，后段只要相邻命中就顶到 BOARD_SIZE 哨兵
            parts = str(filt or '').split(';')
            board_count = sum(1 for n in board
                              if n is not None and contains_token(parts[0], n.key)) if parts else 0
            has_adjacent = (len(parts) > 1
                            and any(n is not None and contains_token(parts[1], n.key)
                                    for n in nearby))
            return BOARD_SIZE if has_adjacent else board_count
        if name == 'board_key_cycle_ready':
            parts = str(filt or '').split('|')
            if len(parts) < 2 or not parts[1].strip().lstrip('-').isdigit():
                return 0
            threshold = int(parts[1])
            for n in board:
                if n is not None and n.key == parts[0] and self.effective_cycle_age(n) >= threshold:
                    return 1
            return 0
        if name == 'max_adjacent_base':
            source = board if wide else nearby
            best = 0
            for n in source:
                if n is None or (piece is not None and n.uid == piece.uid):
                    continue
                best = max(best, self.configured_base_income(n))
            return best
        if name == 'self_corner':
            if index < 0:
                return 0
            row, col = divmod(index, BOARD_COLUMNS)
            return 1 if row in (0, BOARD_ROWS - 1) and col in (0, BOARD_COLUMNS - 1) else 0
        if name == 'self_left':
            return 1 if index >= 0 and index % BOARD_COLUMNS == 0 else 0
        # ── 名册 / 道具 / 票据 ──
        if name == 'pool_key':
            return sum(1 for p in self.pool if contains_token(filt, p.key))
        if name == 'pool_duplicate_count':
            counts = Counter()
            duplicates = 0
            for p in self.pool:
                if counts[p.key] > 0:
                    duplicates += 1
                counts[p.key] += 1
            return duplicates
        if name == 'owned_item_key':
            return sum(1 for k in self.items if contains_token(filt, k))
        if name == 'owned_item_rounds':
            return self.owned_item_rounds[filt]
        if name == 'item_counter':
            return self.item_counters[filt]
        if name == 'item_counter_capped':
            parts = str(filt or '').split('|')
            cap = int(parts[1]) if len(parts) > 1 and parts[1].strip().lstrip('-').isdigit() else 10 ** 9
            return min(self.item_counters[parts[0] if parts else ''], cap)
        if name == 'removal_tokens':
            return self.removal
        if name == 'inspiration_tokens':
            return self.inspiration
        if name == 'skipped_count':
            return self.skipped
        # ── 进度 ──
        if name == 'instance_round_number':
            return self.effective_cycle_age(piece) + 1 if piece else 0
        if name == 'waves_remaining':
            return max(0, self.stage_rounds_total + self.stage_bonus_rounds
                       - self.stage_rounds_done)
        self.unsupported[name] += 1
        return 0

    def matches_source(self, rule, piece):
        if piece is None:
            return not rule.get('source_kinds') and not rule.get('source_keys')
        if rule.get('owner_type') == 'element':
            owner = rule.get('owner_key')
            if owner and owner != '*' and owner != piece.key:
                return False
        return (contains_token(rule.get('source_kinds'), piece.kind)
                and contains_token(rule.get('source_keys'), piece.key))

    def uses_diagonal(self, piece):
        for rule in self.cfg.other_rules.get('adjacency', []):
            if rule.get('owner_key') not in self.items:
                continue
            if rule.get('operation') != 'include_diagonal':
                continue
            if self.matches_source(rule, piece):
                return True
        return False

    # ── 一轮结算 ──
    def play_round(self):
        self.round_trigger_counts.clear()   # round_capped 每轮重新计数
        for key in self.items:
            self.owned_item_rounds[key] += 1
        # 营业前道具（清名册 / 收纳）先跑，再铺盘——和实机顺序一致
        self.settle_before_round()
        self.use_active_items()
        board = self.build_board()
        total = 0
        generated, transformed, removed = [], [], []
        used_once = set()               # once_per_round 的道具规则，整波只吃一次

        for col in range(BOARD_COLUMNS):
            for row in range(BOARD_ROWS):
                index = row * BOARD_COLUMNS + col
                piece = board[index]
                if piece is None:
                    continue
                nearby = self.neighbors(board, index, self.uses_diagonal(piece))
                amount = piece.permanent

                for rule in self.cfg.round_rules.get(piece.key, []) + self.cfg.round_rules.get('*', []):
                    if not self.matches_source(rule, piece):
                        continue
                    primary = self.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                         piece, index, board, nearby, 0)
                    secondary = self.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                           piece, index, board, nearby, 0)
                    if not passes(rule.get('primary_comparator'), primary, int(rule.get('primary_threshold') or 0)):
                        continue
                    if not passes(rule.get('secondary_comparator'), secondary, int(rule.get('secondary_threshold') or 0)):
                        continue
                    if self.is_rule_suppressed(rule):
                        continue
                    op = rule.get('operation')
                    repeats = self.rule_repeat_count(rule)
                    if op == 'income':
                        amount += rule_value(rule, primary, secondary) * repeats
                    elif op == 'chance_income':
                        # 概率收益：掷成功几次就结算几份
                        triggers = self.roll_rule_triggers(rule) * repeats
                        if triggers > 0:
                            amount += rule_value(rule, primary, secondary) * triggers
                    elif op == 'random_income':
                        # base_value 与 primary_factor 当作区间两端，闭区间取整
                        if self.roll_rule_triggers(rule) > 0:
                            lo = min(int(rule.get('base_value') or 0),
                                     int(rule.get('primary_factor') or 0))
                            hi = max(int(rule.get('base_value') or 0),
                                     int(rule.get('primary_factor') or 0))
                            for _ in range(repeats):
                                amount += self.apply_random_income_modifiers(
                                    rule, self.rng.randint(lo, hi), lo, hi)
                    elif op == 'multiply_income':
                        for _ in range(repeats):
                            amount = round(amount * (float(rule.get('multiplier') or 1.0) or 1.0))
                    elif op == 'generate':
                        for _ in range(self.roll_rule_triggers(rule) * repeats):
                            for _ in range(max(1, int(rule.get('result_count') or 1))):
                                generated.append(rule.get('result_key'))
                    elif op in ('generate_random', 'rarity_random'):
                        for _ in range(self.roll_rule_triggers(rule) * repeats):
                            for _ in range(max(1, int(rule.get('result_count') or 1))):
                                key = self.choose_result_key(rule)
                                if key:
                                    generated.append(key)
                    elif op == 'generate_history_random':
                        # 从本局被清走过的对象里随机召回一个
                        for _ in range(self.roll_rule_triggers(rule) * repeats):
                            if self.removed_history:
                                generated.append(self.rng.choice(self.removed_history))
                    elif op == 'generate_source':
                        for _ in range(self.roll_rule_triggers(rule) * repeats):
                            for _ in range(max(1, int(rule.get('result_count') or 1))):
                                generated.append(piece.key)
                    elif op == 'transform':
                        if self.roll_rule_triggers(rule) > 0:
                            transformed.append((piece, rule.get('result_key')))
                    elif op == 'remove_targets':
                        if self.roll_rule_triggers(rule) > 0:
                            targets = [n for n in nearby if n is not None
                                       and contains_token(rule.get('target_filter'), n.kind)
                                       and not self.is_immune(n)]
                            limit = int(rule.get('target_limit') or 0) or len(targets)
                            targets = targets[:limit]
                            if targets:
                                amount += (int(rule.get('base_value') or 0)
                                           + len(targets) * int(rule.get('primary_factor') or 0))
                                removed.extend(targets)
                    elif op == 'force_skip':
                        # 强制跳过本轮的三选一（少拿一枚棋子，是负面效果）
                        if self.roll_rule_triggers(rule) > 0:
                            self.force_skip_choice = True
                    elif op == 'force_choose':
                        if self.roll_rule_triggers(rule) > 0:
                            self.force_choice_key = rule.get('result_key')
                    if rule.get('consume_self'):
                        removed.append(piece)

                # 道具乘区
                for rule in self.cfg.modify_rules:
                    if amount == 0:
                        break            # 实机在乘区循环里直接 break，0 收益不进乘区
                    if rule.get('owner_key') not in self.items:
                        continue
                    if not self.matches_source(rule, piece) or self.is_rule_suppressed(rule):
                        continue
                    if rule.get('once_per_round') and rule.get('rule_id') in used_once:
                        continue
                    primary = self.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                         piece, index, board, nearby, 0)
                    secondary = self.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                           piece, index, board, nearby, 0)
                    if not passes(rule.get('primary_comparator'), primary, int(rule.get('primary_threshold') or 0)):
                        continue
                    if not passes(rule.get('secondary_comparator'), secondary, int(rule.get('secondary_threshold') or 0)):
                        continue
                    mod_op = rule.get('operation')
                    if mod_op == 'add':
                        amount += rule_value(rule, primary, secondary)
                    elif mod_op == 'set_income':
                        amount = rule_value(rule, primary, secondary)
                    elif mod_op == 'multiply':
                        amount = round(amount * (float(rule.get('multiplier') or 1.0) or 1.0))
                    elif mod_op == 'set_max_adjacent':
                        # 「按最强邻座结算」：直接把收益改写成 primary（邻座最高底薪）
                        amount = primary
                    if rule.get('once_per_round'):
                        used_once.add(rule.get('rule_id'))

                total += amount
                piece.rounds += 1

        # 回合末道具
        consumed_items = []
        for rule in self.cfg.round_end_rules:
            owner_type = rule.get('owner_type')
            if owner_type == 'item' and rule.get('owner_key') not in self.items:
                continue
            if owner_type == 'element':
                continue                       # 实例成长在下面单独处理
            primary = self.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                 None, -1, board, [], total)
            if not passes(rule.get('primary_comparator'), primary, int(rule.get('primary_threshold') or 0)):
                continue
            secondary = self.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                   None, -1, board, [], total)
            if not passes(rule.get('secondary_comparator'), secondary,
                          int(rule.get('secondary_threshold') or 0)):
                continue
            op = rule.get('operation')
            value = rule_value(rule, primary, secondary)
            fired = False
            if op == 'income':
                total += value
                fired = value != 0
            elif op == 'add_removal':
                self.removal += value
                self.removal_granted += max(0, value)
                fired = value != 0
            elif op == 'add_reroll':
                self.reroll += value
                fired = value != 0
            elif op == 'add_inspiration':
                self.inspiration += value
                fired = value != 0
            elif op in ('generate', 'generate_random'):
                # triggers_per_primary：primary 有几个就掷几次
                attempts = max(0, primary) if rule.get('target_value_mode') == 'triggers_per_primary' else 1
                triggers = sum(self.roll_rule_triggers(rule) for _ in range(attempts))
                for _ in range(triggers):
                    for _ in range(max(1, int(rule.get('result_count') or 1))):
                        key = (rule.get('result_key') if op == 'generate'
                               else self.choose_result_key(rule))
                        if key:
                            generated.append(key)
                fired = triggers > 0
            elif op == 'store_value':
                # 记进该道具的计数器，之后由 item_counter 作用域读出
                self.item_counters[rule.get('owner_key')] += value
                fired = value != 0
            if fired and rule.get('consume_self'):
                consumed_items.append(rule.get('owner_key'))
        for key in consumed_items:
            if key in self.items:
                self.items.remove(key)

        # 实例永久成长
        for rule in self.cfg.round_end_rules:
            if rule.get('owner_type') != 'element' or rule.get('operation') != 'permanent_add':
                continue
            for piece in self.pool:
                if piece.key != rule.get('owner_key'):
                    continue
                if passes(rule.get('primary_comparator'), self.effective_cycle_age(piece),
                          int(rule.get('primary_threshold') or 0)):
                    piece.permanent += int(rule.get('base_value') or 0)

        for piece in removed:
            if piece in self.pool:
                self.remove_piece(piece)
                self.removed_history.append(piece.key)
                self.consumed += 1
                self.on_consume(piece)
        for piece, key in transformed:
            if piece in self.pool and key in self.cfg.elements:
                piece.key = key
                piece.kind = self.cfg.elements[key]['kind']
        for key in generated:
            self.add_piece(key)

        self.money += total
        self.round_income.append(total)
        self.round_index += 1
        return total

    def on_consume(self, source):
        """棋子被清走时的道具响应（复刻 EvaluateItemTrigger("on_consume")）。

        实机先给用 item_counter 的道具各记一次数，再统一跑规则——所以「每清 N 个
        给一次」是靠计数器 + 比较符表达的，不是写死的取模。
        """
        rules = self.cfg.by_trigger('on_consume', 'item')
        counted = set()
        for rule in rules:
            if rule.get('owner_key') not in self.items or not self.matches_source(rule, source):
                continue
            if rule.get('primary_scope') != 'item_counter' or rule.get('owner_key') in counted:
                continue
            counted.add(rule.get('owner_key'))
            self.item_counters[rule.get('owner_key')] += 1
        self.apply_item_trigger('on_consume', source=source)

    def apply_item_trigger(self, trigger, source=None, round_income=0):
        """道具触发器通用路径：income / 票据 / 生成，含 multiply_value 取值模式。"""
        gained = 0
        for rule in self.cfg.by_trigger(trigger, 'item'):
            if rule.get('owner_key') not in self.items:
                continue
            if not self.matches_source(rule, source):
                continue
            primary = self.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                 source, -1, [None] * BOARD_SIZE, [], round_income)
            secondary = self.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                   source, -1, [None] * BOARD_SIZE, [], round_income)
            if not passes(rule.get('primary_comparator'), primary,
                          int(rule.get('primary_threshold') or 0)):
                continue
            if not passes(rule.get('secondary_comparator'), secondary,
                          int(rule.get('secondary_threshold') or 0)):
                continue
            value = (round(primary * float(rule.get('multiplier') or 0.0))
                     if rule.get('target_value_mode') == 'multiply_value'
                     else rule_value(rule, primary, secondary))
            op = rule.get('operation')
            if op == 'income':
                self.money += value
                gained += value
            elif op == 'add_removal':
                self.removal += value
                self.removal_granted += max(0, value)
            elif op == 'add_reroll':
                self.reroll += value
            elif op == 'add_inspiration':
                self.inspiration += value
            elif op in ('generate', 'generate_random'):
                for _ in range(self.roll_rule_triggers(rule)):
                    for _ in range(max(1, int(rule.get('result_count') or 1))):
                        key = (rule.get('result_key') if op == 'generate'
                               else self.choose_result_key(rule))
                        if key:
                            self.add_piece(key)
            elif op == 'generate_source' and source is not None:
                for _ in range(self.roll_rule_triggers(rule)):
                    for _ in range(max(1, int(rule.get('result_count') or 1))):
                        self.add_piece(source.key)
            elif op == 'set_reward_minimum':
                self.pending_reward_minimum = rule.get('result_key')
        return gained

    def settle_before_round(self):
        """营业前道具（清名册 / 收纳 / 结算），对齐 SettleBeforeRoundItems。"""
        for rule in self.cfg.by_trigger('before_round', 'item'):
            if rule.get('owner_key') not in self.items:
                continue
            primary = self.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                 None, -1, [None] * BOARD_SIZE, [], 0)
            secondary = self.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                   None, -1, [None] * BOARD_SIZE, [], 0)
            if not passes(rule.get('primary_comparator'), primary,
                          int(rule.get('primary_threshold') or 0)):
                continue
            if not passes(rule.get('secondary_comparator'), secondary,
                          int(rule.get('secondary_threshold') or 0)):
                continue
            removed = self.apply_removal_rule(rule)
            if removed <= 0:
                continue
            if rule.get('operation') == 'income':
                self.money += rule_value(rule, removed, secondary)
            elif rule.get('operation') == 'store_removed':
                self.item_counters[rule.get('owner_key')] += removed
            if rule.get('consume_self') and rule.get('owner_key') in self.items:
                self.items.remove(rule.get('owner_key'))

    def apply_removal_rule(self, rule):
        """remove_scope/remove_filter/remove_limit：从名册里清掉一批对象并结算离场收益。"""
        scope_name = rule.get('remove_scope') or ''
        if not scope_name:
            return 0
        filt = rule.get('remove_filter')
        if scope_name == 'pool_key':
            targets = [p for p in self.pool if contains_token(filt, p.key)]
        elif scope_name == 'pool_kind':
            targets = [p for p in self.pool if contains_token(filt, p.kind)]
        else:
            targets = []
        targets = [p for p in targets if not self.is_immune(p)]
        limit = int(rule.get('remove_limit') or 0)
        if limit > 0:
            targets = targets[:limit]
        count = 0
        for piece in targets:
            if len(self.pool) <= 1:
                break
            self.remove_piece(piece)
            self.removed_history.append(piece.key)
            self.consumed += 1
            count += 1
            for sub in (self.cfg.dismiss_rules.get(piece.key, [])):
                if sub.get('operation') == 'income':
                    self.money += int(sub.get('base_value') or 0)
        return count

    def use_active_items(self):
        """主动使用类道具（on_click）。真人会在有正收益时点掉，模拟里就按此近似。"""
        for key in list(self.items):
            rules = [r for r in self.cfg.by_trigger('on_click', 'item')
                     if r.get('owner_key') == key]
            if not rules:
                continue
            fired = False
            consume = False
            for rule in rules:
                primary = self.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                     None, -1, [None] * BOARD_SIZE, [], 0)
                secondary = self.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                       None, -1, [None] * BOARD_SIZE, [], 0)
                if not passes(rule.get('primary_comparator'), primary,
                              int(rule.get('primary_threshold') or 0)):
                    continue
                if not passes(rule.get('secondary_comparator'), secondary,
                              int(rule.get('secondary_threshold') or 0)):
                    continue
                removed = self.apply_removal_rule(rule)
                effective = primary if not rule.get('remove_scope') else removed
                value = (round(effective * float(rule.get('multiplier') or 0.0))
                         if rule.get('target_value_mode') == 'multiply_value'
                         else rule_value(rule, effective, secondary))
                op = rule.get('operation')
                if op == 'income':
                    self.money += value
                elif op == 'add_removal':
                    self.removal += value
                    self.removal_granted += max(0, value)
                elif op == 'add_reroll':
                    self.reroll += value
                elif op in ('generate', 'generate_random', 'choose_generate'):
                    for _ in range(self.roll_rule_triggers(rule)):
                        for _ in range(max(1, int(rule.get('result_count') or 1))):
                            gen = (rule.get('result_key') if op == 'generate'
                                   else self.choose_result_key(rule))
                            if gen:
                                self.add_piece(gen)
                elif op == 'set_reward_minimum':
                    self.pending_reward_minimum = rule.get('result_key')
                elif op == 'skip_last_round':
                    self.skip_last_round = True
                fired = True
                consume = consume or bool(rule.get('consume_self'))
            if fired:
                self.item_uses += 1
                if consume and key in self.items:
                    self.items.remove(key)

    def flat_income(self, key):
        return sum(int(r.get('base_value') or 0)
                   for r in self.cfg.round_rules.get(key, [])
                   if r.get('operation') == 'income'
                   and r.get('primary_scope') in ('none', '', None)
                   and r.get('primary_comparator') in ('always', '', None))

    # ── 送走 ──
    def dismiss_value(self, piece):
        coins = 0
        for rule in self.cfg.dismiss_rules.get(piece.key, []):
            if rule.get('operation') == 'income':
                coins += int(rule.get('base_value') or 0)
        return coins

    def dismiss(self, piece):
        if self.removal <= 0 or len(self.pool) <= 1:
            return 0
        self.remove_piece(piece)
        self.removal -= 1
        self.dismissals += 1
        coins = 0
        # 送走 = 浓缩：transfer_permanent 把身价永久转移给名册里同类最强的一位。
        for rule in (self.cfg.dismiss_rules.get(piece.key, [])
                     + self.cfg.dismiss_rules.get('*', [])):
            if rule.get('operation') != 'transfer_permanent':
                continue
            primary = self.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                 piece, -1, [], [], 0)
            if not passes(rule.get('primary_comparator'), primary,
                          int(rule.get('primary_threshold') or 0)):
                continue
            gain = rule_value(rule, primary, 0)
            same = [p for p in self.pool if p.kind == piece.kind] or self.pool
            if gain > 0 and same:
                heir = max(same, key=lambda p: (self.flat_income(p.key) + p.permanent, -p.uid))
                heir.permanent += gain
                self.transfers += 1
        for rule in self.cfg.dismiss_rules.get(piece.key, []):
            op = rule.get('operation')
            if op == 'income':
                base = int(rule.get('base_value') or 0)
                if getattr(self.cfg, 'model_v2', False):
                    base = round(base * (1 + 0.25 * max(0, self.day - 1)))
                coins += base
            elif op == 'add_removal':
                self.removal += int(rule.get('base_value') or 0)
            elif op == 'add_reroll':
                self.reroll += int(rule.get('base_value') or 0)
            elif op == 'add_inspiration':
                self.inspiration += int(rule.get('base_value') or 0)
            elif op == 'generate':
                for _ in range(max(1, int(rule.get('result_count') or 1))):
                    self.add_piece(rule.get('result_key'))
            elif op == 'generate_random':
                for _ in range(max(1, int(rule.get('result_count') or 1))):
                    key = self.choose_result_key(rule)
                    if key:
                        self.add_piece(key)
            elif op == 'set_reward_minimum':
                self.pending_reward_minimum = rule.get('result_key')
        self.removed_history.append(piece.key)
        self.money += coins
        return coins

    # ── 奖励 ──
    def roll_rarity(self, context, minimum=0):
        weights = self.cfg.weights.get(context, self.cfg.weights['stage1'])
        values = [int(weights['common']), int(weights['uncommon']), int(weights['rare'])]
        for rule in self.cfg.other_rules.get('rarity_weights', []):
            if rule.get('owner_key') not in self.items or rule.get('operation') != 'scale':
                continue
            count = sum(1 for p in self.pool if p.kind in ('cat', 'kitten'))
            factor = 1.0 + count * float(rule.get('multiplier') or 0.0)
            for i, name in enumerate(RARITY_ORDER[:3]):
                if contains_token(rule.get('source_keys'), name):
                    values[i] = round(values[i] * factor)
        pool = list(range(minimum, 3))
        total = sum(values[i] for i in pool) or 1
        roll = self.rng.randrange(total)
        for i in pool:
            roll -= values[i]
            if roll < 0:
                return i
        return pool[-1]

    def roll_item_rarity(self, tier):
        weights = self.cfg.weights.get('item_tier%d' % tier)
        if weights is None:
            return 0
        values = [int(weights.get(name) or 0) for name in RARITY_ORDER]
        have = [any(it.get('rarity') == RARITY_ORDER[i] and k not in self.items
                    for k, it in self.cfg.items.items()) for i in range(len(RARITY_ORDER))]
        total = sum(v for i, v in enumerate(values) if have[i])
        if total <= 0:
            return 0
        roll = self.rng.randrange(total)
        for i, v in enumerate(values):
            if not have[i]:
                continue
            roll -= v
            if roll < 0:
                return i
        return 0

    def reward_options(self, context, minimum=0):
        count = int(self.cfg.num('base_reward_option_count', 3))
        for rule in self.cfg.other_rules.get('reward_options', []):
            if rule.get('owner_key') in self.items and rule.get('operation') in ('add_count', 'add_choice'):
                count += int(rule.get('base_value') or 0)
        # set_reward_minimum 攒下来的稀有度下限，用一次就清掉
        if self.pending_reward_minimum in RARITY_ORDER:
            minimum = max(minimum, RARITY_ORDER.index(self.pending_reward_minimum))
            self.pending_reward_minimum = None
        minimum = min(minimum, 2)
        keys = []
        guard = 0
        while len(keys) < count and guard < 40:
            guard += 1
            rarity = RARITY_ORDER[self.roll_rarity(context, minimum)]
            candidates = [k for k in self.cfg.reward_pool.get(rarity, []) if k not in keys]
            if candidates:
                keys.append(self.rng.choice(candidates))
        return keys


# ────────────────────────── 决策策略 ──────────────────────────

def estimate_value(run, key, samples=6):
    """把候选牌塞进随机棋盘，量它的边际收益（含相邻协同）。"""
    element = run.cfg.elements.get(key)
    if not element:
        return 0.0
    probe = Piece(key, element['kind'], -1)
    rules = run.cfg.round_rules.get(key, []) + run.cfg.round_rules.get('*', [])
    if not rules:
        return 0.0
    total = 0.0
    for _ in range(samples):
        others = run.rng.sample(run.pool, min(BOARD_SIZE - 1, len(run.pool)))
        board = list(others) + [None] * (BOARD_SIZE - 1 - len(others))
        run.rng.shuffle(board)
        index = run.rng.randrange(BOARD_SIZE)
        board.insert(index, probe)
        board = board[:BOARD_SIZE]
        nearby = run.neighbors(board, index)
        amount = 0
        for rule in rules:
            if not run.matches_source(rule, probe) or rule.get('operation') != 'income':
                continue
            primary = run.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                probe, index, board, nearby, 0)
            secondary = run.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                  probe, index, board, nearby, 0)
            if not passes(rule.get('primary_comparator'), primary, int(rule.get('primary_threshold') or 0)):
                continue
            if not passes(rule.get('secondary_comparator'), secondary, int(rule.get('secondary_threshold') or 0)):
                continue
            amount += rule_value(rule, primary, secondary)
        total += amount
    return total / samples


def flat_value(run, key, samples=0):
    """牌面估值：只认「每次营业获得 N 金币」这种无条件项。

    相邻加成、连锁翻倍、场上计数这些条件项一律当作看不见——这是「照着数字挑牌」
    的玩家的真实视角：卡面上写着 3 就是 3，写着「每有1名相邻客人再获得2金币」
    的那张，在他眼里只值卡面那个 1。
    """
    return float(run.flat_income(key))


def greedy_policy(run, keys, context):
    """取边际收益最高的一张；名册已超过 16 且这张明显低于中位数就跳过。"""
    if not keys:
        return None
    scored = [(estimate_value(run, k), k) for k in keys]
    scored.sort(reverse=True)
    best_value, best_key = scored[0]
    if len(run.pool) >= BOARD_SIZE:
        current = [estimate_value(run, p.key, samples=2) for p in run.pool]
        median = statistics.median(current) if current else 0
        if best_value < median * 0.6:
            return None                     # 跳过：加进来只会稀释
    return best_key


def random_policy(run, keys, context):
    """下限对照：随手拿一张，从不跳过。"""
    return run.rng.choice(keys) if keys else None


def board_value(run, extra_key=None, samples=8):
    """整盘估值：把候选牌加进名册后，一波的期望总收入。

    和 estimate_value 的区别是它算整个盘面而不是候选牌自己那一格——猫粮自己只值
    1 金币，但它会让橘猫从 1 涨到 9。幸运房东这类游戏摆位是随机的，玩家唯一能
    影响联动的手段就是往名册里塞组件、把密度堆上去，所以估值必须算这个外溢。
    """
    pool = list(run.pool)
    if extra_key is not None:
        element = run.cfg.elements.get(extra_key)
        if element is None:
            return 0.0
        pool.append(Piece(extra_key, element['kind'], -1))
    if not pool:
        return 0.0
    total = 0.0
    for _ in range(samples):
        picks = run.rng.sample(pool, min(BOARD_SIZE, len(pool)))
        board = list(picks) + [None] * (BOARD_SIZE - len(picks))
        run.rng.shuffle(board)
        saved = run.pool
        run.pool = [p for p in board if p is not None]
        amount = 0
        for index in range(BOARD_SIZE):
            piece = board[index]
            if piece is None:
                continue
            nearby = run.neighbors(board, index, run.uses_diagonal(piece))
            for rule in (run.cfg.round_rules.get(piece.key, [])
                         + run.cfg.round_rules.get('*', [])):
                if rule.get('operation') != 'income' or not run.matches_source(rule, piece):
                    continue
                primary = run.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                    piece, index, board, nearby, 0)
                secondary = run.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                      piece, index, board, nearby, 0)
                if not passes(rule.get('primary_comparator'), primary,
                              int(rule.get('primary_threshold') or 0)):
                    continue
                if not passes(rule.get('secondary_comparator'), secondary,
                              int(rule.get('secondary_threshold') or 0)):
                    continue
                amount += rule_value(rule, primary, secondary)
            amount += piece.permanent
        run.pool = saved
        total += amount
    return total / samples


def synergy_value(run, key, samples=8):
    """这张牌进名册后，整盘期望收入的增量。"""
    return board_value(run, key, samples) - board_value(run, None, samples)


def synergy_policy(run, keys, context):
    """真懂联动：按「进名册后整盘涨多少」挑牌，会主动堆组件密度。"""
    if not keys:
        return None
    scored = sorted(((synergy_value(run, k, samples=32), k) for k in keys), reverse=True)
    best_value, best_key = scored[0]
    if len(run.pool) >= BOARD_SIZE and best_value <= 0:
        return None
    return best_key


def naive_greedy_policy(run, keys, context):
    """会算数、但不懂联动：每次都挑牌面数字最大的，条件项看不懂。

    和 greedy 只差一个估值函数，所以两者的差额就是「读懂联动」这件事值多少钱。
    券照用（他知道名册太满不好），只是判断谁强谁弱时只看卡面。
    """
    if not keys:
        return None
    scored = sorted(((flat_value(run, k), k) for k in keys), reverse=True)
    best_value, best_key = scored[0]
    if len(run.pool) >= BOARD_SIZE:
        current = [flat_value(run, p.key) for p in run.pool]
        median = statistics.median(current) if current else 0
        if best_value < median * 0.6:
            return None
    return best_key


def casual_policy(run, keys, context):
    """普通玩家：大体挑好的，但有 35% 概率看走眼；名册撑爆了才想起跳过。

    通关率指标挂在这个策略上——greedy 是接近最优的解，random 是完全不动脑，
    两头都不是真实玩家。
    """
    if not keys:
        return None
    if run.rng.random() < 0.35:
        return run.rng.choice(keys)
    scored = [(estimate_value(run, k), k) for k in keys]
    scored.sort(reverse=True)
    best_value, best_key = scored[0]
    if len(run.pool) >= BOARD_SIZE + 6 and best_value <= 1:
        return None
    return best_key


def hoard_policy(run, keys, context):
    """只拿不删、也不跳过，但按边际收益挑——测「不会用送走的玩家」。"""
    if not keys:
        return None
    return max(keys, key=lambda k: estimate_value(run, k))


POLICIES = {}


def dismiss_policy(run, target, rounds_left):
    """两条动机：结算前差钱就变现；名册超 16 就把最弱的送走。"""
    if run.removal <= 0 or len(run.pool) <= 1:
        return
    # 1) 差钱变现
    if rounds_left <= 1 and run.money < target:
        cash = sorted(((run.dismiss_value(p), p.uid, p) for p in run.pool
                       if run.dismiss_value(p) > 0), reverse=True)
        for value, _uid, piece in cash:
            if run.money >= target or run.removal <= 0:
                break
            run.dismiss(piece)
    value_of = getattr(run.policy, 'valuation', estimate_value)
    # 2) 瘦身/浓缩
    if getattr(run.cfg, 'model_v2', False):
        # v2：只要名册超过盘面就持续浓缩，把最弱的送走喂给最强的
        while run.removal > 0 and len(run.pool) > BOARD_SIZE:
            scored = sorted((value_of(run, p.key, samples=2) + p.permanent, p.uid, p)
                            for p in run.pool)
            run.dismiss(scored[0][2])
    elif run.removal > 1 and len(run.pool) > BOARD_SIZE:
        scored = sorted((value_of(run, p.key, samples=2), p.uid, p) for p in run.pool)
        if scored and scored[0][0] <= 0:
            run.dismiss(scored[0][2])


# ────────────────────────── 主循环 ──────────────────────────

def simulate(cfg, seed, policy=None, use_dismiss=True, forced_items=None):
    policy = policy or greedy_policy
    rng = random.Random(seed)
    run = Run(cfg, rng, policy)
    if forced_items:
        run.items.extend(forced_items)
    for stage_index, stage in enumerate(cfg.stages):
        rounds = int(stage['rounds'])
        target = int(stage['target'])
        context = stage['rarity_context']
        start_money = run.money
        dismiss_income = 0
        run.day = stage_index + 1
        run.stage_rounds_total = rounds
        run.stage_rounds_done = 0
        run.stage_bonus_rounds = 0
        for r in range(rounds):
            run.play_round()
            run.stage_rounds_done += 1
            rounds_left = rounds - r - 1
            before = run.money
            if use_dismiss:
                dismiss_policy(run, target, rounds_left)
            dismiss_income += run.money - before
            if rounds_left > 0:
                if run.force_skip_choice:
                    # force_skip：这一轮不给挑，直接记一次跳过
                    run.force_skip_choice = False
                    run.skipped += 1
                    continue
                keys = run.reward_options(context)
                choice = run.force_choice_key or policy(run, keys, context)
                run.force_choice_key = None
                if choice:
                    run.add_piece(choice)
                    for rule in cfg.other_rules.get('on_choose', []):
                        if rule.get('owner_key') in run.items and rule.get('operation') == 'income':
                            run.money += int(rule.get('base_value') or 0)
                else:
                    run.skipped += 1
        run.day_income.append(run.money - start_money)
        run.day_dismiss_income.append(dismiss_income)

        # 回合耗尽还差钱：看有没有 stage_deadline 道具能续一轮（应急保温壶那类）
        while run.money < target:
            saved = None
            for rule in cfg.other_rules.get('stage_deadline', []):
                if (rule.get('owner_key') in run.items
                        and rule.get('operation') == 'extra_round'):
                    saved = rule
                    break
            if saved is None:
                break
            run.items.remove(saved['owner_key'])
            run.deadline_saves += 1
            for _ in range(max(1, int(saved.get('base_value') or 1))):
                before = run.money
                run.play_round()
                run.day_income[-1] += run.money - before

        if run.money < target:
            run.failed_day = stage_index + 1
            break
        run.money -= target
        run.cleared_days += 1
        run.reroll += int(cfg.num('stage_clear_reroll_reward', 1))
        reward = int(cfg.num('stage_clear_removal_reward', 1))
        per_excess = int(cfg.num('stage_clear_removal_per_excess', 0))
        if per_excess > 0 and len(run.pool) > BOARD_SIZE:
            reward += (len(run.pool) - BOARD_SIZE) // per_excess
        run.removal += reward
        run.removal_granted += reward
        run.removal_peak = max(run.removal_peak, run.removal)
        # 通关道具奖励：按 item_tier{N} 的权重掷稀有度，再在该档未持有的道具里取
        tier = int(stage.get('clear_item_tier') or 0)
        offers = []
        guard = 0
        while len(offers) < int(cfg.num('base_item_option_count', 3)) and guard < 40:
            guard += 1
            rarity = run.roll_item_rarity(tier)
            pool = [k for k, it in cfg.items.items()
                    if it.get('rarity') == RARITY_ORDER[rarity]
                    and k not in run.items and k not in offers]
            if not pool:
                pool = [k for k in cfg.items if k not in run.items and k not in offers]
            if not pool:
                break
            offers.append(rng.choice(pool))
        if offers:
            # 真人会挑看着最好的那件，不会闭眼随机拿；用品质当代理
            offers.sort(key=lambda k: RARITY_ORDER.index(cfg.items[k].get('rarity', 'common')),
                        reverse=True)
            run.items.append(offers[0])
        # 通关额外棋子奖励（带稀有度下限）
        minimum = RARITY_ORDER.index(stage.get('clear_reward_min_rarity', 'common'))
        keys = run.reward_options(context, min(minimum, 2))
        choice = policy(run, keys, context)
        if choice:
            run.add_piece(choice)
    return run


def describe(runs, label):
    total = len(runs)
    cleared = sum(1 for r in runs if r.failed_day is None)
    print(f'\n════════ {label}｜{total} 局 ════════')
    print(f'通关率 {cleared}/{total} = {cleared / total:.0%}')
    fails = Counter(r.failed_day for r in runs if r.failed_day)
    if fails:
        print('失败天分布：' + '  '.join(f'第{d}天 {c}局({c / total:.0%})'
                                        for d, c in sorted(fails.items())))
    print(f'\n{"天":>3} {"目标":>5} {"轮":>3} {"收入中位":>9} {"均值":>7} {"P10":>6} {"P90":>6} {"达标率":>7} {"送走变现":>9}')
    for i, stage in enumerate(runs[0].cfg.stages):
        vals = [r.day_income[i] for r in runs if len(r.day_income) > i]
        if not vals:
            continue
        dis = [r.day_dismiss_income[i] for r in runs if len(r.day_dismiss_income) > i]
        vals_sorted = sorted(vals)
        p10 = vals_sorted[int(len(vals_sorted) * 0.1)]
        p90 = vals_sorted[min(len(vals_sorted) - 1, int(len(vals_sorted) * 0.9))]
        target = int(stage['target'])
        ok = sum(1 for v in vals if v >= target) / len(vals)
        print(f'{i + 1:>3} {target:>5} {int(stage["rounds"]):>3} '
              f'{statistics.median(vals):>9.0f} {statistics.mean(vals):>7.1f} '
              f'{p10:>6} {p90:>6} {ok:>6.0%} {statistics.mean(dis):>9.1f}')
    finals = [sum(r.day_income) for r in runs]
    print(f'\n全程总收入：中位 {statistics.median(finals):.0f}｜'
          f'均值 {statistics.mean(finals):.1f}｜'
          f'标准差 {statistics.pstdev(finals):.1f}｜'
          f'区间 [{min(finals)}, {max(finals)}]')
    print('每轮收入（按天）：' + '  '.join(
        f'D{i + 1} {statistics.mean([r.day_income[i] / int(runs[0].cfg.stages[i]["rounds"]) for r in runs if len(r.day_income) > i]):.1f}'
        for i in range(len(runs[0].cfg.stages))
        if any(len(r.day_income) > i for r in runs)))
    pools = [len(r.pool) for r in runs]
    print(f'终局名册：中位 {statistics.median(pools):.0f}｜'
          f'出场率 {BOARD_SIZE / statistics.median(pools):.0%}｜'
          f'送走次数均值 {statistics.mean([r.dismissals for r in runs]):.1f}')
    unsupported = Counter()
    for r in runs:
        unsupported.update(r.unsupported)
    if unsupported:
        print(f'未覆盖机制：{dict(unsupported)}')
        print('本次模拟结果无效；必须先补齐上述机制，禁止用于调整正式数值。')
        return False
    return True


# ────────────────────────── 数值变体 ──────────────────────────

def variant_proposed(cfg):
    """v2 数值模型。

    诊断：产出被 16 格硬顶住（D4 之后每轮收入停滞在 45-51），目标却线性爬到
    164，全靠中期盈余结转硬扛。送走几乎无人问津（0.1 次/局）。

    三处改动：
      A 送走改为「浓缩」：离场棋子按稀有度把 1/2/3 点永久收益转移给同类最强者，
        现金牌另按天数缩放（base × (1+0.25×(天-1))）。这是唯一能突破 16 格
        天花板的成长通道。
      B 下班券供给与名册膨胀同构：通关给 max(1, (名册-16)//3) 张。
      C 目标曲线按实测产出重标定：前两天减压，后三天跟上浓缩带来的成长。
    """
    cfg.model_v2 = True   # 旧变体：配置未接入时也强制打开，便于对照
    new_targets = [18, 46, 88, 145, 205, 280]
    new_rounds = [3, 4, 4, 5, 5, 5]
    for i, stage in enumerate(cfg.stages):
        stage['target'] = new_targets[i]
        stage['rounds'] = new_rounds[i]


ITEM_REBALANCE = {
    # key: (新品质, 说明)  —— 数值改动在 rebalance_item_rules 里
    'catApron': ('rare', '所有猫与幼崽 +1，实测 +5.3/波，是全表最强盘面道具'),
    'v3Item107': ('special', '每波 +8'),
    'v3Item083': ('rare', '每波 +5'),
    'houseSpecial': ('rare', '条件放宽到 2 猫 2 客'),
    'goldenRegister': ('rare', '每件物件 +2，随收集成长'),
    'reservationBook': ('rare', '多 1 个备选，实测 +91/局'),
    'v3Item066': ('uncommon', '每波 +3'),
    'matchingCushions': ('uncommon', '2 连同名即翻倍'),
    'panoramicWindow': ('uncommon', '斜角相邻对所有棋子生效'),
    'doubleTray': ('uncommon', '本波第一个结算的物件翻倍'),
    'recyclingBin': ('uncommon', '每送走 3 位给 12 金币 + 1 券'),
    'luckyPaw': ('uncommon', '每只猫 +12% 稀有权重'),
    'emergencyThermos': ('uncommon', '保险类，不参与数值梯度'),
    'v3Item049': ('common', '收入为 3 倍数时 +6'),
    'v3Item033': ('common', '每波 +1'),
    'quietBell': ('common', '本波收入 ≤28 时 +6'),
    'snackShelf': ('common', '咖啡/点心/糖果/蛋糕 +2（原引用的 milk 不存在）'),
    'stampCard': ('common', '每次选牌 +2'),
}


def rebalance_item_rules(cfg):
    """道具数值重排：只改现有 schema 能表达的字段。"""
    def each(owner, trigger=None):
        pools = (cfg.round_end_rules + cfg.modify_rules
                 + [r for rs in cfg.other_rules.values() for r in rs])
        for rule in pools:
            if rule.get('owner_key') == owner and (trigger is None or rule['trigger'] == trigger):
                yield rule

    for rule in each('v3Item107'):
        rule['base_value'] = 8
    for rule in each('v3Item083'):
        rule['base_value'] = 5
    for rule in each('v3Item066'):
        rule['base_value'] = 3
    for rule in each('v3Item049'):
        rule['base_value'] = 6
    for rule in each('goldenRegister'):
        rule['primary_factor'] = 2
    for rule in each('houseSpecial'):
        rule['primary_threshold'] = 2
        rule['secondary_threshold'] = 2
    for rule in each('matchingCushions'):
        rule['primary_threshold'] = 2
    for rule in each('quietBell'):
        rule['primary_threshold'] = 28
        rule['base_value'] = 6
    for rule in each('snackShelf'):
        rule['source_keys'] = 'pastry|coffee|candy|cherryCake|americanCoffee|championBlend|driedCheese'
    for rule in each('panoramicWindow'):
        rule['source_kinds'] = ''
        rule['source_keys'] = ''
    for rule in each('doubleTray'):
        # 原来挂在 consume_self 上，而可消耗牌几乎不存在（且双券礼袋是负收益，
        # 翻倍等于加倍惩罚）。改挂「本波第一个结算的物件」，once_per_round 天然成立。
        rule['primary_scope'] = 'none'
        rule['primary_comparator'] = 'always'
        rule['primary_threshold'] = 0
        rule['source_kinds'] = 'Prop'
    for rule in each('luckyPaw'):
        rule['multiplier'] = 0.12
    for rule in each('recyclingBin'):
        if rule.get('operation') == 'income':
            rule['base_value'] = 12
    # 双券礼袋：每波 −12 在第 1 关就是一击必杀（当时每波总收入才 9）。
    # 它结算后自我移除，本质是「一次性花钱买两张券」，代价压到 −4 才合理。
    for rule in cfg.round_rules.get('doubleCouponBag', []):
        if rule.get('operation') == 'income' and int(rule.get('base_value') or 0) < 0:
            rule['base_value'] = -4

    for key, (rarity, _note) in ITEM_REBALANCE.items():
        if key in cfg.items:
            cfg.items[key]['rarity'] = rarity


def variant_v3(cfg):
    """v3：v2 的送走浓缩 + 道具重排 + 按通关率标定的目标曲线。"""
    variant_proposed(cfg)
    rebalance_item_rules(cfg)
    targets = getattr(cfg, 'tuned_targets', None) or [17, 58, 116, 198, 294, 412]
    rounds = [3, 4, 4, 5, 5, 5]
    for i, stage in enumerate(cfg.stages):
        stage['target'] = targets[i]
        stage['rounds'] = rounds[i]


VARIANTS = {'current': None, 'proposed': variant_proposed, 'v3': variant_v3}
POLICIES.update({'greedy': greedy_policy, 'random': random_policy,
                 'hoard': hoard_policy, 'casual': casual_policy,
                 'naive': naive_greedy_policy, 'synergy': synergy_policy})
greedy_policy.valuation = estimate_value
hoard_policy.valuation = estimate_value
casual_policy.valuation = estimate_value
random_policy.valuation = estimate_value
naive_greedy_policy.valuation = flat_value
synergy_policy.valuation = estimate_value


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runs', type=int, default=100)
    parser.add_argument('--seed', type=int, default=20260818)
    parser.add_argument('--variant', default='current', choices=list(VARIANTS))
    parser.add_argument('--config', default=CONFIG)
    parser.add_argument('--policy', default='greedy',
                        choices=['greedy', 'random', 'hoard', 'casual', 'naive', 'synergy'])
    parser.add_argument('--no-dismiss', action='store_true')
    args = parser.parse_args()

    cfg = Config(args.config, VARIANTS[args.variant])
    missing = {key: values for key, values in cfg.unsupported_protocol.items() if values}
    if missing:
        print('balance_sim 尚未覆盖当前目标模式协议：')
        for key, values in missing.items():
            print(f'  {key}: {values}')
        print('拒绝输出通关率，避免用不完整模拟结果调整正式数值。')
        return 2
    policy = POLICIES[args.policy]
    runs = [simulate(cfg, args.seed + i, policy, not args.no_dismiss)
            for i in range(args.runs)]
    valid = describe(
        runs, f'{args.variant} / {args.policy}' + ('（不送走）' if args.no_dismiss else ''))
    return 0 if valid else 2


if __name__ == '__main__':
    raise SystemExit(main())
