#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""配置 × 代码 一致性审计：找出「表里配了、代码不认」和「引用了不存在的东西」。

规则表是自由文本，写错一个算子名不会报错——规则只是静默失效，看起来像"这张牌就是弱"。
这个脚本把 Rules 表用到的 trigger/operation/scope 和 CatCafeGameController 实际处理的
集合对一遍，再校验所有 key 引用。

用法：python Tools/CatCafeConfig/audit_rules.py
"""

import json
import os
import re
import sys

CONFIG = os.path.join('Assets', 'Resources', 'GameData', 'cat_cafe_config.json')
SOURCE = os.path.join('Assets', 'Scripts', 'CatCafe', 'CatCafeGameController.cs')

# 每个 trigger 下，代码真正分支处理的 operation
HANDLED = {
    'round': {'income', 'set_income', 'chance_income', 'random_income', 'multiply_income',
              'remove_targets', 'generate', 'generate_random',
              'generate_history_random', 'transform', 'force_skip', 'force_choose'},
    'modify_income': {'add', 'multiply', 'set_max_adjacent'},
    'round_end': {'income', 'add_removal', 'add_reroll', 'add_inspiration', 'generate',
                  'generate_random', 'permanent_add', 'store_value'},
    'on_dismiss': {'income', 'add_removal', 'add_reroll', 'add_inspiration', 'generate',
                   'generate_random', 'set_reward_minimum', 'transfer_permanent'},
    'on_consume': {'income', 'add_removal', 'add_reroll', 'generate',
                   'generate_random', 'generate_source'},
    'on_choose': {'income'},
    'on_skip': {'income'},
    'on_removal_spent': {'income', 'add_removal', 'add_reroll', 'generate'},
    'on_reroll_spent': {'income', 'add_removal', 'add_reroll', 'generate'},
    'on_click': {'income', 'add_removal', 'add_reroll', 'generate', 'choose_generate',
                 'generate_random', 'set_reward_minimum', 'skip_last_round'},
    'before_round': {'income', 'store_removed'},
    'adjacency': {'include_diagonal', 'global_all', 'global_corners', 'global_key'},
    'rarity_weights': {'scale', 'multiply'},
    'reward_options': {'add_count'},
    'stage_deadline': {'extra_round'},
    'prevent_remove': {'prevent_remove', 'immune'},
    'on_external_bonus': {'permanent_add'},
    'on_any_dismiss': {'permanent_add', 'cycle_reduce'},
    'on_random_result': {'consume_self'},
    'on_external_granted': {'consume_at_count'},
    'modify_random_income': {'reroll', 'set_max'},
    'modify_rule_triggers': {'add_count'},
    'item_round_action': {'remove_targets', 'transform'},
    'modify_generated_result': {'rarity_filter'},
    'modify_transform_result': {'rarity_random'},
    'item_stack_limit': {'max_count'},
    'item_stack_resolution': {'cashout'},
    'reward_sequence': {'add_choice'},
    'stage_clear': {'add_item_choice'},
    'stage_settlement': {'extra_round', 'waive_payment_generate'},
    'before_settlement': {'swap_two', 'shuffle_column'},
    'modify_round_events': {'multiply_targets', 'set_targets_zero'},
    'modify_rule_chance': {'multiply'},
    'modify_target_limit': {'add'},
    'modify_money_loss': {'reduce_loss'},
    'modify_dismiss_income': {'multiply'},
    'pool_limit': {'max_count'},
    'pool_rarity': {'set'},
    'cycle': {'add'},
    'element_enter': {'transform'},
    'suppress_rules': {'suppress'},
}
COMPARATORS = {'always', 'eq', 'ne', 'ge', 'gt', 'le', 'lt', 'modulo_zero', ''}
KIND_TOKENS = {'cat', 'kitten', 'guest', 'prop', 'staff', 'item'}   # item 是 prop 的旧别名
RARITY_TOKENS = {'common', 'uncommon', 'rare', 'special'}


def code_scopes(text):
    """从 EvaluateScope 里抓出所有被处理的 scope 名。"""
    body = text[text.index('private int EvaluateScope'):]
    body = body[:body.index('\n        private ', 10)]
    return set(re.findall(r'scope == "([a-z_]+)"', body)) | {'none', ''}


def tokens(value):
    if not value:
        return []
    return [t.strip() for t in str(value).split('|') if t.strip()]


def main():
    with open(CONFIG, encoding='utf-8') as handle:
        cfg = json.load(handle)
    with open(SOURCE, encoding='utf-8') as handle:
        source = handle.read()

    scopes = code_scopes(source)
    elements = {e['key'] for e in cfg['elements']}
    items = {i['key'] for i in cfg['items']}
    weights = {w['context'] for w in cfg['weights'] if w.get('enabled')}
    rules = [r for r in cfg['rules'] if r.get('enabled')]

    problems = []

    for rule in rules:
        rid = rule['rule_id']
        trigger = rule['trigger']
        operation = rule['operation']

        if trigger not in HANDLED:
            problems.append(('未知 trigger', rid, f'{trigger} 没有任何代码分支处理'))
        elif operation not in HANDLED[trigger]:
            problems.append(('算子不被处理', rid,
                             f'{trigger} 下的 {operation}；代码只认 '
                             f'{"/".join(sorted(HANDLED[trigger]))}'))

        for field in ('primary_scope', 'secondary_scope'):
            scope = rule.get(field) or ''
            if scope and scope not in scopes:
                problems.append(('未知 scope', rid, f'{field}={scope}'))

        for field in ('primary_comparator', 'secondary_comparator'):
            if (rule.get(field) or '') not in COMPARATORS:
                problems.append(('未知比较符', rid, f'{field}={rule.get(field)}'))

        owner = rule.get('owner_key')
        if owner and owner != '*':
            pool = elements if rule.get('owner_type') == 'element' else items
            if owner not in pool:
                problems.append(('owner 不存在', rid, f'{rule.get("owner_type")}:{owner}'))

        for token in tokens(rule.get('source_keys')):
            if token == '*':
                continue
            if token not in elements and token not in RARITY_TOKENS:
                problems.append(('引用不存在的棋子', rid, f'source_keys={token}'))

        result_kind = operation in ('generate', 'generate_random', 'transform')
        rarity_result = operation == 'set_reward_minimum' or trigger == 'pool_rarity'
        if result_kind or rarity_result:
            for token in tokens(rule.get('result_key')):
                valid = token in elements if result_kind else token in RARITY_TOKENS
                if not valid:
                    label = '棋子' if result_kind else '稀有度'
                    problems.append((f'引用不存在的{label}', rid, f'result_key={token}'))

        for field in ('primary_filter', 'secondary_filter'):
            scope = rule.get(field.replace('_filter', '_scope')) or ''
            for token in tokens(rule.get(field)):
                if scope == 'board_key_or_adjacent_key':
                    for key in str(rule.get(field) or '').split(';'):
                        if key and key not in elements:
                            problems.append(('引用不存在的棋子', rid, f'{field}={key}'))
                    break
                if scope in ('board_key', 'same_row_key', 'adjacent_key', 'pool_key'):
                    if token not in elements:
                        problems.append(('引用不存在的棋子', rid, f'{field}={token}'))
                elif scope in ('owned_item_key', 'owned_item_rounds', 'item_counter'):
                    if token not in items:
                        problems.append(('引用不存在的道具', rid, f'{field}={token}'))
                elif scope == 'item_counter_capped':
                    key = str(rule.get(field) or '').split('|', 1)[0]
                    if key not in items:
                        problems.append(('引用不存在的道具', rid, f'{field}={key}'))
                    break
                elif scope.endswith('_kind'):
                    if token.lower() not in KIND_TOKENS:
                        problems.append(('未知 kind', rid, f'{field}={token}'))

        for field in ('remove_filter', 'target_filter'):
            scope = rule.get(field.replace('_filter', '_scope')) or ''
            if 'key' not in scope:
                continue
            for token in tokens(rule.get(field)):
                if token != '*' and token not in elements:
                    problems.append(('引用不存在的棋子', rid, f'{field}={token}'))

        if operation in ('generate', 'transform') and not rule.get('result_key'):
            problems.append(('缺 result_key', rid, operation))

    # 反向：代码认得、但表里一次都没用过的算子（可能是没接上的功能）
    used = {(r['trigger'], r['operation']) for r in rules}
    unused = [(t, o) for t, ops in HANDLED.items() for o in ops if (t, o) not in used]

    # 棋子/道具引用完整性
    for element in cfg['elements']:
        for field in ('grown_form',):
            value = element.get(field)
            if value and value not in elements:
                problems.append(('棋子引用不存在', element['key'], f'{field}={value}'))
    for row in (r for r in cfg['breeding'] if r.get('enabled')):
        parent_a = row.get('parent_a') or ''
        parent_b = row.get('parent_b') or ''
        child = row.get('child') or ''
        result_mode = row.get('result_mode') or ''
        where = f'{parent_a}+{parent_b}'
        parent_a_wildcard = parent_a == '*'
        parent_b_wildcard = parent_b == '*'

        if parent_a_wildcard != parent_b_wildcard:
            problems.append(('繁殖表通配配置错误', where, 'parent_a 与 parent_b 必须同时为 *'))
        elif parent_a_wildcard:
            if result_mode != 'rarity_random':
                problems.append(('繁殖表通配配置错误', where, 'result_mode 必须为 rarity_random'))
            if child:
                problems.append(('繁殖表通配配置错误', where, f'child 必须留空，当前为 {child}'))
            context = row.get('rarity_context') or ''
            if context not in weights:
                problems.append(('繁殖表引用不存在', where, f'rarity_context={context}'))
        else:
            for field in ('parent_a', 'parent_b', 'child'):
                value = row.get(field)
                if value and value not in elements:
                    problems.append(('繁殖表引用不存在', where, f'{field}={value}'))

        for field in ('mutation_child',):
            value = row.get(field)
            if value and value not in elements:
                problems.append(('繁殖表引用不存在', where, f'{field}={value}'))

    if problems:
        print(f'发现 {len(problems)} 处问题：\n')
        current = None
        for kind, where, detail in sorted(problems):
            if kind != current:
                current = kind
                print(f'── {kind} ──')
            print(f'  {where:<34}{detail}')
    else:
        print('未发现配置与代码不一致的地方。')

    if unused:
        print(f'\n── 代码支持但表里没用到的算子（{len(unused)} 个）──')
        for trigger, operation in sorted(unused):
            print(f'  {trigger}.{operation}')

    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
