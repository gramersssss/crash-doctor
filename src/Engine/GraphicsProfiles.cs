using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// Graphics profiles: save the game's graphics settings under a name, edit them, and put them back.
//
// This is the one feature that writes to something the game owns, so it is built the same way the known-fix catalogue
// is: nothing happens without the user asking, every write is backed up first and can be undone in one step, and
// the result is read back and checked rather than assumed.
//
// Scope is deliberately narrow. Only graphics and display options are touched - never controls, audio, key bindings,
// accessibility or gameplay - and only by changing the "value" and "index" of options that already exist in the
// file. The file's structure, its version, and every other option are left exactly as the game wrote them; JsonNode
// preserves the original text of every number it was not asked to change.
//
// Two kinds of option, and the difference matters:
//   - Fixed: the file lists every allowed value ("values": ["Low","Medium","High"]), or it is an on/off, or a number
//     with a min, max and step. These can be edited, because a valid value can be checked before it is written.
//   - Dynamic: resolution, upscaler modes, VSync and the like. Their valid values depend on the monitor and the card,
//     and the game fills the list in at runtime, so the file does not say what is allowed. These are saved and
//     restored exactly as the game wrote them, value and index together, and are never typed in by hand.
//
// The game rewrites UserSettings.json when it exits, which would silently undo any change made while it runs, so
// nothing is written while Cyberpunk 2077 is running.

public sealed class GraphicsOption
{
    public string Key { get; set; } = "";          // "/graphics/advanced/ScreenSpaceReflectionsQuality"
    public string Group { get; set; } = "";         // "Ray tracing", "Advanced", ...
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";          // bool | string_list | name_list | int_list | int | float
    public JsonNode? Value { get; set; }
    public int? Index { get; set; }
    public List<JsonNode?>? Values { get; set; }    // allowed values, when the file lists them
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public bool Editable { get; set; }
    public string? Note { get; set; }                // why an option cannot be edited, when it cannot
}

public sealed class GraphicsProfile
{
    public string Name { get; set; } = "";
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public string? Notes { get; set; }
    /// <summary>key -> {value, index}. Index is kept alongside value for every list option, because the game reads both.</summary>
    public Dictionary<string, ProfileValue> Options { get; set; } = new();
}

public sealed class ProfileValue
{
    public JsonNode? Value { get; set; }
    public int? Index { get; set; }
}

public sealed class ProfileSummary
{
    public string Name { get; set; } = "";
    public DateTime Updated { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, string> Headline { get; set; } = new();  // a handful of settings people recognise a profile by
    public Dictionary<string, ProfileValue> Options { get; set; } = new();
    public bool MatchesGame { get; set; }                               // every saved option equals what the game has now
}

public sealed class ProfileResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

public static class GraphicsProfiles
{
    // Groups that hold graphics and display settings. Nothing outside these is ever read into a profile or written.
    static readonly (string path, string label)[] Groups =
    {
        ("/graphics/presets", "Upscaling and frame generation"),
        ("/graphics/raytracing", "Ray tracing"),
        ("/graphics/advanced", "Advanced"),
        ("/graphics/performance", "Performance"),
        ("/graphics/basic", "Basic"),
        ("/video/display", "Display"),
    };

    // Written by the game about the hardware rather than chosen by the player. Restoring them from a profile made on
    // another day, or another monitor, would be wrong.
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "OutputMonitor", "DLSS_UNSUPPORTED", "XESS_UNSUPPORTED", "FSR4_UNSUPPORTED", "AntiAliasing",
    };

    static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["QuickPresets"] = "Quick preset", ["ResolutionScaling"] = "Upscaler", ["DLSS"] = "DLSS mode", ["DLSS_BackendPreset"] = "DLSS model",
        ["DLSS_NewSharpness"] = "DLSS sharpness", ["DLAA_NewSharpness"] = "DLAA sharpness", ["DLSS_D"] = "DLSS ray reconstruction",
        ["FSR2"] = "FSR 2 mode", ["FSR2_Sharpness"] = "FSR 2 sharpness", ["FSR3"] = "FSR 3 mode", ["FSR3_Sharpness"] = "FSR 3 sharpness",
        ["FSR3_FrameGeneration"] = "FSR 3 frame generation", ["FSR4"] = "FSR 4 mode", ["FSR4_Sharpness"] = "FSR 4 sharpness",
        ["XESS"] = "XeSS mode", ["XESS_Sharpness"] = "XeSS sharpness", ["XESS_FrameGeneration"] = "XeSS frame generation",
        ["DynamicResolutionScaling"] = "Dynamic resolution", ["DRS_TargetFPS"] = "Dynamic resolution target FPS",
        ["DRS_MinResolution"] = "Dynamic resolution minimum %", ["DRS_MaxResolution"] = "Dynamic resolution maximum %",
        ["FrameGeneration"] = "Frame generation", ["DLSS_MultiFrameGeneration"] = "DLSS multi frame generation", ["DLSSFrameGen"] = "DLSS frame generation",
        ["TextureQuality"] = "Textures",
        ["RayTracing"] = "Ray tracing", ["RayTracedReflections"] = "Ray traced reflections", ["RayTracedSunShadows"] = "Ray traced sun shadows",
        ["RayTracedLocalShadows"] = "Ray traced local shadows", ["RayTracedLighting"] = "Ray traced lighting", ["RayTracedPathTracing"] = "Path tracing",
        ["RayTracedPathTracingForPhotoMode"] = "Path tracing in photo mode",
        ["ContactShadows"] = "Contact shadows", ["FacialTangentUpdates"] = "Improved facial lighting", ["Anisotropy"] = "Anisotropic filtering",
        ["ShadowMeshQuality"] = "Shadow mesh quality", ["LocalShadowsQuality"] = "Local shadow quality", ["CascadedShadowsRange"] = "Cascaded shadows range",
        ["CascadedShadowsResolution"] = "Cascaded shadows resolution", ["DistantShadowsResolution"] = "Distant shadows resolution",
        ["VolumetricFogResolution"] = "Volumetric fog", ["VolumetricCloudsQuality"] = "Volumetric clouds", ["MaxDynamicDecals"] = "Max dynamic decals",
        ["ScreenSpaceReflectionsQuality"] = "Screen space reflections", ["SubsurfaceScatteringQuality"] = "Subsurface scattering",
        ["AmbientOcclusion"] = "Ambient occlusion", ["ColorPrecision"] = "Colour precision", ["GlobaIlluminationRange"] = "Global illumination range",
        ["MirrorQuality"] = "Mirror quality", ["LODPreset"] = "Level of detail", ["CrowdDensity"] = "Crowd density",
        ["FieldOfView"] = "Field of view", ["FilmGrain"] = "Film grain", ["ChromaticAberration"] = "Chromatic aberration", ["DepthOfField"] = "Depth of field",
        ["LensFlares"] = "Lens flares", ["MotionBlur"] = "Motion blur", ["Vignette"] = "Vignette",
        ["VSync"] = "VSync", ["MaximumFPS_OnOff"] = "Frame rate limit", ["MaximumFPS_Value"] = "Frame rate limit value", ["WindowMode"] = "Window mode",
        ["Resolution"] = "Resolution", ["HDRModes"] = "HDR", ["TonemappingMidpoint"] = "Tone-mapping midpoint", ["HDR10PlusGaming"] = "HDR10+ Gaming",
        ["MaxMonitorBrightness"] = "Maximum brightness", ["PaperWhiteLevel"] = "Paper white", ["ReflexMode"] = "NVIDIA Reflex",
        ["XellMode"] = "Intel XeLL", ["XellFrameCap"] = "Intel XeLL frame cap", ["Saturation"] = "Saturation", ["Gamma"] = "Gamma",
    };

    // The settings a person recognises a profile by at a glance.
    static readonly string[] HeadlineKeys =
    {
        "/video/display/Resolution", "/graphics/presets/ResolutionScaling", "/graphics/presets/TextureQuality",
        "/graphics/raytracing/RayTracedLighting", "/graphics/raytracing/RayTracedPathTracing", "/graphics/presets/FrameGeneration",
    };

    public static string ProfilesDir => Path.Combine(GamePaths.AppData, "profiles");
    public static string BackupsDir => Path.Combine(GamePaths.AppData, "settings-backups");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ------------------------------------------------------------------------------------------------ reading

    static JsonNode? Load()
    {
        var f = GamePaths.UserSettings;
        if (!File.Exists(f)) return null;
        try { return JsonNode.Parse(Files.ReadAllTextShared(f)); } catch { return null; }
    }

    static IEnumerable<(string key, string groupLabel, JsonObject opt)> Walk(JsonNode root)
    {
        if (root["data"] is not JsonArray data) yield break;
        foreach (var g in data.OfType<JsonObject>())
        {
            var path = g["group_name"]?.GetValue<string>();
            var grp = Groups.FirstOrDefault(x => x.path == path);
            if (grp.path == null || g["options"] is not JsonArray opts) continue;
            foreach (var o in opts.OfType<JsonObject>())
            {
                var name = o["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name) || Skip.Contains(name)) continue;
                yield return ($"{path}/{name}", grp.label, o);
            }
        }
    }

    /// <summary>The graphics options as the game has them now, in the order the game lists them.</summary>
    public static List<GraphicsOption> Current()
    {
        var root = Load(); if (root == null) return new();
        var list = new List<GraphicsOption>();
        foreach (var (key, grp, o) in Walk(root))
        {
            var name = key[(key.LastIndexOf('/') + 1)..];
            var type = o["type"]?.GetValue<string>() ?? "";
            var dynamic = o["is_dynamic"]?.GetValue<bool>() == true;
            var values = o["values"] is JsonArray va ? va.Select(v => v?.DeepClone()).ToList() : null;
            var opt = new GraphicsOption
            {
                Key = key, Group = grp, Label = Labels.TryGetValue(name, out var l) ? l : Humanise(name), Type = type,
                Value = o["value"]?.DeepClone(), Index = o["index"]?.GetValue<int>(), Values = values,
                Min = Num(o["min_value"]), Max = Num(o["max_value"]), Step = Num(o["step_value"]),
            };
            opt.Editable = type switch
            {
                "bool" => true,
                "int" or "float" => opt.Min != null && opt.Max != null,
                "string_list" or "name_list" or "int_list" => !dynamic && values is { Count: > 0 },
                _ => false,
            };
            if (!opt.Editable)
                opt.Note = dynamic ? "Depends on your monitor and graphics card, so it is set in the game. A profile saves and restores it as the game wrote it." : "Not editable here.";
            list.Add(opt);
        }
        return list;
    }

    static double? Num(JsonNode? n) { try { return n?.GetValue<double>(); } catch { return null; } }
    static string Humanise(string name) => Regex.Replace(name.Replace("_", " "), "(?<=[a-z])(?=[A-Z])", " ");

    static Dictionary<string, ProfileValue> Snapshot(IEnumerable<GraphicsOption> opts) =>
        opts.ToDictionary(o => o.Key, o => new ProfileValue { Value = o.Value?.DeepClone(), Index = o.Index });

    // ------------------------------------------------------------------------------------------------ profiles

    // Names become file names and appear inside the page's buttons, so the characters that break either are refused
    // up front rather than escaped in two places and got wrong in one.
    static string? NameProblem(string name) =>
        name.Length == 0 ? "Give the profile a name."
        : name.Length > 60 ? "That name is too long; keep it under 60 characters."
        : Regex.IsMatch(name, "[\"\\\\/<>:*?|`]") ? "Profile names cannot contain \" \\ / < > : * ? | or `."
        : null;

    static string FileFor(string name) => Path.Combine(ProfilesDir, SafeName(name) + ".json");
    static string SafeName(string name) => Regex.Replace(name.Trim(), @"[^\w\- ]", "_");

    public static List<GraphicsProfile> All()
    {
        var list = new List<GraphicsProfile>();
        if (!Directory.Exists(ProfilesDir)) return list;
        foreach (var f in Directory.EnumerateFiles(ProfilesDir, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<GraphicsProfile>(File.ReadAllText(f), Opts) is { } p && p.Name.Length > 0) list.Add(p); }
            catch { /* a damaged profile is skipped, never fatal */ }
        }
        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<ProfileSummary> Summaries(List<GraphicsOption> current)
    {
        var now = current.ToDictionary(o => o.Key);
        return All().Select(p => new ProfileSummary
        {
            Name = p.Name, Updated = p.Updated, Notes = p.Notes, Options = p.Options,
            Headline = HeadlineKeys.Where(k => p.Options.ContainsKey(k))
                .ToDictionary(k => now.TryGetValue(k, out var o) ? o.Label : k[(k.LastIndexOf('/') + 1)..], k => Show(p.Options[k].Value)),
            MatchesGame = p.Options.Count > 0 && p.Options.All(kv => now.TryGetValue(kv.Key, out var o) && Same(o.Value, kv.Value.Value)),
        }).ToList();
    }

    static string Show(JsonNode? v) => v == null ? "" : v.GetValueKind() switch
    {
        JsonValueKind.True => "On", JsonValueKind.False => "Off",
        JsonValueKind.String => v.GetValue<string>().Replace("UI-Settings-Video-QualitySetting-", ""),
        _ => v.ToJsonString(),
    };
    static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    public static ProfileResult SaveCurrent(string name, string? notes = null)
    {
        name = name.Trim();
        if (NameProblem(name) is { } bad) return Fail(bad);
        var cur = Current();
        if (cur.Count == 0) return Fail("Could not read the game's settings file. Launch the game once so it creates one.");
        var existing = All().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var p = new GraphicsProfile { Name = name, Created = existing?.Created ?? DateTime.Now, Updated = DateTime.Now, Notes = notes ?? existing?.Notes, Options = Snapshot(cur) };
        Write(p);
        return Ok(existing == null ? $"Saved the game's current graphics settings as \"{name}\"." : $"Updated \"{name}\" with the game's current graphics settings.");
    }

    /// <summary>Replace a profile's values with edited ones. Every edited value is validated against what the game allows.</summary>
    public static ProfileResult Update(string name, Dictionary<string, JsonNode?> edits, string? newName = null, string? notes = null)
    {
        var p = All().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (p == null) return Fail($"There is no profile called \"{name}\".");
        var cur = Current().ToDictionary(o => o.Key);
        var changed = 0;
        foreach (var (key, raw) in edits)
        {
            if (!cur.TryGetValue(key, out var opt)) return Fail($"\"{key}\" is not a graphics setting this game has.");
            if (!opt.Editable) return Fail($"{opt.Label} depends on your hardware and can only be set in the game.");
            if (!Validate(opt, raw, out var value, out var index, out var why)) return Fail($"{opt.Label}: {why}");
            if (!p.Options.TryGetValue(key, out var pv) || !Same(pv.Value, value)) changed++;
            p.Options[key] = new ProfileValue { Value = value, Index = index ?? pv?.Index };
        }
        // a hand-edited profile is by definition not one of the game's quick presets
        if (changed > 0 && p.Options.ContainsKey("/graphics/presets/QuickPresets"))
            p.Options["/graphics/presets/QuickPresets"] = new ProfileValue { Value = JsonValue.Create("Custom"), Index = 0 };

        if (!string.IsNullOrWhiteSpace(newName) && !newName.Trim().Equals(p.Name, StringComparison.Ordinal))
        {
            newName = newName.Trim();
            if (NameProblem(newName) is { } bad) return Fail(bad);
            if (All().Any(x => x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && !x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                return Fail($"There is already a profile called \"{newName}\".");
            try { File.Delete(FileFor(p.Name)); } catch { }
            p.Name = newName;
        }
        if (notes != null) p.Notes = notes;
        p.Updated = DateTime.Now;
        Write(p);
        return Ok(changed == 0 ? $"\"{p.Name}\" saved." : $"\"{p.Name}\" saved with {changed} change{(changed == 1 ? "" : "s")}. Apply it to put it into the game.");
    }

    public static ProfileResult Delete(string name)
    {
        var f = FileFor(name);
        if (!File.Exists(f)) return Fail($"There is no profile called \"{name}\".");
        File.Delete(f);
        return Ok($"Deleted \"{name}\". The game's settings are unchanged.");
    }

    static void Write(GraphicsProfile p)
    {
        Directory.CreateDirectory(ProfilesDir);
        File.WriteAllText(FileFor(p.Name), JsonSerializer.Serialize(p, Opts));
    }

    static bool Validate(GraphicsOption opt, JsonNode? raw, out JsonNode? value, out int? index, out string why)
    {
        value = null; index = null; why = "";
        try
        {
            switch (opt.Type)
            {
                case "bool":
                    if (raw?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) { why = "must be on or off."; return false; }
                    value = JsonValue.Create(raw.GetValue<bool>()); return true;
                case "int":
                case "float":
                    var d = raw!.GetValue<double>();
                    if (d < opt.Min || d > opt.Max) { why = $"must be between {opt.Min} and {opt.Max}."; return false; }
                    value = opt.Type == "int" ? JsonValue.Create((int)Math.Round(d)) : JsonValue.Create(d);
                    return true;
                default:
                    var i = opt.Values!.FindIndex(v => JsonNode.DeepEquals(v, raw) || (v != null && raw != null && v.ToJsonString() == raw.ToJsonString()));
                    if (i < 0) { why = $"must be one of {string.Join(", ", opt.Values!.Select(Show))}."; return false; }
                    value = opt.Values[i]?.DeepClone(); index = i; return true;
            }
        }
        catch { why = "is not a valid value."; return false; }
    }

    // ------------------------------------------------------------------------------------------------ writing to the game

    public static bool GameRunning() => Process.GetProcessesByName("Cyberpunk2077").Length > 0;

    /// <summary>Put a profile's values into the game's settings file. Backed up first, checked after.</summary>
    public static ProfileResult Apply(string name)
    {
        var p = All().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (p == null) return Fail($"There is no profile called \"{name}\".");
        if (GameRunning()) return Fail("Close Cyberpunk 2077 first. The game rewrites its settings file when it exits, so a change made while it is running would be undone.");
        var f = GamePaths.UserSettings;
        var root = Load();
        if (root == null) return Fail("Could not read the game's settings file.");

        var set = 0; var missing = new List<string>();
        var byKey = Walk(root).ToDictionary(x => x.key, x => x.opt);
        foreach (var (key, pv) in p.Options)
        {
            if (!byKey.TryGetValue(key, out var o)) { missing.Add(key[(key.LastIndexOf('/') + 1)..]); continue; }
            if (!Same(o["value"], pv.Value)) set++;
            o["value"] = pv.Value?.DeepClone();
            if (pv.Index != null && o.ContainsKey("index")) o["index"] = pv.Index;
        }
        if (set == 0) return Ok($"The game already has the \"{p.Name}\" settings. Nothing was changed.");

        var backup = Backup();
        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var tmp = f + ".crashdoctor-tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, f, overwrite: true);

        // read it back: a change is only reported when it is actually in the file
        var check = Current().ToDictionary(o => o.Key);
        var wrong = p.Options.Where(kv => check.TryGetValue(kv.Key, out var o) && !Same(o.Value, kv.Value.Value)).Select(kv => kv.Key).ToList();
        if (wrong.Count > 0)
        {
            File.Copy(backup, f, overwrite: true);
            return Fail($"The settings did not take ({wrong.Count} value{(wrong.Count == 1 ? "" : "s")} read back differently), so the original file was put back.");
        }
        return Ok($"Applied \"{p.Name}\": {set} setting{(set == 1 ? "" : "s")} changed. Start the game to use it. The previous settings are backed up; Undo puts them back."
                  + (missing.Count > 0 ? $" {missing.Count} saved setting{(missing.Count == 1 ? " is" : "s are")} not in this game version and {(missing.Count == 1 ? "was" : "were")} skipped." : ""));
    }

    static string Backup()
    {
        Directory.CreateDirectory(BackupsDir);
        var dest = Path.Combine(BackupsDir, $"UserSettings-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.Copy(GamePaths.UserSettings, dest);
        // keep the last 30; older ones are no use to anyone and settings files are ~90 KB each
        foreach (var old in Directory.EnumerateFiles(BackupsDir, "UserSettings-*.json").OrderByDescending(x => x).Skip(30))
            try { File.Delete(old); } catch { }
        return dest;
    }

    public static DateTime? LastBackup() =>
        Directory.Exists(BackupsDir) ? Directory.EnumerateFiles(BackupsDir, "UserSettings-*.json").Select(File.GetLastWriteTime).DefaultIfEmpty().Max() is { } d && d != default ? d : null : null;

    /// <summary>Put back the settings file as it was before the most recent Apply.</summary>
    public static ProfileResult Undo()
    {
        if (GameRunning()) return Fail("Close Cyberpunk 2077 first, for the same reason as applying.");
        if (!Directory.Exists(BackupsDir)) return Fail("There is nothing to undo.");
        var last = Directory.EnumerateFiles(BackupsDir, "UserSettings-*.json").OrderByDescending(x => x).FirstOrDefault();
        if (last == null) return Fail("There is nothing to undo.");
        File.Copy(last, GamePaths.UserSettings, overwrite: true);
        File.Delete(last);   // undo is one step at a time; the next Undo goes one further back
        var when = DateTime.TryParseExact(Path.GetFileNameWithoutExtension(last)["UserSettings-".Length..], "yyyyMMdd-HHmmss-fff",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) ? t.ToString("d MMM HH:mm") : "before the last change";
        return Ok($"Put back the settings the game had before the change made {when}.");
    }

    static ProfileResult Ok(string m) => new() { Ok = true, Message = m };
    static ProfileResult Fail(string m) => new() { Ok = false, Message = m };
}
