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

        try
        {
            var globalPath = Path.Combine(_stripperPath, "global.jsonc");
            if (File.Exists(globalPath))
            {
                GlobalActions = JsonProvider.Load(globalPath);
                _logger?.LogDebug("Loaded global.jsonc with {Count} actions", GlobalActions.Count);
            }

            var globalDefaultPath = Path.Combine(_stripperPath, "global_default.jsonc");
            if (File.Exists(globalDefaultPath))
            {
                GlobalDefaultActions = JsonProvider.Load(globalDefaultPath);
                _logger?.LogDebug("Loaded global_default.jsonc with {Count} actions", GlobalDefaultActions.Count);
            }

            Global        = LoadFile(globalPath);
            GlobalDefault = LoadFile(globalDefaultPath);
        }
        catch
        {
            Purge();

            throw;
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
                Lumps.Clear();
                LumpsActions.Clear();

                throw new FileLoadException("Failed to parse stripper file", filePath, e);
            }
        }
    }

    private StripperFile? LoadFile(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        return StripperFileParser.ParseFile(file, _encoding);
    }
}
