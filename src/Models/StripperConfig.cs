using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Kxnrl.StripperSharp.Actions;
using Microsoft.Extensions.Logging;

namespace Kxnrl.StripperSharp.Models;

internal class StripperConfig
{
    public StripperFile?                                        Global        { get; private set; }
    public StripperFile?                                        GlobalDefault { get; private set; }
    public Dictionary<string, StripperFile>                    Lumps         { get; init; }
    public List<BaseAction>                                     GlobalActions { get; private set; }
    public List<BaseAction>                                     GlobalDefaultActions { get; private set; }
    public Dictionary<string, List<BaseAction>>                LumpsActions  { get; init; }

    public bool HasData => Global is not null || GlobalDefault is not null || Lumps.Count > 0
                           || (GlobalActions?.Count ?? 0) > 0 || (GlobalDefaultActions?.Count ?? 0) > 0 || LumpsActions.Count > 0;

    private readonly string       _stripperPath;
    private readonly UTF8Encoding _encoding;
    private readonly ILogger?      _logger;

    public StripperConfig(string path, ILogger? logger = null)
    {
        _stripperPath = path;
        _encoding     = new UTF8Encoding(false);
        Lumps         = new Dictionary<string, StripperFile>(StringComparer.OrdinalIgnoreCase);
        LumpsActions  = new Dictionary<string, List<BaseAction>>(StringComparer.OrdinalIgnoreCase);
        GlobalActions = new List<BaseAction>();
        GlobalDefaultActions = new List<BaseAction>();
        _logger = logger;
        
        if (logger != null)
        {
            StripperFileParser.SetLogger(logger);
            JsonProvider.SetLogger(logger);
        }
    }

    public void Purge()
    {
        Global        = null;
        GlobalDefault = null;
        Lumps.Clear();
        GlobalActions?.Clear();
        GlobalDefaultActions?.Clear();
        LumpsActions.Clear();
    }

    public void Load(string mapName)
    {
        if (!Directory.Exists(_stripperPath))
        {
            return;
        }

        var globalPath        = Path.Combine(_stripperPath, "global.jsonc");
        var globalDefaultPath = Path.Combine(_stripperPath, "global_default.jsonc");

        if (File.Exists(globalPath))
        {
            try
            {
                GlobalActions = JsonProvider.Load(globalPath);
                _logger?.LogDebug("Loaded global.jsonc with {Count} actions", GlobalActions.Count);
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Failed to load global.jsonc actions, skipping");
            }

            try
            {
                Global = LoadFile(globalPath);
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Failed to parse global.jsonc data, skipping");
            }
        }

        if (File.Exists(globalDefaultPath))
        {
            try
            {
                GlobalDefaultActions = JsonProvider.Load(globalDefaultPath);
                _logger?.LogDebug("Loaded global_default.jsonc with {Count} actions", GlobalDefaultActions.Count);
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Failed to load global_default.jsonc actions, skipping");
            }

            try
            {
                GlobalDefault = LoadFile(globalDefaultPath);
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Failed to parse global_default.jsonc data, skipping");
            }
        }

        var mapPath = Path.Combine(_stripperPath, "maps", mapName);

        if (!Directory.Exists(mapPath))
        {
            return;
        }

        foreach (var filePath in Directory.GetFiles(mapPath, "*.jsonc", SearchOption.AllDirectories))
        {
            try
            {
                var cleanPath = Path.GetRelativePath(mapPath, filePath);

                // Canonical path 검증 (symlink / .. 구성으로 mapPath 밖 지시 방지)
                var canonical = Path.GetFullPath(Path.Combine(mapPath, cleanPath));
                var mapRoot   = Path.GetFullPath(mapPath) + Path.DirectorySeparatorChar;
                if (!canonical.StartsWith(mapRoot, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning("Path traversal detected, skipping: {File}", filePath);
                    continue;
                }

                var parentDir = Path.GetDirectoryName(cleanPath);
                var worldName = string.IsNullOrWhiteSpace(parentDir) ? mapName : parentDir;
                var lumpName  = Path.GetFileNameWithoutExtension(cleanPath);
                var keyPair   = $"{worldName}::{lumpName}";

                var actions = JsonProvider.Load(filePath);
                if (actions.Count > 0)
                {
                    LumpsActions[keyPair] = actions;
                    _logger?.LogDebug("Loaded {Path} with {Count} actions", filePath, actions.Count);
                }

                var lumpData = LoadFile(filePath);
                if (lumpData != null)
                {
                    Lumps.Add(keyPair, lumpData);
                }
            }
            catch (Exception e)
            {
                // 한 파일의 파싱 실패로 이전까지 누적된 정상 설정을 모두 날리지 않는다.
                // 실패한 파일만 건너뛰고 다음 파일 처리 계속.
                _logger?.LogError(e, "Failed to parse stripper file, skipping: {File}", filePath);
            }
        }
    }

    private StripperFile? LoadFile(string file)
    {
        if (!File.Exists(file))
        {
            _logger?.LogDebug("File does not exist: {File}", file);
            return null;
        }

        _logger?.LogDebug("Loading file: {File}", file);
        var result = StripperFileParser.ParseFile(file, _encoding);
        if (result == null)
        {
            _logger?.LogWarning("ParseFile returned null for: {File}", file);
        }
        else
        {
            _logger?.LogDebug("Successfully parsed file: {File}, Add={AddCount}, Modify={ModifyCount}, Remove={RemoveCount}", 
                file, result.Add?.Count ?? 0, result.Modify?.Count ?? 0, result.Remove?.Count ?? 0);
        }
        return result;
    }
}
