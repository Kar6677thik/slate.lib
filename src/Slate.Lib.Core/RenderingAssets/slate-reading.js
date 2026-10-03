(() => {
  'use strict';
  const headings = Array.from(document.querySelectorAll('main [data-slate-heading]')).slice(0, 1024);
  const levels = [];
  const outline = headings.map(h => {
    const level = Number(h.tagName.substring(1));
    while (levels.length && levels[levels.length - 1] >= level) levels.pop();
    const depth = levels.length; levels.push(level);
    return { id: h.id, text: h.textContent, level, depth };
  });
  let active = headings[0]?.id ?? '';
  let scheduled = false;
  let positions = [];
  function measure() {
    positions = headings.filter(h => h.getClientRects().length).map(h => ({ id: h.id, top: h.getBoundingClientRect().top + window.scrollY }));
    track();
  }
  function track() {
    scheduled = false;
    // Scrolling uses cached geometry and binary search, not a layout read per heading per frame.
    let low = 0, high = positions.length;
    while (low < high) { const mid = (low + high) >>> 1; if (positions[mid].top <= window.scrollY + 90) low = mid + 1; else high = mid; }
    active = positions[Math.max(0, low - 1)]?.id ?? '';
  }
  function jump(id) {
    const target = headings.find(h => h.id === id); if (!target) return;
    for (let parent = target.parentElement; parent; parent = parent.parentElement) if (parent.tagName === 'DETAILS') parent.open = true;
    target.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
    active = id;
  }
  const key = 'slate-reading:' + document.body.dataset.note;
  let saveTimer;
  function save() {
    try {
      sessionStorage.setItem(key, JSON.stringify({ y: window.scrollY, closed: Array.from(document.querySelectorAll('details.callout')).map(d => !d.open) }));
    } catch { /* Sandboxed readers may disable session storage. */ }
  }
  window.addEventListener('scroll', () => {
    if (!scheduled) { scheduled = true; requestAnimationFrame(track); }
    clearTimeout(saveTimer); saveTimer = setTimeout(save, 300);
  }, { passive: true });
  document.addEventListener('toggle', () => { save(); measure(); }, true);
  window.addEventListener('pagehide', save);
  window.addEventListener('load', () => {
    try {
      const state = JSON.parse(sessionStorage.getItem(key) ?? 'null');
      if (state) {
        document.querySelectorAll('details.callout').forEach((d, i) => { if (typeof state.closed?.[i] === 'boolean') d.open = !state.closed[i]; });
        if (!location.hash && Number.isFinite(state.y)) window.scrollTo(0, state.y);
      }
    } catch { }
    if (location.hash) jump(decodeURIComponent(location.hash.substring(1)));
    measure();
  });
  document.addEventListener('click', event => {
    const anchor = event.target.closest?.('a[href^="#"]');
    if (anchor) { event.preventDefault(); jump(decodeURIComponent(anchor.getAttribute('href').substring(1))); }
  });
  window.slateReading = {
    outline: () => outline,
    active: () => active,
    jump,
    step: delta => { const i = headings.findIndex(h => h.id === active); jump(headings[Math.max(0, Math.min(headings.length - 1, i + delta))]?.id); },
    link: () => active ? '[[id:' + document.body.dataset.note + '#' + active + ']]' : '',
    save
  };
  if (typeof ResizeObserver === 'function') new ResizeObserver(measure).observe(document.querySelector('main'));
  window.addEventListener('resize', measure);
  measure();
})();
