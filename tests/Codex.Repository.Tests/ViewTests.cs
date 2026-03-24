using Codex.Core;
using Codex.Repository;
using Xunit;

namespace Codex.Repository.Tests;

public class ViewTests : IDisposable
{
    readonly string m_tempDir;
    readonly FactStore m_store;

    public ViewTests()
    {
        m_tempDir = Path.Combine(Path.GetTempPath(), "codex_view_test_" + Guid.NewGuid().ToString("N")[..8]);
        m_store = FactStore.Init(m_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(m_tempDir, true); } catch { }
    }

    [Fact]
    public void Default_current_view_is_canonical()
    {
        Assert.Equal("canonical", m_store.GetCurrentViewName());
    }

    [Fact]
    public void CreateView_creates_empty_view()
    {
        m_store.CreateView("feature-x");
        IReadOnlyList<ViewInfo> views = m_store.ListViews();
        Assert.Contains(views, v => v.Name == "feature-x" && v.DefinitionCount == 0);
    }

    [Fact]
    public void CreateView_duplicate_throws()
    {
        m_store.CreateView("feature-x");
        Assert.Throws<InvalidOperationException>(() => m_store.CreateView("feature-x"));
    }

    [Fact]
    public void CreateView_with_copy_copies_current()
    {
        Fact def = Fact.CreateDefinition("square x = x * x", "alice", "init");
        m_store.Store(def);
        m_store.UpdateView("square", def.Hash);

        m_store.CreateView("experiment", copyFromCurrent: true);

        ValueMap<string, ContentHash> experiment = m_store.GetNamedView("experiment");
        ContentHash? hash = null;
        foreach (KeyValuePair<string, ContentHash> kv in experiment)
        {
            if (kv.Key == "square") hash = kv.Value;
        }
        Assert.NotNull(hash);
        Assert.Equal(def.Hash, hash.Value);
    }

    [Fact]
    public void SwitchView_changes_current()
    {
        m_store.CreateView("feature-x");
        m_store.SwitchView("feature-x");
        Assert.Equal("feature-x", m_store.GetCurrentViewName());
    }

    [Fact]
    public void SwitchView_nonexistent_throws()
    {
        Assert.Throws<InvalidOperationException>(() => m_store.SwitchView("nonexistent"));
    }

    [Fact]
    public void DeleteView_removes_view()
    {
        m_store.CreateView("feature-x");
        m_store.DeleteView("feature-x");
        IReadOnlyList<ViewInfo> views = m_store.ListViews();
        Assert.DoesNotContain(views, v => v.Name == "feature-x");
    }

    [Fact]
    public void DeleteView_canonical_throws()
    {
        Assert.Throws<InvalidOperationException>(() => m_store.DeleteView("canonical"));
    }

    [Fact]
    public void DeleteView_resets_current_to_canonical()
    {
        m_store.CreateView("feature-x");
        m_store.SwitchView("feature-x");
        m_store.DeleteView("feature-x");
        Assert.Equal("canonical", m_store.GetCurrentViewName());
    }

    [Fact]
    public void UpdateNamedView_and_GetNamedView_roundtrip()
    {
        m_store.CreateView("experiment");
        Fact def = Fact.CreateDefinition("add x y = x + y", "alice", "init");
        m_store.Store(def);

        m_store.UpdateNamedView("experiment", "add", def.Hash);

        ValueMap<string, ContentHash> view = m_store.GetNamedView("experiment");
        ContentHash? hash = null;
        foreach (KeyValuePair<string, ContentHash> kv in view)
        {
            if (kv.Key == "add") hash = kv.Value;
        }
        Assert.NotNull(hash);
        Assert.Equal(def.Hash, hash.Value);
    }

    [Fact]
    public void RemoveFromView_removes_definition()
    {
        m_store.CreateView("experiment");
        Fact def = Fact.CreateDefinition("f x = x", "alice", "init");
        m_store.Store(def);
        m_store.UpdateNamedView("experiment", "f", def.Hash);

        m_store.RemoveFromView("experiment", "f");

        ValueMap<string, ContentHash> view = m_store.GetNamedView("experiment");
        bool found = false;
        foreach (KeyValuePair<string, ContentHash> kv in view)
        {
            if (kv.Key == "f") found = true;
        }
        Assert.False(found);
    }

    [Fact]
    public void ListViews_marks_current()
    {
        m_store.CreateView("feature-x");
        m_store.SwitchView("feature-x");

        IReadOnlyList<ViewInfo> views = m_store.ListViews();
        ViewInfo? featureView = null;
        foreach (ViewInfo v in views)
        {
            if (v.Name == "feature-x") featureView = v;
        }
        Assert.NotNull(featureView);
        Assert.True(featureView.IsCurrent);
    }

    [Fact]
    public void Named_views_are_independent()
    {
        m_store.CreateView("view-a");
        m_store.CreateView("view-b");

        Fact defA = Fact.CreateDefinition("a = 1", "alice", "init");
        Fact defB = Fact.CreateDefinition("b = 2", "bob", "init");
        m_store.Store(defA);
        m_store.Store(defB);

        m_store.UpdateNamedView("view-a", "x", defA.Hash);
        m_store.UpdateNamedView("view-b", "x", defB.Hash);

        ValueMap<string, ContentHash> viewA = m_store.GetNamedView("view-a");
        ValueMap<string, ContentHash> viewB = m_store.GetNamedView("view-b");

        ContentHash? hashA = null;
        ContentHash? hashB = null;
        foreach (KeyValuePair<string, ContentHash> kv in viewA)
        {
            if (kv.Key == "x") hashA = kv.Value;
        }
        foreach (KeyValuePair<string, ContentHash> kv in viewB)
        {
            if (kv.Key == "x") hashB = kv.Value;
        }

        Assert.NotNull(hashA);
        Assert.NotNull(hashB);
        Assert.NotEqual(hashA.Value, hashB.Value);
    }

    [Fact]
    public void Legacy_view_json_accessible_as_canonical()
    {
        Fact def = Fact.CreateDefinition("legacy = 42", "alice", "init");
        m_store.Store(def);
        m_store.UpdateView("my-fn", def.Hash);

        ValueMap<string, ContentHash> canonical = m_store.GetNamedView("canonical");
        ContentHash? hash = null;
        foreach (KeyValuePair<string, ContentHash> kv in canonical)
        {
            if (kv.Key == "my-fn") hash = kv.Value;
        }
        Assert.NotNull(hash);
        Assert.Equal(def.Hash, hash.Value);
    }

    [Fact]
    public void CreateView_empty_name_throws()
    {
        Assert.Throws<ArgumentException>(() => m_store.CreateView(""));
    }

    [Fact]
    public void CreateView_whitespace_name_throws()
    {
        Assert.Throws<ArgumentException>(() => m_store.CreateView("  "));
    }

    [Fact]
    public void CreateView_path_separator_throws()
    {
        Assert.Throws<ArgumentException>(() => m_store.CreateView("../evil"));
    }

    [Fact]
    public void CreateView_dot_name_throws()
    {
        Assert.Throws<ArgumentException>(() => m_store.CreateView(".."));
    }

    [Fact]
    public void GetNamedView_nonexistent_throws()
    {
        Assert.Throws<InvalidOperationException>(() => m_store.GetNamedView("nonexistent"));
    }

    [Fact]
    public void UpdateNamedView_nonexistent_throws()
    {
        Fact def = Fact.CreateDefinition("f x = x", "alice", "init");
        m_store.Store(def);
        Assert.Throws<InvalidOperationException>(
            () => m_store.UpdateNamedView("nonexistent", "f", def.Hash));
    }

    [Fact]
    public void RemoveFromView_nonexistent_view_throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => m_store.RemoveFromView("nonexistent", "f"));
    }

    [Fact]
    public void RemoveFromView_missing_key_is_silent_noop()
    {
        m_store.CreateView("experiment");
        Fact def = Fact.CreateDefinition("f x = x", "alice", "init");
        m_store.Store(def);
        m_store.UpdateNamedView("experiment", "f", def.Hash);

        m_store.RemoveFromView("experiment", "nonexistent-key");

        ValueMap<string, ContentHash> view = m_store.GetNamedView("experiment");
        Assert.Equal(1, view.Count);
    }

    [Fact]
    public void ViewExists_returns_true_for_created_view()
    {
        m_store.CreateView("feature-x");
        Assert.True(m_store.ViewExists("feature-x"));
    }

    [Fact]
    public void ViewExists_returns_false_for_missing_view()
    {
        Assert.False(m_store.ViewExists("nonexistent"));
    }

    [Fact]
    public void ViewExists_returns_true_for_legacy_canonical()
    {
        // FactStore.Init creates view.json, so canonical exists via legacy
        Assert.True(m_store.ViewExists("canonical"));
    }

    [Fact]
    public void CreateView_canonical_with_legacy_throws()
    {
        // FactStore.Init creates view.json — trying to create canonical should fail
        Assert.Throws<InvalidOperationException>(() => m_store.CreateView("canonical"));
    }

    [Fact]
    public void DeleteView_nonexistent_throws()
    {
        Assert.Throws<InvalidOperationException>(() => m_store.DeleteView("nonexistent"));
    }
}

public class ViewConsistencyTests : IDisposable
{
    readonly string m_tempDir;
    readonly FactStore m_store;

    public ViewConsistencyTests()
    {
        m_tempDir = Path.Combine(Path.GetTempPath(), "codex_vc_test_" + Guid.NewGuid().ToString("N")[..8]);
        m_store = FactStore.Init(m_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(m_tempDir, true); } catch { }
    }

    sealed class AlwaysConsistentChecker : IViewConsistencyChecker
    {
        public List<ViewDefinition> ReceivedDefinitions { get; } = [];

        public ViewConsistencyResult Check(IReadOnlyList<ViewDefinition> definitions)
        {
            ReceivedDefinitions.AddRange(definitions);
            return new ViewConsistencyResult(true, []);
        }
    }

    sealed class AlwaysFailsChecker : IViewConsistencyChecker
    {
        public ViewConsistencyResult Check(IReadOnlyList<ViewDefinition> definitions)
        {
            return new ViewConsistencyResult(false, ["type error: cannot unify Integer with Text"]);
        }
    }

    [Fact]
    public void Empty_view_is_consistent()
    {
        m_store.CreateView("empty");
        AlwaysConsistentChecker checker = new();

        ViewConsistencyResult result = m_store.CheckViewConsistency("empty", checker);

        Assert.True(result.IsConsistent);
        Assert.Empty(result.Errors);
        Assert.Empty(checker.ReceivedDefinitions);
    }

    [Fact]
    public void Checker_receives_all_definitions_from_view()
    {
        m_store.CreateView("test-view");
        Fact defA = Fact.CreateDefinition("square x = x * x", "alice", "init");
        Fact defB = Fact.CreateDefinition("double x = x + x", "alice", "init");
        m_store.Store(defA);
        m_store.Store(defB);
        m_store.UpdateNamedView("test-view", "square", defA.Hash);
        m_store.UpdateNamedView("test-view", "double", defB.Hash);

        AlwaysConsistentChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistency("test-view", checker);

        Assert.True(result.IsConsistent);
        Assert.Equal(2, checker.ReceivedDefinitions.Count);

        List<string> names = checker.ReceivedDefinitions.Select(d => d.Name).OrderBy(n => n).ToList();
        Assert.Equal("double", names[0]);
        Assert.Equal("square", names[1]);

        List<string> sources = checker.ReceivedDefinitions.Select(d => d.Source).OrderBy(s => s).ToList();
        Assert.Contains("double x = x + x", sources);
        Assert.Contains("square x = x * x", sources);
    }

    [Fact]
    public void Inconsistent_view_returns_errors()
    {
        m_store.CreateView("bad-view");
        Fact def = Fact.CreateDefinition("broken = ???", "alice", "init");
        m_store.Store(def);
        m_store.UpdateNamedView("bad-view", "broken", def.Hash);

        AlwaysFailsChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistency("bad-view", checker);

        Assert.False(result.IsConsistent);
        Assert.Single(result.Errors);
        Assert.Contains("cannot unify", result.Errors[0]);
    }

    [Fact]
    public void Missing_fact_returns_error()
    {
        m_store.CreateView("missing-view");
        ContentHash fakeHash = ContentHash.Of("nonexistent");
        m_store.UpdateNamedView("missing-view", "ghost", fakeHash);

        AlwaysConsistentChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistency("missing-view", checker);

        Assert.False(result.IsConsistent);
        Assert.Contains(result.Errors, e => e.Contains("missing fact"));
    }

    [Fact]
    public void Non_definition_fact_returns_error()
    {
        m_store.CreateView("wrong-kind");
        ContentHash defHash = ContentHash.Of("some-def");
        Fact trust = Fact.CreateTrust(defHash, TrustDegree.Reviewed, "bob", "ok");
        m_store.Store(trust);
        m_store.UpdateNamedView("wrong-kind", "bad-entry", trust.Hash);

        AlwaysConsistentChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistency("wrong-kind", checker);

        Assert.False(result.IsConsistent);
        Assert.Contains(result.Errors, e => e.Contains("Trust") && e.Contains("expected Definition"));
    }
}

public class ViewCompositionTests : IDisposable
{
    readonly string m_tempDir;
    readonly FactStore m_store;

    public ViewCompositionTests()
    {
        m_tempDir = Path.Combine(Path.GetTempPath(), "codex_vc_test_" + Guid.NewGuid().ToString("N")[..8]);
        m_store = FactStore.Init(m_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(m_tempDir, true); } catch { }
    }

    Fact StoreDef(string source)
    {
        Fact def = Fact.CreateDefinition(source, "alice", "init");
        m_store.Store(def);
        return def;
    }

    [Fact]
    public void OverrideView_basic()
    {
        Fact defA = StoreDef("a = 1");
        Fact defB = StoreDef("b = 2");
        Fact defA2 = StoreDef("a = 10");

        m_store.CreateView("base");
        m_store.UpdateNamedView("base", "a", defA.Hash);
        m_store.UpdateNamedView("base", "b", defB.Hash);

        ValueMap<string, ContentHash> overrides = ValueMap<string, ContentHash>.s_empty
            .Set("a", defA2.Hash);

        m_store.OverrideView("base", "derived", overrides);

        ValueMap<string, ContentHash> derived = m_store.GetNamedView("derived");
        Assert.Equal(defA2.Hash, derived["a"]);
        Assert.Equal(defB.Hash, derived["b"]);
    }

    [Fact]
    public void OverrideView_adds_new_entry()
    {
        Fact defA = StoreDef("a = 1");
        Fact defC = StoreDef("c = 3");

        m_store.CreateView("base");
        m_store.UpdateNamedView("base", "a", defA.Hash);

        ValueMap<string, ContentHash> overrides = ValueMap<string, ContentHash>.s_empty
            .Set("c", defC.Hash);

        m_store.OverrideView("base", "extended", overrides);

        ValueMap<string, ContentHash> extended = m_store.GetNamedView("extended");
        Assert.Equal(2, extended.Count);
        Assert.Equal(defA.Hash, extended["a"]);
        Assert.Equal(defC.Hash, extended["c"]);
    }

    [Fact]
    public void OverrideView_nonexistent_base_throws()
    {
        ValueMap<string, ContentHash> overrides = ValueMap<string, ContentHash>.s_empty;
        Assert.Throws<InvalidOperationException>(
            () => m_store.OverrideView("nonexistent", "target", overrides));
    }

    [Fact]
    public void OverrideView_target_exists_throws()
    {
        m_store.CreateView("base");
        m_store.CreateView("target");
        ValueMap<string, ContentHash> overrides = ValueMap<string, ContentHash>.s_empty;
        Assert.Throws<InvalidOperationException>(
            () => m_store.OverrideView("base", "target", overrides));
    }

    [Fact]
    public void MergeViews_disjoint()
    {
        Fact defA = StoreDef("a = 1");
        Fact defB = StoreDef("b = 2");

        m_store.CreateView("view-a");
        m_store.UpdateNamedView("view-a", "a", defA.Hash);
        m_store.CreateView("view-b");
        m_store.UpdateNamedView("view-b", "b", defB.Hash);

        ViewMergeResult result = m_store.MergeViews("view-a", "view-b", "merged");

        Assert.True(result.Success);
        Assert.Empty(result.Conflicts);

        ValueMap<string, ContentHash> merged = m_store.GetNamedView("merged");
        Assert.Equal(2, merged.Count);
        Assert.Equal(defA.Hash, merged["a"]);
        Assert.Equal(defB.Hash, merged["b"]);
    }

    [Fact]
    public void MergeViews_overlapping_same_hash()
    {
        Fact defA = StoreDef("a = 1");

        m_store.CreateView("view-a");
        m_store.UpdateNamedView("view-a", "a", defA.Hash);
        m_store.CreateView("view-b");
        m_store.UpdateNamedView("view-b", "a", defA.Hash);

        ViewMergeResult result = m_store.MergeViews("view-a", "view-b", "merged");

        Assert.True(result.Success);
        ValueMap<string, ContentHash> merged = m_store.GetNamedView("merged");
        Assert.Equal(1, merged.Count);
        Assert.Equal(defA.Hash, merged["a"]);
    }

    [Fact]
    public void MergeViews_conflict_different_hash()
    {
        Fact defA1 = StoreDef("a = 1");
        Fact defA2 = StoreDef("a = 2");

        m_store.CreateView("view-a");
        m_store.UpdateNamedView("view-a", "a", defA1.Hash);
        m_store.CreateView("view-b");
        m_store.UpdateNamedView("view-b", "a", defA2.Hash);

        ViewMergeResult result = m_store.MergeViews("view-a", "view-b", "conflict-target");

        Assert.False(result.Success);
        Assert.Single(result.Conflicts);
        Assert.Equal("a", result.Conflicts[0].DefinitionName);
        Assert.Equal(defA1.Hash, result.Conflicts[0].HashA);
        Assert.Equal(defA2.Hash, result.Conflicts[0].HashB);

        Assert.False(m_store.ViewExists("conflict-target"));
    }

    [Fact]
    public void MergeViews_nonexistent_source_throws()
    {
        m_store.CreateView("view-a");
        Assert.Throws<InvalidOperationException>(
            () => m_store.MergeViews("view-a", "nonexistent", "target"));
    }

    [Fact]
    public void MergeViews_target_exists_throws()
    {
        m_store.CreateView("view-a");
        m_store.CreateView("view-b");
        m_store.CreateView("target");
        Assert.Throws<InvalidOperationException>(
            () => m_store.MergeViews("view-a", "view-b", "target"));
    }

    [Fact]
    public void FilterView_subset()
    {
        Fact defA = StoreDef("a = 1");
        Fact defB = StoreDef("b = 2");
        Fact defC = StoreDef("c = 3");

        m_store.CreateView("full");
        m_store.UpdateNamedView("full", "a", defA.Hash);
        m_store.UpdateNamedView("full", "b", defB.Hash);
        m_store.UpdateNamedView("full", "c", defC.Hash);

        HashSet<string> keep = ["a", "c"];
        m_store.FilterView("full", "filtered", keep);

        ValueMap<string, ContentHash> filtered = m_store.GetNamedView("filtered");
        Assert.Equal(2, filtered.Count);
        Assert.Equal(defA.Hash, filtered["a"]);
        Assert.Equal(defC.Hash, filtered["c"]);
        Assert.Null(filtered["b"]);
    }

    [Fact]
    public void FilterView_empty_filter_creates_empty_view()
    {
        Fact defA = StoreDef("a = 1");
        m_store.CreateView("full");
        m_store.UpdateNamedView("full", "a", defA.Hash);

        HashSet<string> keep = [];
        m_store.FilterView("full", "empty", keep);

        ValueMap<string, ContentHash> filtered = m_store.GetNamedView("empty");
        Assert.Equal(0, filtered.Count);
    }

    [Fact]
    public void FilterView_names_not_in_source_are_ignored()
    {
        Fact defA = StoreDef("a = 1");
        m_store.CreateView("source");
        m_store.UpdateNamedView("source", "a", defA.Hash);

        HashSet<string> keep = ["a", "nonexistent", "also-missing"];
        m_store.FilterView("source", "filtered", keep);

        ValueMap<string, ContentHash> filtered = m_store.GetNamedView("filtered");
        Assert.Equal(1, filtered.Count);
        Assert.Equal(defA.Hash, filtered["a"]);
    }

    [Fact]
    public void FilterView_nonexistent_source_throws()
    {
        HashSet<string> keep = ["a"];
        Assert.Throws<InvalidOperationException>(
            () => m_store.FilterView("nonexistent", "target", keep));
    }

    [Fact]
    public void FilterView_target_exists_throws()
    {
        m_store.CreateView("source");
        m_store.CreateView("target");
        HashSet<string> keep = ["a"];
        Assert.Throws<InvalidOperationException>(
            () => m_store.FilterView("source", "target", keep));
    }
}

public class ViewImportTests : IDisposable
{
    readonly string m_tempDir;
    readonly FactStore m_store;

    public ViewImportTests()
    {
        m_tempDir = Path.Combine(Path.GetTempPath(), "codex_import_test_" + Guid.NewGuid().ToString("N")[..8]);
        m_store = FactStore.Init(m_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(m_tempDir, true); } catch { }
    }

    Fact StoreDef(string source)
    {
        Fact def = Fact.CreateDefinition(source, "alice", "init");
        m_store.Store(def);
        return def;
    }

    [Fact]
    public void ImportFact_by_hash_roundtrips()
    {
        Fact external = StoreDef("json-parse s = s");
        m_store.CreateView("app");
        m_store.ImportFactIntoView("app", external.Hash, "json-parser");

        IReadOnlyList<ImportedFact> imports = m_store.GetViewImports("app");
        Assert.Single(imports);
        Assert.Equal(external.Hash, imports[0].Hash);
        Assert.Equal("json-parser", imports[0].LocalName);
    }

    [Fact]
    public void ImportFact_missing_hash_throws()
    {
        ContentHash fakeHash = ContentHash.Of("nonexistent-fact-content");
        m_store.CreateView("app");
        Assert.Throws<InvalidOperationException>(
            () => m_store.ImportFactIntoView("app", fakeHash, "missing"));
    }

    [Fact]
    public void ImportFact_non_definition_throws()
    {
        ContentHash defHash = ContentHash.Of("some-def");
        Fact trust = Fact.CreateTrust(defHash, TrustDegree.Reviewed, "bob", "ok");
        m_store.Store(trust);
        m_store.CreateView("app");
        Assert.Throws<InvalidOperationException>(
            () => m_store.ImportFactIntoView("app", trust.Hash, "bad"));
    }

    [Fact]
    public void ImportFact_duplicate_local_name_throws()
    {
        Fact def1 = StoreDef("a = 1");
        Fact def2 = StoreDef("b = 2");
        m_store.CreateView("app");
        m_store.ImportFactIntoView("app", def1.Hash, "util");
        Assert.Throws<InvalidOperationException>(
            () => m_store.ImportFactIntoView("app", def2.Hash, "util"));
    }

    [Fact]
    public void RemoveImport_removes_by_local_name()
    {
        Fact def = StoreDef("x = 1");
        m_store.CreateView("app");
        m_store.ImportFactIntoView("app", def.Hash, "x-lib");

        m_store.RemoveImportFromView("app", "x-lib");

        IReadOnlyList<ImportedFact> imports = m_store.GetViewImports("app");
        Assert.Empty(imports);
    }

    [Fact]
    public void RemoveImport_missing_name_is_silent()
    {
        m_store.CreateView("app");
        m_store.RemoveImportFromView("app", "nonexistent");
        Assert.Empty(m_store.GetViewImports("app"));
    }

    [Fact]
    public void GetViewImports_empty_view_returns_empty()
    {
        m_store.CreateView("app");
        Assert.Empty(m_store.GetViewImports("app"));
    }

    [Fact]
    public void Consistency_check_includes_imports()
    {
        Fact local = StoreDef("local-fn x = x");
        Fact external = StoreDef("imported-fn y = y + 1");

        m_store.CreateView("app");
        m_store.UpdateNamedView("app", "local-fn", local.Hash);
        m_store.ImportFactIntoView("app", external.Hash, "imported-fn");

        ImportTestChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistency("app", checker);

        Assert.True(result.IsConsistent);
        Assert.Equal(2, checker.ReceivedDefinitions.Count);
        Assert.Contains(checker.ReceivedDefinitions, d => d.Name == "imported-fn");
    }

    [Fact]
    public void Consistency_check_fails_on_missing_import()
    {
        m_store.CreateView("app");

        // Manually write an imports file referencing a hash not in the store
        string importsPath = Path.Combine(m_tempDir, ".codex", "views", "app.imports.json");
        Directory.CreateDirectory(Path.GetDirectoryName(importsPath)!);
        File.WriteAllText(importsPath,
            "[{\"hash\":\"" + ContentHash.Of("ghost").ToHex() + "\",\"localName\":\"ghost\"}]");

        ImportTestChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistency("app", checker);

        Assert.False(result.IsConsistent);
        Assert.Contains(result.Errors, e => e.Contains("ghost") && e.Contains("missing fact"));
    }

    sealed class ImportTestChecker : IViewConsistencyChecker
    {
        public List<ViewDefinition> ReceivedDefinitions { get; } = [];

        public ViewConsistencyResult Check(IReadOnlyList<ViewDefinition> definitions)
        {
            ReceivedDefinitions.AddRange(definitions);
            return new ViewConsistencyResult(true, []);
        }
    }

    [Fact]
    public void DeleteView_cleans_up_imports()
    {
        Fact def = StoreDef("x = 1");
        m_store.CreateView("temp");
        m_store.ImportFactIntoView("temp", def.Hash, "x-lib");

        m_store.DeleteView("temp");

        string importsPath = Path.Combine(m_tempDir, ".codex", "views", "temp.imports.json");
        Assert.False(File.Exists(importsPath));
    }
}

public class TrustLatticeTests : IDisposable
{
    readonly string m_tempDir;
    readonly FactStore m_store;

    public TrustLatticeTests()
    {
        m_tempDir = Path.Combine(Path.GetTempPath(), "codex_trust_test_" + Guid.NewGuid().ToString("N")[..8]);
        m_store = FactStore.Init(m_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(m_tempDir, true); } catch { }
    }

    Fact StoreDef(string source, string author = "alice")
    {
        Fact def = Fact.CreateDefinition(source, author, "init");
        m_store.Store(def);
        return def;
    }

    [Fact]
    public void Direct_vouch_gives_full_weight()
    {
        Fact def = StoreDef("f x = x");
        Fact trust = Fact.CreateTrust(def.Hash, TrustDegree.Verified, "bob", "reviewed");
        m_store.Store(trust);

        TrustScore score = m_store.ComputeTrust(def.Hash, "bob");
        Assert.Equal(0.75, score.Weight);
        Assert.Contains("direct vouch", score.Reason);
    }

    [Fact]
    public void No_vouch_gives_zero()
    {
        Fact def = StoreDef("f x = x");
        TrustScore score = m_store.ComputeTrust(def.Hash, "bob");
        Assert.Equal(0.0, score.Weight);
    }

    [Fact]
    public void Direct_vouch_critical_gives_1()
    {
        Fact def = StoreDef("f x = x");
        Fact trust = Fact.CreateTrust(def.Hash, TrustDegree.Critical, "bob", "critical");
        m_store.Store(trust);

        TrustScore score = m_store.ComputeTrust(def.Hash, "bob");
        Assert.Equal(1.0, score.Weight);
    }

    [Fact]
    public void Transitive_trust_decays()
    {
        // Alice creates a fact
        Fact def = StoreDef("f x = x", "alice");

        // Alice vouches for her own fact (Verified = 0.75)
        Fact aliceVouch = Fact.CreateTrust(def.Hash, TrustDegree.Verified, "alice", "self-review");
        m_store.Store(aliceVouch);

        // Bob vouches for something by alice (Tested = 0.5) — establishes trust in alice
        Fact aliceDef2 = StoreDef("g y = y", "alice");
        Fact bobVouchAlice = Fact.CreateTrust(aliceDef2.Hash, TrustDegree.Tested, "bob", "alice seems good");
        m_store.Store(bobVouchAlice);

        // Bob's trust in def = bob's trust in alice (0.5) * alice's vouch weight (0.75) = 0.375
        TrustScore score = m_store.ComputeTrust(def.Hash, "bob");
        Assert.True(score.Weight > 0.0, "Should have transitive trust");
        Assert.True(score.Weight <= 0.5, $"Transitive trust should decay, got {score.Weight}");
    }

    [Fact]
    public void Trust_threshold_rejects_untrusted_imports()
    {
        Fact local = StoreDef("local-fn x = x");
        Fact external = StoreDef("external-fn y = y", "stranger");

        m_store.CreateView("app");
        m_store.UpdateNamedView("app", "local-fn", local.Hash);
        m_store.ImportFactIntoView("app", external.Hash, "ext");

        // No vouches for external — trust is 0.0
        TrustChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistencyWithTrust("app", checker, 0.5, "bob");

        Assert.False(result.IsConsistent);
        Assert.Contains(result.Errors, e => e.Contains("trust") && e.Contains("0.00"));
    }

    [Fact]
    public void Trust_threshold_accepts_vouched_imports()
    {
        Fact external = StoreDef("external-fn y = y", "alice");

        // Bob vouches for alice's fact
        Fact vouch = Fact.CreateTrust(external.Hash, TrustDegree.Verified, "bob", "reviewed");
        m_store.Store(vouch);

        m_store.CreateView("app");
        m_store.ImportFactIntoView("app", external.Hash, "ext");

        TrustChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistencyWithTrust("app", checker, 0.5, "bob");

        Assert.True(result.IsConsistent);
    }

    [Fact]
    public void Trust_threshold_zero_skips_check()
    {
        Fact external = StoreDef("f x = x", "stranger");
        m_store.CreateView("app");
        m_store.ImportFactIntoView("app", external.Hash, "ext");

        TrustChecker checker = new();
        ViewConsistencyResult result = m_store.CheckViewConsistencyWithTrust("app", checker, 0.0, "bob");

        Assert.True(result.IsConsistent);
    }

    [Fact]
    public void Multiple_vouchers_takes_max()
    {
        Fact def = StoreDef("f x = x");
        Fact v1 = Fact.CreateTrust(def.Hash, TrustDegree.Reviewed, "bob", "quick look");
        Fact v2 = Fact.CreateTrust(def.Hash, TrustDegree.Verified, "bob", "deep review");
        m_store.Store(v1);
        m_store.Store(v2);

        TrustScore score = m_store.ComputeTrust(def.Hash, "bob");
        Assert.Equal(0.75, score.Weight); // max(Reviewed=0.25, Verified=0.75)
    }

    sealed class TrustChecker : IViewConsistencyChecker
    {
        public ViewConsistencyResult Check(IReadOnlyList<ViewDefinition> definitions)
            => new(true, []);
    }
}

public class ProofFactTests : IDisposable
{
    readonly string m_tempDir;
    readonly FactStore m_store;

    public ProofFactTests()
    {
        m_tempDir = Path.Combine(Path.GetTempPath(), "codex_proof_test_" + Guid.NewGuid().ToString("N")[..8]);
        m_store = FactStore.Init(m_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(m_tempDir, true); } catch { }
    }

    [Fact]
    public void CreateProof_has_correct_kind()
    {
        Fact proof = Fact.CreateProof("reverse-reverse", "Proof: by induction", "alice", "lemma proven");
        Assert.Equal(FactKind.Proof, proof.Kind);
    }

    [Fact]
    public void CreateProof_content_includes_claim_name()
    {
        Fact proof = Fact.CreateProof("reverse-reverse", "Proof: by induction", "alice", "lemma proven");
        Assert.Contains("claim:reverse-reverse", proof.Content);
        Assert.Contains("Proof: by induction", proof.Content);
    }

    [Fact]
    public void CreateProof_is_content_addressed()
    {
        Fact proof1 = Fact.CreateProof("claim-a", "Proof: refl", "alice", "init");
        Fact proof2 = Fact.CreateProof("claim-a", "Proof: refl", "bob", "copy");
        Assert.Equal(proof1.Hash, proof2.Hash);
    }

    [Fact]
    public void CreateProof_with_definition_reference()
    {
        Fact def = Fact.CreateDefinition("reverse xs = fold (flip cons) [] xs", "alice", "init");
        Fact proof = Fact.CreateProof("reverse-reverse", "Proof: by induction", "alice", "lemma",
            definitionHash: def.Hash);
        Assert.Single(proof.References);
        Assert.Equal(def.Hash, proof.References[0]);
    }

    [Fact]
    public void Proof_fact_round_trips_through_store()
    {
        Fact proof = Fact.CreateProof("assoc", "Proof: by induction on xs", "alice", "verified");
        m_store.Store(proof);
        Fact? loaded = m_store.Load(proof.Hash);
        Assert.NotNull(loaded);
        Assert.Equal(FactKind.Proof, loaded.Kind);
        Assert.Equal(proof.Content, loaded.Content);
        Assert.Equal(proof.Author, loaded.Author);
    }

    [Fact]
    public void GetFactsByKind_returns_proof_facts()
    {
        Fact def = Fact.CreateDefinition("id x = x", "alice", "init");
        Fact proof = Fact.CreateProof("id-id", "Proof: refl", "alice", "trivial");
        m_store.Store(def);
        m_store.Store(proof);

        IReadOnlyList<Fact> proofs = m_store.GetFactsByKind(FactKind.Proof);
        Assert.Single(proofs);
        Assert.Equal(proof.Hash, proofs[0].Hash);
    }

    [Fact]
    public void ViewConsistencyResult_carries_proof_coverage()
    {
        ViewConsistencyResult result = new(true, [], ClaimCount: 3, ProvenClaimCount: 2);
        Assert.Equal(3, result.ClaimCount);
        Assert.Equal(2, result.ProvenClaimCount);
    }
}
