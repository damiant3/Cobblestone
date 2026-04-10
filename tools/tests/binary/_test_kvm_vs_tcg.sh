#!/bin/bash
# Compare KVM vs TCG for self-hosted ELFs
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
BM_SMALL=/tmp/bm-small-v2.elf

echo "=== KVM tests ==="
for label in "Stage0(ref-compiled)" "Stage1(self-compiled)" "SmallBM"; do
    case $label in
        Stage0*) ELF="$STAGE0" ;;
        Stage1*) ELF="$STAGE1" ;;
        SmallBM) ELF="$BM_SMALL" ;;
    esac
    RAW=/tmp/kvm-test-$$-$label
    timeout 8 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 \
        > "$RAW" 2>/dev/null < /dev/null
    SIZE=$(wc -c < "$RAW" 2>/dev/null)
    CONTENT=$(head -c 50 "$RAW" 2>/dev/null | tr '\0' '.')
    echo "  $label KVM: $SIZE bytes [$CONTENT]"
    rm -f "$RAW"
done

echo ""
echo "=== TCG tests ==="
for label in "Stage0(ref-compiled)" "Stage1(self-compiled)" "SmallBM"; do
    case $label in
        Stage0*) ELF="$STAGE0" ;;
        Stage1*) ELF="$STAGE1" ;;
        SmallBM) ELF="$BM_SMALL" ;;
    esac
    RAW=/tmp/tcg-test-$$-$label
    timeout 8 qemu-system-x86_64 -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 \
        > "$RAW" 2>/dev/null < /dev/null
    SIZE=$(wc -c < "$RAW" 2>/dev/null)
    CONTENT=$(head -c 50 "$RAW" 2>/dev/null | tr '\0' '.')
    echo "  $label TCG: $SIZE bytes [$CONTENT]"
    rm -f "$RAW"
done
