#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""机制闭包体检：表里配的机制，代码是不是真的会去取、去执行。

规则引擎只有一个取规则的入口：

    ConfiguredRules(trigger, ownerType)

它**同时**按 trigger 和 owner_type 过滤。所以一条规则要真的跑起来，需要
(trigger, owner_type) 这一对在代码里有对应的调用点——只对上 trigger 不够。
历史上出过的静默失效就是这种：规则写成 owner_type=element，
而代码那处只取 item，于是规则安安静静地一次都没执行过。

查四个方向：

  1 表 → 代码调用点   (trigger, owner_type) 有没有人取     ← 取不到 = 规则是死的
  2 表 → 代码分支     取到之后，operation 有没有对应分支   ← 没分支 = 取到了也不做事
  3 代码 → 表         代码支持但表里没用到的机制           ← 未接入的能力
  4 作用域双向        scope 在代码 / 在模拟器

用法：python Tools/CatCafeConfig/audit_mechanics.py [--verbose]
"""

import argparse
import json
import os
import re
import sys
from collections import defaultdict

CONFIG = os.path.join('Assets', 'Resources', 'GameData', 'cat_cafe_config.json')
SOURCE = os.path.join('Assets', 'Scripts', 'CatCafe', 'CatCafeGameController.cs')
SIM = os.path.join('Tools', 'CatCafeConfig', 'balance_sim.py')



# EvaluateItemTrigger 是道具侧的通用分发器：它用变量 trigger 调
# ConfiguredRules(trigger, "item")，所以凡是传给它的 trigger 都算有调用点。
# 它统一处理下面这组算子，与具体 trigger 无关。
ITEM_GENERIC_OPS = {'income', 'add_removal', 'add_reroll',
                    'generate', 'generate_random', 'generate_source'}


def code_dispatch(text):
    """所有能取到规则的 (trigger, ownerType)。

    两条路径：字面量 ConfiguredRules("t","o")，以及经 EvaluateItemTrigger /
    ApplyImmediateItemRules 传进去的 trigger（那条路固定取 owner_type=item）。
    只认字面量会把 on_choose / on_consume 这些误判成"取不到"。
    """
    pairs = set(re.findall(r'ConfiguredRules\(\s*"([a-z_]+)"\s*,\s*"([a-z]+)"\s*\)', text))
    for trigger in re.findall(
            r'(?:EvaluateItemTrigger|ApplyImmediateItemRules)\(\s*"([a-z_]+)"', text):
        pairs.add((trigger, 'item'))
    return pairs


def methods_of(text):
    """(方法起始偏移, 方法名) 列表，用来判断某处代码落在哪个方法里。"""
    return [(m.start(), m.group(1)) for m in re.finditer(
        r'\n        (?:private|public|internal)[^\n(]*?(\w+)\s*\(', text)]


def enclosing(methods, pos):
    name = '(顶层)'
    for start, mname in methods:
        if start <= pos:
            name = mname
        else:
            break
    return name


def method_body(text, methods, name):
    for i, (start, mname) in enumerate(methods):
        if mname != name:
            continue
        end = methods[i + 1][0] if i + 1 < len(methods) else len(text)
        return text[start:end]
    return ''


def expand(text, methods, bodies, names):
    """names 及它们直接调用到的方法名（跟一跳）。顺便把方法体缓存进 bodies。"""
    known = {name for _, name in methods}
    result = set(names)
    for name in list(names):
        if name not in bodies:
            bodies[name] = method_body(text, methods, name)
        for callee in re.findall(r'(\w+)\s*\(', bodies[name]):
            if callee in known:
                result.add(callee)
    for name in result:
        if name not in bodies:
            bodies[name] = method_body(text, methods, name)
    return result


def trigger_handlers(text, methods):
    """trigger -> 处理它的方法名集合。

    从 ConfiguredRules("t", ...) 的所在方法反推，比手写一张 HANDLED 表可靠：
    代码加了新分支不用回来同步，也不会把自己漏登记的东西报成游戏 bug。
    round 触发在结算主循环里内联遍历，单独指过去。
    """
    handlers = defaultdict(set)
    for m in re.finditer(r'ConfiguredRules\(\s*"([a-z_]+)"', text):
        handlers[m.group(1)].add(enclosing(methods, m.start()))
    for m in re.finditer(r'(?:EvaluateItemTrigger|ApplyImmediateItemRules)\(\s*"([a-z_]+)"', text):
        handlers[m.group(1)].add('EvaluateItemTrigger')
    handlers['round'].add('CalculateEvents')
    return handlers


def code_scopes(text):
    body = text[text.index('private int EvaluateScope'):]
    body = body[:body.index('\n        private ', 10)]
    return set(re.findall(r'scope == "([a-z_]+)"', body)) | {'none', ''}


def sim_scopes(text):
    start = text.index('def scope(')
    return set(re.findall(r"'([a-z_]+)'", text[start:start + 9000]))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--verbose', action='store_true')
    args = parser.parse_args()

    with open(CONFIG, encoding='utf-8') as handle:
        cfg = json.load(handle)
    source = open(SOURCE, encoding='utf-8').read()
    rules = [r for r in cfg['rules'] if r.get('enabled')]

    dispatch = code_dispatch(source)
    # round 触发在结算主循环里直接遍历，不走 ConfiguredRules 的 owner_type 过滤
    inline_triggers = {'round'}

    used_pairs = defaultdict(list)
    for rule in rules:
        used_pairs[(rule.get('trigger'), rule.get('owner_type'))].append(rule['rule_id'])

    # ── 1 取不到的 (trigger, owner_type) ──
    unreached = []
    for (trigger, owner_type), ids in sorted(used_pairs.items()):
        if trigger in inline_triggers:
            continue
        if (trigger, owner_type) not in dispatch:
            unreached.append((trigger, owner_type, ids))

    # ── 2 取到了但没有 operation 分支 ──
    # 判据：这条规则的 trigger 由哪几个方法处理，那些方法体里有没有出现它的算子字符串。
    methods = methods_of(source)
    handlers = trigger_handlers(source, methods)
    bodies = {}
    no_branch = []
    for rule in rules:
        trigger, op = rule.get('trigger'), rule.get('operation')
        names = handlers.get(trigger) or set()
        if not names:
            no_branch.append((rule['rule_id'], trigger, op, '没有方法处理这个 trigger'))
            continue
        # 分发方法常常把规则转手给一个 helper（RollRarity → ApplyRarityWeightRules），
        # 所以除了它自己的方法体，还要看它调用的那些方法体。只跟一跳，够用了。
        found = False
        for name in expand(source, methods, bodies, names):
            if '"%s"' % op in bodies[name]:
                found = True
                break
        if not found:
            no_branch.append((rule['rule_id'], trigger, op,
                              '处理方法 %s 里没有这个算子分支' % '/'.join(sorted(names))))

    # ── 3 代码支持但表里没用到 ──
    used_ops = {(r.get('trigger'), r.get('operation')) for r in rules}
    unused = sorted((t, o) for t, names in handlers.items()
                    for name in names
                    for o in set(re.findall(r'operation == "([a-z_]+)"',
                                            bodies.setdefault(name, method_body(source, methods, name))))
                    if (t, o) not in used_ops)

    # ── 4 作用域 ──
    scopes_used = {r.get(f) for r in rules for f in ('primary_scope', 'secondary_scope') if r.get(f)}
    in_code = code_scopes(source)
    in_sim = sim_scopes(open(SIM, encoding='utf-8').read())
    scope_missing_code = sorted(s for s in scopes_used if s not in in_code)
    scope_missing_sim = sorted(s for s in scopes_used if s not in in_sim)

    print('启用规则 %d 条  ·  用到 %d 组 (trigger, owner_type)  ·  代码调用点 %d 组'
          % (len(rules), len(used_pairs), len(dispatch)))

    print('\n── 1 表里配了、代码取不到的 (trigger, owner_type)（%d）'
          '——这些规则一次都不会执行 ──' % len(unreached))
    for trigger, owner_type, ids in unreached:
        print('   %-26s owner_type=%-9s %d 条规则' % (trigger, owner_type, len(ids)))
        show = ids if args.verbose else ids[:4]
        for rid in show:
            print('        %s' % rid)
        if len(ids) > len(show):
            print('        …还有 %d 条' % (len(ids) - len(show)))
    if not unreached:
        print('   无')

    print('\n── 2 取到了但没有算子分支（%d）──' % len(no_branch))
    for rid, trigger, op, why in no_branch[:20]:
        print('   %-40s %s.%s  %s' % (rid, trigger, op, why))
    if len(no_branch) > 20:
        print('   …还有 %d 条' % (len(no_branch) - 20))
    if not no_branch:
        print('   无')

    print('\n── 3 代码支持、表里没用到的机制（%d）──' % len(unused))
    print('   ' + '、'.join('%s.%s' % p for p in unused[:24]) + ('…' if len(unused) > 24 else ''))

    print('\n── 4 作用域 ──')
    print('   表里用到 %d 个｜代码缺 %d 个｜模拟器缺 %d 个'
          % (len(scopes_used), len(scope_missing_code), len(scope_missing_sim)))
    if scope_missing_code:
        print('   代码缺失（真 bug）: ' + '、'.join(scope_missing_code))
    if scope_missing_sim:
        print('   模拟器缺失（只影响数值模拟）: ' + '、'.join(scope_missing_sim))

    bad = bool(unreached or no_branch or scope_missing_code)
    print('\n结论：%s' % ('机制闭包完整' if not bad else '未闭包，见 1/2/4'))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
