using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Kxnrl.StripperSharp.Models;
using Kxnrl.StripperSharp.Natives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sharp.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.Hooks;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;

namespace Kxnrl.StripperSharp;

internal sealed unsafe class Stripper : IModSharpModule, IGameListener
{
    public string DisplayName   => "StripperSharp";
    public string DisplayAuthor => "Kxnrl";

    public static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas  = true,
        ReadCommentHandling  = JsonCommentHandling.Skip,
        PropertyNamingPolicy = null,
        WriteIndented        = true,
    };

    private static Stripper?                                         _sInstance;
    private static delegate* unmanaged<nint, CSingleWorldRep*, nint> _sTrampoline;

    private readonly ILogger<Stripper> _logger;
    private readonly IModSharp         _modSharp;
    private readonly IDetourHook       _detour;
    private readonly StripperConfig    _config;

    private readonly IConVar _cvarEnableVerbose;
    private readonly IConVar _cvarEnableReplace;

    public Stripper(ISharedSystem sharedSystem,
        string                    dllPath,
        string                    sharpPath,
        Version                   version,
        IConfiguration            coreConfiguration,
        bool                      hotReload)
    {
        _logger = sharedSystem.GetLoggerFactory()
                              .CreateLogger<Stripper>();

        _modSharp = sharedSystem.GetModSharp();
        _detour   = sharedSystem.GetHookManager().CreateDetourHook();
        _config   = new StripperConfig(Path.Combine(sharpPath, "stripper"), _logger);

        _cvarEnableVerbose = sharedSystem.GetConVarManager()
                                         .CreateConVar("ms_stripper_verbose_enabled",
                                                       false,
                                                       "Enable verbose logging of stripper",
                                                       ConVarFlags.Release)
                             ?? throw new EntryPointNotFoundException("Failed to create conVar 'ms_stripper_verbose_enabled'");

        _cvarEnableReplace = sharedSystem.GetConVarManager()
                                         .CreateConVar("ms_stripper_replace_enabled",
                                                       true,
                                                       "Enable 'replace' block in 'modify' section.",
                                                       ConVarFlags.Release)
                             ?? throw new EntryPointNotFoundException("Failed to create conVar 'ms_stripper_replace_enabled'");

        _sInstance = this;
    }

    public bool Init()
    {
        _modSharp.GetGameData()
                 .Register("stripper.games");

        _detour.Prepare("IWorldRendererMgr::CreateWorldInternal",
                        (nint) (delegate* unmanaged<nint, CSingleWorldRep*, nint>) &CreateWorldInternal);

        return _detour.Install();
    }

    public void PostInit()
    {
        _sTrampoline = (delegate *unmanaged<nint, CSingleWorldRep*, nint>) _detour.Trampoline;

        _modSharp.InstallGameListener(this);

        CEntityKeyValues.Init(_modSharp);
        CKeyValues3.Init(_modSharp);
    }

    public void Shutdown()
    {
        _modSharp.GetGameData()
                 .Unregister("stripper.games");

        _detour.Uninstall();

        _modSharp.RemoveGameListener(this);
    }

    int IGameListener.ListenerPriority => 0;
    int IGameListener.ListenerVersion  => IGameListener.ApiVersion;

    public void OnServerInit()
        => _config.Purge();

    public void OnGameInit()
    {
        try
        {
            _config.Load(_modSharp.GetGlobals().MapName);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load stripper configuration");
        }
    }

    [UnmanagedCallersOnly]
    private static nint CreateWorldInternal(nint pWorldRendererMgr, CSingleWorldRep* pSingleWorld)
    {
        var call = _sTrampoline(pWorldRendererMgr, pSingleWorld);

        if (_sInstance is { _config.HasData: true } stripper)
        {
            stripper.ApplyOverrides(pSingleWorld);
        }

        return call;
    }

    private void ApplyOverrides(CSingleWorldRep* pSingleWorld)
    {
        try
        {
            ref var lumpHandles = ref pSingleWorld->pWorld->EntityLumps;

            var mapName   = _modSharp.GetGlobals().MapName;
            var worldName = pSingleWorld->Name.Get();

            for (var i = 0; i < lumpHandles.Count; i++)
            {
                ref var lump     = ref lumpHandles.Element(i);
                var     lumpData = lump.AsRef().m_pLumpData;
                var     lumpName = lumpData->pName.Get();

                if (_config.Lumps.TryGetValue($"{worldName}::{lumpName}", out var lumpOverrides))
                {
                    ApplyOverrides(lumpOverrides, lumpData);
                }

                if (_config.Global is not null)
                {
                    ApplyOverrides(_config.Global, lumpData);
                }

                if (_config.GlobalDefault is not null
                    && mapName.Equals(worldName, StringComparison.OrdinalIgnoreCase)
                    && lumpName.Equals("default_ents", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyOverrides(_config.GlobalDefault, lumpData);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to apply stripper overrides");
        }
    }

    private void ApplyOverrides(StripperFile config, CEntityLump* lump)
    {
        if (config.Remove is { Count: > 0 } removes)
        {
            foreach (var remove in removes)
            {
                for (var j = lump->EntityKeyValues.Size - 1; j >= 0; j--)
                {
                    var kv = lump->EntityKeyValues.Element(j).Value;

                    if (Matcher.DoesEntityMatch(kv, remove))
                    {
                        lump->EntityKeyValues.Remove(j);

                        if (_cvarEnableVerbose.GetBool())
                        {
                            _logger.LogInformation("Removed\n{e}", JsonSerializer.Serialize(remove, SerializerOptions));
                        }
                    }
                }
            }
        }

        if (config.Add is { Count: > 0 } adds)
        {
            foreach (var add in adds)
            {
                var kv = CEntityKeyValues.Create(lump->pAllocatorContext, CEntityKeyValues.AllocatorType.External);

                Modifier.InsertKeyValues(kv, add);

                kv->RefCount++;
                lump->EntityKeyValues.Add(kv);

                if (_cvarEnableVerbose.GetBool())
                {
                    _logger.LogInformation("Added\n{e}", JsonSerializer.Serialize(add, SerializerOptions));
                }
            }
        }

        if (config.Modify is { Count: > 0 } modifies)
        {
            foreach (var modify in modifies)
            {
                if (!modify.TryGetValue("match", out var matchDoc))
                {
                    throw new JsonException("Missing 'match' block in 'modify' section");
                }

                var matches = matchDoc.Deserialize<Dictionary<string, JsonDocument>>(SerializerOptions)
                              ?? throw new JsonException("Failed to Deserialize<Dictionary<string, JsonDocument>>");

                // Deserialize를 루프 밖에서 한 번만 수행
                Dictionary<string, JsonDocument>? deletions = null;
                Dictionary<string, JsonDocument>? replaces = null;
                Dictionary<string, JsonDocument>? insertions = null;

                if (modify.TryGetValue("delete", out var deleteDoc))
                {
                    deletions = deleteDoc.Deserialize<Dictionary<string, JsonDocument>>(SerializerOptions);
                }

                if (modify.TryGetValue("replace", out var replaceDoc) && _cvarEnableReplace.GetBool())
                {
                    replaces = replaceDoc.Deserialize<Dictionary<string, JsonDocument>>(SerializerOptions);
                }

                if (modify.TryGetValue("insert", out var insertDoc))
                {
                    insertions = insertDoc.Deserialize<Dictionary<string, JsonDocument>>(SerializerOptions);
                }

                var enableVerbose = _cvarEnableVerbose.GetBool();
                StringBuilder? builder = enableVerbose ? new StringBuilder() : null;

                for (var j = 0; j < lump->EntityKeyValues.Size; j++)
                {
                    var kv = lump->EntityKeyValues.Element(j).Value;

                    if (Matcher.DoesEntityMatch(kv, matches))
                    {
                        if (deletions is { } del)
                        {
                            Modifier.DeleteKeyValues(kv, del);

                            if (enableVerbose && builder != null)
                            {
                                builder.Append($"  Deleted\n    {JsonSerializer.Serialize(del, SerializerOptions)}\n");
                            }
                        }

                        if (replaces is { } rep)
                        {
                            Modifier.InsertKeyValues(kv, rep, false);

                            if (enableVerbose && builder != null)
                            {
                                builder.Append($"  Replaced\n    {JsonSerializer.Serialize(rep, SerializerOptions)}\n");
                            }
                        }

                        if (insertions is { } ins)
                        {
                            Modifier.InsertKeyValues(kv, ins);

                            if (enableVerbose && builder != null)
                            {
                                builder.Append($"  Inserted\n    {JsonSerializer.Serialize(ins, SerializerOptions)}\n");
                            }
                        }

                        if (enableVerbose && builder != null)
                        {
                            _logger.LogInformation("Modified\n{m}\n{b}",
                                                   JsonSerializer.Serialize(matches, SerializerOptions),
                                                   builder.ToString());
                            builder.Clear();
                        }
                    }
                }
            }
        }
    }
}

file static unsafe class Matcher
{
    internal const string ConnectionsKey = "connections";
    internal const string IOKey = "io";

    internal static bool DoesEntityMatch(CEntityKeyValues* kv, Dictionary<string, JsonDocument> matches)
    {
        foreach (var (key, doc) in matches)
        {
            if (key.Equals(ConnectionsKey, StringComparison.OrdinalIgnoreCase) || key.Equals(IOKey, StringComparison.OrdinalIgnoreCase))
            {
                var connectionCount = kv->ConnectionDescs.Count;

                if (connectionCount == 0)
                {
                    return false;
                }

                var connections = doc.Deserialize<List<StripperConnection>>(Stripper.SerializerOptions)
                                  ?? throw new JsonException("Failed to Deserialize<List<StripperConnection>>");

                if (connections.Count == 0)
                {
                    continue;
                }

                for (var i = 0; i < connectionCount; i++)
                {
                    ref var desc = ref kv->ConnectionDescs[i];

                    if (!MatchConnection(in desc, connections))
                    {
                        return false;
                    }
                }

                continue;
            }

            if (doc.RootElement.GetString() is not { } match)
            {
                throw new JsonException($"Invalid value of [{key}]");
            }

            var pKeyValue = kv->FindKeyValuesMember(key);

            if (pKeyValue == null)
            {
                return false;
            }

            var allowWildcard = key.Equals("targetname",   StringComparison.OrdinalIgnoreCase)
                                || key.Equals("classname", StringComparison.OrdinalIgnoreCase);

            if (!MatchValue(pKeyValue->GetStringAuto(), match, allowWildcard))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool MatchConnection(in EntityIOConnectionDescFat desc, List<StripperConnection> connections)
    {
        var output = desc.OutputName;
        var target = desc.TargetName;
        var input  = desc.InputName;
        var param  = desc.OverrideParam;
        var delay  = desc.Delay;
        var limit  = desc.TimesToFire;

        foreach (var match in connections)
        {
            bool matches = true;

            if (match.Input is not null && !MatchValue(input, match.Input))
            {
                matches = false;
            }

            if (match.Output is not null && !MatchValue(output, match.Output, true))
            {
                matches = false;
            }

            if (match.Target is not null && !MatchValue(target, match.Target))
            {
                matches = false;
            }

            if (match.Param is not null && !MatchValue(param, match.Param, true))
            {
                matches = false;
            }

            if (match.Delay is { } md && !MatchValue(delay, md))
            {
                matches = false;
            }

            if (match.Limit is { } ml && limit != ml)
            {
                matches = false;
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool MatchValue(string value, string match, bool allowWildcard = false)
    {
        if (allowWildcard && match.EndsWith("*"))
        {
            var wildcard = match[..^1];

            return value.StartsWith(wildcard, StringComparison.OrdinalIgnoreCase);
        }

        return value.Equals(match, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchValue(float value, float match, float epsilon = 0.001f)
        => MathF.Abs(value - match) <= epsilon;
}

file static unsafe class Modifier
{
    internal static void InsertKeyValues(CEntityKeyValues* kv,
        Dictionary<string, JsonDocument>                   insertions,
        bool                                               loopableConnection = true)
    {
        foreach (var (key, doc) in insertions)
        {
            if (key.Equals(Matcher.ConnectionsKey, StringComparison.OrdinalIgnoreCase) || key.Equals(Matcher.IOKey, StringComparison.OrdinalIgnoreCase))
            {
                if (loopableConnection)
                {
                    var connections = doc.Deserialize<List<StripperConnection>>(Stripper.SerializerOptions)
                                      ?? throw new JsonException("Failed to Deserialize<List<StripperConnection>>");

                    foreach (var connection in connections)
                    {
                        if (string.IsNullOrWhiteSpace(connection.Output)
                            || string.IsNullOrWhiteSpace(connection.Input)
                            || string.IsNullOrWhiteSpace(connection.Target))
                        {
                            throw new InvalidDataException("Missing 'output' or 'input' or 'target'");
                        }

                        kv->AddConnectionDesc(connection.Output,
                                              EntityIOTargetType.EntityNameOrClassName,
                                              connection.Target,
                                              connection.Input,
                                              connection.Param ?? "",
                                              connection.Delay.GetValueOrDefault(),
                                              connection.Limit.GetValueOrDefault(-1));
                    }
                }
                else
                {
                    var connection = doc.Deserialize<StripperConnection>(Stripper.SerializerOptions)
                                     ?? throw new JsonException("Failed to Deserialize<StripperConnection>");

                    if (string.IsNullOrWhiteSpace(connection.Output)
                        || string.IsNullOrWhiteSpace(connection.Input)
                        || string.IsNullOrWhiteSpace(connection.Target))
                    {
                        throw new InvalidDataException("Missing 'output' or 'input' or 'target'");
                    }

                    kv->AddConnectionDesc(connection.Output,
                                          EntityIOTargetType.EntityNameOrClassName,
                                          connection.Target,
                                          connection.Input,
                                          connection.Param ?? "",
                                          connection.Delay.GetValueOrDefault(),
                                          connection.Limit.GetValueOrDefault(-1));
                }
            }
            else
            {
                if (doc.RootElement.GetString() is not { } value)
                {
                    throw new JsonException($"Invalid value of [{key}]");
                }

                kv->AddOrSetKeyValueMemberString(key, value);
            }
        }
    }

    internal static void DeleteKeyValues(CEntityKeyValues* kv, Dictionary<string, JsonDocument> deletions)
    {
        foreach (var (key, doc) in deletions)
        {
            if (key.Equals(Matcher.ConnectionsKey, StringComparison.OrdinalIgnoreCase) || key.Equals(Matcher.IOKey, StringComparison.OrdinalIgnoreCase))
            {
                var connectionCount = kv->ConnectionDescs.Count;

                if (connectionCount == 0)
                {
                    continue;
                }

                var connections = doc.Deserialize<List<StripperConnection>>(Stripper.SerializerOptions)
                                  ?? throw new JsonException("Failed to Deserialize<List<StripperConnection>>");

                if (connections.Count == 0)
                {
                    continue;
                }

                for (var i = connectionCount - 1; i >= 0; i--)
                {
                    ref var desc = ref kv->ConnectionDescs[i];

                    if (Matcher.MatchConnection(in desc, connections))
                    {
                        kv->RemoveConnectionDesc(i);
                    }
                }
            }
            else
            {
                if (doc.RootElement.GetString() is not { } match)
                {
                    throw new JsonException($"Invalid value of [{key}]");
                }

                var pKeyValue = kv->FindKeyValuesMember(key);

                if (pKeyValue == null)
                {
                    continue;
                }

                if (Matcher.MatchValue(pKeyValue->GetStringAuto(), match, true))
                {
                    kv->RemoveKeyValues(key);
                }
            }
        }
    }
}
