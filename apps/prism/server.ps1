# Prism server -- Codex compiler explorer with live compilation and plug outputs.
# Usage: pwsh apps/prism/server.ps1 [-Port 8080]
[CmdletBinding()]
param(
    [int]$Port = 8080
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$CacheDir = Join-Path $PSScriptRoot 'build-output' 'cache'
$compileScript = Join-Path $Repo 'build' 'compile.ps1'

if (-not (Test-Path $CacheDir)) { New-Item -ItemType Directory -Force $CacheDir | Out-Null }

# ── Source file catalog ──────────────────────────────────────
$SourcePaths = @(
    'codex/compiler/opening.codex'
    'codex/compiler/Syntax/Lexer.codex'
    'codex/compiler/Syntax/Parser.codex'
    'codex/compiler/Syntax/ParserCore.codex'
    'codex/compiler/Types/TypeChecker.codex'
    'codex/compiler/IR/Lowering.codex'
    'codex/compiler/IR/IRChapter.codex'
    'codex/compiler/IR/ResolveTypes.codex'
    'codex/compiler/IR/LambdaLifting.codex'
    'codex/compiler/Emit/X86_64.codex'
    'codex/compiler/Emit/IRTextEmitter.codex'
    'codex/compiler/Emit/CodexEmitter.codex'
    'codex/compiler/Core/CdxCodes.codex'
    'codex/plugs/arm64/Arm64CodeGen.codex'
    'codex/plugs/riscv/RiscVCodeGen.codex'
    'codex/plugs/wasm/WasmEmitter.codex'
    'codex/plugs/python/PythonEmitter.codex'
    'codex/plugs/rust/RustEmitter.codex'
    'codex/plugs/javascript/JavaScriptEmitter.codex'
    'codex/foreword/core/ListUtils.codex'
    'codex/foreword/core/StringUtils.codex'
    'codex/os/net/WebServer.codex'
    'codex/os/net/NetworkStack.codex'
    'codex/test/punctual-smoke.codex'
    'codex/test/examples/missile-warning.codex'
    'apps/prism/Prism.codex'
)

$PlugTargets = @(
    @{name='python';     dir='codex/plugs/python'}
    @{name='javascript'; dir='codex/plugs/javascript'}
    @{name='rust';       dir='codex/plugs/rust'}
    @{name='haskell';    dir='codex/plugs/haskell'}
    @{name='go';         dir='codex/plugs/go'}
    @{name='c#';         dir='codex/plugs/csharp'}
)

# ── Per-request working directory ────────────────────────────
# Every compile gets its own directory and nothing is kept between requests.
# There is no IR cache and no pre-bake: Damian's ruling 2026-08-24, "we
# definitely need compile/transpile on the fly for the prism. the canned IR is
# not the correct design." A cache keyed on a repo PATH could not have served
# submitted source anyway, because submitted source has no path.
function New-RequestDir {
    $d = Join-Path $CacheDir ("req-" + [System.Guid]::NewGuid().ToString('N').Substring(0, 12))
    New-Item -ItemType Directory -Force $d | Out-Null
    return $d
}

function Remove-RequestDir([string]$Dir) {
    if ($Dir -and (Test-Path $Dir)) {
        try { Remove-Item -Recurse -Force $Dir -ErrorAction Stop } catch { }
    }
}

# Reads one member out of a JSON body without throwing when it is absent.
# `$bodyObj.source` under `Set-StrictMode -Version Latest` THROWS on a missing
# property, and the throw happened before the `missing source` guard could run,
# so a request that parsed but carried the wrong member answered 500 with no
# diagnostic where it meant 400. That was PRISM-4.
function Read-BodyMember([string]$Body, [string]$Name) {
    if (-not $Body) { return '' }
    $obj = $null
    try { $obj = $Body | ConvertFrom-Json -ErrorAction Stop } catch { return '' }
    if ($null -eq $obj) { return '' }
    if (-not ($obj.PSObject.Properties.Name -contains $Name)) { return '' }
    $v = $obj.$Name
    if ($null -eq $v) { return '' }
    return [string]$v
}

# A compile is bounded. The compiler is a VM boot and a runaway one would hold
# the single-threaded listener open forever, so the wall budget is the same 60 s
# the test harness gives a kernel.
$CompileWallMs = 60000

function Invoke-Bounded([string]$Script, [string[]]$ScriptArgs, [int]$WallMs) {
    $p = Start-Process -FilePath 'pwsh' `
        -ArgumentList (@('-NoProfile', '-File', $Script) + $ScriptArgs) `
        -PassThru -NoNewWindow -RedirectStandardOutput ([System.IO.Path]::GetTempFileName()) `
        -RedirectStandardError ([System.IO.Path]::GetTempFileName())
    if (-not $p.WaitForExit($WallMs)) {
        try { $p.Kill($true) } catch { }
        return $false
    }
    return $true
}

# ── On-the-fly IR compilation ────────────────────────────────
# Takes SOURCE TEXT, never a path. The visitor's text is the input, which is
# what makes this a REPL rather than a viewer over a catalogue.
function Compile-SourceToIr([string]$Source, [string]$WorkDir) {
    $srcFile = Join-Path $WorkDir 'prog.codex'
    # UTF8 without BOM: the compiler reads its source as UTF-8 and a BOM lands
    # in the first token.
    [System.IO.File]::WriteAllText($srcFile, $Source, [System.Text.UTF8Encoding]::new($false))

    $irOut = Join-Path $WorkDir 'prog.cdx'
    $irLog = Join-Path $WorkDir 'prog.log'
    # `| Out-Null` and not just `2>$null`, matching Invoke-PlugOnSource below. The IR is
    # read from the LOG, never from stdout, and an uncaptured child's stdout
    # joins THIS function's output stream: compile.ps1 prints a `kernel:` line
    # and a bare `True`, so the caller received an ARRAY whose first element was
    # that noise, and `.StartsWith(...)` then member-enumerated to an array of
    # booleans, which is truthy.
    $finished = Invoke-Bounded $compileScript @('-Src', $srcFile, '-Out', $irOut, '-Log', $irLog, '-IrUni') $CompileWallMs
    if (-not $finished) { return "COMPILE-ERROR:`ncompile exceeded the $([int]($CompileWallMs / 1000)) s budget" }

    if (-not (Test-Path $irLog)) { return "COMPILE-ERROR:`nthe compiler produced no log" }

    $lines = Get-Content $irLog
    $capturing = $false; $irLines = @()
    foreach ($l in $lines) {
        if ($l -eq 'IR-BEGIN') { $capturing = $true; continue }
        if ($l -eq 'IR-END')   { $capturing = $false; continue }
        if ($capturing) { $irLines += $l }
    }
    if ($irLines.Count -gt 0) {
        Write-Host "[prism]   -> $($irLines.Count) IR lines" -ForegroundColor Green
        return ($irLines -join "`n")
    }

    # No IR means the frontend refused. Report the diagnostics the compiler
    # actually emitted; falling through to a bare null would render as "no
    # output" and read as a server fault rather than as a rejected program.
    $errors = ($lines | Where-Object { $_ -match '(?i)error CDX|^CODEGEN-HALTED' }) -join "`n"
    if ($errors) {
        # The compiler names the file it was handed, which is this request's own
        # scratch directory. Sending that to the browser publishes a server
        # absolute path and tells the reader nothing: the file is THEIR program.
        $errors = $errors.Replace($srcFile, 'your program').Replace(($srcFile -replace '\\', '/'), 'your program')
        return "COMPILE-ERROR:`n$errors"
    }
    return "COMPILE-ERROR:`nno IR emitted and no diagnostic captured"
}

# ── Plug invocation ──────────────────────────────────────────
# Takes SOURCE TEXT and the request's own directory, like the compile above.
# No cache: a plug output keyed on a repo path cannot answer for submitted text.
function Invoke-PlugOnSource([string]$PlugName, [string]$Source, [string]$WorkDir) {
    $plug = $PlugTargets | Where-Object { $_.name -eq $PlugName }
    if (-not $plug) { return $null }

    $plugDir = Join-Path $Repo ($plug.dir -replace '/', '\')
    $runScript = Join-Path $plugDir 'run.ps1'
    if (-not (Test-Path $runScript)) { return "Plug run.ps1 not found at $plugDir" }

    $plugCdx = Join-Path $plugDir 'build-output' "$PlugName-plug.cdx"
    if (-not (Test-Path $plugCdx)) { return "Plug CDX not built. Run: $plugDir\build.ps1" }

    # The plug takes a source FILE, so the submitted text is written into this
    # request's directory and never anywhere shared. Written per plug rather
    # than reused from the compile step so a plug can never read a neighbour's.
    $srcFile = Join-Path $WorkDir "plug-$PlugName-in.codex"
    [System.IO.File]::WriteAllText($srcFile, $Source, [System.Text.UTF8Encoding]::new($false))
    $outFile = Join-Path $WorkDir "plug-$PlugName-out.txt"

    Write-Host "[prism] Running $PlugName plug..." -ForegroundColor Yellow
    try {
        $finished = Invoke-Bounded $runScript @('-Src', $srcFile, '-Out', $outFile) $CompileWallMs
        if (-not $finished) { return "Plug $PlugName exceeded the $([int]($CompileWallMs / 1000)) s budget." }
        if ((Test-Path $outFile) -and (Get-Item $outFile).Length -gt 0) {
            $output = [System.IO.File]::ReadAllText($outFile)
            Write-Host "[prism]   -> $PlugName OK ($($output.Length) chars)" -ForegroundColor Green
            return $output
        }
        return "Plug produced no output. The plug CDX may need rebuilding."
    } catch {
        Write-Host "[prism]   -> $PlugName failed: $_" -ForegroundColor Red
        return "Plug failed: $_"
    }
}

# ── JSON helpers ─────────────────────────────────────────────
function ConvertTo-JsonString([string]$s) {
    $s = $s.Replace('\', '\\').Replace('"', '\"').Replace("`n", '\n').Replace("`r", '\r').Replace("`t", '\t')
    return "`"$s`""
}

function Send-Json($Response, [string]$Json, [int]$Status = 200) {
    $buf = [System.Text.Encoding]::UTF8.GetBytes($Json)
    $Response.StatusCode = $Status
    $Response.ContentType = 'application/json; charset=utf-8'
    $Response.Headers.Add('Access-Control-Allow-Origin', '*')
    $Response.ContentLength64 = $buf.Length
    $Response.OutputStream.Write($buf, 0, $buf.Length)
}

function Send-Html($Response, [string]$Html) {
    $buf = [System.Text.Encoding]::UTF8.GetBytes($Html)
    $Response.StatusCode = 200
    $Response.ContentType = 'text/html; charset=utf-8'
    $Response.ContentLength64 = $buf.Length
    $Response.OutputStream.Write($buf, 0, $buf.Length)
}

# ── HTML page ────────────────────────────────────────────────
$IndexHtml = @'
<!DOCTYPE html><html><head><meta charset="utf-8"><title>Prism</title>
<style>
* { margin:0; padding:0; box-sizing:border-box; }
body { font-family:'Cascadia Code','Fira Code',monospace; background:#1e1e2e; color:#cdd6f4; }
header { padding:8px 20px; background:#181825; border-bottom:1px solid #313244; display:flex; align-items:center; gap:12px; }
header h1 { font-size:18px; color:#cba6f7; letter-spacing:2px; }
.sub { font-size:12px; color:#6c7086; font-style:italic; flex:1; }
#compile-btn { padding:5px 20px; font-size:13px; font-family:inherit; background:#45475a; color:#cba6f7; border:1px solid #585b70; border-radius:4px; cursor:pointer; font-weight:bold; letter-spacing:1px; }
#compile-btn:hover:not(:disabled) { background:#585b70; color:#f5c2e7; }
#compile-btn:disabled { background:#313244; color:#585b70; border-color:#45475a; cursor:default; }
#edit-btn { padding:5px 14px; font-size:13px; font-family:inherit; background:#313244; color:#cdd6f4; border:1px solid #45475a; border-radius:4px; cursor:pointer; }
#edit-btn:hover { background:#45475a; color:#cba6f7; }
#editor { width:100%; height:100%; min-height:340px; background:#1e1e2e; color:#cdd6f4; border:none; outline:none; resize:none; font-family:inherit; font-size:12px; line-height:1.6; padding:0; tab-size:2; }
#status { font-size:11px; color:#6c7086; margin-left:8px; }
.workspace { display:flex; height:calc(100vh - 40px); }
nav { width:220px; padding:8px; overflow-y:auto; border-right:1px solid #313244; background:#11111b; flex-shrink:0; }
nav h2 { font-size:11px; color:#a6adc8; margin-bottom:4px; text-transform:uppercase; letter-spacing:1px; }
nav ul { list-style:none; }
nav li { padding:2px 6px; cursor:pointer; font-size:11px; border-radius:3px; color:#bac2de; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
nav li:hover { background:#313244; }
nav li.active { background:#45475a; color:#cba6f7; }
nav li .dir { color:#6c7086; font-size:10px; }
.panels { flex:1; display:flex; overflow:hidden; }
.panel { flex:1; display:flex; flex-direction:column; border-right:1px solid #313244; min-width:0; }
.panel:last-child { border-right:none; }
.panel-header { padding:4px 12px; background:#181825; border-bottom:1px solid #313244; font-size:11px; color:#a6adc8; display:flex; align-items:center; gap:8px; flex-shrink:0; }
.panel-header .label { font-weight:bold; color:#cba6f7; }
.panel-header .info { color:#6c7086; }
.panel-body { flex:1; overflow:auto; }
.panel-body pre { padding:8px 12px; font-size:11px; line-height:1.5; white-space:pre; tab-size:2; color:#cdd6f4; margin:0; }
.plug-tabs { display:flex; gap:1px; background:#11111b; flex-shrink:0; }
.plug-tab { padding:3px 10px; font-size:10px; cursor:pointer; color:#6c7086; background:#181825; border:none; font-family:inherit; }
.plug-tab.active { color:#cba6f7; background:#1e1e2e; }
.plug-tab.loading { color:#f9e2af; }
.line-num { color:#45475a; display:inline-block; width:4em; text-align:right; padding-right:1em; user-select:none; }
.kw { color:#cba6f7; } .str { color:#a6e3a1; } .num { color:#fab387; } .ty { color:#f9e2af; } .hd { color:#89b4fa; font-weight:bold; } .op { color:#89dceb; } .bool { color:#fab387; font-style:italic; } .eff { color:#f38ba8; } .ann { color:#94e2d5; }
.empty { color:#585b70; padding:20px; font-size:12px; }
</style></head><body>
<header>
  <h1>Prism</h1><span class="sub">Codex through every lens</span>
  <button id="edit-btn" onclick="toggleEdit()">Edit</button>
  <button id="compile-btn" onclick="compileAll()">Compile</button>
  <span id="status"></span>
</header>
<div class="workspace">
  <nav><h2>Source Files</h2><ul id="file-list"></ul></nav>
  <div class="panels">
    <div class="panel" id="source-panel">
      <div class="panel-header"><span class="label">Codex</span><span class="info" id="source-info"></span></div>
      <div class="panel-body"><pre id="code"><span class="empty">Select a file from the sidebar.</span></pre></div>
    </div>
    <div class="panel" id="output-panel">
      <div class="plug-tabs" id="plug-tabs">
        <button class="plug-tab active" onclick="selectTab('ir',this)">IR</button>
        <button class="plug-tab" onclick="selectTab('python',this)">Python</button>
        <button class="plug-tab" onclick="selectTab('javascript',this)">JavaScript</button>
        <button class="plug-tab" onclick="selectTab('rust',this)">Rust</button>
        <button class="plug-tab" onclick="selectTab('haskell',this)">Haskell</button>
        <button class="plug-tab" onclick="selectTab('go',this)">Go</button>
        <button class="plug-tab" onclick="selectTab('c#',this)">C#</button>
      </div>
      <div class="panel-header"><span class="label" id="plug-label">IR</span><span class="info" id="plug-info"></span></div>
      <div class="panel-body"><pre id="plug-code"><span class="empty">Select a file and press Compile.</span></pre></div>
    </div>
  </div>
</div>
<script>
let currentPath=null,currentTab='ir',outputs={},buffer='',editing=false;
async function loadFiles(){
  const r=await fetch('/api/files');const d=await r.json();
  const ul=document.getElementById('file-list');
  d.files.forEach(f=>{
    const li=document.createElement('li');
    const parts=f.path.split('/');const name=parts.pop();const dir=parts.join('/')+'/';
    li.innerHTML='<span class="dir">'+dir+'</span>'+name;
    li.title=f.path;li.onclick=()=>loadSource(f.path,li);ul.appendChild(li);
  });
}
async function loadSource(path,el){
  document.querySelectorAll('#file-list li').forEach(x=>x.classList.remove('active'));
  el.classList.add('active');currentPath=path;
  document.getElementById('compile-btn').disabled=false;
  document.getElementById('compile-btn').textContent='Compile';
  document.getElementById('source-info').textContent='Loading...';
  const r=await fetch('/api/source?path='+encodeURIComponent(path));
  const d=await r.json();
  if(d.error){document.getElementById('code').innerHTML='<span class="empty">'+d.error+'</span>';return;}
  buffer=d.source;renderSource();
  document.getElementById('source-info').textContent=path+' ('+d.lines+' lines)';
}
// The BUFFER is what compiles, never a path. Loading a file seeds it; editing
// replaces it; either way the bytes sent are the bytes on screen.
function renderSource(){
  var el=document.getElementById('code');
  if(editing){
    el.innerHTML='';
    var ta=document.createElement('textarea');
    ta.id='editor';ta.spellcheck=false;ta.value=buffer;
    ta.addEventListener('input',function(){buffer=ta.value;});
    el.appendChild(ta);ta.focus();
  }else{
    var lines=buffer.split('\n');
    el.innerHTML=lines.map((l,i)=>'<span class="line-num">'+(i+1)+'</span>'+hl(l)).join('\n');
  }
}
function toggleEdit(){
  if(editing){buffer=document.getElementById('editor').value;}
  editing=!editing;
  document.getElementById('edit-btn').textContent=editing?'Done':'Edit';
  document.getElementById('compile-btn').disabled=false;
  renderSource();
}
async function compileAll(){
  if(editing){buffer=document.getElementById('editor').value;}
  if(!buffer){document.getElementById('status').textContent='Nothing to compile. Pick a file or press Edit.';return;}
  var btn=document.getElementById('compile-btn');
  btn.disabled=true;btn.textContent='Compiling...';
  document.getElementById('status').textContent='Compiling IR...';
  outputs={};showOutput();
  try{
    const r=await fetch('/api/compile',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({source:buffer})});
    const d=await r.json();
    if(d.error){outputs.ir='Error: '+d.error;showOutput();btn.textContent='Compile';btn.disabled=false;return;}
    if(d.status==='error'){
      // A refused program is not a server fault and must not read as one.
      outputs.ir=d.diagnostics;showOutput();
      document.getElementById('status').textContent='The compiler refused this program.';
      btn.textContent='Compile';btn.disabled=false;return;
    }
    outputs.ir=d.ir||'No output';
    document.getElementById('status').textContent='IR ready ('+d.irLines+' lines). Fetching plugs...';
    showOutput();
    var plugs=['python','javascript','rust','haskell','go','c#'];
    for(var p of plugs){
      setTabLoading(p,true);
      try{
        const pr=await fetch('/api/plug',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({source:buffer,plug:p})});
        const pd=await pr.json();
        outputs[p]=pd.output||pd.error||pd.note||'No output';
      }catch(e){outputs[p]='Request failed: '+e;}
      setTabLoading(p,false);
      if(currentTab===p)showOutput();
    }
    document.getElementById('status').textContent='Done.';
  }catch(e){document.getElementById('status').textContent='Error: '+e;}
  btn.textContent='Compile';btn.disabled=false;
}
function selectTab(name,el){
  document.querySelectorAll('.plug-tab').forEach(b=>b.classList.remove('active'));
  el.classList.add('active');currentTab=name;
  document.getElementById('plug-label').textContent=el.textContent;
  showOutput();
}
function setTabLoading(name,loading){
  var tabs=document.querySelectorAll('.plug-tab');
  tabs.forEach(t=>{if(t.textContent.toLowerCase()===name||t.textContent===name){
    if(loading)t.classList.add('loading');else t.classList.remove('loading');}});
}
function showOutput(){
  var text=outputs[currentTab]||'';
  var el=document.getElementById('plug-code');
  if(!text){el.innerHTML='<span class="empty">No output yet. Press Compile.</span>';return;}
  el.textContent=text;
  document.getElementById('plug-info').textContent=text.split('\n').length+' lines';
}
function hl(l){
  l=l.replace(/&/g,'&amp;').replace(/</g,'&lt;');
  if(/^(Chapter|Section|Foreword):/.test(l)){
    var c=l.indexOf(':');
    return'<span class="kw">'+l.slice(0,c)+'</span>:<span class="hd">'+l.slice(c+1)+'</span>';}
  if(/^\s*We say:\s*$/.test(l))return'<span class="kw">'+l+'</span>';
  if(/^Page\s+\d+/.test(l))return'<span class="kw">'+l+'</span>';
  var KW=/^(let|in|between|if|then|else|when|where|is|otherwise|act|end|record|cites|effect|with|with-timeout|linear|mutable|punctual|unit|class|instance|lazy|trying|revised|for|claim|proof|qed|forall|exists|induction)$/;
  var BOOL=/^(True|False|None|Nothing)$/;
  return l.replace(/(#[0-9a-fA-F][0-9a-fA-F_]*|"[^"]*"|@[a-z][a-z0-9-]*|\[[A-Z][A-Za-z0-9]*\]|\b[A-Z][A-Za-z0-9]*\b|\b\d[\d_]*(?:\.\d[\d_]*)?\b|\b[a-z][a-z0-9]*(?:-[a-z][a-z0-9]*)*\b|->|<-|=>|==|\|>|\|)/g,function(m){
    if(m[0]==='"')return'<span class="str">'+m+'</span>';
    if(m[0]==='#')return'<span class="num">'+m+'</span>';
    if(m[0]==='@')return'<span class="ann">'+m+'</span>';
    if(m[0]==='['&&/^\[[A-Z]/.test(m))return'<span class="eff">'+m+'</span>';
    if(BOOL.test(m))return'<span class="bool">'+m+'</span>';
    if(KW.test(m))return'<span class="kw">'+m+'</span>';
    if(/^\d/.test(m))return'<span class="num">'+m+'</span>';
    if(/^[A-Z]/.test(m))return'<span class="ty">'+m+'</span>';
    if(/^(->|<-|=>|==|\|>|\|)$/.test(m))return'<span class="op">'+m+'</span>';
    return m;});
}
loadFiles();
</script></body></html>
'@

# ── HTTP Server ──────────────────────────────────────────────
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()

Write-Host ""
Write-Host "  Prism running at http://localhost:$Port/" -ForegroundColor Magenta
Write-Host "  $($SourcePaths.Count) source files, $($PlugTargets.Count) plug targets" -ForegroundColor Gray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $resp = $ctx.Response

        try {
            $path = $ctx.Request.Url.AbsolutePath
            $query = $ctx.Request.QueryString

            if ($path -eq '/' -or $path -eq '/index.html') {
                Send-Html -Response $resp -Html $IndexHtml
            }
            elseif ($path -eq '/api/health') {
                Send-Json -Response $resp -Json '{"status":"ok"}'
            }
            elseif ($path -eq '/api/files') {
                $entries = ($SourcePaths | ForEach-Object {
                    "{`"path`":$(ConvertTo-JsonString $_)}"
                }) -join ','
                Send-Json -Response $resp -Json "{`"files`":[$entries]}"
            }
            elseif ($path -eq '/api/source') {
                $filePath = $query['path']
                if (-not $filePath -or $filePath -notin $SourcePaths) {
                    Send-Json -Response $resp -Json '{"error":"file not in catalog"}' -Status 404
                } else {
                    $fullPath = Join-Path $Repo ($filePath -replace '/', '\')
                    if (Test-Path -PathType Leaf $fullPath) {
                        $content = [System.IO.File]::ReadAllText($fullPath)
                        $lineCount = ($content -split "`n").Count
                        Send-Json -Response $resp -Json "{`"path`":$(ConvertTo-JsonString $filePath),`"source`":$(ConvertTo-JsonString $content),`"lines`":$lineCount}"
                    } else {
                        Send-Json -Response $resp -Json '{"error":"file not found on disk"}' -Status 404
                    }
                }
            }
            elseif ($path -eq '/api/compile') {
                $reader = [System.IO.StreamReader]::new($ctx.Request.InputStream)
                $body = $reader.ReadToEnd(); $reader.Close()
                $source = Read-BodyMember $body 'source'

                if (-not $source) {
                    Send-Json -Response $resp -Json '{"error":"missing source"}' -Status 400
                } else {
                    $work = New-RequestDir
                    try {
                        $ir = Compile-SourceToIr $source $work
                        if ($ir.StartsWith('COMPILE-ERROR:')) {
                            $errMsg = ($ir -replace '^COMPILE-ERROR:', '').Trim()
                            if (-not $errMsg) { $errMsg = '(no diagnostic details captured)' }
                            # `status` carries the verdict. The old shape put the
                            # diagnostics in `ir` behind a "-- Compile errors --"
                            # banner, so a caller had to parse English out of the
                            # field it wanted IR in to tell a refusal from a result.
                            Send-Json -Response $resp -Json "{`"status`":`"error`",`"diagnostics`":$(ConvertTo-JsonString $errMsg),`"ir`":`"`",`"irLines`":0}"
                        } else {
                            $irLines = ($ir -split "`n").Count
                            Send-Json -Response $resp -Json "{`"status`":`"ok`",`"diagnostics`":`"`",`"ir`":$(ConvertTo-JsonString $ir),`"irLines`":$irLines}"
                        }
                    } catch {
                        Send-Json -Response $resp -Json "{`"error`":$(ConvertTo-JsonString "Compile failed: $_")}" -Status 500
                    } finally {
                        Remove-RequestDir $work
                    }
                }
            }
            elseif ($path -eq '/api/plug') {
                $reader = [System.IO.StreamReader]::new($ctx.Request.InputStream)
                $body = $reader.ReadToEnd(); $reader.Close()
                $source = Read-BodyMember $body 'source'
                $plugName = Read-BodyMember $body 'plug'

                if (-not $source -or -not $plugName) {
                    Send-Json -Response $resp -Json '{"error":"missing source or plug"}' -Status 400
                } else {
                    $work = New-RequestDir
                    try {
                        $output = Invoke-PlugOnSource $plugName $source $work
                        if ($output) {
                            Send-Json -Response $resp -Json "{`"plug`":$(ConvertTo-JsonString $plugName),`"output`":$(ConvertTo-JsonString $output)}"
                        } else {
                            Send-Json -Response $resp -Json "{`"note`":`"Plug $plugName not available`"}"
                        }
                    } catch {
                        Send-Json -Response $resp -Json "{`"error`":$(ConvertTo-JsonString "Plug failed: $_")}" -Status 500
                    } finally {
                        Remove-RequestDir $work
                    }
                }
            }
            else {
                $resp.StatusCode = 404
                $buf = [System.Text.Encoding]::UTF8.GetBytes('Not found')
                $resp.ContentType = 'text/plain'
                $resp.ContentLength64 = $buf.Length
                $resp.OutputStream.Write($buf, 0, $buf.Length)
            }
        } catch {
            Write-Host "  Error: $_" -ForegroundColor Red
            try { $resp.StatusCode = 500 } catch {}
        } finally {
            try { $resp.Close() } catch {}
        }
    }
} finally {
    $listener.Stop(); $listener.Close()
    Write-Host "Server stopped." -ForegroundColor Yellow
}
