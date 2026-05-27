// item-ui.js — DOM glue for ItemDesignerApp (Codex-transpiled).
// This file reads the exported functions from item-app.js and wires
// them to the browser DOM. All data and prompt logic lives in Codex.
(function () {
  var ITEMS = item_categories();
  var RARITIES = rarity_tiers();
  var MATERIALS = material_types();
  var sel = { item: 0, rarity: 2, material: 0 };

  function renderPills(id, items, key, labelFn, colorFn) {
    var el = document.getElementById(id);
    el.innerHTML = items.map(function (it, i) {
      var active = i === sel[key] ? ' active' : '';
      var style = colorFn
        ? ' style="border-color:' + colorFn(it) + (i === sel[key] ? ';color:' + colorFn(it) : '') + '"'
        : '';
      return '<span class="pill' + active + '"' + style + ' onclick="pickItem(\'' + key + '\',' + i + ')">' + labelFn(it) + '</span>';
    }).join('');
  }

  window.pickItem = function (key, idx) {
    sel[key] = idx;
    renderControls();
    generate();
  };

  function renderControls() {
    renderPills('item-type', ITEMS, 'item', function (it) { return it.name; }, null);
    renderPills('rarity', RARITIES, 'rarity', function (r) { return r.name; }, function (r) { return r.color; });
    renderPills('material', MATERIALS, 'material', function (m) { return m.name; }, null);
  }

  function generate() {
    var item = ITEMS[sel.item];
    var rarity = RARITIES[sel.rarity];
    var mat = MATERIALS[sel.material];
    var extra = document.getElementById('prompt').value;
    var seed = parseInt(document.getElementById('seed').value) || 424242;
    var prompt = build_item_prompt(item, mat, rarity, extra);
    var neg = default_negative();

    var params = {
      prompt: prompt, negative_prompt: neg,
      model_title: '', model_short: 'default',
      sampler: 'DPM++ SDE', steps: 12, cfg: 2,
      lora: '', lora_short: 'none',
      seed: seed, width: 768, height: 768
    };

    var grid = document.getElementById('results');
    var card = document.createElement('div');
    card.className = 'result-card';
    card.innerHTML = '<div class="spinner"></div>'
      + '<img src="" style="aspect-ratio:1/1">'
      + '<div class="info"><b>' + item.name + '</b> — ' + mat.name
      + ' <span class="rarity-badge" style="background:' + rarity.color + '22;color:' + rarity.color + '">' + rarity.name + '</span></div>';
    grid.prepend(card);
    card.addEventListener('click', function () {
      var img = card.querySelector('img');
      if (img.src) openLb(img.src);
    });

    fetch('/api/generate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(params)
    }).then(function (resp) { return resp.json(); })
      .then(function (data) {
        if (data.url) card.querySelector('img').src = data.url;
        card.querySelector('.spinner').style.display = 'none';
      }).catch(function () {
        card.querySelector('.spinner').style.display = 'none';
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

  renderControls();
})();
