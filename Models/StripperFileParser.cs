/*
 * StripperSharp
 * Copyright (C) 2023-2025 Kxnrl. All Rights Reserved.
 *
 * This file is part of StripperSharp.
 * ModSharp is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as
 * published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version.
 *
 * ModSharp is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with ModSharp. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Kxnrl.StripperSharp.Models;

internal static class StripperFileParser
{
    private static ILogger? _logger;

    public static void SetLogger(ILogger logger)
    {
        _logger = logger;
    }

    public static StripperFile? ParseFile(string filePath, Encoding encoding)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var content = File.ReadAllText(filePath, encoding);
        content = RemoveComments(content);

        var parsedData = ParseWithDuplicateKeys(content);

        _logger?.LogDebug("ParseWithDuplicateKeys result: {Keys}", string.Join(", ", parsedData.Keys));

        var addList = parsedData.TryGetValue("add", out var addValues) && addValues.Count > 0
            ? addValues.SelectMany(jsonStr => ParseArrayOrObject(jsonStr)).Where(dict => dict.Count > 0).ToList()
            : null;

        if (addValues != null)
        {
            _logger?.LogDebug("Found {Count} 'add' entries", addValues.Count);
        }

        var modifyList = parsedData.TryGetValue("modify", out var modifyValues) && modifyValues.Count > 0
            ? modifyValues.SelectMany(jsonStr => ParseArrayOrObject(jsonStr)).Where(dict => dict.Count > 0).ToList()
            : null;

        if (modifyValues != null)
        {
            _logger?.LogDebug("Found {Count} 'modify' entries", modifyValues.Count);
        }

        List<string>? removeValues = null;
        List<string>? filterValues = null;
        var hasRemove = parsedData.TryGetValue("remove", out removeValues) && removeValues.Count > 0;
        var hasFilter = parsedData.TryGetValue("filter", out filterValues) && filterValues.Count > 0;

        var removeList = (hasRemove ? removeValues : hasFilter ? filterValues : null)
            ?.SelectMany(jsonStr => ParseArrayOrObject(jsonStr)).Where(dict => dict.Count > 0).ToList();

        if (hasRemove || hasFilter)
        {
            _logger?.LogDebug("Found {Count} 'remove' entries", (removeValues?.Count ?? filterValues?.Count) ?? 0);
        }

        _logger?.LogDebug("Parsed result: Add={AddCount}, Modify={ModifyCount}, Remove={RemoveCount}", 
            addList?.Count ?? 0, modifyList?.Count ?? 0, removeList?.Count ?? 0);

        if (addList == null && modifyList == null && removeList == null)
        {
            return null;
        }

        return new StripperFile
        {
            Add = addList,
            Modify = modifyList,
            Remove = removeList
        };
    }

    private static Dictionary<string, List<string>> ParseWithDuplicateKeys(string json)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        string? currentKey = null;
        int depth = 0;
        long valueStartPosition = 0;
        bool isValueArray = false;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    depth++;
                    // 최상위 키의 object 값 시작점을 '{' 위치로 고정.
                    // depth==2 == 루트 object 내부의 값 object. StartArray 분기와 달리
                    // object 값은 명시적 시작점 재설정이 없어 깨졌던 버그 수정.
                    if (depth == 2 && currentKey != null)
                    {
                        var pos = (int)reader.BytesConsumed - 1;
                        while (pos > 0 && char.IsWhiteSpace((char)bytes[pos - 1]))
                        {
                            pos--;
                        }

                        valueStartPosition = pos;
                    }
                    break;

                case JsonTokenType.EndObject:
                    if (depth == 2 && currentKey != null)
                    {
                        var valueBytes = bytes.AsSpan((int)valueStartPosition, (int)(reader.BytesConsumed - valueStartPosition));
                        var valueJson = Encoding.UTF8.GetString(valueBytes);
                        if (!result.ContainsKey(currentKey))
                        {
                            result[currentKey] = new List<string>();
                        }
                        result[currentKey].Add(valueJson);
                        currentKey = null;
                    }
                    depth--;
                    break;

                case JsonTokenType.StartArray:
                    if (depth == 1 && currentKey != null)
                    {
                        isValueArray = true;
                        var pos = (int)reader.BytesConsumed;
                        while (pos > 0 && char.IsWhiteSpace((char)bytes[pos - 1]))
                        {
                            pos--;
                        }
                        if (pos > 0 && bytes[pos - 1] == (byte)'[')
                        {
                            valueStartPosition = pos - 1;
                        }
                        else
                        {
                            valueStartPosition = pos;
                        }
                    }
                    depth++;
                    break;

                case JsonTokenType.EndArray:
                    depth--;
                    if (depth == 1 && currentKey != null && isValueArray)
                    {
                        var endPos = (int)reader.BytesConsumed;
                        while (endPos > 0 && char.IsWhiteSpace((char)bytes[endPos - 1]))
                        {
                            endPos--;
                        }
                        var length = endPos - (int)valueStartPosition;
                        if (length > 0)
                        {
                            var valueBytes = bytes.AsSpan((int)valueStartPosition, length);
                            var valueJson = Encoding.UTF8.GetString(valueBytes);
                            valueJson = valueJson.TrimEnd(',', ' ', '\t', '\n', '\r');
                            if (!result.ContainsKey(currentKey))
                            {
                                result[currentKey] = new List<string>();
                            }
                            result[currentKey].Add(valueJson);
                        }
                        currentKey = null;
                        isValueArray = false;
                    }
                    break;

                case JsonTokenType.PropertyName:
                    if (depth == 1)
                    {
                        currentKey = reader.GetString();
                        isValueArray = false;
                    }
                    break;
            }
        }

        return result;
    }

    private static string RemoveComments(string json)
    {
        var lines = json.Split('\n');
        var result = new List<string>();
        bool inBlockComment = false;

        foreach (var line in lines)
        {
            var processedLine = line;
            var i = 0;

            while (i < processedLine.Length)
            {
                if (inBlockComment)
                {
                    var blockEnd = processedLine.IndexOf("*/", i);
                    if (blockEnd >= 0)
                    {
                        processedLine = processedLine.Substring(0, i) + processedLine.Substring(blockEnd + 2);
                        inBlockComment = false;
                    }
                    else
                    {
                        processedLine = processedLine.Substring(0, i);
                        break;
                    }
                }
                else
                {
                    var inString = false;
                    var escapeNext = false;

                    for (var j = i; j < processedLine.Length; j++)
                    {
                        if (escapeNext)
                        {
                            escapeNext = false;
                            continue;
                        }

                        if (processedLine[j] == '\\')
                        {
                            escapeNext = true;
                            continue;
                        }

                        if (processedLine[j] == '"')
                        {
                            inString = !inString;
                            continue;
                        }

                        if (!inString)
                        {
                            if (j < processedLine.Length - 1 && processedLine[j] == '/' && processedLine[j + 1] == '/')
                            {
                                processedLine = processedLine.Substring(0, j);
                                break;
                            }

                            if (j < processedLine.Length - 1 && processedLine[j] == '/' && processedLine[j + 1] == '*')
                            {
                                var blockEnd = processedLine.IndexOf("*/", j + 2);
                                if (blockEnd >= 0)
                                {
                                    processedLine = processedLine.Substring(0, j) + processedLine.Substring(blockEnd + 2);
                                    i = j;
                                }
                                else
                                {
                                    processedLine = processedLine.Substring(0, j);
                                    inBlockComment = true;
                                    break;
                                }
                            }
                        }
                    }

                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(processedLine) || !inBlockComment)
            {
                result.Add(processedLine);
            }
        }

        return string.Join("\n", result);
    }

    private static IEnumerable<Dictionary<string, JsonElement>> ParseArrayOrObject(string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            return Array.Empty<Dictionary<string, JsonElement>>();
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonString, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var results = new List<Dictionary<string, JsonElement>>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        results.Add(ConvertElementToDictionary(element));
                    }
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                results.Add(ConvertElementToDictionary(doc.RootElement));
            }

            return results;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse JSON string from ParseWithDuplicateKeys, skipping. Length: {Length}, Preview: {Preview}",
                jsonString.Length,
                jsonString.Length > 500 ? jsonString.Substring(0, 500) + "..." : jsonString);
            return Array.Empty<Dictionary<string, JsonElement>>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error parsing JSON string. Length: {Length}, Error: {Error}", jsonString.Length, ex.Message);
            return Array.Empty<Dictionary<string, JsonElement>>();
        }
    }

    private static Dictionary<string, JsonElement> ConvertElementToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, JsonElement>();

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value.Clone();
        }

        return dict;
    }
}
