<#
.SYNOPSIS
    Generates Codex.sln with correct VS2022 format (UTF-8 BOM, tab indentation, CRLF).

.DESCRIPTION
    VS .sln files have a finicky binary-ish format that text editors and AI tools
    routinely corrupt. This script produces a byte-perfect file every time.

    Edit the data tables below to reorganize the solution, then re-run.

.EXAMPLE
    pwsh -File tools/Build-Solution.ps1
    # Writes Codex.sln in the repo root
#>

param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\Codex.sln')
)

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

# ── Constants ────────────────────────────────────────────────────────────────
$T  = "`t"
$TT = "`t`t"
$NL = "`r`n"
$SolutionFolderType = '2150E333-8FDC-42A3-9474-1A3956D46DE8'
$CSharpProjectType  = 'FAE04EC0-301F-11D3-BF4B-00C04F79EFBC'

# ── Solution Folders (virtual grouping) ──────────────────────────────────────
# Format: Name, GUID, ParentGUID (empty = top level)
$solutionFolders = @(
    # Top-level groups
    ,@('src',            'A0000000-0000-0000-0000-000000000001', '')
    ,@('tests',          'A0000000-0000-0000-0000-000000000002', '')
    ,@('tools',          'A0000000-0000-0000-0000-000000000003', '')
    ,@('samples',        'A0000000-0000-0000-0000-000000000005', '')
    ,@('docs',           'A0000000-0000-0000-0000-000000000006', '')
    ,@('Solution Items', '8EC462FD-D22E-90A8-E5CE-7E832BA40C5D', '')

    # Under src
    ,@('Emitters',       'AB000000-0000-0000-0000-000000000001', 'A0000000-0000-0000-0000-000000000001')

    # Under docs
    ,@('Agents',         'AA000000-0000-0000-0000-000000000001', 'A0000000-0000-0000-0000-000000000006')
    ,@('Codex.OS',       'AA000000-0000-0000-0000-000000000002', 'A0000000-0000-0000-0000-000000000006')
    ,@('Compiler',       'AA000000-0000-0000-0000-000000000003', 'A0000000-0000-0000-0000-000000000006')
    ,@('Designs',        'AA000000-0000-0000-0000-000000000010', 'A0000000-0000-0000-0000-000000000006')
    ,@('ForFun',         'AA000000-0000-0000-0000-000000000020', 'A0000000-0000-0000-0000-000000000006')
    ,@('History',        'AA000000-0000-0000-0000-000000000021', 'A0000000-0000-0000-0000-000000000006')
    ,@('Milestones',     'AA000000-0000-0000-0000-000000000022', 'A0000000-0000-0000-0000-000000000006')
    ,@('Projects',       'AA000000-0000-0000-0000-000000000024', 'A0000000-0000-0000-0000-000000000006')
    ,@('Reviews',        'AA000000-0000-0000-0000-000000000025', 'A0000000-0000-0000-0000-000000000006')
    ,@('Stories',        'AA000000-0000-0000-0000-000000000026', 'A0000000-0000-0000-0000-000000000006')
    ,@('ToDo',           'AA000000-0000-0000-0000-000000000027', 'A0000000-0000-0000-0000-000000000006')
    ,@('User',           'AA000000-0000-0000-0000-000000000028', 'A0000000-0000-0000-0000-000000000006')
    ,@('Vision',         'AA000000-0000-0000-0000-000000000029', 'A0000000-0000-0000-0000-000000000006')

    # Under docs > Designs
    ,@('Backends',       'AA000000-0000-0000-0000-000000000011', 'AA000000-0000-0000-0000-000000000010')
    ,@('Features',       'AA000000-0000-0000-0000-000000000012', 'AA000000-0000-0000-0000-000000000010')
    ,@('Language',       'AA000000-0000-0000-0000-000000000013', 'AA000000-0000-0000-0000-000000000010')
    ,@('Memory',         'AA000000-0000-0000-0000-000000000014', 'AA000000-0000-0000-0000-000000000010')
    ,@('Tools',          'AA000000-0000-0000-0000-000000000015', 'AA000000-0000-0000-0000-000000000010')

    # Under docs > Milestones
    ,@('MM1',            'AA000000-0000-0000-0000-000000000023', 'AA000000-0000-0000-0000-000000000022')

    # Under tools
    ,@('vscode',         'E693D3C3-4C1F-4A96-ADA4-3FF981952E10', 'A0000000-0000-0000-0000-000000000003')
)

# ── C# Projects ──────────────────────────────────────────────────────────────
# Format: RelativePath, GUID, ParentFolderGUID
$csharpProjects = @(
    # src - pipeline
    ,@('src\Codex.Core\Codex.Core.csproj',                     'B1000000-0000-0000-0000-000000000001', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Syntax\Codex.Syntax.csproj',                 'B1000000-0000-0000-0000-000000000002', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Ast\Codex.Ast.csproj',                       'B1000000-0000-0000-0000-000000000003', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Semantics\Codex.Semantics.csproj',           'B1000000-0000-0000-0000-000000000004', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Types\Codex.Types.csproj',                   'B1000000-0000-0000-0000-000000000005', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.IR\Codex.IR.csproj',                         'B1000000-0000-0000-0000-000000000006', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Proofs\Codex.Proofs.csproj',                 'B1000000-0000-0000-0000-000000000007', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Repository\Codex.Repository.csproj',         'B1000000-0000-0000-0000-000000000008', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Narration\Codex.Narration.csproj',           'C3ECBBE5-05ED-4BCA-42B2-ABE8B28A368F', 'A0000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Lsp\Codex.Lsp.csproj',                      '8A803F78-740C-433A-85DA-EBD8235A5188', 'A0000000-0000-0000-0000-000000000001')

    # src > Emitters
    ,@('src\Codex.Emit\Codex.Emit.csproj',                    'B1000000-0000-0000-0000-000000000009', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.CSharp\Codex.Emit.CSharp.csproj',      'B1000000-0000-0000-0000-00000000000A', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.JavaScript\Codex.Emit.JavaScript.csproj','B1000000-0000-0000-0000-00000000000B','AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Rust\Codex.Emit.Rust.csproj',          '211CDEBC-186E-4D14-9DA4-648F7C95AEE6', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Python\Codex.Emit.Python.csproj',      '976FF80D-2BBE-4392-909A-B5270B9C36EE', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Cpp\Codex.Emit.Cpp.csproj',            '571C5563-B935-411B-A693-3F15A21C5081', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Go\Codex.Emit.Go.csproj',              '3242B7A0-5859-478F-A6E5-79C9A5A04B6D', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Java\Codex.Emit.Java.csproj',          'F861EE70-F5EA-43FD-9947-BC8B61398395', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Ada\Codex.Emit.Ada.csproj',            '9A73E681-90F9-46E8-B9CF-68F2812E8DE3', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Babbage\Codex.Emit.Babbage.csproj',    '8CA893FC-F8E3-458B-972A-52B93169EB77', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Fortran\Codex.Emit.Fortran.csproj',    'EDD661B4-6ECC-44A7-8C72-3FADB30ADEE5', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Cobol\Codex.Emit.Cobol.csproj',        '8B16F91F-D010-433A-AF36-D740B3192D71', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.IL\Codex.Emit.IL.csproj',              'B1D4509F-0F25-49DF-93BF-31E8226B831A', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.RiscV\Codex.Emit.RiscV.csproj',        'E4AEE581-E3A4-44AD-9637-403CF6C1C8C7', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Wasm\Codex.Emit.Wasm.csproj',          'E36D6E0B-0CAC-4F25-8C1A-49BC4903163C', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Arm64\Codex.Emit.Arm64.csproj',        '14F23699-1D70-47A0-BD77-DFF970E379AF', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.X86_64\Codex.Emit.X86_64.csproj',      '2798253C-F167-4144-99F3-3A0505B2D7EF', 'AB000000-0000-0000-0000-000000000001')
    ,@('src\Codex.Emit.Codex\Codex.Emit.Codex.csproj',        '420327FB-02B3-43F4-B161-226165D0278C', 'AB000000-0000-0000-0000-000000000001')

    # tools
    ,@('tools\Codex.Cli\Codex.Cli.csproj',                    'C1000000-0000-0000-0000-000000000001', 'A0000000-0000-0000-0000-000000000003')
    ,@('tools\Codex.VsExtension\Codex.VsExtension.csproj',    'D2A8BA59-7275-4D3A-A7BE-40F566D37F30', 'A0000000-0000-0000-0000-000000000003')
    ,@('tools\Codex.Bootstrap\Codex.Bootstrap.csproj',         '22945126-2CA6-6E15-B2C6-F2A387131518', 'A0000000-0000-0000-0000-000000000003')
    ,@('tools\Codex.Mcp\Codex.Mcp.csproj',                    'EF92DF72-42E8-49B2-95C9-F75AF9833E27', 'A0000000-0000-0000-0000-000000000003')

    # tests
    ,@('tests\Codex.Core.Tests\Codex.Core.Tests.csproj',            'D1000000-0000-0000-0000-000000000001', 'A0000000-0000-0000-0000-000000000002')
    ,@('tests\Codex.Syntax.Tests\Codex.Syntax.Tests.csproj',        'D1000000-0000-0000-0000-000000000002', 'A0000000-0000-0000-0000-000000000002')
    ,@('tests\Codex.Ast.Tests\Codex.Ast.Tests.csproj',              'D1000000-0000-0000-0000-000000000003', 'A0000000-0000-0000-0000-000000000002')
    ,@('tests\Codex.Semantics.Tests\Codex.Semantics.Tests.csproj',  '7DB102EF-18C6-4A75-80EE-6FC87B944314', 'A0000000-0000-0000-0000-000000000002')
    ,@('tests\Codex.Types.Tests\Codex.Types.Tests.csproj',          '8AA30FCE-8396-4A86-B82C-D9B9A894DEE6', 'A0000000-0000-0000-0000-000000000002')
    ,@('tests\Codex.Lsp.Tests\Codex.Lsp.Tests.csproj',              '0EE1DF46-B772-4374-84EE-859DAE4DF5C3', 'A0000000-0000-0000-0000-000000000002')
    ,@('tests\Codex.Repository.Tests\Codex.Repository.Tests.csproj','176BA98E-C341-4503-B743-1982DB4236C4', 'A0000000-0000-0000-0000-000000000002')
    ,@('tests\Codex.AgentToolkit.Tests\Codex.AgentToolkit.Tests.csproj','A0153615-F4B1-45F5-94FB-088D7210A3B7','A0000000-0000-0000-0000-000000000002')

    # top-level
    ,@('Codex.Codex\Codex.Codex.csproj',                      'E64958B8-1790-4757-BDC7-4F119888469F', '')
)
# ── Solution Items (files shown at solution level) ───────────────────────────
# Format: FolderGUID => array of relative paths
$solutionItems = [ordered]@{
    # samples — only .codex files
    'A0000000-0000-0000-0000-000000000005' = @(
        'samples\arithmetic.codex'
        'samples\effectful-hello.codex'
        'samples\effects-demo.codex'
        'samples\expr-calculator.codex'
        'samples\factorial.codex'
        'samples\fibonacci.codex'
        'samples\greeting.codex'
        'samples\hamt-test.codex'
        'samples\hello.codex'
        'samples\is-prime-fancy.codex'
        'samples\is-prime.codex'
        'samples\list-test.codex'
        'samples\MathLib.codex'
        'samples\mini-bootstrap.codex'
        'samples\person.codex'
        'samples\proofs.codex'
        'samples\prose-banking.codex'
        'samples\prose-greeting.codex'
        'samples\safe-divide.codex'
        'samples\shapes.codex'
        'samples\stage1-test.codex'
        'samples\state-demo.codex'
        'samples\string-ops.codex'
        'samples\tco-stress.codex'
        'samples\test-42.codex'
        'samples\test-call.codex'
        'samples\test-fact1.codex'
        'samples\test-fact2.codex'
        'samples\test-fact3.codex'
        'samples\test-fact5.codex'
        'samples\test-if.codex'
        'samples\test-mul.codex'
        'samples\test-rec.codex'
        'samples\test-run-process.codex'
        'samples\type-checker-test.codex'
        'samples\use-math-lib.codex'
    )

    # docs root-level files
    'A0000000-0000-0000-0000-000000000006' = @(
        'docs\00-OVERVIEW.md'
        'docs\10-PRINCIPLES.md'
        'docs\BUGS.md'
        'docs\CurrentPlan.md'
        'docs\KNOWN-CONDITIONS.md'
        'docs\SYNTAX-QUICKREF.md'
        'docs\TOOL-ERROR-REGISTRY.md'
    )

    # Solution Items
    '8EC462FD-D22E-90A8-E5CE-7E832BA40C5D' = @(
        '.editorconfig'
        '.gitattributes'
        '.gitignore'
        'CONTRIBUTING.md'
        '.github\copilot-instructions.md'
        'Directory.Build.props'
        'README.md'
        'CLAUDE.md'
        'DEDICATION.md'
        'GRACE-HOPPER.md'
    )

    # docs\Agents
    'AA000000-0000-0000-0000-000000000001' = @(
        'docs\Agents\Agent Linux.txt'
        'docs\Agents\Agent Nut.txt'
        'docs\Agents\Agent Windows.txt'
        'docs\Agents\Cam.txt'
    )

    # docs\Codex.OS
    'AA000000-0000-0000-0000-000000000002' = @(
        'docs\Codex.OS\DistributedAgentOS.txt'
    )

    # docs\Compiler
    'AA000000-0000-0000-0000-000000000003' = @(
        'docs\Compiler\MM3-REALITY-CHECK.md'
        'docs\Compiler\REFERENCE-COMPILER-LOCK.md'
        'docs\Compiler\REFERENCE-COMPILER-NOTES.md'
    )

    # docs\Designs\Backends
    'AA000000-0000-0000-0000-000000000011' = @(
        'docs\Designs\Backends\CAMP-IIC-SELF-HOSTED-RISCV.md'
        'docs\Designs\Backends\IL-EFFECT-HANDLERS.md'
        'docs\Designs\Backends\NATIVE-BACKEND-RISCV.md'
        'docs\Designs\Backends\RISCV-PARITY.md'
        'docs\Designs\Backends\WASM-BACKEND.md'
    )

    # docs\Designs\Features
    'AA000000-0000-0000-0000-000000000012' = @(
        'docs\Designs\Features\CAMP-IIIC-STRUCTURED-CONCURRENCY.md'
        'docs\Designs\Features\CAPABILITY-REFINEMENT.md'
        'docs\Designs\Features\CLOSURE-ESCAPE-ANALYSIS.md'
        'docs\Designs\Features\P1-BUILTIN-EXPANSION.md'
        'docs\Designs\Features\SAFE-MUTATION.md'
        'docs\Designs\Features\STDLIB-AND-CONCURRENCY.md'
        'docs\Designs\Features\V2-NARRATION-LAYER.md'
        'docs\Designs\Features\V3-REPOSITORY-FEDERATION.md'
    )

    # docs\Designs\Language
    'AA000000-0000-0000-0000-000000000013' = @(
        'docs\Designs\Language\CCE-DESIGN.md'
        'docs\Designs\Language\CCE-ENCODING-INTEGRATION.md'
        'docs\Designs\Language\CCE-NATIVE-TEXT.md'
        'docs\Designs\Language\CCE-WHITESPACE-DECISION.md'
        'docs\Designs\Language\CHAR-TYPE.md'
        'docs\Designs\Language\ProseGrammarProposal.md'
    )

    # docs\Designs\Memory
    'AA000000-0000-0000-0000-000000000014' = @(
        'docs\Designs\Memory\CAMP-IIIA-ESCAPE-ANALYSIS.md'
        'docs\Designs\Memory\Camp-IIIA-Linear-Allocator.md'
        'docs\Designs\Memory\HAMT-DESIGN.md'
        'docs\Designs\Memory\MM3-GAP-ANALYSIS.md'
        'docs\Designs\Memory\MM3-MEMORY-OPTIMIZATION-PLAN.md'
        'docs\Designs\Memory\MM3-SUMMIT-SESSION.md'
    )

    # docs\Designs\Tools
    'AA000000-0000-0000-0000-000000000015' = @(
        'docs\Designs\Tools\AGENT-TOOLKIT.md'
        'docs\Designs\Tools\MCP-SERVER.md'
        'docs\Designs\Tools\PerformanceReportAndRecommendation.md'
        'docs\Designs\Tools\REPL.md'
    )

    # docs\ForFun
    'AA000000-0000-0000-0000-000000000020' = @(
        'docs\ForFun\Clarifier.txt'
        'docs\ForFun\CompilableLaws.txt'
    )

    # docs\History
    'AA000000-0000-0000-0000-000000000021' = @(
        'docs\History\BOOTSTRAP-CONVERGENCE-PLAN.md'
        'docs\History\BOOTSTRAP-FIXEDPOINT-PLAN.md'
        'docs\History\BOOTSTRAP-STATUS.md'
        'docs\History\CAM-SESSION-PROMPT-2026-03-23.md'
        'docs\History\CAMP-IIC-SUMMIT-HANDOFF.md'
        'docs\History\CurrentPlan-2026-03-24-peak2-complete.md'
        'docs\History\CurrentPlan-2026-03-24-peak3-evening.md'
        'docs\History\CurrentPlan-2026-03-26-evening.md'
        'docs\History\CurrentPlan-2026-03-26-morning.md'
        'docs\History\DATE-AUDIT.md'
        'docs\History\DECISIONS.md'
        'docs\History\FORWARD-PLAN.md'
        'docs\History\HANDOFF-BOOTSTRAP-FIXEDPOINT.md'
        'docs\History\HANDOFF-STAGE1-OUTPUT.md'
        'docs\History\HANDOFF-TYPED-LOWERING.md'
        'docs\History\ITERATION-10-HANDOFF.md'
        'docs\History\ITERATION-11-HANDOFF.md'
        'docs\History\ITERATION-12-HANDOFF.md'
        'docs\History\ITERATION-3-HANDOFF.md'
        'docs\History\ITERATION-4-HANDOFF.md'
        'docs\History\ITERATION-5-HANDOFF.md'
        'docs\History\ITERATION-6-HANDOFF.md'
        'docs\History\ITERATION-7-HANDOFF.md'
        'docs\History\ITERATION-8-HANDOFF.md'
        'docs\History\ITERATION-9-HANDOFF.md'
        'docs\History\M13-BOOTSTRAP-PLAN.md'
        'docs\History\PostFixedPointCleanUp.md'
        'docs\History\REFLECTIONS.md'
        'docs\History\Reflections2.md'
        'docs\History\TWRP-SESSION-HANDOFF.md'
        'docs\History\V1-VIEWS-HANDOFF.md'
        'docs\History\WINDOWS-REVIEW-HANDOFF.md'
        'docs\History\X86-64-REVIEW-HANDOFF.md'
    )

    # docs\Milestones (root level)
    'AA000000-0000-0000-0000-000000000022' = @(
        'docs\Milestones\THE-LAST-PEAK.md'
    )

    # docs\Milestones\MM1
    'AA000000-0000-0000-0000-000000000023' = @(
        'docs\Milestones\MM1\00-OVERVIEW.md'
        'docs\Milestones\MM1\01-ARCHITECTURE.md'
        'docs\Milestones\MM1\02-LANGUAGE-DESIGN.md'
        'docs\Milestones\MM1\03-TYPE-SYSTEM.md'
        'docs\Milestones\MM1\04-COMPILER-PIPELINE.md'
        'docs\Milestones\MM1\05-REPOSITORY-MODEL.md'
        'docs\Milestones\MM1\06-ENVIRONMENT.md'
        'docs\Milestones\MM1\07-TRANSPILATION.md'
        'docs\Milestones\MM1\08-MILESTONES.md'
        'docs\Milestones\MM1\09-RISKS.md'
        'docs\Milestones\MM1\10-PRINCIPLES.md'
        'docs\Milestones\MM1\GLOSSARY.md'
        'docs\Milestones\MM1\PreviouslyCurrentPlan.md'
        'docs\Milestones\MM1\README.md'
    )

    # docs\Projects
    'AA000000-0000-0000-0000-000000000024' = @(
        'docs\Projects\CODEX-OS-LAB.md'
        'docs\Projects\CODEX-PHONE.md'
        'docs\Projects\MM2-BARE-METAL-COMPILER-READINESS.md'
        'docs\Projects\PHONE-WIPE.md'
        'docs\Projects\TWRP-BUILD-HANDOFF.md'
    )

    # docs\Reviews
    'AA000000-0000-0000-0000-000000000025' = @(
        'docs\Reviews\arena-repl-review.md'
        'docs\Reviews\ARM64-QEMU-VERIFICATION.md'
        'docs\Reviews\cam-apply-chain-reuse-review.md'
        'docs\Reviews\cam-cce-fixes-consolidated-review.md'
        'docs\Reviews\cam-codex-emitter-dedup-scoring-review.md'
        'docs\Reviews\cam-codex-emitter-identity-backend-review.md'
        'docs\Reviews\cam-fix-bare-metal-closure-addr.md'
        'docs\Reviews\cam-fix-tco-binary-tail-position-review.md'
        'docs\Reviews\cam-fixed-point-verified-review.md'
        'docs\Reviews\cam-floppy-disk-streaming-review.md'
        'docs\Reviews\cam-native-backend-parity.md'
        'docs\Reviews\CAM-PHASE2C-REGION-RECLAMATION-REVIEW.md'
        'docs\Reviews\cam-ring4-self-hosting.md'
        'docs\Reviews\cce-native-text-review.md'
        'docs\Reviews\CCE-PERF-IMPACT.md'
        'docs\Reviews\field-access-fix-handoff.md'
        'docs\Reviews\HANDOFF-cam.md'
        'docs\Reviews\P2-HAMT-REVERT.md'
        'docs\Reviews\parity-audit-2026-03-28.md'
        'docs\Reviews\riscv-escape-copy-review.md'
        'docs\Reviews\riscv-parity-phases1-4-review.md'
        'docs\Reviews\x86-64-backend-review.md'
    )

    # docs\Stories
    'AA000000-0000-0000-0000-000000000026' = @(
        'docs\Stories\2bFixConfirm.txt'
        'docs\Stories\2bFixConfirm2.txt'
        'docs\Stories\2bRootCause.txt'
        'docs\Stories\AgentLinux.txt'
        'docs\Stories\Opus.md'
        'docs\Stories\parser.txt'
        'docs\Stories\PostFixedPointCleanUp-AUDIT.md'
        'docs\Stories\prompt.txt'
        'docs\Stories\REVIEW-NITS.md'
        'docs\Stories\SelfHost.md'
        'docs\Stories\THE-ASCENT.md'
        'docs\Stories\x86-64.md'
    )

    # docs\ToDo
    'AA000000-0000-0000-0000-000000000027' = @(
        'docs\ToDo\BinaryEmitterGaps.md'
        'docs\ToDo\CSharpCleanup.md'
    )

    # docs\User
    'AA000000-0000-0000-0000-000000000028' = @(
        'docs\User\MCP-SETUP.md'
        'docs\User\VSCODE-SETUP.md'
    )

    # docs\Vision
    'AA000000-0000-0000-0000-000000000029' = @(
        'docs\Vision\IntelligenceLayer.txt'
        'docs\Vision\NewRepository.txt'
    )
}

# ── Build Configurations ─────────────────────────────────────────────────────
$configs = @(
    'Debug|Any CPU'
    'Debug|x64'
    'Debug|x86'
    'Release|Any CPU'
    'Release|x64'
    'Release|x86'
)

# ── Helpers ──────────────────────────────────────────────────────────────────
function G([string]$guid) { "{$($guid.ToUpper())}" }

# ── Generate ─────────────────────────────────────────────────────────────────
$sb = [System.Text.StringBuilder]::new(64000)

# Header (VS expects blank line before the format line)
[void]$sb.Append($NL)
[void]$sb.Append("Microsoft Visual Studio Solution File, Format Version 12.00$NL")
[void]$sb.Append("# Visual Studio Version 17$NL")
[void]$sb.Append("VisualStudioVersion = 17.14.37111.16$NL")
[void]$sb.Append("MinimumVisualStudioVersion = 10.0.40219.1$NL")

# Solution Folders
foreach ($f in $solutionFolders) {
    [string]$name = $f[0]
    [string]$guid = $f[1]
    $items = $solutionItems[$guid]

    [void]$sb.Append("Project(`"$(G $SolutionFolderType)`") = `"$name`", `"$name`", `"$(G $guid)`"$NL")
    if ($items -and $items.Count -gt 0) {
        [void]$sb.Append("${T}ProjectSection(SolutionItems) = preProject$NL")
        foreach ($item in $items) {
            [void]$sb.Append("$TT$item = $item$NL")
        }
        [void]$sb.Append("${T}EndProjectSection$NL")
    }
    [void]$sb.Append("EndProject$NL")
}

# C# Projects
foreach ($p in $csharpProjects) {
    [string]$path = $p[0]
    [string]$guid = $p[1]
    [string]$name = [System.IO.Path]::GetFileNameWithoutExtension($path)

    [void]$sb.Append("Project(`"$(G $CSharpProjectType)`") = `"$name`", `"$path`", `"$(G $guid)`"$NL")
    [void]$sb.Append("EndProject$NL")
}

# Global
[void]$sb.Append("Global$NL")

# Solution Configuration Platforms
[void]$sb.Append("${T}GlobalSection(SolutionConfigurationPlatforms) = preSolution$NL")
foreach ($c in $configs) {
    [void]$sb.Append("$TT$c = $c$NL")
}
[void]$sb.Append("${T}EndGlobalSection$NL")

# Project Configuration Platforms
[void]$sb.Append("${T}GlobalSection(ProjectConfigurationPlatforms) = postSolution$NL")
foreach ($p in $csharpProjects) {
    [string]$guid = G $p[1]
    foreach ($c in $configs) {
        [void]$sb.Append("$TT$guid.$c.ActiveCfg = Debug|Any CPU$NL")
        [void]$sb.Append("$TT$guid.$c.Build.0 = Debug|Any CPU$NL")
    }
}
[void]$sb.Append("${T}EndGlobalSection$NL")

# Nested Projects
[void]$sb.Append("${T}GlobalSection(NestedProjects) = preSolution$NL")

# Nest solution folders under their parents
foreach ($f in $solutionFolders) {
    [string]$guid   = $f[1]
    [string]$parent = $f[2]
    if ($parent -ne '') {
        [void]$sb.Append("$TT$(G $guid) = $(G $parent)$NL")
    }
}

# Nest C# projects under their parent folders
foreach ($p in $csharpProjects) {
    [string]$guid   = $p[1]
    [string]$parent = $p[2]
    if ($parent -ne '') {
        [void]$sb.Append("$TT$(G $guid) = $(G $parent)$NL")
    }
}

[void]$sb.Append("${T}EndGlobalSection$NL")

# Extensibility Globals
[void]$sb.Append("${T}GlobalSection(ExtensibilityGlobals) = postSolution$NL")
[void]$sb.Append("${TT}SolutionGuid = {AF02C109-BF5B-4C3E-87D8-31B2097DC5C8}$NL")
[void]$sb.Append("${T}EndGlobalSection$NL")

[void]$sb.Append("EndGlobal$NL")

# ── Write with UTF-8 BOM ────────────────────────────────────────────────────
$bom   = [byte[]]@(0xEF, 0xBB, 0xBF)
$bytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
$all   = [byte[]]::new($bom.Length + $bytes.Length)
[System.Buffer]::BlockCopy($bom, 0, $all, 0, $bom.Length)
[System.Buffer]::BlockCopy($bytes, 0, $all, $bom.Length, $bytes.Length)
[System.IO.File]::WriteAllBytes($OutputPath, $all)

# ── Verify ───────────────────────────────────────────────────────────────────
$check = [System.IO.File]::ReadAllBytes($OutputPath)
$hasBom = ($check[0] -eq 0xEF -and $check[1] -eq 0xBB -and $check[2] -eq 0xBF)
$hasTab = $check -contains 9
$lineCount = ($sb.ToString().Split("`n")).Count

Write-Host "Written: $OutputPath"
Write-Host "  Size:     $($all.Length) bytes"
Write-Host "  Lines:    $lineCount"
Write-Host "  UTF-8 BOM: $hasBom"
Write-Host "  Has tabs:  $hasTab"
if (-not $hasBom) { Write-Error "BOM missing!" }
if (-not $hasTab) { Write-Error "Tabs missing - VS will reject this!" }
