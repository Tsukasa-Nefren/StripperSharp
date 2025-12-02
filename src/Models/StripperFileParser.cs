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

        var addList = parsedData.TryGetValue("add", out var addValues) && addValues.Count > 0
            ? addValues.Select(jsonStr => ConvertStringToDictionary(jsonStr)).ToList()
            : null;

        var modifyList = parsedData.TryGetValue("modify", out var modifyValues) && modifyValues.Count > 0
            ? modifyValues.Select(jsonStr => ConvertStringToDictionary(jsonStr)).ToList()
            : null;

        var removeList = (parsedData.TryGetValue("remove", out var removeValues) && removeValues.Count > 0
            ? removeValues
            : parsedData.TryGetValue("filter", out var filterValues) && filterValues.Count > 0
                ? filterValues
                : null)
            ?.Select(jsonStr => ConvertStringToDictionary(jsonStr)).ToList();

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
                    if (depth == 1)
                    {
                        valueStartPosition = reader.BytesConsumed;
                    }
                    break;

                case JsonTokenType.EndObject:
                    if (depth == 1 && currentKey != null)
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
                        valueStartPosition = reader.BytesConsumed - 1;
                    }
                    depth++;
                    break;

                case JsonTokenType.EndArray:
                    depth--;
                    if (depth == 1 && currentKey != null && isValueArray)
                    {
                        var valueBytes = bytes.AsSpan((int)valueStartPosition, (int)(reader.BytesConsumed - valueStartPosition + 1));
                        var valueJson = Encoding.UTF8.GetString(valueBytes);
                        if (!result.ContainsKey(currentKey))
                        {
                            result[currentKey] = new List<string>();
                        }
                        result[currentKey].Add(valueJson);
                        currentKey = null;
                        isValueArray = false;
                    }
                    break;

                case JsonTokenType.PropertyName:
                    if (depth == 1)
                    {
                        currentKey = reader.GetString();
                        valueStartPosition = reader.BytesConsumed;
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

    private static Dictionary<string, JsonDocument> ConvertStringToDictionary(string jsonString)
    {
        var dict = new Dictionary<string, JsonDocument>();

        using var doc = JsonDocument.Parse(jsonString);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var propJsonString = property.Value.GetRawText();
            dict[property.Name] = JsonDocument.Parse(propJsonString);
        }

        return dict;
    }
}
