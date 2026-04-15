using Codex.Core;

namespace Codex.Repository;

public sealed record ViewInfo(string Name, int DefinitionCount, bool IsCurrent);

public sealed record ViewConsistencyResult(
    bool IsConsistent,
    IReadOnlyList<string> Errors,
    int ClaimCount = 0,
    int ProvenClaimCount = 0);

public interface IViewConsistencyChecker
{
    ViewConsistencyResult Check(IReadOnlyList<ViewDefinition> definitions);
}

public sealed record ViewDefinition(string Name, string Source);

public sealed record ImportedFact(ContentHash Hash, string LocalName);

public sealed record ViewMergeConflict(string DefinitionName, ContentHash HashA, ContentHash HashB);

public sealed record ViewMergeResult(bool Success, IReadOnlyList<ViewMergeConflict> Conflicts);

partial class FactStore
{
    readonly string m_viewsPath = Path.Combine(rootPath, ".codex", "views");
    readonly string m_currentViewMarker = Path.Combine(rootPath, ".codex", "current-view");

    public ViewConsistencyResult CheckViewConsistency(string viewName, IViewConsistencyChecker checker)
    {
        ValueMap<string, ContentHash> view = GetNamedView(viewName);
        List<ViewDefinition> definitions = [];

        foreach (KeyValuePair<string, ContentHash> kv in view)
        {
            Fact? fact = Load(kv.Value);
            if (fact is null)
            {
                return new ViewConsistencyResult(false,
                    [$"Definition '{kv.Key}' references missing fact {kv.Value.ToHex()}"]);
            }
            if (fact.Kind != FactKind.Definition)
            {
                return new ViewConsistencyResult(false,
                    [$"View entry '{kv.Key}' references a {fact.Kind} fact, expected Definition"]);
            }
            definitions.Add(new ViewDefinition(kv.Key, fact.Content));
        }

        foreach (ImportedFact import in LoadViewImports(viewName))
        {
            Fact? fact = Load(import.Hash);
            if (fact is null)
            {
                return new ViewConsistencyResult(false,
                    [$"Import '{import.LocalName}' references missing fact {import.Hash.ToShortHex()}"]);
            }
            if (fact.Kind != FactKind.Definition)
            {
                return new ViewConsistencyResult(false,
                    [$"Import '{import.LocalName}' references a {fact.Kind} fact, expected Definition"]);
            }
            definitions.Add(new ViewDefinition(import.LocalName, fact.Content));
        }

        if (definitions.Count == 0)
        {
            return new ViewConsistencyResult(true, []);
        }

        return checker.Check(definitions);
    }

    public string GetCurrentViewName()
    {
        if (File.Exists(m_currentViewMarker))
        {
            string name = File.ReadAllText(m_currentViewMarker).Trim();
            if (name.Length > 0)
            {
                return name;
            }
        }
        return "canonical";
    }

    public void CreateView(string name, bool copyFromCurrent = false)
    {
        ValidateViewName(name);
        RequireViewNotExists(name);

        Directory.CreateDirectory(m_viewsPath);
        string viewFile = Path.Combine(m_viewsPath, name + ".json");

        if (copyFromCurrent)
        {
            Map<string, string> current = LoadCurrentViewMap();
            SaveViewMapTo(viewFile, current);
        }
        else
        {
            File.WriteAllText(viewFile, "{}");
        }
    }

    public bool ViewExists(string name)
    {
        string viewFile = Path.Combine(m_viewsPath, name + ".json");
        if (File.Exists(viewFile))
        {
            return true;
        }

        if (name == "canonical" && File.Exists(m_viewPath))
        {
            return true;
        }

        return false;
    }

    public IReadOnlyList<ViewInfo> ListViews()
    {
        List<ViewInfo> views = [];
        string currentName = GetCurrentViewName();

        // Legacy view.json → canonical
        if (File.Exists(m_viewPath) && !File.Exists(Path.Combine(m_viewsPath, "canonical.json")))
        {
            Map<string, string> legacy = LoadViewMapFrom(m_viewPath);
            views.Add(new ViewInfo("canonical", legacy.Count, currentName == "canonical"));
        }

        if (!Directory.Exists(m_viewsPath))
        {
            return views;
        }

        foreach (string file in Directory.GetFiles(m_viewsPath, "*.json"))
        {
            string viewName = Path.GetFileNameWithoutExtension(file);
            Map<string, string> map = LoadViewMapFrom(file);
            views.Add(new ViewInfo(viewName, map.Count, viewName == currentName));
        }

        return views;
    }

    public void SwitchView(string name)
    {
        RequireViewExists(name);
        Directory.CreateDirectory(Path.GetDirectoryName(m_currentViewMarker)!);
        File.WriteAllText(m_currentViewMarker, name);
    }

    public void DeleteView(string name)
    {
        if (name == "canonical")
        {
            throw new InvalidOperationException("Cannot delete the canonical view.");
        }

        string viewFile = Path.Combine(m_viewsPath, name + ".json");
        if (!File.Exists(viewFile))
        {
            throw new InvalidOperationException($"View '{name}' does not exist.");
        }

        File.Delete(viewFile);

        string importsFile = ImportsFilePath(name);
        if (File.Exists(importsFile))
        {
            File.Delete(importsFile);
        }

        if (GetCurrentViewName() == name)
        {
            File.WriteAllText(m_currentViewMarker, "canonical");
        }
    }

    public ValueMap<string, ContentHash> GetNamedView(string viewName)
    {
        RequireViewExists(viewName);
        Map<string, string> raw = LoadViewRaw(viewName);
        ValueMap<string, ContentHash> result = ValueMap<string, ContentHash>.s_empty;
        foreach (KeyValuePair<string, string> kv in raw)
        {
            result = result.Set(kv.Key, ContentHash.FromHex(kv.Value));
        }

        return result;
    }

    public void UpdateNamedView(string viewName, string definitionName, ContentHash hash)
    {
        RequireViewExists(viewName);
        string viewFile = ResolveViewFile(viewName);
        Map<string, string> map = LoadViewMapFrom(viewFile);
        map = map.Set(definitionName, hash.ToHex());
        SaveViewMapTo(viewFile, map);
    }

    public void RemoveFromView(string viewName, string definitionName)
    {
        RequireViewExists(viewName);
        string viewFile = ResolveViewFile(viewName);
        Map<string, string> map = LoadViewMapFrom(viewFile);
        map = map.Remove(definitionName);
        SaveViewMapTo(viewFile, map);
    }

    public void OverrideView(
        string baseViewName,
        string targetViewName,
        ValueMap<string, ContentHash> overrides)
    {
        ValidateViewName(targetViewName);
        RequireViewExists(baseViewName);
        RequireViewNotExists(targetViewName);

        Map<string, string> baseMap = LoadViewRaw(baseViewName);
        foreach (KeyValuePair<string, ContentHash> kv in overrides)
        {
            baseMap = baseMap.Set(kv.Key, kv.Value.ToHex());
        }

        Directory.CreateDirectory(m_viewsPath);
        string targetFile = Path.Combine(m_viewsPath, targetViewName + ".json");
        SaveViewMapTo(targetFile, baseMap);
    }

    public ViewMergeResult MergeViews(
        string viewNameA,
        string viewNameB,
        string targetViewName)
    {
        ValidateViewName(targetViewName);
        RequireViewExists(viewNameA);
        RequireViewExists(viewNameB);
        RequireViewNotExists(targetViewName);

        Map<string, string> mapA = LoadViewRaw(viewNameA);
        Map<string, string> mapB = LoadViewRaw(viewNameB);

        Map<string, string> merged = mapA;
        List<ViewMergeConflict> conflicts = [];

        foreach (KeyValuePair<string, string> kv in mapB)
        {
            string? existing = merged[kv.Key];
            if (existing is not null && existing != kv.Value)
            {
                conflicts.Add(new ViewMergeConflict(
                    kv.Key,
                    ContentHash.FromHex(existing),
                    ContentHash.FromHex(kv.Value)));
            }
            else
            {
                merged = merged.Set(kv.Key, kv.Value);
            }
        }

        if (conflicts.Count > 0)
        {
            return new ViewMergeResult(false, conflicts);
        }

        Directory.CreateDirectory(m_viewsPath);
        string targetFile = Path.Combine(m_viewsPath, targetViewName + ".json");
        SaveViewMapTo(targetFile, merged);
        return new ViewMergeResult(true, []);
    }

    public void FilterView(
        string sourceViewName,
        string targetViewName,
        IReadOnlySet<string> keepNames)
    {
        ValidateViewName(targetViewName);
        RequireViewExists(sourceViewName);
        RequireViewNotExists(targetViewName);

        Map<string, string> sourceMap = LoadViewRaw(sourceViewName);
        Map<string, string> filtered = Map<string, string>.s_empty;
        foreach (KeyValuePair<string, string> kv in sourceMap)
        {
            if (keepNames.Contains(kv.Key))
            {
                filtered = filtered.Set(kv.Key, kv.Value);
            }
        }

        Directory.CreateDirectory(m_viewsPath);
        string targetFile = Path.Combine(m_viewsPath, targetViewName + ".json");
        SaveViewMapTo(targetFile, filtered);
    }

    string ResolveViewFile(string name)
    {
        // Check views directory first
        string viewFile = Path.Combine(m_viewsPath, name + ".json");
        if (File.Exists(viewFile))
        {
            return viewFile;
        }

        // Fall back to legacy view.json for "canonical"
        if (name == "canonical" && File.Exists(m_viewPath))
        {
            return m_viewPath;
        }

        return viewFile; // caller checks existence
    }

    Map<string, string> LoadCurrentViewMap()
    {
        string viewFile = ResolveViewFile(GetCurrentViewName());
        return LoadViewMapFrom(viewFile);
    }

    void RequireViewExists(string name)
    {
        string viewFile = ResolveViewFile(name);
        if (!File.Exists(viewFile))
        {
            throw new InvalidOperationException($"View '{name}' does not exist.");
        }
    }

    void RequireViewNotExists(string name)
    {
        if (name == "canonical" && File.Exists(m_viewPath))
        {
            throw new InvalidOperationException($"View '{name}' already exists.");
        }

        string viewFile = Path.Combine(m_viewsPath, name + ".json");
        if (File.Exists(viewFile))
        {
            throw new InvalidOperationException($"View '{name}' already exists.");
        }
    }

    Map<string, string> LoadViewRaw(string name)
    {
        string viewFile = ResolveViewFile(name);
        return LoadViewMapFrom(viewFile);
    }

    static void ValidateViewName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("View name cannot be empty or whitespace.", nameof(name));
        }

        if (trimmed.Length != name.Length)
        {
            throw new ArgumentException("View name cannot have leading or trailing whitespace.", nameof(name));
        }

        if (name.Contains('/') || name.Contains('\\') || name.Contains(Path.DirectorySeparatorChar))
        {
            throw new ArgumentException("View name cannot contain path separators.", nameof(name));
        }

        if (name is "." or "..")
        {
            throw new ArgumentException("View name cannot be '.' or '..'.", nameof(name));
        }
    }

    static Map<string, string> LoadViewMapFrom(string path)
    {
        if (!File.Exists(path))
        {
            return Map<string, string>.s_empty;
        }

        string json = File.ReadAllText(path);
        Dictionary<string, string>? raw =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json, s_jsonOptions);
        if (raw is null)
        {
            return Map<string, string>.s_empty;
        }

        Map<string, string> result = Map<string, string>.s_empty;
        foreach (KeyValuePair<string, string> kv in raw)
        {
            result = result.Set(kv.Key, kv.Value);
        }

        return result;
    }

    static void SaveViewMapTo(string path, Map<string, string> view)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Dictionary<string, string> raw = [];
        foreach (KeyValuePair<string, string> kv in view)
        {
            raw[kv.Key] = kv.Value;
        }

        string json = System.Text.Json.JsonSerializer.Serialize(raw, s_jsonOptions);
        File.WriteAllText(path, json);
    }

    // --- Cross-repo imports (V3 Federation Phase 1) ---

    public void ImportFactIntoView(string viewName, ContentHash factHash, string localName)
    {
        RequireViewExists(viewName);
        Fact? fact = Load(factHash);
        if (fact is null)
        {
            throw new InvalidOperationException(
                $"Fact {factHash.ToShortHex()} not found in local store.");
        }

        if (fact.Kind != FactKind.Definition)
        {
            throw new InvalidOperationException(
                $"Cannot import {fact.Kind} fact, expected Definition.");
        }

        List<ImportedFact> imports = LoadViewImports(viewName);
        if (imports.Any(i => i.LocalName == localName))
        {
            throw new InvalidOperationException(
                $"Import with local name '{localName}' already exists in view '{viewName}'.");
        }

        imports.Add(new ImportedFact(factHash, localName));
        SaveViewImports(viewName, imports);
    }

    public void RemoveImportFromView(string viewName, string localName)
    {
        RequireViewExists(viewName);
        List<ImportedFact> imports = LoadViewImports(viewName);
        imports.RemoveAll(i => i.LocalName == localName);
        SaveViewImports(viewName, imports);
    }

    public IReadOnlyList<ImportedFact> GetViewImports(string viewName)
    {
        RequireViewExists(viewName);
        return LoadViewImports(viewName);
    }

    string ImportsFilePath(string viewName)
        => Path.Combine(m_viewsPath, viewName + ".imports.json");

    List<ImportedFact> LoadViewImports(string viewName)
    {
        string path = ImportsFilePath(viewName);
        if (!File.Exists(path))
        {
            return [];
        }

        string json = File.ReadAllText(path);
        List<ImportEntryDto>? raw =
            System.Text.Json.JsonSerializer.Deserialize<List<ImportEntryDto>>(json, s_jsonOptions);
        if (raw is null)
        {
            return [];
        }

        return raw.Select(e => new ImportedFact(ContentHash.FromHex(e.Hash), e.LocalName)).ToList();
    }

    void SaveViewImports(string viewName, List<ImportedFact> imports)
    {
        string path = ImportsFilePath(viewName);
        if (imports.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }
        List<ImportEntryDto> raw = imports
            .Select(i => new ImportEntryDto { Hash = i.Hash.ToHex(), LocalName = i.LocalName })
            .ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = System.Text.Json.JsonSerializer.Serialize(raw, s_jsonOptions);
        File.WriteAllText(path, json);
    }

    sealed class ImportEntryDto
    {
        public string Hash { get; set; } = "";
        public string LocalName { get; set; } = "";
    }
}
