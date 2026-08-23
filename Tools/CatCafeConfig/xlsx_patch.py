#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""xlsx 定点补丁：改单元格 / 追加行，只替换被动到的 worksheet 部件。

为什么不用 openpyxl：它会把整个工作簿重写一遍，实测会丢掉 sharedStrings.xml 和
Excel 365 的 featurePropertyBag.xml。策划表是多人协作的源文件，能不动的部件一律不动。

写入一律用 inlineStr（C# 导出器和 Excel 都认），这样不必去动共享字符串表。
"""

import re
import shutil
import zipfile

CELL_RE = r'<(?:\w+:)?c\b[^>]*/>|<(?:\w+:)?c\b.*?</(?:\w+:)?c>'
ROW_RE = r'<(?:\w+:)?row[^>]*>.*?</(?:\w+:)?row>|<(?:\w+:)?row[^>]*/>'


def _esc(value):
    return (str(value).replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;'))


class Workbook:
    def __init__(self, path):
        self.path = path
        with zipfile.ZipFile(path) as zf:
            self.names = zf.namelist()
            self.parts = {name: zf.read(name) for name in self.names}
            self.infos = {i.filename: i for i in zf.infolist()}
        wbx = self.parts['xl/workbook.xml'].decode('utf-8-sig')
        rels = self.parts['xl/_rels/workbook.xml.rels'].decode('utf-8-sig')
        self.sheets = {}
        for match in re.finditer(r'<(?:\w+:)?sheet[^>]*name="([^"]+)"[^>]*r:id="([^"]+)"', wbx):
            name, rid = match.group(1), match.group(2)
            rel = next(r for r in re.findall(r'<Relationship[^>]*/>', rels) if 'Id="%s"' % rid in r)
            target = re.search(r'Target="([^"]+)"', rel).group(1)
            self.sheets[name] = 'xl/' + target.lstrip('/').replace('xl/', '', 1)
        self.shared = []
        if 'xl/sharedStrings.xml' in self.parts:
            ss = self.parts['xl/sharedStrings.xml'].decode('utf-8-sig')
            self.shared = [re.sub(r'<[^>]+>', '', m)
                           for m in re.findall(r'<(?:\w+:)?si>(.*?)</(?:\w+:)?si>', ss, re.S)]
        self._xml = {}
        self.edits = 0

    # ── 读 ──
    def xml(self, sheet):
        if sheet not in self._xml:
            self._xml[sheet] = self.parts[self.sheets[sheet]].decode('utf-8-sig')
        return self._xml[sheet]

    def _prefix(self, sheet):
        m = re.match(r'(?:﻿)?<\?xml.*?\?>\s*<(\w+):', self.xml(sheet))
        return m.group(1) + ':' if m else ''

    def cell_text(self, cell):
        t = re.search(r'\bt="([^"]+)"', cell)
        v = re.search(r'<(?:\w+:)?v>(.*?)</(?:\w+:)?v>', cell, re.S)
        if t and t.group(1) == 's' and v:
            index = int(v.group(1))
            return self.shared[index] if index < len(self.shared) else ''
        m = re.search(r'<(?:\w+:)?is>.*?<(?:\w+:)?t[^>]*>(.*?)</(?:\w+:)?t>', cell, re.S)
        return m.group(1) if m else (v.group(1) if v else '')

    def rows(self, sheet):
        return re.findall(ROW_RE, self.xml(sheet), re.S)

    def row_values(self, row):
        out = {}
        for cell in re.findall(CELL_RE, row, re.S):
            ref = re.search(r'r="([A-Z]+)\d+"', cell)
            if ref:
                out[ref.group(1)] = self.cell_text(cell)
        return out

    def find_row(self, sheet, key, column='A'):
        """按某列的值找行，返回 (行 xml, 行号)。"""
        for row in self.rows(sheet):
            values = self.row_values(row)
            if values.get(column) == key:
                number = int(re.search(r'r="(\d+)"', row).group(1))
                return row, number
        return None, None

    def header_map(self, sheet, header_row=3):
        """第 3 行是英文字段名，返回 {字段名: 列字母}。"""
        for row in self.rows(sheet):
            if int(re.search(r'r="(\d+)"', row).group(1)) == header_row:
                return {v: k for k, v in self.row_values(row).items() if v}
        return {}

    # ── 写 ──
    def set_cell(self, sheet, row_key, field, value, header_row=3, key_column='A'):
        columns = self.header_map(sheet, header_row)
        if field not in columns:
            raise KeyError('%s 表没有字段 %s' % (sheet, field))
        column = columns[field]
        row, number = self.find_row(sheet, row_key, key_column)
        if row is None:
            raise KeyError('%s 表找不到行 %s' % (sheet, row_key))
        ref = '%s%d' % (column, number)
        prefix = self._prefix(sheet)
        target = None
        for cell in re.findall(CELL_RE, row, re.S):
            if re.search(r'r="%s"' % ref, cell):
                target = cell
                break
        style = re.search(r'\bs="(\d+)"', target) if target else None
        new = ('<%sc r="%s"%s t="inlineStr"><%sis><%st>%s</%st></%sis></%sc>'
               % (prefix, ref, (' s="%s"' % style.group(1)) if style else '',
                  prefix, prefix, _esc(value), prefix, prefix, prefix))
        if target:
            new_row = row.replace(target, new)
        else:                                   # 该行原本没有这一格，按列序插进去
            cells = re.findall(CELL_RE, row, re.S)
            insert_at = len(cells)
            for i, cell in enumerate(cells):
                other = re.search(r'r="([A-Z]+)\d+"', cell).group(1)
                if (len(other), other) > (len(column), column):
                    insert_at = i
                    break
            new_cells = cells[:insert_at] + [new] + cells[insert_at:]
            body = ''.join(new_cells)
            head = row[:row.index(cells[0])] if cells else row[:row.rindex('</')]
            new_row = head + body + '</%srow>' % prefix
        self._xml[sheet] = self.xml(sheet).replace(row, new_row, 1)
        self.edits += 1

    def set_cell_ref(self, sheet, row_number, column, value):
        """按坐标写单元格，用于新增列时先补表头（表头行本身不在 header_map 里）。"""
        prefix = self._prefix(sheet)
        ref = '%s%d' % (column, row_number)
        target_row = None
        for row in self.rows(sheet):
            if int(re.search(r'r="(\d+)"', row).group(1)) == row_number:
                target_row = row
                break
        if target_row is None:
            raise KeyError('%s 表没有第 %d 行' % (sheet, row_number))
        cells = re.findall(CELL_RE, target_row, re.S)
        existing = next((c for c in cells if re.search(r'r="%s"' % ref, c)), None)
        new_cell = ('<%sc r="%s" t="inlineStr"><%sis><%st>%s</%st></%sis></%sc>'
                    % (prefix, ref, prefix, prefix, _esc(value), prefix, prefix, prefix))
        if existing:
            new_row = target_row.replace(existing, new_cell)
        else:
            insert_at = len(cells)
            for i, cell in enumerate(cells):
                other = re.search(r'r="([A-Z]+)\d+"', cell).group(1)
                if (len(other), other) > (len(column), column):
                    insert_at = i
                    break
            body = ''.join(cells[:insert_at] + [new_cell] + cells[insert_at:])
            head = target_row[:target_row.index(cells[0])] if cells else target_row[:target_row.rindex('</')]
            new_row = head + body + '</%srow>' % prefix
        self._xml[sheet] = self.xml(sheet).replace(target_row, new_row, 1)
        self.edits += 1

    def append_row(self, sheet, values, header_row=3):
        """values: {字段名: 值}，追加到最后一行之后。"""
        columns = self.header_map(sheet, header_row)
        rows = self.rows(sheet)
        last = max(int(re.search(r'r="(\d+)"', r).group(1)) for r in rows)
        number = last + 1
        prefix = self._prefix(sheet)
        cells = []
        for field, value in sorted(values.items(),
                                   key=lambda kv: (len(columns[kv[0]]), columns[kv[0]])):
            if value is None or value == '':
                continue
            ref = '%s%d' % (columns[field], number)
            cells.append('<%sc r="%s" t="inlineStr"><%sis><%st>%s</%st></%sis></%sc>'
                         % (prefix, ref, prefix, prefix, _esc(value),
                            prefix, prefix, prefix))
        row = '<%srow r="%d">%s</%srow>' % (prefix, number, ''.join(cells), prefix)
        xml = self.xml(sheet)
        anchor = '</%ssheetData>' % prefix
        self._xml[sheet] = xml.replace(anchor, row + anchor, 1)
        # dimension 跟着扩，Excel 打开时不会提示修复
        self._xml[sheet] = re.sub(
            r'(<(?:\w+:)?dimension ref="[A-Z]+\d+:[A-Z]+)(\d+)"',
            lambda m: '%s%d"' % (m.group(1), max(int(m.group(2)), number)),
            self._xml[sheet], count=1)
        self.edits += 1
        return number

    def insert_row(self, sheet, after_row, values, header_row=3):
        """在 after_row 之后插入一行，后续行整体下移。

        Tutorial 这类表的行序就是玩家看到的顺序（字条回看列表按 sheet 顺序渲染），
        新beat 必须插在正确的位置，append 到表尾会让开局教学排在最后。
        """
        columns = self.header_map(sheet, header_row)
        prefix = self._prefix(sheet)
        number = after_row + 1
        xml = self.xml(sheet)
        for row in sorted(self.rows(sheet),
                          key=lambda r: -int(re.search(r'r="(\d+)"', r).group(1))):
            old = int(re.search(r'r="(\d+)"', row).group(1))
            if old < number:
                continue
            shifted = re.sub(r'(<(?:\w+:)?row[^>]*\br=")\d+"',
                             lambda m: '%s%d"' % (m.group(1), old + 1), row, count=1)
            shifted = re.sub(r'(\br="[A-Z]+)%d"' % old,
                             lambda m: '%s%d"' % (m.group(1), old + 1), shifted)
            xml = xml.replace(row, shifted, 1)
        self._xml[sheet] = xml

        cells = []
        for field, value in sorted(values.items(),
                                   key=lambda kv: (len(columns[kv[0]]), columns[kv[0]])):
            if value is None or value == '':
                continue
            cells.append('<%sc r="%s%d" t="inlineStr"><%sis><%st>%s</%st></%sis></%sc>'
                         % (prefix, columns[field], number, prefix, prefix,
                            _esc(value), prefix, prefix, prefix))
        row = '<%srow r="%d">%s</%srow>' % (prefix, number, ''.join(cells), prefix)
        anchor = next(r for r in self.rows(sheet)
                      if int(re.search(r'r="(\d+)"', r).group(1)) == after_row)
        self._xml[sheet] = self.xml(sheet).replace(anchor, anchor + row, 1)
        last = max(int(re.search(r'r="(\d+)"', r).group(1)) for r in self.rows(sheet))
        self._xml[sheet] = re.sub(
            r'(<(?:\w+:)?dimension ref="[A-Z]+\d+:[A-Z]+)(\d+)"',
            lambda m: '%s%d"' % (m.group(1), max(int(m.group(2)), last)),
            self._xml[sheet], count=1)
        self.edits += 1
        return number

    def delete_row(self, sheet, row_key, key_column='A'):
        """按某列的值删掉一整行，后续行整体上移。insert_row 的逆操作。"""
        row, number = self.find_row(sheet, row_key, key_column)
        if row is None:
            return False
        xml = self.xml(sheet).replace(row, '', 1)
        self._xml[sheet] = xml
        for other in sorted(self.rows(sheet),
                            key=lambda r: int(re.search(r'r="(\d+)"', r).group(1))):
            old = int(re.search(r'r="(\d+)"', other).group(1))
            if old <= number:
                continue
            shifted = re.sub(r'(<(?:\w+:)?row[^>]*\br=")\d+"',
                             lambda m: '%s%d"' % (m.group(1), old - 1), other, count=1)
            shifted = re.sub(r'(\br="[A-Z]+)%d"' % old,
                             lambda m: '%s%d"' % (m.group(1), old - 1), shifted)
            self._xml[sheet] = self.xml(sheet).replace(other, shifted, 1)
        self.edits += 1
        return True

    def save(self, backup=True):
        if backup:
            shutil.copy(self.path, self.path + '.bak')
        with zipfile.ZipFile(self.path, 'w', zipfile.ZIP_DEFLATED) as out:
            for name in self.names:
                info = self.infos[name]
                sheet_path = {v: k for k, v in self.sheets.items()}.get(name)
                if sheet_path and sheet_path in self._xml:
                    out.writestr(info, self._xml[sheet_path].encode('utf-8'))
                else:
                    out.writestr(info, self.parts[name])
