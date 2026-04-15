using Codex.Core;

namespace Codex.Syntax;

public sealed partial class ProseParser
{
    void ValidateProseNotationConsistency(IReadOnlyList<ChapterNode> chapters)
    {
        foreach (ChapterNode chapter in chapters)
        {
            ValidateMembers(chapter.Members);
        }
    }

    void ValidateMembers(IReadOnlyList<DocumentMember> members)
    {
        for (int i = 0; i < members.Count; i++)
        {
            DocumentMember member = members[i];

            if (member is SectionNode section)
            {
                ValidateMembers(section.Members);
                continue;
            }

            if (member is not ProseBlockNode prose)
            {
                continue;
            }

            // Find the next notation block after this prose block
            NotationBlockNode? notation = null;
            for (int j = i + 1; j < members.Count; j++)
            {
                if (members[j] is NotationBlockNode nb)
                {
                    notation = nb;
                    break;
                }
                if (members[j] is ProseBlockNode or SectionNode)
                {
                    break;
                }
            }

            if (notation is null)
            {
                if (prose.ClaimTemplate is not null)
                {
                    m_diagnostics.Warning(CdxCodes.ProseClaimWithoutNotation,
                        "Prose declares a claim but no formal claim follows in notation",
                        prose.Span);
                }
                continue;
            }

            // Check function template against notation definitions
            if (prose.FunctionTemplate is not null && notation.Definitions.Count > 0)
            {
                ValidateFunctionTemplate(prose.FunctionTemplate, notation.Definitions[0]);
            }

            // Check claim template against notation claims
            if (prose.ClaimTemplate is not null && notation.Claims.Count > 0)
            {
                // Claim exists in both prose and notation — good, no warning needed
            }
            else if (prose.ClaimTemplate is not null && notation.Claims.Count == 0
                     && notation.Definitions.Count == 0)
            {
                m_diagnostics.Warning(CdxCodes.ProseClaimWithoutNotation,
                    "Prose declares a claim but no formal claim follows in notation",
                    prose.Span);
            }
        }
    }

    void ValidateFunctionTemplate(FunctionTemplateInfo template, DefinitionNode definition)
    {
        string defName = definition.Name.Text;
        if (!string.Equals(template.FunctionName, defName, StringComparison.OrdinalIgnoreCase))
        {
            m_diagnostics.Warning(CdxCodes.ProseFunctionNameMismatch,
                $"Prose declares function '{template.FunctionName}' but notation defines '{defName}'",
                template.Span);
        }

        // Check parameter names
        IReadOnlyList<Token> defParams = definition.Parameters;
        for (int p = 0; p < template.Parameters.Count && p < defParams.Count; p++)
        {
            string proseName = template.Parameters[p].Name;
            string notationName = defParams[p].Text;
            if (!string.Equals(proseName, notationName, StringComparison.OrdinalIgnoreCase))
            {
                m_diagnostics.Warning(CdxCodes.ProseParameterNameMismatch,
                    $"Prose parameter '{proseName}' does not match notation parameter '{notationName}'",
                    template.Span);
            }
        }
    }
}
