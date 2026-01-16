namespace ACE.Server.Mods;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Serilog;

public static class RecipeComponentUseEmote
{
    private static bool _initialized;
    private static readonly object _sync = new();

    private static bool _enabled = true;
    private static HashSet<uint> _allowed = new();

    public static void Initialize()
    {
        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                var path = Path.Combine(exeDir, "recipe_use_emote_whitelist.json");
                Log.Information("[RIMS] whitelist path: {Path}", path);
                if (!File.Exists(path))
                {
                    _enabled = true;
                    _allowed = new HashSet<uint>();
                    Log.Warning(
                        "[RIMS] whitelist missing at {Path}; denying all (fail-closed).",
                        path
                    );
                    return;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                if (
                    root.TryGetProperty("enabled", out var enabledEl)
                    && (
                        enabledEl.ValueKind == JsonValueKind.True
                        || enabledEl.ValueKind == JsonValueKind.False
                    )
                )
                {
                    _enabled = enabledEl.GetBoolean();
                }

                var allowed = new HashSet<uint>();

                if (
                    root.TryGetProperty("allowedWcids", out var arr)
                    && arr.ValueKind == JsonValueKind.Array
                )
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (
                            el.ValueKind == JsonValueKind.Number
                            && el.TryGetUInt32(out var v)
                            && v > 0
                        )
                        {
                            allowed.Add(v);
                        }
                    }
                }

                _allowed = allowed;

                Log.Information(
                    "[RIMS] whitelist loaded: enabled={Enabled} count={Count}",
                    _enabled,
                    _allowed.Count
                );
            }
            catch (Exception ex)
            {
                _enabled = true;
                _allowed = new HashSet<uint>();
                Log.Warning(
                    ex,
                    "[RIMS] whitelist failed to load; denying all (fail-closed)."
                );
            }
        }
    }

    public static bool IsAllowed(uint componentWcid)
    {
        if (!_initialized)
        {
            Initialize();
        }

        if (!_enabled)
        {
            return false;
        }

        return _allowed.Contains(componentWcid);
    }
}
