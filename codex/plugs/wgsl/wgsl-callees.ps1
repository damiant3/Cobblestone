# wgsl-callees.ps1 -- the names a WGSL module calls and never declares.
#
# Dot-sourced by run.ps1. WebGPU refuses a whole module that calls a function it
# does not declare ("unresolved call target"), so the runner refuses it first.
#
# A call is an identifier followed by `(`. What may legally stand there without a
# declaration in the module, per the WGSL spec (W3C CR):
#   - the predeclared built-in functions of section 17;
#   - the predeclared types used as value constructors (section 6), including
#     the vecN/matCxR shorthands with an f, h, i or u suffix;
#   - the statement keywords that take a parenthesised expression.
# A templated constructor or built-in (`vec4<f32>(`, `bitcast<u32>(`,
# `array<f32, 4>(`) puts `>` before the `(` and is not matched as a call.
# Attributes (`@workgroup_size(`) are excluded by their `@`.

$script:WgslPredeclared = @(
    # 17.1 constructor built-ins and 17.2 bit reinterpretation
    'bitcast',
    # 17.3 logical
    'all', 'any', 'select',
    # 17.4 array
    'arrayLength',
    # 17.5 numeric
    'abs', 'acos', 'acosh', 'asin', 'asinh', 'atan', 'atanh', 'atan2', 'ceil', 'clamp', 'cos', 'cosh',
    'countLeadingZeros', 'countOneBits', 'countTrailingZeros', 'cross', 'degrees', 'determinant',
    'distance', 'dot', 'dot4U8Packed', 'dot4I8Packed', 'exp', 'exp2', 'extractBits', 'faceForward',
    'firstLeadingBit', 'firstTrailingBit', 'floor', 'fma', 'fract', 'frexp', 'insertBits', 'inverseSqrt',
    'ldexp', 'length', 'log', 'log2', 'max', 'min', 'mix', 'modf', 'normalize', 'pow', 'quantizeToF16',
    'radians', 'reflect', 'refract', 'reverseBits', 'round', 'saturate', 'sign', 'sin', 'sinh',
    'smoothstep', 'sqrt', 'step', 'tan', 'tanh', 'transpose', 'trunc',
    # 17.6 derivative
    'dpdx', 'dpdxCoarse', 'dpdxFine', 'dpdy', 'dpdyCoarse', 'dpdyFine', 'fwidth', 'fwidthCoarse', 'fwidthFine',
    # 17.7 texture
    'textureDimensions', 'textureGather', 'textureGatherCompare', 'textureLoad', 'textureNumLayers',
    'textureNumLevels', 'textureNumSamples', 'textureSample', 'textureSampleBias', 'textureSampleCompare',
    'textureSampleCompareLevel', 'textureSampleGrad', 'textureSampleLevel', 'textureSampleBaseClampToEdge',
    'textureStore',
    # 17.8 atomic
    'atomicLoad', 'atomicStore', 'atomicAdd', 'atomicSub', 'atomicMax', 'atomicMin', 'atomicAnd', 'atomicOr',
    'atomicXor', 'atomicExchange', 'atomicCompareExchangeWeak',
    # 17.9 data packing and 17.10 unpacking
    'pack4x8snorm', 'pack4x8unorm', 'pack4xI8', 'pack4xU8', 'pack4xI8Clamp', 'pack4xU8Clamp',
    'pack2x16snorm', 'pack2x16unorm', 'pack2x16float',
    'unpack4x8snorm', 'unpack4x8unorm', 'unpack4xI8', 'unpack4xU8', 'unpack2x16snorm', 'unpack2x16unorm',
    'unpack2x16float',
    # 17.11 synchronization
    'storageBarrier', 'textureBarrier', 'workgroupBarrier', 'workgroupUniformLoad',
    # 17.12 subgroup and 17.13 quad
    'subgroupAdd', 'subgroupExclusiveAdd', 'subgroupInclusiveAdd', 'subgroupAll', 'subgroupAnd', 'subgroupAny',
    'subgroupBallot', 'subgroupBroadcast', 'subgroupBroadcastFirst', 'subgroupElect', 'subgroupMax',
    'subgroupMin', 'subgroupMul', 'subgroupExclusiveMul', 'subgroupInclusiveMul', 'subgroupOr',
    'subgroupShuffle', 'subgroupShuffleDown', 'subgroupShuffleUp', 'subgroupShuffleXor', 'subgroupXor',
    'quadBroadcast', 'quadSwapDiagonal', 'quadSwapX', 'quadSwapY',
    # 6 predeclared types as value constructors
    'bool', 'f16', 'f32', 'i32', 'u32', 'vec2', 'vec3', 'vec4', 'array',
    'mat2x2', 'mat2x3', 'mat2x4', 'mat3x2', 'mat3x3', 'mat3x4', 'mat4x2', 'mat4x3', 'mat4x4',
    # statement keywords followed by a parenthesised expression
    'if', 'for', 'while', 'switch', 'return', 'loop', 'case', 'else'
)
foreach ($n in 2, 3, 4) {
    foreach ($s in 'f', 'h', 'i', 'u') { $script:WgslPredeclared += "vec$n$s" }
    foreach ($m in 2, 3, 4) { foreach ($s in 'f', 'h') { $script:WgslPredeclared += "mat${n}x$m$s" } }
}

function Get-WgslUndefinedCallees([string]$Wgsl) {
    $code = [regex]::Replace($Wgsl, '/\*.*?\*/', ' ', 'Singleline')
    $code = [regex]::Replace($code, '//[^\n]*', '')
    $declared = @{}
    foreach ($m in [regex]::Matches($code, '\b(?:fn|struct|alias)\s+([A-Za-z_][A-Za-z0-9_]*)')) { $declared[$m.Groups[1].Value] = $true }
    $known = @{}
    foreach ($n in $script:WgslPredeclared) { $known[$n] = $true }
    $out = @()
    foreach ($m in [regex]::Matches($code, '(?<![@\w.])([A-Za-z_][A-Za-z0-9_]*)\s*\(')) {
        $name = $m.Groups[1].Value
        if ($declared.ContainsKey($name) -or $known.ContainsKey($name)) { continue }
        $out += $name
    }
    return @($out | Sort-Object -Unique)
}
