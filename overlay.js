'use strict';
let lastPicture = '';
const scene = document.getElementById('scene');
const status = document.getElementById('status');
const reduced = matchMedia('(prefers-reduced-motion: reduce)');
let forcedReduced = false;
const compactLabels = {think:'处理中',code:'编辑中',read:'阅读中',search:'查找中',web:'查资料',build:'构建中',test:'测试中',install:'准备中',git:'同步中',pull:'下载中',python:'运行中',shell:'运行中',paint:'绘图中',ask:'等你回答',plan:'整理中',compact:'整理中',agent:'协作中',party:'已完成',bug:'出错了',fire:'出错了',idle:'待命',sleep:'休息中'};
function motion() { const svg=scene.querySelector('svg'); if (svg) (reduced.matches || forcedReduced) ? svg.pauseAnimations() : svg.unpauseAnimations(); }
window.setReducedMotion = flag => { forcedReduced=!!flag; motion(); };
reduced.addEventListener('change', motion);
window.updateState = state => {
  const compact = state.layout === 'portrait';
  document.body.classList.toggle('compact', compact);
  const key = JSON.stringify([state.layout, state.kind, state.crates, state.helpers]);
  if (key !== lastPicture) { scene.innerHTML = compact ? PixelArtwork.portrait(state.kind) : PixelArtwork.render(state); lastPicture = key; motion(); }
  status.textContent = compact ? compactLabels[state.kind] || '工作中' : state.label || '等你发话';
  status.title = status.textContent;
};
window.updateState({ kind:'idle', label:'正在连接当前 Codex 会话', crates:[], helpers:[] });
