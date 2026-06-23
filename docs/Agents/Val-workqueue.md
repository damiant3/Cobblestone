# Val Work Queue

Agent: val
Model: Claude Opus 4.6 (1M context)
Workspace: D:\Projects\NewRepository-val
P4Client: BigWhite_Codex_val
Stream: //Codex/CodexMagic
Main client: BigWhite_Codex_val_main

## Session: 2026-06-21 to 2026-06-22

### Delivered

#### AssetForge (CLs 5537-5563, 5600, 5623)
- 25 new AI foreword modules (SafeTensors, Conv2d, Normalization, ImageTensor,
  UNet, UNetXL, TextEncoder, TextEncoderXL, DiffusionPipeline, Sampler,
  PromptParser, LoraLoader, ModelRegistry, VaeDecoder, VaeTiling, Inpainting,
  HiresFix, ControlNet, FluxPipeline, ClipInterrogator, Upscaler, FaceRestore,
  ImageTo3d, PngMetadata, Gltf encoder)
- Extended GpuProxy with all 17 op command constructors
- sd-generate.cu: standalone CUDA SD 1.5 inference (full 25-block UNet with
  skip connections, self-attention, cross-attention, CLIP 12 layers, DDIM,
  full VAE decoder). Produces 512x512 BMP from text prompt
- gpu-dispatch.exe built (tools/build-gpu-dispatch.ps1)
- DiffusionApp GuiOS view (view 25, sidebar slot 20)
- AssetForge app (apps/assetforge/AssetForge.codex, CardArtPipeline.codex)
- Design doc: docs/Designs/Tools/Active/AssetForge.md

#### Tracker + Workflow (CLs 5630-5682)
- codex.tracker quire: IssueTypes, IssueStore, SprintEngine, WorkflowBridge
- codex.workflow quire: ProcessTypes, ProcessEngine, ProcessTemplates
  (extracted from apps/workflow/)
- TrackerApp: Linear-style issue tracker in GuiOS (view 10, replaces old
  static Tasks view). List/Board/Cycle views, detail pane, command palette,
  keyboard nav, DbServer-backed via ShellUiState.su-tracker-db
- TrackerDb: DB init + 14 seed issues, 4 projects, 2 cycles, 6 labels

#### Shared UI Components (CLs 5699-5712)
- 6 new foreword/ui modules: SettingsPanel, FilterableList, DetailPane,
  StatusBadge, SearchBar, CommandPalette
- Adopted across 37 app modules (TrackerApp, DiffusionApp, Helm, Collab,
  Diagram, Mathbook, WorkflowHtml, Explorer, ERP, CVMM)

#### Housekeeping
- Removed agent identity files (fester.txt, red.txt), cleaned CLAUDE.md
- 5 merge-downs from main, 4 copy-ups to main
- Reviewed CL 5571 (RISC-V memoization)

### Next Steps (for next session)

1. **sd-generate multi-head attention** -- The duck image is noisy because
   self-attention and cross-attention use single-head instead of proper
   multi-head (split Q/K/V into per-head chunks). This is the last
   architectural gap for quality output.

2. **Wire sd-generate into codex-vm** -- Bridge the GPU proxy shared memory
   between codex-vm and gpu-dispatch.exe so the Codex VM can dispatch
   inference operations to the host GPU.

3. **Data binding sweep** -- Binding.codex is imported but unused in all apps.
   Wire up reactive updates (Observables, dirty tracking) so sidebar stats,
   gauges, and progress bars update incrementally.

4. **Accessibility pass** -- Accessibility.codex exists but zero apps use it.
   Add acc-label and acc-role to widgets across all apps.

5. **Theme unification** -- Each app has its own palette. Migrate to the
   Theme system's Palette with app-specific accent overrides.

6. **ARM64 field index resolution** -- Previous session's WIP (CL 5460
   disabled hardcoded table). Type-based field resolution still crashes.
   Needs ConstructedTy info carried through IR.

### Perforce State
- No pending CLs
- No shelved changes
- No open files
- Last submitted: CL 5712 (copy-up CVMM widget adoption)
- Main is current through CL 5712
