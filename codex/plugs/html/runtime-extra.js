// runtime-extra.js — additional JS runtime for the HTML plug.
// Inlined by build.ps1 into the plug source as a text literal.
function save_theme(n,th){try{localStorage.setItem('codex-theme-'+n,JSON.stringify(th))}catch(e){}return 0}
function load_theme(n){try{var s=localStorage.getItem('codex-theme-'+n);return s?JSON.parse(s):null}catch(e){return null}}
function save_layout(n,w){try{localStorage.setItem('codex-layout-'+n,JSON.stringify(w))}catch(e){}return 0}
function load_layout(n){try{var s=localStorage.getItem('codex-layout-'+n);return s?JSON.parse(s):null}catch(e){return null}}
