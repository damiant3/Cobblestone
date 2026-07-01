// CVMM Browser Client
// Fetches data from /api/* endpoints and renders the management UI.

const NAV = [
  { id: 'dashboard',  icon: '▣', label: 'Dashboard',  api: '/api/system/status' },
  { id: 'files',      icon: '❐', label: 'Files',      api: '/api/fs/list' },
  { id: 'drives',     icon: '▨', label: 'Drives',     api: '/api/drive/list' },
  { id: 'usb',        icon: '⬓', label: 'USB',        api: '/api/usb/list' },
  { id: 'processes',  icon: '⚙', label: 'Processes',   api: '/api/process/list' },
  { id: 'services',   icon: '⚡', label: 'Services',    api: '/api/service/list' },
  { id: 'ports',      icon: '⌖', label: 'Ports',       api: '/api/port/listeners' },
  { id: 'network',    icon: '⛁', label: 'Network',     api: '/api/net/nics' },
  { id: 'display',    icon: '◻', label: 'Display',     api: '/api/display/monitors' },
  { id: 'servers',    icon: '☐', label: 'Servers',     api: '/api/server/list' },
  { id: 'fleet',      icon: '⌂', label: 'Fleet',       api: '/api/fleet/list' },
  { id: 'deploy',     icon: '⇧', label: 'Deploy',      api: '/api/deploy/list' },
  { id: 'monitor',    icon: '≈', label: 'Monitor',     api: '/api/monitor/layout' },
  { id: 'logs',       icon: '▤', label: 'Logs',        api: '/api/log/list' },
  { id: 'terminal',   icon: '▸', label: 'Terminal',    api: '/api/terminal/state' },
  { id: 'settings',   icon: '☸', label: 'Settings',    api: null }
];

let currentView = 'dashboard';
let viewData = {};
let sidebarOpen = true;

// -- API ------------------------------------------------------------------

async function fetchApi(path) {
  try {
    const r = await fetch(path);
    return await r.json();
  } catch (e) {
    return { error: e.message };
  }
}

// -- Helpers --------------------------------------------------------------

function h(tag, attrs, ...children) {
  const el = document.createElement(tag);
  if (attrs) {
    for (const [k, v] of Object.entries(attrs)) {
      if (k === 'onclick' || k === 'oninput') el[k] = v;
      else if (k === 'className') el.className = v;
      else el.setAttribute(k, v);
    }
  }
  for (const c of children) {
    if (typeof c === 'string') el.appendChild(document.createTextNode(c));
    else if (c) el.appendChild(c);
  }
  return el;
}

function fmtBytes(n) {
  if (n >= 1073741824) return (n / 1073741824 | 0) + ' GB';
  if (n >= 1048576) return (n / 1048576 | 0) + ' MB';
  if (n >= 1024) return (n / 1024 | 0) + ' KB';
  return n + ' B';
}

function fmtUptime(secs) {
  const d = secs / 86400 | 0;
  const hr = (secs - d * 86400) / 3600 | 0;
  const m = (secs - d * 86400 - hr * 3600) / 60 | 0;
  if (d > 0) return d + 'd ' + hr + 'h ' + m + 'm';
  if (hr > 0) return hr + 'h ' + m + 'm';
  return m + 'm';
}

function stClass(status) {
  if (status === 'online' || status === 'running') return 'st online';
  if (status === 'offline' || status === 'stopped') return 'st offline';
  if (status === 'error' || status === 'failed') return 'st error';
  if (status === 'busy' || status === 'starting' || status === 'stopping') return 'st busy';
  return 'st';
}

// -- Sidebar --------------------------------------------------------------

function renderSidebar() {
  const container = document.getElementById('nav-items');
  container.innerHTML = '';
  for (const item of NAV) {
    const cls = 'nav-item' + (item.id === currentView ? ' active' : '');
    const el = h('div', { className: cls, onclick: () => navigate(item.id) },
      h('span', { className: 'nav-icon' }, item.icon),
      h('span', null, item.label)
    );
    container.appendChild(el);
  }
}

// -- Navigation -----------------------------------------------------------

async function navigate(viewId) {
  currentView = viewId;
  document.getElementById('view-title').textContent =
    NAV.find(n => n.id === viewId)?.label || viewId;
  renderSidebar();

  const nav = NAV.find(n => n.id === viewId);
  if (nav && nav.api) {
    viewData[viewId] = await fetchApi(nav.api);
  }
  renderContent();
}

// -- Content Renderers ----------------------------------------------------

function renderContent() {
  const el = document.getElementById('content');
  el.innerHTML = '';

  switch (currentView) {
    case 'dashboard':  el.appendChild(renderDashboard()); break;
    case 'files':      el.appendChild(renderTable(viewData.files, ['name','kind','size','extension'], 'Files')); break;
    case 'drives':     el.appendChild(renderTable(viewData.drives, ['id','model','bus','capacity','health'], 'Drives')); break;
    case 'usb':        el.appendChild(renderTable(viewData.usb, ['id','name','class','speed','power','driver','status'], 'USB Devices')); break;
    case 'processes':  el.appendChild(renderTable(viewData.processes, ['pid','name','state','cpu','mem','threads'], 'Processes')); break;
    case 'services':   el.appendChild(renderTable(viewData.services, ['id','name','type','state','enabled','uptime','restarts'], 'Services')); break;
    case 'ports':      el.appendChild(renderTable(viewData.ports, ['port','protocol','bind','service','connections','bytesIn','bytesOut'], 'Port Listeners')); break;
    case 'network':    el.appendChild(renderTable(viewData.network, ['id','name','type','mac','ip','speed','status','rxBytes','txBytes'], 'Network Interfaces')); break;
    case 'display':    el.appendChild(renderDisplayView()); break;
    case 'servers':    el.appendChild(renderTable(viewData.servers, ['id','name','type','endpoint','status','requests','errors','uptime'], 'Managed Servers')); break;
    case 'fleet':      el.appendChild(renderTable(viewData.fleet, ['uuid','name','kind','endpoint','status','cpu','latency','group'], 'Fleet Nodes')); break;
    case 'deploy':     el.appendChild(renderDeployView()); break;
    case 'monitor':    el.appendChild(renderMonitorView()); break;
    case 'logs':       el.appendChild(renderLogView()); break;
    case 'terminal':   el.appendChild(renderTerminalView()); break;
    case 'settings':   el.appendChild(renderSettings()); break;
    default:           el.appendChild(h('p', null, 'Unknown view: ' + currentView));
  }
}

function renderDashboard() {
  const d = viewData.dashboard || {};
  const memPct = d.memTotal > 0 ? (d.memUsed * 100 / d.memTotal | 0) : 0;
  const diskPct = d.diskTotal > 0 ? (d.diskUsed * 100 / d.diskTotal | 0) : 0;

  document.getElementById('sys-cpu').value = d.cpu || 0;
  document.getElementById('sys-cpu-pct').textContent = (d.cpu || 0) + '%';
  document.getElementById('sys-mem').value = memPct;
  document.getElementById('sys-mem-pct').textContent = memPct + '%';

  const grid = h('div', { className: 'dash-grid' });

  grid.appendChild(dashCard('CPU', (d.cpu || 0) + '%', d.cpu || 0, null));
  grid.appendChild(dashCard('Memory', fmtBytes(d.memUsed || 0), memPct, fmtBytes(d.memTotal || 0)));
  grid.appendChild(dashCard('Disk', fmtBytes(d.diskUsed || 0), diskPct, fmtBytes(d.diskTotal || 0)));
  grid.appendChild(dashCard('Uptime', fmtUptime(d.uptime || 0), null, d.hostname || 'unknown'));
  grid.appendChild(dashCountCard('Processes', d.processes, 'processes'));
  grid.appendChild(dashCountCard('Services', d.services, 'services'));
  grid.appendChild(dashCountCard('Ports', d.ports, 'ports'));
  grid.appendChild(dashCountCard('NICs', d.nics, 'network'));
  grid.appendChild(dashCountCard('Fleet', d.fleet, 'fleet'));

  return grid;
}

function dashCard(title, value, pct, sub) {
  const card = h('div', { className: 'card' },
    h('div', { className: 'card-title' }, title),
    h('div', { className: 'card-value' }, String(value))
  );
  if (pct !== null) {
    const prog = h('progress', { max: '100', value: String(pct) });
    card.appendChild(prog);
  }
  if (sub) card.appendChild(h('div', { className: 'card-sub' }, sub));
  return card;
}

function dashCountCard(title, count, navTarget) {
  const card = h('div', { className: 'card', onclick: () => navigate(navTarget) },
    h('div', { className: 'card-title' }, title),
    h('div', { className: 'card-value' }, String(count || 0)),
    h('div', { className: 'card-sub' }, 'View all →')
  );
  return card;
}

function renderTable(data, cols, title) {
  const wrap = h('div');
  wrap.appendChild(h('div', { className: 'toolbar' },
    h('input', { type: 'text', placeholder: 'Filter...', oninput: function() { filterTable(this.value); } })
  ));

  if (!data || data.error) {
    wrap.appendChild(h('p', { style: 'color:var(--muted)' }, data?.error || 'No data (CDX not connected)'));
    return wrap;
  }

  const rows = Array.isArray(data) ? data : [];
  const table = h('table', { className: 'mgr-table' });
  const thead = h('thead');
  const hrow = h('tr');
  for (const c of cols) hrow.appendChild(h('th', null, c));
  thead.appendChild(hrow);
  table.appendChild(thead);

  const tbody = h('tbody', { id: 'table-body' });
  for (const row of rows) {
    const tr = h('tr');
    for (const c of cols) {
      let val = row[c];
      let td;
      if (c === 'status' || c === 'state') {
        td = h('td', null, h('span', { className: stClass(String(val)) }, String(val || '')));
      } else if (c === 'size' || c === 'capacity' || c === 'mem' || c === 'memUsed' || c === 'memTotal' ||
                 c === 'bytesIn' || c === 'bytesOut' || c === 'rxBytes' || c === 'txBytes') {
        td = h('td', null, fmtBytes(val || 0));
      } else if (c === 'cpu') {
        td = h('td', null, h('span', { className: 'gauge-inline' },
          h('progress', { max: '100', value: String(val || 0) }),
          document.createTextNode((val || 0) + '%')
        ));
      } else if (c === 'enabled') {
        td = h('td', null, val ? 'yes' : 'no');
      } else {
        td = h('td', null, String(val !== undefined ? val : ''));
      }
      tr.appendChild(td);
    }
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  wrap.appendChild(table);
  wrap.appendChild(h('div', { className: 'card-sub', style: 'margin-top:8px' }, rows.length + ' items'));
  return wrap;
}

function filterTable(query) {
  const tbody = document.getElementById('table-body');
  if (!tbody) return;
  const q = query.toLowerCase();
  for (const tr of tbody.children) {
    const text = tr.textContent.toLowerCase();
    tr.style.display = text.includes(q) ? '' : 'none';
  }
}

function renderDisplayView() {
  const d = viewData.display || {};
  return h('div', null,
    h('div', { className: 'dash-grid' },
      dashCard('GPUs', d.gpus || 0, null, null),
      dashCard('Monitors', d.monitors || 0, null, null),
      dashCard('Total Pixels', ((d.totalPixels || 0) / 1000000).toFixed(1) + 'M', null, null)
    )
  );
}

function renderDeployView() {
  const d = viewData.deploy || {};
  return h('div', null,
    h('div', { className: 'dash-grid' },
      dashCard('Active', d.active || 0, null, null),
      dashCard('Total', d.total || 0, null, null)
    ),
    h('button', { className: 'btn', style: 'margin-top:12px' }, 'New Deployment')
  );
}

function renderMonitorView() {
  const data = viewData.monitor;
  const wrap = h('div');

  if (!data || data.error) {
    wrap.appendChild(h('p', { style: 'color:var(--muted)' }, data?.error || 'No data'));
    return wrap;
  }

  // Layout tabs
  const tabBar = h('div', { className: 'tab-bar' });
  if (data.panels) {
    // We have a layout
    const toolbar = h('div', { className: 'toolbar' },
      h('span', { style: 'font-weight:600' }, data.name || 'Monitor'),
      h('span', { style: 'color:var(--muted);margin-left:8px' }, data.panelCount + ' panels'),
      h('button', { className: 'btn secondary', style: 'margin-left:auto', onclick: openMonitorCatalog }, 'Add Panel'),
      h('button', { className: 'btn secondary', onclick: async function() {
        const layouts = ['system', 'network', 'fleet'];
        const current = layouts.indexOf(data.id);
        const next = layouts[(current + 1) % layouts.length];
        viewData.monitor = await fetchApi('/api/monitor/layout?id=' + next);
        renderContent();
      }}, 'Switch Layout')
    );
    wrap.appendChild(toolbar);

    // Panel grid
    const grid = h('div', { className: 'monitor-grid' });
    const panels = Array.isArray(data.panels) ? data.panels : [];
    for (const panel of panels) {
      const panelEl = renderMonitorPanel(panel);
      grid.appendChild(panelEl);
    }
    wrap.appendChild(grid);
  } else {
    wrap.appendChild(h('p', null, 'Layout has no panels'));
  }

  return wrap;
}

function renderMonitorPanel(panel) {
  const stateClass = panel.state === 2 ? 'panel-crit' : panel.state === 1 ? 'panel-warn' : '';
  const el = h('div', { className: 'monitor-panel ' + stateClass,
    style: 'grid-column: span ' + (panel.width || 1) + '; grid-row: span ' + (panel.height || 1)
  });

  const header = h('div', { className: 'panel-header' },
    h('span', { className: 'panel-title' }, panel.title || panel.id),
    h('span', { className: 'panel-source' }, panel.source || '')
  );
  el.appendChild(header);

  const body = h('div', { className: 'panel-body' });
  const viz = panel.viz || 'Number';
  const value = panel.value || '';

  if (viz === 'Gauge') {
    const pct = parseInt(value) || 0;
    body.appendChild(h('div', { className: 'gauge-ring', 'data-value': String(pct) },
      h('span', { className: 'gauge-ring-value' }, pct + '%')
    ));
    const prog = h('progress', { max: '100', value: String(pct), style: 'width:100%;height:10px;margin-top:8px' });
    body.appendChild(prog);
  } else if (viz === 'Number') {
    body.appendChild(h('div', { className: 'panel-number' }, value));
  } else if (viz === 'Sparkline') {
    body.appendChild(h('div', { className: 'panel-sparkline' },
      h('span', { style: 'color:var(--muted);font-size:12px' }, viz + ': ' + value),
      h('div', { className: 'spark-placeholder', style: 'height:40px;background:var(--bg);border-radius:4px;margin-top:4px' })
    ));
  } else if (viz === 'Table') {
    body.appendChild(h('div', { style: 'font-size:12px;color:var(--muted)' }, value));
  } else if (viz === 'Log Tail') {
    body.appendChild(h('div', { style: 'font-size:12px;color:var(--muted);font-family:monospace' }, value));
  } else {
    body.appendChild(h('div', null, value));
  }

  el.appendChild(body);
  return el;
}

async function openMonitorCatalog() {
  const data = await fetchApi('/api/monitor/catalog');
  if (!data || data.error || !Array.isArray(data)) return;

  const modal = h('div', { className: 'catalog-modal' });
  const inner = h('div', { className: 'catalog-inner' },
    h('div', { style: 'display:flex;justify-content:space-between;align-items:center;margin-bottom:12px' },
      h('h3', null, 'Panel Catalog'),
      h('button', { className: 'icon-btn', onclick: function() { modal.remove(); } }, 'X')
    )
  );

  // Group by category
  const cats = {};
  for (const entry of data) {
    const cat = entry.category || 'Other';
    if (!cats[cat]) cats[cat] = [];
    cats[cat].push(entry);
  }

  for (const [cat, entries] of Object.entries(cats)) {
    inner.appendChild(h('div', { className: 'section-title' }, cat));
    for (const entry of entries) {
      const row = h('div', { className: 'catalog-row' },
        h('span', { style: 'font-weight:500' }, entry.title),
        h('span', { style: 'color:var(--muted);font-size:12px;margin-left:8px' }, entry.viz + ' - ' + entry.description),
        h('button', { className: 'btn small', style: 'margin-left:auto', onclick: function() {
          modal.remove();
        }}, 'Add')
      );
      inner.appendChild(row);
    }
  }

  modal.appendChild(inner);
  document.body.appendChild(modal);
}

function renderLogView() {
  const data = viewData.logs;
  if (!data || data.error) {
    return h('p', { style: 'color:var(--muted)' }, data?.error || 'No data');
  }

  const rows = Array.isArray(data) ? data : [];
  const wrap = h('div');

  const toolbar = h('div', { className: 'toolbar' },
    h('input', { type: 'text', placeholder: 'Filter log messages...', oninput: function() { filterTable(this.value); } }),
    h('button', { className: 'btn secondary', onclick: function() { filterLogBySeverity('INFO'); } }, 'Info+'),
    h('button', { className: 'btn secondary', onclick: function() { filterLogBySeverity('WARN'); } }, 'Warn+'),
    h('button', { className: 'btn secondary', onclick: function() { filterLogBySeverity('ERROR'); } }, 'Error+')
  );
  wrap.appendChild(toolbar);

  const table = h('table', { className: 'mgr-table' });
  const thead = h('thead');
  thead.appendChild(h('tr', null,
    h('th', null, 'Time'), h('th', null, 'Severity'), h('th', null, 'Source'), h('th', null, 'Message')
  ));
  table.appendChild(thead);

  const tbody = h('tbody', { id: 'table-body' });
  for (const row of rows) {
    const sevClass = (row.severity === 'ERROR' || row.severity === 'FATAL') ? 'st error' :
                     row.severity === 'WARN' ? 'st warning' :
                     row.severity === 'DEBUG' || row.severity === 'TRACE' ? 'st offline' : 'st online';
    const tr = h('tr', null,
      h('td', null, String(row.timestamp || 0)),
      h('td', null, h('span', { className: sevClass }, row.severity || '')),
      h('td', null, row.source || ''),
      h('td', null, row.message || '')
    );
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  wrap.appendChild(table);
  wrap.appendChild(h('div', { className: 'card-sub', style: 'margin-top:8px' }, rows.length + ' log entries'));
  return wrap;
}

function filterLogBySeverity(minSev) {
  const levels = { 'TRACE': 0, 'DEBUG': 1, 'INFO': 2, 'WARN': 3, 'ERROR': 4, 'FATAL': 5 };
  const min = levels[minSev] || 0;
  const tbody = document.getElementById('table-body');
  if (!tbody) return;
  for (const tr of tbody.children) {
    const sevCell = tr.children[1];
    if (!sevCell) continue;
    const sev = sevCell.textContent.trim();
    const level = levels[sev] !== undefined ? levels[sev] : 2;
    tr.style.display = level >= min ? '' : 'none';
  }
}

function renderTerminalView() {
  const data = viewData.terminal;
  const wrap = h('div');

  const info = h('div', { className: 'toolbar' },
    h('span', { style: 'color:var(--muted)' }, data ? (data.cols + 'x' + data.rows + ' | scrollback: ' + data.scrollback + ' | history: ' + data.history) : 'Not connected')
  );
  wrap.appendChild(info);

  const termBox = h('div', {
    style: 'background:#121212;border:1px solid var(--border);border-radius:var(--radius);padding:12px;font-family:monospace;font-size:14px;line-height:1.4;white-space:pre;overflow:auto;min-height:400px;color:#e0e0e0;'
  });
  if (data && data.inputLine !== undefined) {
    termBox.textContent = '(Terminal connected)\n\ncodex> ' + data.inputLine + '_';
  } else {
    termBox.textContent = '(Terminal not connected)';
  }
  wrap.appendChild(termBox);

  const inputRow = h('div', { className: 'toolbar', style: 'margin-top:8px' },
    h('input', { type: 'text', placeholder: 'Enter command...', id: 'term-input',
      onkeydown: function(e) { if (e.key === 'Enter') { sendTerminalCommand(this.value); this.value = ''; } }
    }),
    h('button', { className: 'btn', onclick: function() { const inp = document.getElementById('term-input'); if (inp) { sendTerminalCommand(inp.value); inp.value = ''; } } }, 'Send')
  );
  wrap.appendChild(inputRow);
  return wrap;
}

function sendTerminalCommand(cmd) {
  fetchApi('/api/terminal/exec?cmd=' + encodeURIComponent(cmd));
}

function renderSettings() {
  return h('div', null,
    h('div', { className: 'section-title' }, 'Theme'),
    h('button', { className: 'btn secondary', onclick: toggleTheme }, 'Toggle Dark/Light'),
    h('div', { className: 'section-title', style: 'margin-top:24px' }, 'About'),
    h('p', { style: 'color:var(--muted)' }, 'CVMM - Codex Virtual Machine Manager'),
    h('p', { style: 'color:var(--muted)' }, 'codex.OS desktop shell')
  );
}

function toggleTheme() {
  document.body.classList.toggle('light');
}

// -- Header ---------------------------------------------------------------

document.getElementById('toggle-sidebar').onclick = function() {
  sidebarOpen = !sidebarOpen;
  document.getElementById('sidebar').classList.toggle('collapsed');
  this.textContent = sidebarOpen ? '<<' : '>>';
};

// -- Auto-refresh ---------------------------------------------------------

let refreshTimer = null;
function startAutoRefresh() {
  if (refreshTimer) clearInterval(refreshTimer);
  refreshTimer = setInterval(async () => {
    if (currentView === 'dashboard') {
      viewData.dashboard = await fetchApi('/api/system/status');
      renderContent();
    }
  }, 5000);
}

// -- Boot -----------------------------------------------------------------

async function boot() {
  renderSidebar();
  await navigate('dashboard');
  startAutoRefresh();
}

boot();
