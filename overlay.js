'use strict';
let lastPicture = '';
const scene = document.getElementById('scene');
const status = document.getElementById('status');
const reduced = matchMedia('(prefers-reduced-motion: reduce)');
let forcedReduced = false;
function motion() { const svg=scene.querySelector('svg'); if (svg) (reduced.matches || forcedReduced) ? svg.pauseAnimations() : svg.unpauseAnimations(); }
window.setReducedMotion = flag => { forcedReduced=!!flag; motion(); };
reduced.addEventListener('change', motion);
window.updateState = state => {
  const key = JSON.stringify([state.kind, state.crates, state.helpers]);
  if (key !== lastPicture) { scene.innerHTML = PixelArtwork.render(state); lastPicture = key; motion(); }
  status.textContent = state.label || '等你发话';
  status.title = status.textContent;
};
window.updateState({ kind:'idle', label:'正在连接当前 Codex 会话', crates:[], helpers:[] });
