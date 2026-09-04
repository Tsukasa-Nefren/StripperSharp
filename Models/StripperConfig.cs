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
using System.Text;
using Microsoft.Extensions.Logging;

namespace Kxnrl.StripperSharp.Models;

internal class StripperConfig
{
    public StripperFile?                             Global        { get; private set; }
    public StripperFile?                             GlobalDefault { get; private set; }
    public Dictionary<string, StripperFile>          Lumps         { get; init; }

    public bool HasData => Global is not null || GlobalDefault is not null || Lumps.Count > 0;

    private readonly string       _stripperPath;
    private readonly UTF8Encoding _encoding;
    private readonly ILogger?     _logger;

    public StripperConfig(string path, ILogger? logger = null)
    {
        _stripperPath = path;
        _encoding     = new UTF8Encoding(false);
        Lumps         = new Dictionary<string, StripperFile>(StringComparer.OrdinalIgnoreCase);
        _logger       = logger;

        if (logger != null)
        {
            StripperFileParser.SetLogger(logger);
        }
    }

    public void Purge()
    {
        Global        = null;
        GlobalDefault = null;
        Lumps.Clear();
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
            var globalDefaultPath = Path.Combine(_stripperPath, "global_default.jsonc");

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
                // Prefab worlds are named with forward slashes by the engine (e.g. "perfab/mako_skybox"),
                // but Path.GetDirectoryName yields backslashes on Windows — normalize so the
                // runtime lookup ($"{worldName}::{lumpName}") can ever match for prefab lumps.
                var worldName = string.IsNullOrWhiteSpace(parentDir) ? mapName : parentDir.Replace('\\', '/');
                var lumpName  = Path.GetFileNameWithoutExtension(cleanPath);
                var keyPair   = $"{worldName}::{lumpName}";

                var lumpData = LoadFile(filePath);
                if (lumpData != null)
                {
                    Lumps.Add(keyPair, lumpData);
                }
            }
            catch (Exception e)
            {
                Lumps.Clear();

                throw new FileLoadException("Failed to parse stripper file", filePath, e);
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
