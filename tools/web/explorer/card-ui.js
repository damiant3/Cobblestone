// card-ui.js — DOM glue for CardDesignerApp (Codex-transpiled).
// Reads exported functions from card-app.js and wires the
// pin-and-cascade dimension explorer UI.
(function () {
  var DIMS = dimensions();
  var STEPS = steps_options();
  var CFGS = cfg_options();
  var config = null;
  var pinned = {};

  document.getElementById('prompt').value = default_prompt();
  document.getElementById('neg').value = default_negative();

  function loadConfig() {
    fetch('/api/config').then(function (r) { return r.json(); })
      .then(function (data) { config = data; renderDimensions(); })
      .catch(function () {
        config = { models: [], samplers: [], loras: [], current_model: '' };
        renderDimensions();
      });
  }

  function dimValues(key) {
    if (!config) return [];
    if (key === 'model') return config.models || [];
    if (key === 'sampler') return (config.samplers || []).map(function (s) { return { name: s, title: s }; });
    if (key === 'steps') return STEPS.map(function (s) { return { name: String(Number(s)), title: String(Number(s)) }; });
    if (key === 'cfg') return CFGS.map(function (c) { return { name: String(Number(c)), title: String(Number(c)) }; });
    if (key === 'lora') return [{ name: 'none', title: 'None' }].concat((config.loras || []).map(function (l) { return { name: l.name, title: l.name.split('_')[0] }; }));
    return [];
  }

  function renderDimensions() {
    var container = document.getElementById('dimensions');
    container.innerHTML = '';
    DIMS.forEach(function (dim) {
      var key = dim.key;
      var values = dimValues(key);
      var row = document.createElement('div');
      row.className = 'dim-row';
      var header = '<div class="dim-header"><h2>' + dim.name + '</h2>';
      if (pinned[key] !== undefined) {
        header += '<span class="pinned">' + pinned[key] + ' <span style="cursor:pointer" onclick="unpinDim(\'' + key + '\')">&times;</span></span>';
      }
      header += '</div>';
      var grid = '<div class="dim-grid">';
      values.forEach(function (v) {
        var label = v.title || v.name;
        var selected = pinned[key] === (v.name || v.title) ? ' selected' : '';
        grid += '<div class="img-card' + selected + '" onclick="pinDim(\'' + key + '\',\'' + (v.name || v.title).replace(/'/g, "\\'") + '\')">'
          + '<img src="">'
          + '<div class="lbl"><b>' + label + '</b></div></div>';
      });
      grid += '</div>';
      row.innerHTML = header + grid;
      container.appendChild(row);
    });
  }

  window.pinDim = function (key, value) {
    pinned[key] = value;
    renderDimensions();
    generateAll();
  };

  window.unpinDim = function (key) {
    delete pinned[key];
    renderDimensions();
  };

  function generateAll() {
    var prompt = document.getElementById('prompt').value;
    var neg = document.getElementById('neg').value;
    var seed = parseInt(document.getElementById('seed').value) || 424242;
    var width = parseInt(document.getElementById('width').value) || 768;
    var height = parseInt(document.getElementById('height').value) || 1024;

    document.querySelectorAll('.dim-row').forEach(function (row) {
      var cards = row.querySelectorAll('.img-card');
      cards.forEach(function (card) {
        if (card.querySelector('img').src) return;
        card.classList.add('generating');
        var spinner = document.createElement('div');
        spinner.className = 'spinner';
        card.appendChild(spinner);

        var label = card.querySelector('.lbl b').textContent;
        var params = {
          prompt: prompt, negative_prompt: neg,
          model_title: pinned.model || '', model_short: label,
          sampler: pinned.sampler || 'DPM++ SDE',
          steps: parseInt(pinned.steps) || 12,
          cfg: parseInt(pinned.cfg) || 2,
          lora: pinned.lora || '', lora_short: label,
          seed: seed, width: width, height: height
        };
        fetch('/api/generate', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(params)
        }).then(function (r) { return r.json(); })
          .then(function (data) {
            if (data.url) card.querySelector('img').src = data.url;
            card.classList.remove('generating');
            var sp = card.querySelector('.spinner');
            if (sp) sp.remove();
          }).catch(function () {
            card.classList.remove('generating');
            var sp = card.querySelector('.spinner');
            if (sp) sp.remove();
          });
      });
    });
  }

  window.openLb = function (src) {
    document.getElementById('lb-img').src = src;
    document.getElementById('lb').classList.add('on');
  };
  window.closeLb = function () {
    document.getElementById('lb').classList.remove('on');
  };
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') closeLb();
  });

  loadConfig();
})();
