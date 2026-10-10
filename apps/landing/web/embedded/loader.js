(function () {
  'use strict';
  const root = new URL(window.__CB_ASSET_ROOT, document.baseURI);
  const originalFetch = window.fetch.bind(window);
  const assets = window.__CB_ASSETS = window.__CB_ASSETS || Object.create(null);
  function keyFor(input) {
    const url = new URL(typeof input === 'string' || input instanceof URL ? input : input.url, document.baseURI);
    if (url.protocol !== root.protocol || url.host !== root.host || !url.pathname.startsWith(root.pathname)) return null;
    return decodeURIComponent(url.pathname.slice(root.pathname.length));
  }
  function bytes(encoded) {
    const raw = atob(encoded), result = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) result[i] = raw.charCodeAt(i);
    return result;
  }
  window.cbAssetUrl = function (path) {
    const entry = assets[keyFor(path)];
    if (!entry) throw new Error('Required packaged image is missing: ' + path);
    return 'data:' + entry[0] + ';base64,' + entry[1];
  };
  window.cbInstallImports = function () {
    const map = document.createElement('script');
    map.type = 'importmap';
    map.textContent = JSON.stringify({ imports: window.__CB_IMPORTS });
    document.head.appendChild(map);
  };
  window.fetch = function (input, init) {
    let key;
    try { key = keyFor(input); } catch (error) { return Promise.reject(error); }
    const method = String(init?.method || (typeof input === 'object' && input.method) || 'GET').toUpperCase();
    const signal = init?.signal || (typeof input === 'object' && input.signal);
    if (signal?.aborted) return Promise.reject(new DOMException('Request aborted', 'AbortError'));
    const entry = key === null ? null : assets[key];
    if (entry && (method === 'GET' || method === 'HEAD')) {
      return Promise.resolve(new Response(method === 'HEAD' ? null : bytes(entry[1]), {
        status: 200, headers: { 'Content-Type': entry[0], 'X-Cobblestone-Asset': key }
      }));
    }
    const url = new URL(typeof input === 'string' || input instanceof URL ? input : input.url, document.baseURI);
    if (url.protocol === 'file:') return Promise.reject(new Error('Required packaged asset is missing: ' + (key || url.pathname.split('/').pop())));
    return originalFetch(input, init);
  };
})();
