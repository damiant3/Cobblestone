using System.Collections.Immutable;
using Codex.Ast;
using Codex.Core;

namespace Codex.Types;

public sealed class CapabilityChecker(DiagnosticBag diagnostics, Map<string, CodexType> typeMap)
{
    readonly DiagnosticBag m_diagnostics = diagnostics;
    readonly Map<string, CodexType> m_typeMap = typeMap;

    public CapabilityReport CheckChapter(Chapter chapter, Set<string>? grantedCapabilities = null)
    {
        Dictionary<string, ImmutableArray<string>> effectSummary = [];

        foreach (Definition def in chapter.Definitions)
        {
            CodexType? defType = m_typeMap[def.Name.Value];
            if (defType is null)
                continue;

            ImmutableArray<string> effects = ExtractEffectNames(defType);
            effectSummary[def.Name.Value] = effects;
        }

        ImmutableArray<string> mainEffects = ImmutableArray<string>.Empty;
        CodexType? mainType = m_typeMap["main"];
        if (mainType is not null)
            mainEffects = ExtractEffectNames(mainType);

        if (grantedCapabilities is not null && mainType is not null)
        {
            foreach (string effect in mainEffects)
            {
                if (!grantedCapabilities.Contains(effect))
                {
                    SourceSpan span = FindMainSpan(chapter);
                    m_diagnostics.Error("CDX4001",
                        $"Capability '{effect}' is required by main but was not granted. "
                        + $"Granted capabilities: [{string.Join(", ", grantedCapabilities)}]",
                        span);
                }
            }
        }

        return new CapabilityReport(effectSummary, mainEffects);
    }

    static ImmutableArray<string> ExtractEffectNames(CodexType type)
    {
        CodexType current = type;
        while (current is FunctionType ft)
            current = ft.Return;
        while (current is DependentFunctionType dep)
            current = dep.Body;

        if (current is not EffectfulType eft)
            return ImmutableArray<string>.Empty;

        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();
        foreach (EffectType e in eft.Effects)
            names.Add(e.EffectName.Value);
        return names.ToImmutable();
    }

    static SourceSpan FindMainSpan(Chapter chapter)
    {
        foreach (Definition def in chapter.Definitions)
            if (def.Name.Value == "main")
                return def.Span;
        return chapter.Span;
    }
}

public sealed record CapabilityReport(
    IReadOnlyDictionary<string, ImmutableArray<string>> EffectSummary,
    ImmutableArray<string> MainEffects)
{
    public bool MainRequiresEffects => !MainEffects.IsEmpty;

    public Set<string> RequiredCapabilities
    {
        get
        {
            Set<string> caps = Set<string>.s_empty;
            foreach (string e in MainEffects)
                caps = caps.Add(e);
            return caps;
        }
    }
}
