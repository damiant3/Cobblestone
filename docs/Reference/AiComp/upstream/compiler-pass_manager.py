"""
Pass Manager Infrastructure

Provides the framework for running optimization passes on HIR, LIR, and MIR.
Includes CompilerPipeline for full HIR -> LIR -> MIR -> VLIW compilation.
"""

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Optional, Any, Union, TYPE_CHECKING

from .hir import HIRFunction, ForLoop, If, Statement
from .lir import LIRFunction

if TYPE_CHECKING:
    from .mir import MachineFunction


@dataclass
class PassConfig:
    """Configuration for a single pass."""
    name: str
    enabled: bool = True
    options: dict[str, Any] = field(default_factory=dict)


@dataclass
class PassMetrics:
    """Metrics collected by a pass during execution."""
    ir_size_before: int = 0
    ir_size_after: int = 0
    ssa_count_before: int = 0
    ssa_count_after: int = 0
    custom: dict[str, Any] = field(default_factory=dict)
    messages: list[str] = field(default_factory=list)


def count_statements(hir: HIRFunction) -> int:
    """Count total statements in HIR including nested structures."""
    def count_in_body(body: list[Statement]) -> int:
        total = 0
        for stmt in body:
            total += 1
            if isinstance(stmt, ForLoop):
                total += count_in_body(stmt.body)
            elif isinstance(stmt, If):
                total += count_in_body(stmt.then_body)
                total += count_in_body(stmt.else_body)
        return total
    return count_in_body(hir.body)


class CompilerPass(ABC):
    """Base class for all compiler passes (HIR, LIR, or cross-IR)."""

    def __init__(self):
        self._metrics: Optional[PassMetrics] = None

    @property
    @abstractmethod
    def name(self) -> str:
        """Return the pass name for config matching."""
        pass

    @property
    @abstractmethod
    def input_type(self) -> str:
        """Return the input IR type: 'hir', 'lir', or 'vliw'."""
        pass

    @property
    @abstractmethod
    def output_type(self) -> str:
        """Return the output IR type: 'hir', 'lir', or 'vliw'."""
        pass

    def get_metrics(self) -> Optional[PassMetrics]:
        """Return metrics from the last run, if collected."""
        return self._metrics

    def _init_metrics(self):
        """Initialize metrics for a new run."""
        self._metrics = PassMetrics()

    def _add_metric_message(self, msg: str):
        """Add a diagnostic message to metrics."""
        if self._metrics:
            self._metrics.messages.append(msg)


class Pass(CompilerPass):
    """Base class for HIR transformation passes (backwards compatible alias)."""

    @property
    def input_type(self) -> str:
        return "hir"

    @property
    def output_type(self) -> str:
        return "hir"

    @abstractmethod
    def run(self, hir: HIRFunction, config: PassConfig) -> HIRFunction:
        """Transform HIR and return new HIRFunction."""
        pass


# Alias for clarity
HIRPass = Pass


class LoweringPass(CompilerPass):
    """Base class for passes that convert HIR to LIR."""

    @property
    def input_type(self) -> str:
        return "hir"

    @property
    def output_type(self) -> str:
        return "lir"

    @abstractmethod
    def run(self, hir: HIRFunction, config: PassConfig) -> LIRFunction:
        """Lower HIR to LIR."""
        pass


class LIRPass(CompilerPass):
    """Base class for LIR transformation passes."""

    @property
    def input_type(self) -> str:
        return "lir"

    @property
    def output_type(self) -> str:
        return "lir"

    @abstractmethod
    def run(self, lir: LIRFunction, config: PassConfig) -> LIRFunction:
        """Transform LIR and return new LIRFunction."""
        pass


class CodegenPass(CompilerPass):
    """Base class for passes that convert LIR to VLIW bundles."""

    @property
    def input_type(self) -> str:
        return "lir"

    @property
    def output_type(self) -> str:
        return "vliw"

    @abstractmethod
    def run(self, lir: LIRFunction, config: PassConfig) -> list[dict]:
        """Generate VLIW bundles from LIR."""
        pass


class MIRPass(CompilerPass):
    """Base class for MIR transformation passes."""

    @property
    def input_type(self) -> str:
        return "mir"

    @property
    def output_type(self) -> str:
        return "mir"

    @abstractmethod
    def run(self, mir: "MachineFunction", config: PassConfig) -> "MachineFunction":
        """Transform MIR and return new MachineFunction."""
        pass


class LIRToMIRLoweringPass(CompilerPass):
    """Base class for passes that convert LIR to MIR."""

    @property
    def input_type(self) -> str:
        return "lir"

    @property
    def output_type(self) -> str:
        return "mir"

    def _check_no_remaining_phis(self, lir: LIRFunction) -> None:
        """Verify phi-elimination has run. Raises if any phis remain."""
        for block_name, block in lir.blocks.items():
            if block.phis:
                phi_strs = ", ".join(str(p) for p in block.phis)
                raise RuntimeError(
                    f"{self.name}: block '{block_name}' still has phi nodes "
                    f"({phi_strs}). Run phi-elimination before {self.name}."
                )

    @abstractmethod
    def run(self, lir: LIRFunction, config: PassConfig) -> "MachineFunction":
        """Lower LIR to MIR with instruction scheduling."""
        pass


class MIRCodegenPass(CompilerPass):
    """Base class for passes that convert MIR to VLIW bundles."""

    @property
    def input_type(self) -> str:
        return "mir"

    @property
    def output_type(self) -> str:
        return "vliw"

    @abstractmethod
    def run(self, mir: "MachineFunction", config: PassConfig) -> list[dict]:
        """Generate VLIW bundles from MIR."""
        pass


@dataclass
class PassManager:
    """Manages and runs HIR transformation passes."""
    passes: list[Pass] = field(default_factory=list)
    config: dict[str, PassConfig] = field(default_factory=dict)
    print_after_all: bool = False
    print_metrics: bool = False

    def add_pass(self, p: Pass) -> None:
        """Register a pass."""
        self.passes.append(p)

    def set_config(self, data: dict) -> None:
        """Set pass configs from a dict (e.g., parsed JSON)."""
        for pass_name, opts in data.get("passes", {}).items():
            self.config[pass_name] = PassConfig(
                name=pass_name,
                enabled=opts.get("enabled", True),
                options=opts.get("options", {})
            )

    def _print_pass_metrics(self, p: Pass, cfg: PassConfig, before_size: int,
                            before_ssa: int, hir: HIRFunction):
        """Print metrics for a pass execution."""
        from .printing import print_hir

        after_size = count_statements(hir)
        after_ssa = hir.num_ssa_values

        print(f"\n=== Pass: {p.name} ===")
        print(f"Config: {', '.join(f'{k}={v}' for k, v in cfg.options.items()) or '(default)'}")

        # IR size change
        if before_size > 0:
            pct = ((after_size - before_size) / before_size) * 100
            print(f"IR size: {before_size} -> {after_size} statements ({pct:+.0f}%)")
        else:
            print(f"IR size: {before_size} -> {after_size} statements")

        # SSA count change
        if before_ssa > 0:
            pct = ((after_ssa - before_ssa) / before_ssa) * 100
            print(f"SSA values: {before_ssa} -> {after_ssa} ({pct:+.0f}%)")
        else:
            print(f"SSA values: {before_ssa} -> {after_ssa}")

        # Pass-specific metrics
        metrics = p.get_metrics()
        if metrics:
            if metrics.custom:
                print(f"Custom metrics: {metrics.custom}")
            if metrics.messages:
                print("Diagnostics:")
                for msg in metrics.messages:
                    print(f"  - {msg}")

    def run(self, hir: HIRFunction) -> HIRFunction:
        """Run all enabled passes in order."""
        from .printing import print_hir

        if self.print_after_all:
            print("=== HIR (before passes) ===")
            print_hir(hir)

        for p in self.passes:
            cfg = self.config.get(p.name, PassConfig(name=p.name))
            if not cfg.enabled:
                if self.print_metrics:
                    print(f"\n=== Pass: {p.name} === (SKIPPED - disabled)")
                continue

            # Collect before metrics
            before_size = count_statements(hir) if self.print_metrics else 0
            before_ssa = hir.num_ssa_values if self.print_metrics else 0

            hir = p.run(hir, cfg)

            # Print metrics
            if self.print_metrics:
                self._print_pass_metrics(p, cfg, before_size, before_ssa, hir)

            if self.print_after_all:
                print(f"=== HIR (after {p.name}) ===")
                print_hir(hir)

        return hir


def count_lir_instructions(lir: LIRFunction) -> int:
    """Count total instructions in LIR (excluding phis and terminators)."""
    total = 0
    for block in lir.blocks.values():
        total += len(block.instructions)
        if block.terminator:
            total += 1
    return total


def count_lir_phis(lir: LIRFunction) -> int:
    """Count total phi nodes in LIR."""
    return sum(len(block.phis) for block in lir.blocks.values())


@dataclass
class CompilerPipeline:
    """
    Manages the full compilation pipeline from HIR to VLIW.

    Handles passes that operate on different IR types (HIR, LIR, VLIW)
    and validates type compatibility between adjacent passes.
    """
    passes: list[CompilerPass] = field(default_factory=list)
    config: dict[str, PassConfig] = field(default_factory=dict)
    print_after_all: bool = False
    print_metrics: bool = False
    print_ddg_after_all: bool = False

    def add_pass(self, p: CompilerPass) -> None:
        """Register a pass in the pipeline."""
        self.passes.append(p)

    def set_config(self, data: dict) -> None:
        """Set pass configs from a dict (e.g., parsed JSON)."""
        for pass_name, opts in data.get("passes", {}).items():
            self.config[pass_name] = PassConfig(
                name=pass_name,
                enabled=opts.get("enabled", True),
                options=opts.get("options", {})
            )

    def _print_hir_metrics(self, p: CompilerPass, cfg: PassConfig,
                           before_size: int, before_ssa: int,
                           after_hir: HIRFunction):
        """Print metrics for HIR -> HIR pass."""
        after_size = count_statements(after_hir)
        after_ssa = after_hir.num_ssa_values

        print(f"\n=== Pass: {p.name} (HIR → HIR) ===")
        print(f"Config: {', '.join(f'{k}={v}' for k, v in cfg.options.items()) or '(default)'}")

        if before_size > 0:
            pct = ((after_size - before_size) / before_size) * 100
            print(f"IR size: {before_size} -> {after_size} statements ({pct:+.0f}%)")
        else:
            print(f"IR size: {before_size} -> {after_size} statements")

        if before_ssa > 0:
            pct = ((after_ssa - before_ssa) / before_ssa) * 100
            print(f"SSA values: {before_ssa} -> {after_ssa} ({pct:+.0f}%)")
        else:
            print(f"SSA values: {before_ssa} -> {after_ssa}")

        self._print_custom_metrics(p)

    def _print_lowering_metrics(self, p: CompilerPass, cfg: PassConfig,
                                 hir_size: int, lir: LIRFunction):
        """Print metrics for HIR -> LIR lowering pass."""
        lir_size = count_lir_instructions(lir)

        print(f"\n=== Pass: {p.name} (HIR → LIR) ===")
        print(f"Config: {', '.join(f'{k}={v}' for k, v in cfg.options.items()) or '(default)'}")
        print(f"HIR statements: {hir_size} -> LIR instructions: {lir_size}")
        print(f"Blocks: {len(lir.blocks)}, Max scratch: {lir.max_scratch_used}")

        self._print_custom_metrics(p)

    def _print_lir_metrics(self, p: CompilerPass, cfg: PassConfig,
                           before_size: int, before_phis: int,
                           after_lir: LIRFunction):
        """Print metrics for LIR -> LIR pass."""
        after_size = count_lir_instructions(after_lir)
        after_phis = count_lir_phis(after_lir)

        print(f"\n=== Pass: {p.name} (LIR → LIR) ===")
        print(f"Config: {', '.join(f'{k}={v}' for k, v in cfg.options.items()) or '(default)'}")

        if before_size > 0:
            pct = ((after_size - before_size) / before_size) * 100
            print(f"Instructions: {before_size} -> {after_size} ({pct:+.0f}%)")
        else:
            print(f"Instructions: {before_size} -> {after_size}")

        print(f"Phis: {before_phis} -> {after_phis}")

        self._print_custom_metrics(p)

    def _print_codegen_metrics(self, p: CompilerPass, cfg: PassConfig,
                                lir_size: int, bundles: list[dict]):
        """Print metrics for LIR -> VLIW codegen pass."""
        print(f"\n=== Pass: {p.name} (LIR → VLIW) ===")
        print(f"Config: {', '.join(f'{k}={v}' for k, v in cfg.options.items()) or '(default)'}")
        print(f"LIR instructions: {lir_size} -> VLIW bundles: {len(bundles)}")

        self._print_custom_metrics(p)

    def _print_lir_to_mir_metrics(self, p: CompilerPass, cfg: PassConfig,
                                   lir_size: int, mir: "MachineFunction"):
        """Print metrics for LIR -> MIR lowering pass."""
        print(f"\n=== Pass: {p.name} (LIR → MIR) ===")
        print(f"Config: {', '.join(f'{k}={v}' for k, v in cfg.options.items()) or '(default)'}")
        print(f"LIR instructions: {lir_size} -> MIR bundles: {mir.total_bundles()}")
        print(f"Blocks: {len(mir.blocks)}, Instructions: {mir.total_instructions()}")

        self._print_custom_metrics(p)

    def _print_mir_metrics(self, p: CompilerPass, cfg: PassConfig,
                           before_bundles: int, before_insts: int,
                           after_mir: "MachineFunction"):
        """Print metrics for MIR -> MIR pass."""
        after_bundles = after_mir.total_bundles()
        after_insts = after_mir.total_instructions()

        print(f"\n=== Pass: {p.name} (MIR → MIR) ===")
        print(f"Config: {', '.join(f'{k}={v}' for k, v in cfg.options.items()) or '(default)'}")

        if before_bundles > 0:
            pct = ((after_bundles - before_bundles) / before_bundles) * 100
            print(f"Bundles: {before_bundles} -> {after_bundles} ({pct:+.0f}%)")
        else:
            print(f"Bundles: {before_bundles} -> {after_bundles}")

        if before_insts > 0:
            pct = ((after_insts - before_insts) / before_insts) * 100
            print(f"Instructions: {before_insts} -> {after_insts} ({pct:+.0f}%)")
        else:
            print(f"Instructions: {before_insts} -> {after_insts}")

        self._print_custom_metrics(p)

    def _print_mir_codegen_metrics(self, p: CompilerPass, cfg: PassConfig,
                                    mir_bundles: int, bundles: list[dict]):
        """Print metrics for MIR -> VLIW codegen pass."""
        print(f"\n=== Pass: {p.name} (MIR → VLIW) ===")
        print(f"Config: {', '.join(f'{k}={v}' for k, v in cfg.options.items()) or '(default)'}")
        print(f"MIR bundles: {mir_bundles} -> VLIW bundles: {len(bundles)}")

        self._print_custom_metrics(p)

    def _print_custom_metrics(self, p: CompilerPass):
        """Print pass-specific custom metrics."""
        metrics = p.get_metrics()
        if metrics:
            if metrics.custom:
                print(f"Custom metrics: {metrics.custom}")
            if metrics.messages:
                print("Diagnostics:")
                for msg in metrics.messages:
                    print(f"  - {msg}")

    def _print_ddg(self, state: dict, pass_name: str):
        """Print DDGs after a pass."""
        if state["type"] == "hir":
            self._print_hir_ddg(state["ir"], pass_name)
        elif state["type"] == "lir":
            self._print_lir_ddg(state["ir"], pass_name)
        # VLIW doesn't have DDG

    def _print_hir_ddg(self, hir: HIRFunction, pass_name: str):
        """Print DDG for HIR (flat ops from body)."""
        from .hir import Op
        from .ddg import HIRDDGBuilder, print_dag_tree

        def extract_ops(stmts):
            ops = []
            for stmt in stmts:
                if isinstance(stmt, Op):
                    ops.append(stmt)
                elif isinstance(stmt, ForLoop):
                    ops.extend(extract_ops(stmt.body))
                elif isinstance(stmt, If):
                    ops.extend(extract_ops(stmt.then_body))
                    ops.extend(extract_ops(stmt.else_body))
            return ops

        ops = extract_ops(hir.body)
        if not ops:
            return

        builder = HIRDDGBuilder()
        ddgs = builder.build(ops)

        print("-" * 60)
        print(f"After DDG-{pass_name}:")
        print("-" * 60)
        print(f"HIR body: {len(ops)} ops, {len(ddgs.dags)} DAGs")
        for i, dag in enumerate(ddgs.dags):
            print(f"\n--- DAG {i} (root: {dag.root.instruction.opcode}) ---")
            print(print_dag_tree(dag))

    def _print_lir_ddg(self, lir: LIRFunction, pass_name: str):
        """Print DDG for each LIR basic block."""
        from .ddg import LIRDDGBuilder, print_dag_tree

        builder = LIRDDGBuilder()

        print("-" * 60)
        print(f"After DDG-{pass_name}:")
        print("-" * 60)

        for block_name, block in lir.blocks.items():
            if not block.instructions:
                continue
            ddgs = builder.build(block.instructions)
            print(f"\nBlock {block_name}: {len(block.instructions)} insts, {len(ddgs.dags)} DAGs")
            for i, dag in enumerate(ddgs.dags):
                print(f"\n  --- DAG {i} (root: {dag.root.instruction.opcode}) ---")
                # Indent tree output
                tree = print_dag_tree(dag)
                for line in tree.split('\n'):
                    print(f"    {line}")

    def run(self, hir: HIRFunction) -> list[dict]:
        """
        Run the full compilation pipeline.

        Args:
            hir: The HIR function to compile

        Returns:
            List of VLIW bundles
        """
        from .printing import print_hir, print_lir, print_vliw, print_mir

        if self.print_after_all:
            print("\n" + "=" * 60)
            print("COMPILATION START")
            print("=" * 60)
            print_hir(hir)

        # Track current state
        state: dict[str, Any] = {"type": "hir", "ir": hir}

        for p in self.passes:
            cfg = self.config.get(p.name, PassConfig(name=p.name))

            if not cfg.enabled:
                if self.print_metrics:
                    print(f"\n=== Pass: {p.name} === (SKIPPED - disabled)")
                continue

            # Validate type compatibility
            if p.input_type != state["type"]:
                raise TypeError(
                    f"Pass '{p.name}' expects input type '{p.input_type}' "
                    f"but current state is '{state['type']}'"
                )

            # Capture metrics BEFORE running pass (in case pass mutates in place)
            before_metrics: dict[str, Any] = {}
            if self.print_metrics:
                if p.input_type == "hir":
                    before_metrics["size"] = count_statements(state["ir"])
                    before_metrics["ssa"] = state["ir"].num_ssa_values
                elif p.input_type == "lir":
                    before_metrics["size"] = count_lir_instructions(state["ir"])
                    before_metrics["phis"] = count_lir_phis(state["ir"])
                elif p.input_type == "mir":
                    before_metrics["bundles"] = state["ir"].total_bundles()
                    before_metrics["insts"] = state["ir"].total_instructions()

            # Run the pass
            result = p.run(state["ir"], cfg)

            # Print metrics based on pass type
            if self.print_metrics:
                if p.input_type == "hir" and p.output_type == "hir":
                    self._print_hir_metrics(p, cfg, before_metrics["size"],
                                            before_metrics["ssa"], result)
                elif p.input_type == "hir" and p.output_type == "lir":
                    self._print_lowering_metrics(p, cfg, before_metrics["size"], result)
                elif p.input_type == "lir" and p.output_type == "lir":
                    self._print_lir_metrics(p, cfg, before_metrics["size"],
                                            before_metrics["phis"], result)
                elif p.input_type == "lir" and p.output_type == "vliw":
                    self._print_codegen_metrics(p, cfg, before_metrics["size"], result)
                elif p.input_type == "lir" and p.output_type == "mir":
                    self._print_lir_to_mir_metrics(p, cfg, before_metrics["size"], result)
                elif p.input_type == "mir" and p.output_type == "mir":
                    self._print_mir_metrics(p, cfg, before_metrics["bundles"],
                                            before_metrics["insts"], result)
                elif p.input_type == "mir" and p.output_type == "vliw":
                    self._print_mir_codegen_metrics(p, cfg, before_metrics["bundles"], result)

            # Print IR after pass if requested
            if self.print_after_all:
                print("-" * 60)
                print(f"After {p.name}:")
                print("-" * 60)
                if p.output_type == "hir":
                    print_hir(result)
                elif p.output_type == "lir":
                    print_lir(result)
                elif p.output_type == "mir":
                    print_mir(result)
                elif p.output_type == "vliw":
                    print_vliw(result)

            # Update state
            state = {"type": p.output_type, "ir": result}

            # Print DDG after pass if requested
            if self.print_ddg_after_all:
                self._print_ddg(state, p.name)

        if self.print_after_all:
            print("=" * 60)
            print("COMPILATION END")
            print("=" * 60 + "\n")

        # Final state should be VLIW
        if state["type"] != "vliw":
            raise RuntimeError(
                f"Pipeline did not produce VLIW output, got '{state['type']}' instead"
            )

        return state["ir"]
