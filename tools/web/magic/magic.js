// magic.js — CodexMagic shared client logic

const API = '/api/magic';
var currentAccount = null;

async function apiCall(path) {
  try {
    const r = await fetch(API + path);
    if (!r.ok) return null;
    return r.json();
  } catch (e) {
    console.error('API error:', path, e);
    return null;
  }
}

// Account management — stored in localStorage
function getStoredAccountId() {
  return localStorage.getItem('codexmagic-account-id');
}

function setStoredAccountId(id) {
  localStorage.setItem('codexmagic-account-id', id.toString());
}

function getStoredPrivateKey() {
  var h = localStorage.getItem('codexmagic-pk-high');
  var l = localStorage.getItem('codexmagic-pk-low');
  if (h && l) return { high: parseInt(h), low: parseInt(l) };
  return null;
}

function storePrivateKey(high, low) {
  localStorage.setItem('codexmagic-pk-high', high.toString());
  localStorage.setItem('codexmagic-pk-low', low.toString());
}

function generateKeypair() {
  var seedHigh = Math.floor(Math.random() * 2147483647) + 1;
  var seedLow = Math.floor(Math.random() * 2147483647) + 1;
  var pkHigh = hashMix(seedHigh, 2654435761);
  var pkLow = hashMix(seedLow, 2246822519);
  return { privateHigh: seedHigh, privateLow: seedLow, publicHigh: pkHigh, publicLow: pkLow };
}

function hashMix(val, prime) {
  var v1 = Math.imul(val, prime) | 0;
  var v2 = v1 ^ (v1 >>> 16);
  var v3 = Math.imul(v2, 2246822519) | 0;
  return (v3 ^ (v3 >>> 13)) | 0;
}

async function ensureAccount() {
  var id = getStoredAccountId();
  if (id !== null) {
    currentAccount = await apiCall('/account/profile?id=' + id);
    if (currentAccount) {
      updateAccountBar();
      return currentAccount;
    }
  }
  var name = prompt('Enter your player name:', 'Player-' + Date.now() % 1000);
  if (!name) name = 'Player';
  var keys = generateKeypair();
  storePrivateKey(keys.privateHigh, keys.privateLow);
  var data = await apiCall('/account/new?name=' + encodeURIComponent(name) + '&general=0&pk-high=' + keys.publicHigh + '&pk-low=' + keys.publicLow);
  if (data) {
    setStoredAccountId(data['account-id']);
    currentAccount = data;
    updateAccountBar();
    return data;
  }
  return null;
}

async function authenticate() {
  var id = getStoredAccountId();
  var pk = getStoredPrivateKey();
  if (!id || !pk) return false;
  var challenge = await apiCall('/auth/challenge');
  if (!challenge) return false;
  var sigHigh = hashMix((challenge.nonce * 31 + parseInt(id)) | 0, 2654435761);
  var sigLow = hashMix((challenge.nonce * 37 + pk.low) | 0, 2246822519);
  var result = await apiCall('/auth/verify?id=' + id + '&nonce=' + challenge.nonce + '&sig-high=' + sigHigh + '&sig-low=' + sigLow);
  if (result && result.verified) {
    localStorage.setItem('codexmagic-session', result['session-id']);
    return true;
  }
  return false;
}

function updateAccountBar() {
  var bar = document.getElementById('account-bar');
  if (!bar || !currentAccount) return;
  var name = currentAccount.name || 'Unknown';
  var rank = currentAccount.rank || 'Bronze';
  var rating = currentAccount.rating || 1000;
  var bal = currentAccount.balance !== undefined ? currentAccount.balance : '?';
  bar.innerHTML = '<span class="acct-name">' + name + '</span>' +
    '<span class="acct-rank">' + rank + ' ' + rating + '</span>' +
    '<span class="acct-balance">' + bal + ' MC</span>';
}

async function refreshBalance() {
  if (!currentAccount) return;
  var id = currentAccount['account-id'] || getStoredAccountId();
  var data = await apiCall('/store/balance?account=' + id);
  if (data) {
    currentAccount.balance = data.balance;
    currentAccount.subscription = data['sub-tier'];
    currentAccount['tokens'] = data['tokens-owned'];
    updateAccountBar();
  }
}

function accountId() {
  if (currentAccount) return currentAccount['account-id'];
  var id = getStoredAccountId();
  return id !== null ? parseInt(id) : 0;
}

// Utilities
function lifeClass(life, max) {
  var pct = life / max * 100;
  if (pct > 60) return 'healthy';
  if (pct > 30) return 'hurt';
  return 'critical';
}

function phaseName(step) {
  var names = {
    0: 'Untap', 1: 'Upkeep', 2: 'Draw',
    3: 'Pre-combat Main', 4: 'Declare Attackers', 5: 'Declare Blockers',
    6: 'Combat Damage', 7: 'End Combat', 8: 'Post-combat Main',
    9: 'Cleanup'
  };
  return names[step] || 'Unknown';
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
