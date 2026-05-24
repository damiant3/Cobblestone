// card-render.js — CodexMagic card rendering as styled divs

const COLOR_BORDERS = {
  W: '#f0e6d3', U: '#0e68ab', B: '#3d3d3d', R: '#d32f2f', G: '#2e7d32',
  C: '#9e9e9e', M: '#d4a017'
};
const COLOR_BG = {
  W: 'linear-gradient(135deg, #f5f0e8, #e8dcc8)',
  U: 'linear-gradient(135deg, #1a3a5c, #0e4a7a)',
  B: 'linear-gradient(135deg, #1a1a2e, #2d2d44)',
  R: 'linear-gradient(135deg, #4a1a1a, #6b2020)',
  G: 'linear-gradient(135deg, #1a3a1a, #2d5a2d)',
  C: 'linear-gradient(135deg, #2a2a2a, #3a3a3a)',
  M: 'linear-gradient(135deg, #3a2a1a, #5a4a2a)'
};
const RARITY_COLORS = {
  Common: '#c9d1d9', Uncommon: '#58a6ff', Rare: '#d29922',
  Mythic: '#f0883e', 'Legendary Mythic': '#da3633'
};

function cardBorderColor(card) {
  if (!card.color) return COLOR_BORDERS.C;
  const colors = [];
  if (card.color.white) colors.push('W');
  if (card.color.blue) colors.push('U');
  if (card.color.black) colors.push('B');
  if (card.color.red) colors.push('R');
  if (card.color.green) colors.push('G');
  if (colors.length === 0) return COLOR_BORDERS.C;
  if (colors.length > 1) return COLOR_BORDERS.M;
  return COLOR_BORDERS[colors[0]];
}

function cardBackground(card) {
  if (!card.color) return COLOR_BG.C;
  const colors = [];
  if (card.color.white) colors.push('W');
  if (card.color.blue) colors.push('U');
  if (card.color.black) colors.push('B');
  if (card.color.red) colors.push('R');
  if (card.color.green) colors.push('G');
  if (colors.length === 0) return COLOR_BG.C;
  if (colors.length > 1) return COLOR_BG.M;
  return COLOR_BG[colors[0]];
}

function formatManaCost(cost) {
  if (!cost) return '';
  let s = '';
  if (cost.generic > 0) s += `{${cost.generic}}`;
  for (let i = 0; i < (cost.white||0); i++) s += '{W}';
  for (let i = 0; i < (cost.blue||0); i++) s += '{U}';
  for (let i = 0; i < (cost.black||0); i++) s += '{B}';
  for (let i = 0; i < (cost.red||0); i++) s += '{R}';
  for (let i = 0; i < (cost.green||0); i++) s += '{G}';
  return s;
}

function renderManaSymbols(costStr) {
  return costStr.replace(/\{(\w+)\}/g, (m, s) => {
    const cls = isNaN(s) ? `mana-${s}` : 'mana-generic';
    const label = s;
    return `<span class="mana-symbol ${cls}">${label}</span>`;
  });
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
  const hasPTD = isCreature && (card.power !== undefined);

  el.innerHTML = `
    <div class="card-header">
      <span class="card-name">${card.name || '???'}</span>
      <span class="card-cost">${renderManaSymbols(formatManaCost(card.cost))}</span>
    </div>
    <div class="card-art" style="background:${cardBackground(card)}">
      ${card.type === 'General' ? '<div class="general-badge">GENERAL</div>' : ''}
    </div>
    <div class="card-type">${card.type || ''}${card.subtypes ? ' - ' + card.subtypes : ''}</div>
    <div class="card-text">${card.keywords || ''}${card.rules || ''}</div>
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
