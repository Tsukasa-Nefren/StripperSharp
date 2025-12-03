using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kxnrl.StripperSharp.Actions;
using Microsoft.Extensions.Logging;

namespace Kxnrl.StripperSharp;

public static class JsonProvider
{
    private static ILogger? _logger;

    public static void SetLogger(ILogger logger)
    {
        _logger = logger;
    }

    public static List<BaseAction> Load(string path)
    {
        var content = File.ReadAllText(path);
        return LoadFromContent(content);
    }

    public static List<BaseAction> LoadFromContent(string content)
    {
        content = RemoveComments(content);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        using var document = JsonDocument.Parse(content, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var root = document.RootElement;
        var actions = new List<BaseAction>();

        var filterElements = new List<JsonElement>();
        var addElements = new List<JsonElement>();
        var modifyElements = new List<JsonElement>();

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "filter":
                case "remove":
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            filterElements.Add(item);
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        filterElements.Add(property.Value);
                    }
                    break;

                case "add":
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            addElements.Add(item);
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        addElements.Add(property.Value);
                    }
                    break;

                case "modify":
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            modifyElements.Add(item);
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        modifyElements.Add(property.Value);
                    }
                    break;
            }
        }

        foreach (var filterElement in filterElements)
        {
            var filterAction = ParseFilter(filterElement);
            if (filterAction != null)
            {
                actions.Add(filterAction);
            }
        }

        foreach (var addElement in addElements)
        {
            var addAction = ParseAdd(addElement);
            if (addAction != null)
            {
                actions.Add(addAction);
            }
        }

        foreach (var modifyElement in modifyElements)
        {
            var modifyAction = ParseModify(modifyElement);
            if (modifyAction != null)
            {
                actions.Add(modifyAction);
            }
        }

        return actions;
    }

    private static string RemoveComments(string json)
    {
        var lines = json.Split('\n');
        var result = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("//"))
            {
                continue;
            }

            var commentIndex = line.IndexOf("//");
            if (commentIndex >= 0)
            {
                var beforeComment = line.Substring(0, commentIndex);
                var inString = beforeComment.Count(c => c == '"') % 2 != 0;
                if (!inString)
                {
                    result.Add(beforeComment);
                    continue;
                }
            }

            result.Add(line);
        }

        return string.Join("\n", result);
    }

    private static FilterAction? ParseFilter(JsonElement element)
    {
        var action = new FilterAction();
        ParseEntry(element, action.Matches, isIOArray: true);
        return action.Matches.Count > 0 ? action : null;
    }

    private static AddAction? ParseAdd(JsonElement element)
    {
        var action = new AddAction();
        ParseEntry(element, action.Insertions, isIOArray: true);
        return action.Insertions.Count > 0 ? action : null;
    }

    private static ModifyAction? ParseModify(JsonElement element)
    {
        var action = new ModifyAction();

        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "match":
                    ParseEntry(property.Value, action.Matches, isIOArray: true);
                    break;
                case "replace":
                    ParseEntry(property.Value, action.Replacements, isIOArray: false);
                    break;
                case "delete":
                    ParseEntry(property.Value, action.Deletions, isIOArray: true);
                    break;
                case "insert":
                    ParseEntry(property.Value, action.Insertions, isIOArray: true);
                    break;
            }
        }

        return action.Matches.Count > 0 ? action : null;
    }

    private static void ParseEntry(JsonElement element, List<ActionEntry> entries, bool isIOArray)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.ToLowerInvariant() == "io")
            {
                if (isIOArray && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ioElement in property.Value.EnumerateArray())
                    {
                        var entry = new ActionEntry { Name = "io" };
                        entry.Value.IOValue = ParseIOConnection(ioElement);
                        entries.Add(entry);
                    }
                }
                else if (!isIOArray && property.Value.ValueKind == JsonValueKind.Object)
                {
                    var entry = new ActionEntry { Name = "io" };
                    entry.Value.IOValue = ParseIOConnection(property.Value);
                    entries.Add(entry);
                }
            }
            else
            {
                var entry = new ActionEntry { Name = property.Name };
                
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var strValue = property.Value.GetString() ?? "";
                    entry.Value = ParseValue(strValue);
                }
                else if (property.Value.ValueKind == JsonValueKind.Number)
                {
                    entry.Value.StringValue = property.Value.GetRawText();
                }
                else if (property.Value.ValueKind == JsonValueKind.True || property.Value.ValueKind == JsonValueKind.False)
                {
                    entry.Value.StringValue = property.Value.GetBoolean().ToString().ToLowerInvariant();
                }
                else
                {
                    entry.Value.StringValue = property.Value.GetRawText();
                }

                entries.Add(entry);
            }
        }
    }

    private static IOConnection ParseIOConnection(JsonElement element)
    {
        var io = new IOConnection();

        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "outputname":
                    io.OutputName = ParseValue(property.Value.GetString() ?? "");
                    break;
                case "targetname":
                    io.TargetName = ParseValue(property.Value.GetString() ?? "");
                    break;
                case "inputname":
                    io.InputName = ParseValue(property.Value.GetString() ?? "");
                    break;
                case "overrideparam":
                    io.OverrideParam = ParseValue(property.Value.GetString() ?? "");
                    break;
                case "delay":
                    if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        io.Delay = (float)property.Value.GetDouble();
                    }
                    break;
                case "timestofire":
                    if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        io.TimesToFire = property.Value.GetInt32();
                    }
                    break;
                case "targettype":
                    if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        io.TargetType = property.Value.GetInt32();
                    }
                    break;
            }
        }

        return io;
    }

    private static ActionValue ParseValue(string value)
    {
        var actionValue = new ActionValue();

        if (value.Length >= 3 && value.StartsWith("/") && value.EndsWith("/"))
        {
            try
            {
                var regexPattern = value.Substring(1, value.Length - 2);
                actionValue.RegexValue = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                _logger?.LogDebug("Registered regex: {Pattern}", regexPattern);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PCRE2 compilation failed on value: {Value}", value);
                throw new Exception($"Regex compilation failed: {value}", ex);
            }
        }
        else
        {
            actionValue.StringValue = value;
        }

        return actionValue;
    }
}
