// magic.js -- CodexMagic shared client logic

const API = '/api/magic';
const AUTH_API = '/api/auth';
var currentAccount = null;
var authToken = null;

async function apiCall(path) {
  try {
    var url = API + path;
    if (authToken) url += (url.includes('?') ? '&' : '?') + 't=' + encodeURIComponent(authToken);
    const r = await fetch(url);
    if (!r.ok) return null;
    return r.json();
  } catch (e) {
    console.error('API error:', path, e);
    return null;
  }
}

async function authCall(path) {
  try {
    const r = await fetch(AUTH_API + path);
    const j = await r.json();
    if (!r.ok) return j;
    return j;
  } catch (e) {
    console.error('Auth error:', path, e);
    return null;
  }
}

function getStoredToken() { return localStorage.getItem('cx_tok'); }
function setStoredToken(t) { localStorage.setItem('cx_tok', t); authToken = t; }
function clearStoredToken() { localStorage.removeItem('cx_tok'); authToken = null; }

async function ensureAccount() {
  var tok = getStoredToken();
  if (tok) {
    authToken = tok;
    var me = await authCall('/me?t=' + encodeURIComponent(tok));
    if (me && me.handle) {
      currentAccount = { name: me.adorned || me.display || me.handle, handle: me.handle, 'account-id': me.id || 0, admin: !!me.admin, rating: 1000, rank: 'Bronze', balance: 0 };
      updateAccountBar();
      await refreshProfile();
      return currentAccount;
    }
    clearStoredToken();
  }
  showLoginOverlay();
  return null;
}

var _handleTimer = null;
var _passTimer = null;
var _pwScoreColors = ['#f85149','#f0883e','#d29922','#3fb950','#58a6ff'];

function _fieldStyle() { return 'width:100%;padding:8px;margin:4px 0;background:#0d1117;border:1px solid #30363d;color:#e6edf3;border-radius:4px;box-sizing:border-box'; }
function _indicator() { return 'font-size:12px;margin:2px 0 6px;min-height:16px'; }

function showLoginOverlay() {
  if (document.getElementById('login-overlay')) return;
  var ov = document.createElement('div');
  ov.id = 'login-overlay';
  ov.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.85);z-index:9999;display:flex;align-items:center;justify-content:center';
  ov.innerHTML = '<div style="background:#161b22;border:1px solid #30363d;border-radius:8px;padding:32px;width:320px;font-family:monospace;color:#e6edf3">'
    + '<h2 style="margin:0 0 16px;color:#d4a017">CodexMagic</h2>'
    + '<form id="login-mode" onsubmit="doLogin();return false">'
    + '<input id="login-handle" placeholder="Handle" autocomplete="username" style="' + _fieldStyle() + '">'
    + '<input id="login-pass" type="password" placeholder="Password" autocomplete="current-password" style="' + _fieldStyle() + '">'
    + '<button type="submit" style="width:100%;padding:10px;margin:8px 0 4px;background:#d4a017;color:#0d1117;border:none;border-radius:4px;cursor:pointer;font-weight:bold">Sign In</button>'
    + '<button type="button" onclick="showRegister()" style="width:100%;padding:8px;margin:4px 0;background:transparent;color:#58a6ff;border:1px solid #30363d;border-radius:4px;cursor:pointer">Create Account</button>'
    + '<div id="login-msg" style="color:#f85149;margin-top:8px"></div>'
    + '</form>'
    + '<form id="register-mode" style="display:none" onsubmit="doRegister();return false">'
    + '<input id="reg-handle" placeholder="Handle (letters, numbers, hyphens)" autocomplete="username" style="' + _fieldStyle() + '">'
    + '<div id="handle-status" style="' + _indicator() + '"></div>'
    + '<input id="reg-display" placeholder="Display Name (optional)" autocomplete="name" style="' + _fieldStyle() + '">'
    + '<input id="reg-pass" type="password" placeholder="Password (5+ letters/numbers)" autocomplete="new-password" style="' + _fieldStyle() + '">'
    + '<div id="pass-status" style="' + _indicator() + '"></div>'
    + '<button type="submit" id="reg-btn" style="width:100%;padding:10px;margin:8px 0 4px;background:#3fb950;color:#0d1117;border:none;border-radius:4px;cursor:pointer;font-weight:bold">Create Account</button>'
    + '<button type="button" onclick="showLogin()" style="width:100%;padding:8px;margin:4px 0;background:transparent;color:#58a6ff;border:1px solid #30363d;border-radius:4px;cursor:pointer">Back to Login</button>'
    + '<div id="reg-msg" style="color:#f85149;margin-top:8px"></div>'
    + '</form></div>';
  document.body.appendChild(ov);
  var rh = document.getElementById('reg-handle');
  rh.addEventListener('input', function() { debounceHandleCheck(); });
  rh.addEventListener('blur', function() { checkHandle(); });
  var rp = document.getElementById('reg-pass');
  rp.addEventListener('input', function() { debouncePassCheck(); });
  rp.addEventListener('blur', function() { checkPassword(); });
}

function debounceHandleCheck() {
  clearTimeout(_handleTimer);
  _handleTimer = setTimeout(checkHandle, 400);
}
function debouncePassCheck() {
  clearTimeout(_passTimer);
  _passTimer = setTimeout(checkPassword, 400);
}

async function checkHandle() {
  var el = document.getElementById('reg-handle');
  var st = document.getElementById('handle-status');
  if (!el || !st) return;
  var v = el.value.trim();
  if (v.length < 2) { st.innerHTML = ''; return; }
  var r = await authCall('/check-handle?u=' + encodeURIComponent(v));
  if (!r) { st.innerHTML = ''; return; }
  if (r.available) {
    st.innerHTML = '<span style="color:#3fb950">&#x2713; available</span>';
    el.style.borderColor = '#3fb950';
  } else {
    st.innerHTML = '<span style="color:#f85149">&#x2717; ' + (r.reason || 'taken') + '</span>';
    el.style.borderColor = '#f85149';
  }
}

async function checkPassword() {
  var el = document.getElementById('reg-pass');
  var st = document.getElementById('pass-status');
  if (!el || !st) return;
  var v = el.value;
  if (v.length < 1) { st.innerHTML = ''; el.style.borderColor = '#30363d'; return; }
  var r = await authCall('/check-password?p=' + encodeURIComponent(v));
  if (!r) { st.innerHTML = ''; return; }
  var color = _pwScoreColors[r.score] || '#8b949e';
  var bar = '';
  for (var i = 0; i < 4; i++) bar += '<span style="display:inline-block;width:20%;height:4px;margin:0 1px;background:' + (i < r.score ? color : '#30363d') + ';border-radius:2px"></span>';
  st.innerHTML = bar + '<br><span style="color:' + color + '">' + r.message + '</span>';
  el.style.borderColor = r.score === 0 ? '#f85149' : color;
}

function showRegister() {
  document.getElementById('login-mode').style.display = 'none';
  document.getElementById('register-mode').style.display = 'block';
}
function showLogin() {
  document.getElementById('register-mode').style.display = 'none';
  document.getElementById('login-mode').style.display = 'block';
}

async function doLogin() {
  var h = document.getElementById('login-handle').value.trim();
  var p = document.getElementById('login-pass').value;
  if (!h || !p) { document.getElementById('login-msg').textContent = 'Enter handle and password'; return; }
  document.getElementById('login-msg').textContent = '';
  var r = await authCall('/login?u=' + encodeURIComponent(h) + '&p=' + encodeURIComponent(p));
  if (r && r.token) {
    setStoredToken(r.token);
    currentAccount = { name: r.display || h, handle: r.handle || h, 'account-id': r.id || 0, 'pw-score': r['pw-score'], tfa: r.tfa, rating: 1000, rank: 'Bronze', balance: 0 };
    document.getElementById('login-overlay').remove();
    updateAccountBar();
    await refreshBalance();
    if (typeof onAccountReady === 'function') onAccountReady();
  } else {
    document.getElementById('login-msg').textContent = (r && r.error) || 'Login failed';
  }
}

async function doRegister() {
  var h = document.getElementById('reg-handle').value.trim();
  var d = document.getElementById('reg-display').value.trim() || h;
  var p = document.getElementById('reg-pass').value;
  var msg = document.getElementById('reg-msg');
  msg.textContent = '';
  if (!h) { msg.textContent = 'Handle required'; return; }
  if (!p || p.length < 5) { msg.textContent = 'Password must be at least 5 characters'; return; }
  if (!/^[a-zA-Z0-9]+$/.test(p)) { msg.textContent = 'Password must be letters and numbers only'; return; }
  var r = await authCall('/register?u=' + encodeURIComponent(h) + '&d=' + encodeURIComponent(d) + '&p=' + encodeURIComponent(p));
  if (r && r.token) {
    setStoredToken(r.token);
    currentAccount = { name: r.display || d, handle: r.handle || h, 'account-id': r.id || 0, 'pw-score': r['pw-score'], tfa: r.tfa, rating: 1000, rank: 'Bronze', balance: r.starterBalance || 0 };
    document.getElementById('login-overlay').remove();
    updateAccountBar();
    if (r.starterGrant) {
      setTimeout(function() { alert('Welcome to CodexMagic!\n\nYou received:\n  500 Mana Coin\n  ' + (r.starterCards || 15) + ' starter cards\n\nVisit the Store to buy more packs, or check your Collection!'); }, 200);
    }
    await refreshBalance();
    if (typeof onAccountReady === 'function') onAccountReady();
  } else {
    msg.textContent = (r && r.error) || 'Registration failed';
  }
}

async function authenticate() {
  return authToken !== null;
}

var _headerPages = [
  { href: '/game.html', label: 'Game' },
  { href: '/collection.html', label: 'Collection' },
  { href: '/store.html', label: 'Store' },
  { href: '/marketplace.html', label: 'Market' },
  { href: '/clan.html', label: 'Clan' },
  { href: '/queue.html', label: 'Queue' },
  { href: '/decktest.html', label: 'Test' }
];

function buildHeader() {
  var el = document.getElementById('cx-header');
  if (!el) {
    el = document.createElement('div');
    el.id = 'cx-header';
    document.body.insertBefore(el, document.body.firstChild);
  }
  el.className = 'topbar';
  var active = location.pathname;
  var pages = _headerPages.slice();
  if (currentAccount && currentAccount.admin) {
    pages.push({ href: '/admin.html', label: 'Admin' });
  }
  var nav = pages.map(function(p) {
    var cls = (active === p.href) ? ' class="active"' : '';
    return '<a href="' + p.href + '"' + cls + '>' + p.label + '</a>';
  }).join('');
  var right = '';
  if (currentAccount) {
    var name = currentAccount.name || 'Unknown';
    var rank = currentAccount.rank || 'Bronze';
    var rating = currentAccount.rating || 1000;
    var bal = currentAccount.balance !== undefined ? currentAccount.balance.toLocaleString() : '?';
    var initial = name.charAt(0).toUpperCase();
    var isProfile = (active === '/profile.html');
    var rankColors = { Bronze: '#cd7f32', Silver: '#c0c0c0', Gold: '#d29922', Platinum: '#58a6ff', Diamond: '#b9f2ff' };
    var rankColor = rankColors[rank] || '#8b949e';
    right = '<a href="/profile.html" class="acct-profile' + (isProfile ? ' active' : '') + '">'
      + '<span class="acct-avatar">' + initial + '</span>'
      + '<span class="acct-name">' + name + '</span></a>'
      + '<span class="acct-rank" style="color:' + rankColor + '">' + rank + '</span>'
      + '<span class="acct-rating">' + rating + '</span>'
      + '<span class="acct-balance">' + bal + ' MC</span>';
  }
  el.innerHTML = '<div class="hdr-brand">CodexMagic</div>'
    + '<div class="hdr-right">' + nav + right + '</div>';
}

function updateAccountBar() {
  buildHeader();
}

async function refreshProfile() {
  if (!currentAccount) return;
  var data = await authCall('/profile?t=' + encodeURIComponent(authToken));
  if (data && data.handle) {
    currentAccount.balance = data.balance || 0;
    currentAccount.subscription = data.subscription || 'Free';
    currentAccount.name = data.adorned || data.display || currentAccount.handle;
    currentAccount.tokens = data.tokens || 0;
    currentAccount.wins = data.wins || 0;
    currentAccount.losses = data.losses || 0;
    currentAccount.rating = data.rating || 1000;
    currentAccount.rank = data.rank || 'Bronze';
    currentAccount.admin = !!data.admin;
    currentAccount['pw-score'] = data['pw-score'];
    currentAccount['pw-label'] = data['pw-label'];
    updateAccountBar();
  }
}

async function refreshBalance() {
  return refreshProfile();
}

function accountId() {
  if (currentAccount) return currentAccount['account-id'];
  return 0;
}

// Utilities
function lifeClass(life, max) {
  var pct = life / max * 100;
  if (pct > 60) return 'healthy';
  if (pct > 30) return 'hurt';
  return 'critical';
}

var _phaseNames = null;
function phaseName(step) {
  if (!_phaseNames) {
    _phaseNames = {
      0: 'Untap', 1: 'Upkeep', 2: 'Draw',
      3: 'Pre-combat Main', 4: 'Declare Attackers', 5: 'Declare Blockers',
      6: 'Combat Damage', 7: 'End Combat', 8: 'Post-combat Main',
      9: 'Cleanup'
    };
  }
  return _phaseNames[step] || 'Unknown';
}
function loadPhaseNames(phases) {
  if (!phases) return;
  _phaseNames = {};
  phases.forEach(function(p) { _phaseNames[p.step] = p.name; });
}

function addLog(msg, cls) {
  var log = document.getElementById('game-log');
  if (!log) return;
  var div = document.createElement('div');
  if (cls) div.className = cls;
  div.textContent = msg;
  log.appendChild(div);
  log.scrollTop = log.scrollHeight;
}

function clearLog() {
  var log = document.getElementById('game-log');
  if (log) log.innerHTML = '';
}

function buildCardData(perm, template) {
  return {
    id: perm.id,
    name: template.name,
    type: template.type,
    cost: template.cost,
    color: template.color,
    power: (template.power || 0) + (perm['power-mod'] || 0),
    toughness: (template.toughness || 0) + (perm['toughness-mod'] || 0),
    defense: (template.defense || 0) + (perm['defense-mod'] || 0),
    keywords: template.keywords || '',
    rules: template.rules || '',
    damage: perm['damage-marked'] || 0,
    tapped: perm.tapped,
    controller: perm.controller,
    attacking: perm.attacking || false,
    blocking: perm.blocking || false
  };
}

function buildHandCard(templateId, templates) {
  if (templateId >= templates.length) return { name: '???', type: 'Unknown' };
  var t = templates[templateId];
  return {
    id: templateId,
    name: t.name,
    type: t.type,
    cost: t.cost,
    color: t.color,
    power: t.power,
    toughness: t.toughness,
    defense: t.defense,
    keywords: t.keywords || '',
    rules: t.rules || ''
  };
}

function getStoredPrivateKey() {
  var stored = localStorage.getItem('cx_pk');
  if (stored) {
    try { return JSON.parse(stored); }
    catch (e) { return null; }
  }
  var key = { high: Math.floor(Math.random() * 0xFFFFFFFF), low: Math.floor(Math.random() * 0xFFFFFFFF) };
  localStorage.setItem('cx_pk', JSON.stringify(key));
  return key;
}

function hashMix(val, prime) {
  var h = ((val >>> 0) * prime) >>> 0;
  return h.toString(16).padStart(8, '0');
}
