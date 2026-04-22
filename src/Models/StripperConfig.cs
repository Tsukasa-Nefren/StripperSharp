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
                var fileName  = Path.GetFileNameWithoutExtension(cleanPath);

                // 정규 파일명 whitelist — 맵 제작자가 백업/WIP 목적으로 두는 변형 파일
                // (예: default_ents(t).jsonc, default_1ents.jsonc, default_ents.bak.jsonc)이
                // 실수로 로드되어 맵에 적용되는 것을 방지한다.
                //
                // 허용:
                //   "default_ents"              (main world 의 기본 lump)
                //   "<worldName>#<lumpName>"    (sub-world prefab 규칙 — 두 부분 모두 깨끗한 식별자)
                //
                // 거부 (로그만 남기고 skip):
                //   괄호, 공백, '.' 가 들어간 이름
                //   '#' 가 없는 비표준 이름 (default_1ents 등)
                if (!IsCanonicalStripperFileName(fileName))
                {
                    _logger?.LogDebug("Skipping non-canonical stripper file: {File}", filePath);
                    continue;
                }

                // 이 포크가 지원해야 하는 파일명 컨벤션:
                //   1) "default_ents.jsonc"                 → mapName::default_ents
                //   2) "<worldName>#<lumpName>.jsonc"       → worldName::lumpName  (sub-world prefab instance 등)
                //   3) "<subdir>/<lumpName>.jsonc"          → subdir::lumpName     (skybox 등, parentDir 로 진입)
                string worldName;
                string lumpName;

                var hashIdx = fileName.IndexOf('#');
                if (hashIdx > 0 && hashIdx < fileName.Length - 1)
                {
                    // 패턴 (2): 파일명 안의 '#' 로 world/lump 분리
                    worldName = fileName.Substring(0, hashIdx);
                    lumpName  = fileName.Substring(hashIdx + 1);
                }
                else if (!string.IsNullOrWhiteSpace(parentDir))
                {
                    // 패턴 (3): 서브 디렉토리 이름이 곧 sub-world 이름
                    worldName = parentDir;
                    lumpName  = fileName;
                }
                else
                {
                    // 패턴 (1): main world
                    worldName = mapName;
                    lumpName  = fileName;
                }

                var keyPair = $"{worldName}::{lumpName}";

                var actions = JsonProvider.Load(filePath);
                if (actions.Count > 0)
                {
                    LumpsActions[keyPair] = actions;
                    _logger?.LogDebug("Loaded {Path} with {Count} actions (key={Key})", filePath, actions.Count, keyPair);
                }

                var lumpData = LoadFile(filePath);
                if (lumpData != null)
                {
                    // 같은 keyPair 에 여러 파일이 매핑되는 극단 케이스 방어 (예: default_ents.jsonc + default_ents(t).jsonc)
                    // 마지막 파일이 이긴다. Dictionary.Add 대신 인덱서 사용.
                    if (Lumps.ContainsKey(keyPair))
                    {
                        _logger?.LogWarning("Duplicate lump key '{Key}' — overwriting with {File}", keyPair, filePath);
                    }
                    Lumps[keyPair] = lumpData;
                }
            }
            catch (Exception e)
            {
                // 한 파일의 파싱 실패로 이전까지 누적된 정상 설정을 모두 날리지 않는다.
                // 실패한 파일만 건너뛰고 다음 파일 처리 계속.
                _logger?.LogError(e, "Failed to parse stripper file, skipping: {File}", filePath);
            }
        }

        _logger?.LogInformation(
            "Stripper config loaded for map '{Map}': {LumpCount} lump entries, {ActionCount} action entries, global={Global}, global_default={GlobalDefault}",
            mapName, Lumps.Count, LumpsActions.Count, Global is not null, GlobalDefault is not null);
    }

    // 정규 stripper 파일명 판별.
    //
    // 허용 케이스:
    //   "default_ents"              — main/sub world 의 기본 entity lump
    //   "<worldName>#<lumpName>"    — sub-world prefab 규칙. worldName, lumpName 모두 '.', 공백, 괄호 없이 영문/숫자/언더스코어 조합이어야 함.
    //
    // 거부 케이스 (맵 작성자 WIP/백업 방지):
    //   default_ents(t), default_1ents, default_ents.bak, default_ents copy 등
    private static readonly char[] ForbiddenFileNameChars = { '(', ')', '[', ']', '{', '}', '.', ' ', '\t' };

    private static bool IsCanonicalStripperFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        // 금지 문자 포함 여부 (괄호·공백·점 등)
        if (fileName.IndexOfAny(ForbiddenFileNameChars) >= 0)
            return false;

        // 정확히 "default_ents"
        if (fileName.Equals("default_ents", StringComparison.Ordinal))
            return true;

        // "<worldName>#<lumpName>" — '#' 기준 두 부분 모두 비어있지 않아야 함
        var hashIdx = fileName.IndexOf('#');
        if (hashIdx <= 0 || hashIdx >= fileName.Length - 1)
            return false;

        // '#' 가 둘 이상이면 거부 (ambiguous)
        if (fileName.IndexOf('#', hashIdx + 1) >= 0)
            return false;

        return true;
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
