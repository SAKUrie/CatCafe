#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ManyFace.CatCafe.Editor
{
    /// <summary>
    /// Unity 内置的猫咖 Excel 导出器。
    /// 直接读取 xlsx（Open XML 压缩包），不依赖 Excel 插件、Python 或第三方导表程序。
    /// </summary>
    internal static class CatCafeExcelExporter
    {
        private static readonly XNamespace SpreadsheetNs =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipNs =
            "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace DocumentRelationshipNs =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private static readonly SheetSpec[] Sheets =
        {
            new SheetSpec("Settings", "settings"),
            new SheetSpec("Rarities", "rarities"),
            new SheetSpec("Pieces", "elements"),
            new SheetSpec("Buffs", "items"),
            new SheetSpec("Stages", "stages"),
            new SheetSpec("Weights", "weights"),
            new SheetSpec("InitialDeck", "initialDeck"),
            new SheetSpec("Rules", "rules"),
            new SheetSpec("Breeding", "breeding"),
            new SheetSpec("Levels", "levels"),
            new SheetSpec("Tutorial", "tutorials"),
            new SheetSpec("Intimacy", "intimacy"),
            new SheetSpec("Invite", "invites")
        };

        /// <summary>
        /// 读取并校验工作簿。checkOnly=false 时才会覆盖运行时 JSON。
        /// 返回各工作表的有效数据条数，供 Unity 菜单显示。
        /// </summary>
        public static string Export(string workbookPath, string outputPath, bool checkOnly)
        {
            JObject result = new JObject();
            using (FileStream stream = new FileStream(
                workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (ZipArchive workbook = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                List<string> sharedStrings = ReadSharedStrings(workbook);
                Dictionary<string, string> sheetTargets = ReadSheetTargets(workbook);

                foreach (SheetSpec sheet in Sheets)
                {
                    string target;
                    if (!sheetTargets.TryGetValue(sheet.ExcelName, out target))
                    {
                        throw new InvalidDataException("工作簿缺少工作表：" + sheet.ExcelName);
                    }

                    SortedDictionary<int, List<object>> rows = ReadRows(workbook, target, sharedStrings);
                    result[sheet.JsonName] = ParseSheet(rows, sheet.ExcelName);
                }
            }

            Validate(result);

            if (!checkOnly)
            {
                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    outputPath,
                    result.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n",
                    new UTF8Encoding(false));
            }

            return string.Join(", ", Sheets.Select(sheet =>
                sheet.ExcelName + "=" + ((JArray)result[sheet.JsonName]).Count));
        }

        private static List<string> ReadSharedStrings(ZipArchive workbook)
        {
            ZipArchiveEntry entry = workbook.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return new List<string>();
            }

            XDocument document = LoadXml(entry);
            return document.Descendants(SpreadsheetNs + "si")
                .Select(ReadText)
                .ToList();
        }

        private static Dictionary<string, string> ReadSheetTargets(ZipArchive workbook)
        {
            XDocument workbookXml = LoadRequiredXml(workbook, "xl/workbook.xml");
            XDocument relationshipsXml = LoadRequiredXml(workbook, "xl/_rels/workbook.xml.rels");

            Dictionary<string, string> targetById = relationshipsXml
                .Descendants(RelationshipNs + "Relationship")
                .ToDictionary(
                    node => RequiredAttribute(node, "Id"),
                    node => RequiredAttribute(node, "Target"));

            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XElement sheet in workbookXml.Descendants(SpreadsheetNs + "sheet"))
            {
                string name = RequiredAttribute(sheet, "name");
                XAttribute relationshipId = sheet.Attribute(DocumentRelationshipNs + "id");
                if (relationshipId == null || !targetById.ContainsKey(relationshipId.Value))
                {
                    throw new InvalidDataException("工作表缺少关系定义：" + name);
                }

                string target = targetById[relationshipId.Value].Replace('\\', '/');
                if (target.StartsWith("/", StringComparison.Ordinal))
                {
                    target = target.TrimStart('/');
                }
                else if (!target.StartsWith("xl/", StringComparison.Ordinal))
                {
                    target = "xl/" + target;
                }

                result[name] = NormalizeEntryPath(target);
            }

            return result;
        }

        private static SortedDictionary<int, List<object>> ReadRows(
            ZipArchive workbook,
            string target,
            IList<string> sharedStrings)
        {
            XDocument sheetXml = LoadRequiredXml(workbook, target);
            SortedDictionary<int, List<object>> rows = new SortedDictionary<int, List<object>>();

            foreach (XElement row in sheetXml.Descendants(SpreadsheetNs + "sheetData")
                         .Elements(SpreadsheetNs + "row"))
            {
                int rowNumber = ParseInt(RequiredAttribute(row, "r"), "无效的 Excel 行号");
                List<object> values = new List<object>();
                foreach (XElement cell in row.Elements(SpreadsheetNs + "c"))
                {
                    string reference = RequiredAttribute(cell, "r");
                    int column = ColumnIndex(reference);
                    while (values.Count <= column)
                    {
                        values.Add(string.Empty);
                    }

                    values[column] = ReadCellValue(cell, sharedStrings);
                }

                rows[rowNumber] = values;
            }

            return rows;
        }

        private static JArray ParseSheet(
            SortedDictionary<int, List<object>> rows,
            string sheetName)
        {
            List<string> fields = GetRow(rows, 3)
                .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture).Trim())
                .ToList();
            List<string> types = GetRow(rows, 4)
                .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture).Trim().ToLowerInvariant())
                .ToList();

            if (fields.Count == 0 || fields.All(string.IsNullOrEmpty))
            {
                throw new InvalidDataException(sheetName + " 第3行缺少英文字段名");
            }

            JArray result = new JArray();
            foreach (KeyValuePair<int, List<object>> row in rows.Where(pair => pair.Key >= 5))
            {
                if (row.Value.All(IsEmpty))
                {
                    continue;
                }

                JObject item = new JObject();
                for (int index = 0; index < fields.Count; index++)
                {
                    string field = fields[index];
                    if (string.IsNullOrEmpty(field))
                    {
                        continue;
                    }

                    object raw = index < row.Value.Count ? row.Value[index] : string.Empty;
                    string type = index < types.Count && !string.IsNullOrEmpty(types[index])
                        ? types[index]
                        : "string";
                    item[field] = ConvertValue(raw, type, sheetName, row.Key, field);
                }

                JToken enabled;
                if (item.TryGetValue("enabled", out enabled) && !enabled.Value<bool>())
                {
                    continue;
                }

                result.Add(item);
            }

            return result;
        }

        private static JToken ConvertValue(
            object value,
            string valueType,
            string sheet,
            int row,
            string field)
        {
            bool empty = IsEmpty(value);
            try
            {
                switch (valueType)
                {
                    case "int":
                        return empty ? 0 : Convert.ToInt32(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    case "float":
                        return empty ? 0f : Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    case "bool":
                        return !empty && ToBoolean(value);
                    default:
                        return empty
                            ? string.Empty
                            : Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\r\n", "\n");
                }
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    sheet + " 第" + row + "行字段 " + field + " 无法转成 " + valueType + "：" + value,
                    exception);
            }
        }

        private static void Validate(JObject data)
        {
            EnsureUnique(Array(data, "settings"), "key", "Settings");
            EnsureUnique(Array(data, "rarities"), "key", "Rarities");
            EnsureUnique(Array(data, "elements"), "key", "Pieces");
            EnsureUnique(Array(data, "items"), "key", "Buffs");
            EnsureUnique(Array(data, "stages"), "id", "Stages");
            EnsureUnique(Array(data, "weights"), "context", "Weights");
            EnsureUnique(Array(data, "rules"), "rule_id", "Rules");

            HashSet<string> elementKeys = Values(Array(data, "elements"), "key");
            HashSet<string> itemKeys = Values(Array(data, "items"), "key");
            HashSet<string> rarityKeys = Values(Array(data, "rarities"), "key");
            HashSet<string> weightKeys = Values(Array(data, "weights"), "context");

            foreach (JObject row in Array(data, "initialDeck").OfType<JObject>())
            {
                string key = Text(row, "element_key");
                Require(elementKeys.Contains(key), "InitialDeck 引用了不存在的棋子：" + key);
                Require(row.Value<int>("count") > 0, "InitialDeck 数量必须大于0：" + key);
            }

            foreach (JObject row in Array(data, "elements").OfType<JObject>())
            {
                string rarity = Text(row, "rarity");
                Require(rarityKeys.Contains(rarity),
                    "Pieces " + Text(row, "key") + " 的 rarity 不存在：" + rarity);
            }

            foreach (JObject row in Array(data, "items").OfType<JObject>())
            {
                string rarity = Text(row, "rarity");
                Require(rarityKeys.Contains(rarity),
                    "Buffs " + Text(row, "key") + " 的 rarity 不存在：" + rarity);
            }

            foreach (JObject row in Array(data, "stages").OfType<JObject>())
            {
                string context = Text(row, "rarity_context");
                Require(weightKeys.Contains(context),
                    "Stages " + Text(row, "id") + " 的 rarity_context 不存在：" + context);
            }

            foreach (JObject row in Array(data, "rules").OfType<JObject>())
            {
                string ownerType = Text(row, "owner_type");
                string ownerKey = Text(row, "owner_key");
                if (ownerType == "element" && ownerKey != "*")
                {
                    Require(elementKeys.Contains(ownerKey),
                        "Rules " + Text(row, "rule_id") + " 引用了不存在的棋子：" + ownerKey);
                }
                else if (ownerType == "item")
                {
                    Require(itemKeys.Contains(ownerKey),
                        "Rules " + Text(row, "rule_id") + " 引用了不存在的道具：" + ownerKey);
                }
            }

            foreach (JObject row in Array(data, "breeding").OfType<JObject>())
            {
                string parentA = Text(row, "parent_a");
                string parentB = Text(row, "parent_b");
                string child = Text(row, "child");
                string resultMode = Text(row, "result_mode");
                bool parentAWildcard = parentA == "*";
                bool parentBWildcard = parentB == "*";
                bool wildcard = parentAWildcard && parentBWildcard;

                Require(parentAWildcard == parentBWildcard,
                    "Breeding 通配配方必须同时使用 parent_a=*、parent_b=*：" + parentA + "+" + parentB);

                if (wildcard)
                {
                    Require(resultMode == "rarity_random",
                        "Breeding 通配配方必须配置 result_mode=rarity_random");
                    Require(string.IsNullOrEmpty(child),
                        "Breeding rarity_random 通配配方的 child 必须留空");
                    string rarityContext = Text(row, "rarity_context");
                    Require(weightKeys.Contains(rarityContext),
                        "Breeding rarity_context 不存在对应的 Weights 上下文：" + rarityContext);
                }
                else
                {
                    foreach (string field in new[] { "parent_a", "parent_b", "child" })
                    {
                        string key = Text(row, field);
                        Require(elementKeys.Contains(key),
                            "Breeding 引用了不存在的棋子：" + field + "=" + key);
                    }
                    Require(resultMode == "fixed",
                        "Breeding 精确配方仅支持 result_mode=fixed：" + parentA + "+" + parentB);
                }

                string mutation = Text(row, "mutation_child");
                if (!string.IsNullOrEmpty(mutation))
                {
                    Require(elementKeys.Contains(mutation),
                        "Breeding 引用了不存在的突变幼崽：" + mutation);
                }
            }

            EnsureUnique(Array(data, "invites"), "child", "Invite");
            foreach (JObject row in Array(data, "invites").OfType<JObject>())
            {
                string child = Text(row, "child");
                string inviterA = Text(row, "inviter_a");
                Require(elementKeys.Contains(child), "Invite 引用了不存在的猫：child=" + child);
                Require(elementKeys.Contains(inviterA), "Invite 引用了不存在的猫：inviter_a=" + inviterA);
                Require(row.Value<int>("fur_a") > 0, "Invite 的 fur_a 必须大于0：" + child);
                Require(row.Value<int>("cans") >= 0, "Invite 的 cans 不能为负：" + child);

                string inviterB = Text(row, "inviter_b");
                if (string.IsNullOrEmpty(inviterB))
                {
                    continue;
                }

                Require(elementKeys.Contains(inviterB), "Invite 引用了不存在的猫：inviter_b=" + inviterB);
                Require(row.Value<int>("fur_b") > 0, "Invite 配了 inviter_b 就必须配 fur_b：" + child);
            }

            foreach (JObject row in Array(data, "levels").OfType<JObject>())
            {
                string key = Text(row, "cat_key");
                Require(elementKeys.Contains(key), "Levels 引用了不存在的猫：" + key);
            }
        }

        private static void EnsureUnique(JArray rows, string key, string sheet)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject row in rows.OfType<JObject>())
            {
                string value = Text(row, key);
                Require(!string.IsNullOrEmpty(value), sheet + " 存在空 " + key);
                Require(seen.Add(value), sheet + " 存在重复 " + key + "=" + value);
            }
        }

        private static HashSet<string> Values(JArray rows, string key)
        {
            return new HashSet<string>(
                rows.OfType<JObject>().Select(row => Text(row, key)),
                StringComparer.Ordinal);
        }

        private static JArray Array(JObject data, string key)
        {
            return (JArray)data[key];
        }

        private static string Text(JObject row, string key)
        {
            JToken value = row[key];
            return value == null ? string.Empty : value.ToString();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }

        private static bool ToBoolean(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                text == "1" ||
                text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("y", StringComparison.OrdinalIgnoreCase);
        }

        private static object ReadCellValue(XElement cell, IList<string> sharedStrings)
        {
            string kind = (string)cell.Attribute("t") ?? string.Empty;
            if (kind == "inlineStr")
            {
                return ReadText(cell.Element(SpreadsheetNs + "is"));
            }

            XElement valueElement = cell.Element(SpreadsheetNs + "v");
            string value = valueElement == null ? string.Empty : valueElement.Value;
            if (kind == "s")
            {
                int index = ParseInt(value, "共享字符串索引无效");
                if (index < 0 || index >= sharedStrings.Count)
                {
                    throw new InvalidDataException("共享字符串索引越界：" + index);
                }

                return sharedStrings[index];
            }
            if (kind == "b")
            {
                return value == "1";
            }
            if (kind == "str" || kind == "e" || string.IsNullOrEmpty(value))
            {
                return value;
            }

            double number;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return value;
            }

            return Math.Abs(number - Math.Truncate(number)) < double.Epsilon &&
                number >= long.MinValue && number <= long.MaxValue
                ? (object)(long)number
                : number;
        }

        private static string ReadText(XElement node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            return string.Concat(node.Descendants(SpreadsheetNs + "t").Select(part => part.Value));
        }

        private static List<object> GetRow(SortedDictionary<int, List<object>> rows, int rowNumber)
        {
            List<object> row;
            return rows.TryGetValue(rowNumber, out row) ? row : new List<object>();
        }

        private static bool IsEmpty(object value)
        {
            return value == null || (value is string && string.IsNullOrEmpty((string)value));
        }

        private static int ColumnIndex(string reference)
        {
            int result = 0;
            int letters = 0;
            foreach (char character in reference)
            {
                if (character < 'A' || character > 'Z')
                {
                    break;
                }

                result = result * 26 + character - 'A' + 1;
                letters++;
            }

            if (letters == 0)
            {
                throw new InvalidDataException("无效单元格地址：" + reference);
            }

            return result - 1;
        }

        private static XDocument LoadRequiredXml(ZipArchive workbook, string path)
        {
            ZipArchiveEntry entry = workbook.GetEntry(path);
            if (entry == null)
            {
                throw new InvalidDataException("xlsx 内缺少文件：" + path);
            }

            return LoadXml(entry);
        }

        private static XDocument LoadXml(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream);
            }
        }

        private static string RequiredAttribute(XElement element, string name)
        {
            XAttribute attribute = element.Attribute(name);
            if (attribute == null)
            {
                throw new InvalidDataException(element.Name.LocalName + " 缺少属性 " + name);
            }

            return attribute.Value;
        }

        private static int ParseInt(string value, string message)
        {
            int result;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                throw new InvalidDataException(message + "：" + value);
            }

            return result;
        }

        private static string NormalizeEntryPath(string path)
        {
            Stack<string> parts = new Stack<string>();
            foreach (string part in path.Split('/'))
            {
                if (string.IsNullOrEmpty(part) || part == ".")
                {
                    continue;
                }
                if (part == "..")
                {
                    if (parts.Count > 0)
                    {
                        parts.Pop();
                    }
                    continue;
                }

                parts.Push(part);
            }

            return string.Join("/", parts.Reverse().ToArray());
        }

        private struct SheetSpec
        {
            public readonly string ExcelName;
            public readonly string JsonName;

            public SheetSpec(string excelName, string jsonName)
            {
                ExcelName = excelName;
                JsonName = jsonName;
            }
        }
    }
}
#endif
