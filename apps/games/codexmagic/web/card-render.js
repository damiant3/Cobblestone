// card-render.js -- CodexMagic card rendering (Prismatic System)

const COLOR_BORDERS = {
  R: '#d32f2f', Y: '#e6a817', B: '#0e68ab', O: '#e65100', G: '#2e7d32',
  P: '#7b1fa2', W: '#e0d8c8', C: '#9e9e9e', M: '#d4a017'
};
const COLOR_BG = {
  R: 'linear-gradient(135deg, #4a1a1a, #6b2020)',
  Y: 'linear-gradient(135deg, #4a3a10, #6b5a20)',
  B: 'linear-gradient(135deg, #1a3a5c, #0e4a7a)',
  O: 'linear-gradient(135deg, #4a2a0a, #6b3a10)',
  G: 'linear-gradient(135deg, #1a3a1a, #2d5a2d)',
  P: 'linear-gradient(135deg, #2a1a3a, #4a2060)',
  W: 'linear-gradient(135deg, #f5f0e8, #e8dcc8)',
  C: 'linear-gradient(135deg, #2a2a2a, #3a3a3a)',
  M: 'linear-gradient(135deg, #3a2a1a, #5a4a2a)'
};
const RARITY_COLORS = {
  Common: '#c9d1d9', Uncommon: '#58a6ff', Rare: '#d29922',
  Mythic: '#f0883e', 'Legendary Mythic': '#da3633'
};
const SPEED_LABELS = {
  cantrip: 'Cantrip', incantation: 'Incantation',
  summoning: 'Summoning', disruption: 'Disruption'
};

function cardBorderColor(card) {
  if (!card.color) return COLOR_BORDERS.C;
  const colors = [];
  if (card.color.red) colors.push('R');
  if (card.color.yellow) colors.push('Y');
  if (card.color.blue) colors.push('B');
  if (card.color.orange) colors.push('O');
  if (card.color.green) colors.push('G');
  if (card.color.purple) colors.push('P');
  if (colors.length === 0) return COLOR_BORDERS.C;
  if (colors.length > 1) return COLOR_BORDERS.M;
  return COLOR_BORDERS[colors[0]];
}

function cardBackground(card) {
  if (!card.color) return COLOR_BG.C;
  const colors = [];
  if (card.color.red) colors.push('R');
  if (card.color.yellow) colors.push('Y');
  if (card.color.blue) colors.push('B');
  if (card.color.orange) colors.push('O');
  if (card.color.green) colors.push('G');
  if (card.color.purple) colors.push('P');
  if (colors.length === 0) return COLOR_BG.C;
  if (colors.length > 1) return COLOR_BG.M;
  return COLOR_BG[colors[0]];
}

function formatManaCost(cost) {
  if (!cost) return '';
  let s = '';
  if (cost.white > 0) s += `{${cost.white}}`;
  for (let i = 0; i < (cost.red||0); i++) s += '{R}';
  for (let i = 0; i < (cost.yellow||0); i++) s += '{Y}';
  for (let i = 0; i < (cost.blue||0); i++) s += '{B}';
  for (let i = 0; i < (cost.orange||0); i++) s += '{O}';
  for (let i = 0; i < (cost.green||0); i++) s += '{G}';
  for (let i = 0; i < (cost.purple||0); i++) s += '{P}';
  return s;
}

function renderManaSymbols(costStr) {
  return costStr.replace(/\{(\w+)\}/g, (m, s) => {
    const cls = isNaN(s) ? `mana-${s}` : 'mana-generic';
    const label = s;
    return `<span class="mana-symbol ${cls}">${label}</span>`;
  });
}

function formatSpellSpeed(card) {
  if (!card.spellSpeed) return '';
  return SPEED_LABELS[card.spellSpeed] || '';
}

const TYPE_DISPLAY = {
  'Creature': 'Summon', 'Equipment': 'Conjure', 'Enchantment': 'Enchant',
  'Cantrip': 'Cantrip', 'Incantation': 'Incantation', 'Disruption': 'Disruption',
  'Gemstone': 'Gem', 'General': 'General'
};
function formatTypeLine(card) {
  let line = TYPE_DISPLAY[card.type] || card.type || '';
  if (card.type === 'Equipment' && card.slot) line += ' -- ' + card.slot;
  if (card.type === 'Gemstone' && card.gemColor) line += ' -- ' + card.variety;
  return line;
}

function renderCard(card, opts) {
  opts = opts || {};
  const size = opts.size || 'normal';
  const el = document.createElement('div');
  el.className = `magic-card card-${size}`;
  if (opts.tapped) el.classList.add('tapped');
  if (opts.attacking) el.classList.add('attacking');
  if (opts.blocking) el.classList.add('blocking');
  if (opts.highlight) el.classList.add('highlight');
  el.style.borderColor = cardBorderColor(card);

  const isCreature = card.type === 'Creature' || card.type === 'General';
  const isEquipment = card.type === 'Equipment';
  const hasPTD = (isCreature || isEquipment) && (card.power !== undefined);

  const rarityColor = card.rarity ? (RARITY_COLORS[card.rarity] || '#c9d1d9') : '#c9d1d9';
  if (card.rarity && card.rarity !== 'Common') {
    el.style.boxShadow = `0 0 6px ${rarityColor}40`;
  }

  let textLines = card.keywords || '';
  if (card.fluorClause) {
    textLines += (textLines ? '<br>' : '') + '<span class="fluor-clause">With Fluorescence: ' + card.fluorClause + '</span>';
  }
  if (isEquipment && card.socketEmpty) {
    textLines += (textLines ? '<br>' : '') + '<span class="socket-empty">Socket: Empty</span>';
  }

  el.innerHTML = `
    <div class="card-header">
      <span class="card-name">${card.name || '???'}</span>
      <span class="card-cost">${renderManaSymbols(formatManaCost(card.cost))}</span>
    </div>
    <div class="card-art" style="background:${cardBackground(card)}">
      ${card.type === 'General' ? '<div class="general-badge">GENERAL</div>' : ''}
      ${card.type === 'Gemstone' ? '<div class="gem-badge">' + (card.variety || '') + (card.hardness ? ' &middot; H' + card.hardness : '') + '</div>' : ''}
    </div>
    <div class="card-type">${formatTypeLine(card)}${card.rarity ? ' <span style="float:right;color:' + rarityColor + '">' + card.rarity.charAt(0) + '</span>' : ''}</div>
    <div class="card-text">${textLines}${card.rules || ''}</div>
    ${hasPTD ? `<div class="card-ptd">${card.power}/${card.toughness}/${card.defense}</div>` : ''}
    ${card.damage > 0 ? `<div class="card-damage">${card.damage} dmg</div>` : ''}
  `;

  if (opts.onClick) el.addEventListener('click', () => opts.onClick(card));
  return el;
}

function renderGeneralCard(gen, state, loyalty) {
  const el = document.createElement('div');
  el.className = 'magic-card card-general';
  el.style.borderColor = cardBorderColor(gen);

  el.innerHTML = `
    <div class="card-header">
      <span class="card-name">${gen.name}</span>
      <span class="card-cost">${renderManaSymbols(formatManaCost(gen.cost))}</span>
    </div>
    <div class="card-art general-art" style="background:${cardBackground(gen)}">
      <div class="general-badge">GENERAL</div>
      <div class="general-life">${state.life}</div>
    </div>
    <div class="card-type">General</div>
    <div class="general-stats">
      <span class="ptd">${gen.power}/${gen.toughness}/${gen.defense}</span>
      <span class="loyalty">Loyalty: ${loyalty}</span>
    </div>
    ${state.tapped ? '<div class="tapped-overlay">TAPPED</div>' : ''}
  `;
  return el;
}

function renderCardSmall(card, opts) {
  opts = opts || {};
  const el = document.createElement('div');
  el.className = 'magic-card card-small';
  if (opts.tapped) el.classList.add('tapped');
  el.style.borderColor = cardBorderColor(card);
  const isCreature = card.type === 'Creature';
  el.innerHTML = `
    <div class="card-name-small">${card.name || '???'}</div>
    ${isCreature ? `<div class="card-ptd-small">${card.power}/${card.toughness}/${card.defense}</div>` : ''}
  `;
  if (opts.onClick) el.addEventListener('click', () => opts.onClick(card));
  return el;
}

function showCardDetail(card) {
  var ov = document.getElementById('card-detail-overlay');
  if (!ov) {
    ov = document.createElement('div');
    ov.id = 'card-detail-overlay';
    ov.className = 'card-detail-overlay';
    ov.innerHTML = '<div class="card-detail" id="card-detail-content"></div>';
    ov.addEventListener('click', function(e) { if (e.target === ov) ov.classList.remove('active'); });
    document.body.appendChild(ov);
  }
  var rarityColor = card.rarity ? (RARITY_COLORS[card.rarity] || '#c9d1d9') : '#c9d1d9';
  var isCreature = card.type === 'Creature' || card.type === 'General';
  var isEquipment = card.type === 'Equipment';
  var content = document.getElementById('card-detail-content');
  content.style.borderColor = cardBorderColor(card);
  content.innerHTML =
    '<div class="cd-name">' + (card.name || '???') + '</div>' +
    '<div class="cd-type">' + formatTypeLine(card) + '</div>' +
    '<div class="cd-rarity" style="color:' + rarityColor + '">' + (card.rarity || 'Common') + '</div>' +
    '<div class="cd-cost">' + renderManaSymbols(formatManaCost(card.cost)) + '</div>' +
    (isCreature ? '<div class="cd-stats">Power ' + (card.power||0) + ' / Toughness ' + (card.toughness||0) + ' / Defense ' + (card.defense||0) + '</div>' : '') +
    (isEquipment ? '<div class="cd-stats">+' + (card.power||0) + '/+' + (card.toughness||0) + '/+' + (card.defense||0) + '</div>' : '') +
    (card.keywords ? '<div class="cd-keywords">' + card.keywords + '</div>' : '') +
    (card.fluorClause ? '<div class="cd-fluor">With Fluorescence: ' + card.fluorClause + '</div>' : '') +
    (card.type === 'Gemstone' ? '<div class="cd-hardness">' + (card.variety||'') + '<br>Hardness: ' + (card.hardness||0) + '</div>' : '') +
    '<button class="cd-close" onclick="document.getElementById(\'card-detail-overlay\').classList.remove(\'active\')">Close</button>';
  ov.classList.add('active');
}
