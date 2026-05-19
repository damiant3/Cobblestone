function switchTab(name) {
  document.querySelectorAll('.tab-panel').forEach(function(p) { p.classList.remove('active'); });
  document.querySelectorAll('.tab-btn').forEach(function(b) { b.classList.remove('active'); });
  var panel = document.getElementById('tab-' + name);
  if (panel) panel.classList.add('active');
  var btn = document.querySelector('.tab-btn[data-tab="' + name + '"]');
  if (btn) btn.classList.add('active');
  location.hash = name;
  if (name === 'games' && soundEnabled) startAmbient();
  else stopAmbient();
}

function filterGames(category) {
  document.querySelectorAll('.filter-pill').forEach(function(p) {
    p.classList.toggle('active', p.dataset.cat === category);
  });
  var search = document.getElementById('game-search').value.toLowerCase();
  var shown = 0, total = 0;
  document.querySelectorAll('.game-card').forEach(function(card) {
    total++;
    var catMatch = category === 'all' || card.dataset.category === category;
    var nameMatch = !search || card.dataset.name.indexOf(search) !== -1;
    var visible = catMatch && nameMatch;
    card.style.display = visible ? '' : 'none';
    if (visible) shown++;
  });
  document.getElementById('game-count').textContent = 'Showing ' + shown + ' of ' + total + ' games';
}

(function() {
  var hash = location.hash.replace('#', '');
  if (hash === 'games' || hash === 'status') switchTab(hash);
})();

setInterval(function() {
  if (!document.getElementById('tab-status').classList.contains('active')) return;
  location.reload();
}, 30000);

// ── Audio system ──
var previewAudio = null;
var soundEnabled = false;
var audioUnlocked = false;
var audioCache = {};
var ambientMusic = null;

function previewPlay(gameId) {
  if (!soundEnabled || !audioUnlocked) return;
  stopAmbient();
  previewStop();
  var src = '/assets/games/previews/' + gameId + '.wav';
  if (audioCache[gameId]) {
    previewAudio = audioCache[gameId];
    previewAudio.currentTime = 0;
  } else {
    previewAudio = new Audio(src);
    previewAudio.volume = 0.5;
    audioCache[gameId] = previewAudio;
  }
  previewAudio.play().catch(function(){});
}

function previewStop() {
  if (previewAudio) {
    previewAudio.pause();
    previewAudio.currentTime = 0;
    previewAudio = null;
  }
  startAmbient();
}

function toggleSound() {
  audioUnlocked = true;
  soundEnabled = !soundEnabled;
  var btn = document.getElementById('sound-toggle');
  if (btn) btn.textContent = soundEnabled ? '🔊 Sound On' : '🔇 Sound Off';
  if (soundEnabled) {
    startAmbient();
  } else {
    previewStop();
    stopAmbient();
  }
}

function startAmbient() {
  if (!ambientMusic) ambientMusic = document.getElementById('ambient-music');
  if (ambientMusic && soundEnabled && audioUnlocked) {
    ambientMusic.volume = 0.15;
    ambientMusic.play().catch(function(){});
  }
}

function stopAmbient() {
  if (ambientMusic) ambientMusic.pause();
}

function fadeAmbient(targetVol) {
  if (!ambientMusic || !soundEnabled) return;
  ambientMusic.volume = targetVol;
}
