/* TarkovAutoShade UI v2.0 — 交互逻辑（高保真原型） */
"use strict";

const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];

/* ================= Toast ================= */
const toasts = $("#toasts");
function toast(msg, type = "info") {
  if (toasts.children.length >= 3) toasts.firstChild.remove();
  const el = document.createElement("div");
  el.className = `toast ${type}`;
  el.textContent = msg;
  toasts.appendChild(el);
  setTimeout(() => {
    el.classList.add("leaving");
    setTimeout(() => el.remove(), 200);
  }, 4000);
}

/* ================= 日志 ================= */
const logList = $("#logList");
function now() {
  const d = new Date();
  return [d.getHours(), d.getMinutes(), d.getSeconds()].map(n => String(n).padStart(2, "0")).join(":");
}
function addLog(type, html) {
  const el = document.createElement("div");
  el.className = `log-item ${type}`;
  el.innerHTML = `<i class="ico"></i><span class="t">${now()}</span><span class="msg">${html}</span>`;
  logList.prepend(el);
  while (logList.children.length > 30) logList.lastChild.remove();
}
$("#clearLogBtn").addEventListener("click", () => { logList.innerHTML = ""; toast("日志已清空"); });

/* ================= 导航 ================= */
$$(".nav-item[data-view]").forEach(btn => {
  btn.addEventListener("click", () => {
    $$(".nav-item[data-view]").forEach(b => b.classList.toggle("active", b === btn));
    $$(".view").forEach(v => v.classList.toggle("active", v.id === `view-${btn.dataset.view}`));
    requestAnimationFrame(() => { drawCurve(); drawCurveSide(); drawHisto(); });
  });
});

/* ================= 状态（滤镜开关） ================= */
let filterOn = true;
const statusPill = $("#statusPill"), statusPillText = $("#statusPillText");
const runStateCard = $("#runStateCard"), runStateTitle = $("#runStateTitle"), runStateSub = $("#runStateSub");
const toggleBtn = $("#toggleFilterBtn"), toggleText = $("#toggleFilterText");

function renderFilterState() {
  statusPill.classList.toggle("on", filterOn);
  statusPillText.textContent = filterOn ? "滤镜运行中" : "滤镜已关闭";
  runStateCard.classList.toggle("on", filterOn);
  runStateTitle.textContent = filterOn ? "滤镜已应用" : "滤镜已关闭";
  runStateSub.textContent = filterOn ? "正在监听截图目录 · 上次分析 14:32:07" : "监听继续 · 新截图将自动重新开启";
  toggleText.textContent = filterOn ? "关闭滤镜" : "开启滤镜";
  toggleBtn.classList.toggle("primary", !filterOn);
}
function toggleFilter(src = "按钮") {
  filterOn = !filterOn;
  renderFilterState();
  addLog(filterOn ? "ok" : "warn", `<b>${filterOn ? "滤镜已开启" : "滤镜已关闭"}</b>（${src}）`);
  toast(filterOn ? "滤镜已开启，曲线平滑应用中" : "滤镜已关闭，继续监听截图", filterOn ? "ok" : "warn");
}
toggleBtn.addEventListener("click", () => toggleFilter("按钮"));
renderFilterState();

/* ================= 全局热键（原型内模拟 F8） ================= */
let hotkey = "F8";
function matchHotkey(e) {
  const parts = [];
  if (e.ctrlKey) parts.push("Ctrl");
  if (e.altKey) parts.push("Alt");
  if (e.shiftKey) parts.push("Shift");
  if (!["Control", "Alt", "Shift", "Meta"].includes(e.key)) parts.push(e.key.toUpperCase());
  return parts.join("+") === hotkey;
}
window.addEventListener("keydown", e => {
  if (listening) return; // 热键捕获模式另处理
  if (matchHotkey(e)) { e.preventDefault(); toggleFilter("快捷键 " + hotkey); }
});

/* ================= 对比预览 ================= */
const stage = $("#previewStage"), afterWrap = $("#afterWrap"), divider = $("#divider");
let dragging = false;
function setSplit(pct) {
  pct = Math.max(2, Math.min(98, pct));
  afterWrap.style.clipPath = `inset(0 0 0 ${pct}%)`;
  divider.style.left = pct + "%";
}
function pctFromEvent(e) {
  const r = stage.getBoundingClientRect();
  const x = (e.touches ? e.touches[0].clientX : e.clientX) - r.left;
  return (x / r.width) * 100;
}
divider.addEventListener("pointerdown", e => { dragging = true; divider.setPointerCapture(e.pointerId); });
window.addEventListener("pointermove", e => { if (dragging) setSplit(pctFromEvent(e)); });
window.addEventListener("pointerup", () => (dragging = false));
stage.addEventListener("pointerdown", e => { if (e.target !== divider) setSplit(pctFromEvent(e)); });
setSplit(50);

$("#compareSeg").addEventListener("click", e => {
  const b = e.target.closest("button"); if (!b) return;
  $$("#compareSeg button").forEach(x => x.classList.toggle("on", x === b));
  const mode = b.dataset.mode;
  if (mode === "split") setSplit(50);
  else setSplit(mode === "before" ? 98 : 2);
});

/* ================= 参数滑杆 ================= */
const ALGO = [
  { id: "shadow",    name: "暗部可见度",     def: 70, tip: "数值越高，暗场允许的提亮范围越大" },
  { id: "highlight", name: "高光保护",       def: 76, tip: "检测到高光时越会抑制提亮" },
  { id: "color",     name: "色偏校正",       def: 72, tip: "按 RGB 均值轻量通道平衡" },
  { id: "indoor",    name: "室内柔和",       def: 72, tip: "室内特征下降低中间调冲击" },
  { id: "guard",     name: "过亮 / 夜视保护", def: 88, tip: "保护强光、逆光与偏绿夜视场景" },
  { id: "black",     name: "黑位 / 去灰",    def: 56, tip: "提亮后恢复黑色分离度" },
  { id: "strength",  name: "最大调整强度",   def: 82, tip: "生成曲线与原始曲线的最大混合比例" },
];
const FINE = [
  { id: "exposure", name: "亮度微调",   def: 0, min: -20, max: 20, tip: "整体曝光倾向偏移" },
  { id: "contrast", name: "对比度微调", def: 0, min: -20, max: 20, tip: "明暗分离偏移" },
  { id: "warmth",   name: "暖色舒适度", def: 0, min: -20, max: 20, tip: "正偏暖，负偏冷" },
  { id: "sat",      name: "色彩浓度",   def: 0, min: -20, max: 20, tip: "色彩相对亮度的距离" },
];
const PRESETS = {
  auto:      { label: "自动分析",  v: { shadow: 70, highlight: 76, color: 72, indoor: 72, guard: 88, black: 56, strength: 82, exposure: 0, contrast: 0, warmth: 0, sat: 0 } },
  natural:   { label: "自然中性",  v: { shadow: 55, highlight: 82, color: 60, indoor: 55, guard: 90, black: 48, strength: 60, exposure: 0, contrast: 0, warmth: 0, sat: 0 } },
  indoor:    { label: "室内柔和",  v: { shadow: 84, highlight: 70, color: 72, indoor: 90, guard: 85, black: 50, strength: 78, exposure: 4, contrast: -6, warmth: 2, sat: 0 } },
  nv:        { label: "夜视护眼",  v: { shadow: 40, highlight: 92, color: 78, indoor: 60, guard: 98, black: 44, strength: 55, exposure: -4, contrast: -2, warmth: 0, sat: -4 } },
  highlight: { label: "高光保护",  v: { shadow: 62, highlight: 96, color: 70, indoor: 66, guard: 94, black: 58, strength: 70, exposure: -2, contrast: 2, warmth: 0, sat: 0 } },
  custom:    { label: "自定义预设", v: null },
};
const state = {};
let activePreset = "auto";

function sliderRow(cfg) {
  const min = cfg.min ?? 0, max = cfg.max ?? 100;
  const bipolar = min < 0;
  return `
  <div class="slider-row">
    <div class="slider-head">
      <span class="name">${cfg.name}<span class="q" title="${cfg.tip}">?</span></span>
      <span class="val ${bipolar ? "zero" : ""}" id="val-${cfg.id}">${cfg.def}</span>
    </div>
    <input type="range" class="${bipolar ? "bipolar" : ""}" id="sl-${cfg.id}"
      min="${min}" max="${max}" value="${cfg.def}" step="1">
  </div>`;
}
$("#algoSliders").innerHTML = ALGO.map(sliderRow).join("");
$("#fineSliders").innerHTML = FINE.map(sliderRow).join("");

function syncSliderFill(el) {
  const min = +el.min, max = +el.max, v = +el.value;
  el.style.setProperty("--fill", ((v - min) / (max - min)) * 100 + "%");
}
function readParams() {
  [...ALGO, ...FINE].forEach(c => (state[c.id] = +$(`#sl-${c.id}`).value));
}
function applyParams(v) {
  [...ALGO, ...FINE].forEach(c => {
    if (v[c.id] === undefined) return;
    const el = $(`#sl-${c.id}`);
    el.value = v[c.id];
    syncSliderFill(el);
    const val = $(`#val-${c.id}`);
    val.textContent = v[c.id];
    val.classList.toggle("zero", c.min < 0 && +v[c.id] === 0);
  });
  readParams();
  drawCurveSide(); drawCurve(); updateMetrics();
}
[...ALGO, ...FINE].forEach(c => {
  const el = $(`#sl-${c.id}`);
  syncSliderFill(el);
  el.addEventListener("input", () => {
    $(`#val-${c.id}`).textContent = el.value;
    $(`#val-${c.id}`).classList.toggle("zero", c.min < 0 && +el.value === 0);
    syncSliderFill(el);
    readParams(); drawCurveSide(); drawCurve(); updateMetrics();
    if (activePreset !== "custom") setPreset("custom", true);
  });
});
function setPreset(key, silent = false) {
  activePreset = key;
  $$("#presetGrid .preset-card").forEach(c => c.classList.toggle("on", c.dataset.preset === key));
  $("#topPreset").textContent = PRESETS[key].label;
  if (PRESETS[key].v) applyParams(PRESETS[key].v);
  if (!silent) {
    addLog("info", `预设切换为 <b>${PRESETS[key].label}</b>`);
    toast(`已应用预设「${PRESETS[key].label}」`, "ok");
  }
}
$("#presetGrid").addEventListener("click", e => {
  const card = e.target.closest(".preset-card");
  if (card) setPreset(card.dataset.preset);
});
$("#resetBtn").addEventListener("click", () => {
  setPreset("auto");
  toast("已恢复默认数值", "ok");
});
readParams();

/* ================= Gamma 曲线绘制 ================= */
function toneCurve(x, p, ch) {
  // 归一化输入 x∈[0,1]，输出 y∈[0,1]
  const shadowLift = (p.shadow / 100) * 0.28 * Math.pow(1 - x, 2.2);
  const blackAnchor = (p.black / 100) * 0.10 * Math.pow(Math.max(0, 0.25 - x) / 0.25, 2) * -1;
  const hlComp = (p.highlight / 100) * 0.12 * Math.pow(Math.max(0, x - 0.72) / 0.28, 2);
  const expo = (p.exposure / 20) * 0.10;
  const contra = (p.contrast / 20) * 0.16 * (x - 0.5);
  let y = x + shadowLift + blackAnchor - hlComp + expo + contra;
  // 通道差异：色偏校正向中间收拢，暖色偏移 R+/B-
  const warm = (p.warmth / 20) * 0.05;
  if (ch === "r") y += warm + (p.color / 100) * 0.012;
  if (ch === "b") y -= warm * 0.9;
  const mix = p.strength / 100;
  y = x * (1 - mix) + y * mix;
  return Math.max(0, Math.min(1, y));
}
function drawCurveTo(canvas, p, opts = {}) {
  const dpr = window.devicePixelRatio || 1;
  const w = canvas.clientWidth, h = +canvas.getAttribute("height");
  if (!w) return;
  canvas.width = w * dpr; canvas.height = h * dpr;
  const ctx = canvas.getContext("2d");
  ctx.scale(dpr, dpr);
  ctx.clearRect(0, 0, w, h);
  const pad = 6, iw = w - pad * 2, ih = h - pad * 2;
  // 网格
  ctx.strokeStyle = getCSS("--grid-line"); ctx.lineWidth = 1;
  for (let i = 1; i < 4; i++) {
    const gx = pad + (iw / 4) * i, gy = pad + (ih / 4) * i;
    ctx.beginPath(); ctx.moveTo(gx, pad); ctx.lineTo(gx, pad + ih); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(pad, gy); ctx.lineTo(pad + iw, gy); ctx.stroke();
  }
  // 参考对角线
  ctx.strokeStyle = getCSS("--ref-line"); ctx.setLineDash([4, 4]);
  ctx.beginPath(); ctx.moveTo(pad, pad + ih); ctx.lineTo(pad + iw, pad); ctx.stroke();
  ctx.setLineDash([]);
  // RGB 曲线
  const chans = [["r", "--ch-r"], ["g", "--ch-g"], ["b", "--ch-b"]];
  chans.forEach(([ch, css], idx) => {
    ctx.strokeStyle = getCSS(css);
    ctx.lineWidth = idx === 1 ? 2 : 1.4;
    ctx.globalAlpha = idx === 1 ? 1 : 0.85;
    ctx.beginPath();
    for (let i = 0; i <= 128; i++) {
      const x = i / 128, y = toneCurve(x, p, ch);
      const px = pad + x * iw, py = pad + (1 - y) * ih;
      i === 0 ? ctx.moveTo(px, py) : ctx.lineTo(px, py);
    }
    ctx.stroke();
  });
  ctx.globalAlpha = 1; ctx.lineWidth = 1;
  if (opts.tag) {
    const gamma = 1 + (p.shadow / 100) * 0.9 + (p.exposure / 20) * 0.2;
    opts.tag.textContent = `等效 γ ${gamma.toFixed(2)}`;
  }
}
function getCSS(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }
const curveCanvas = $("#curveCanvas"), curveSide = $("#curveCanvasSide");
function drawCurve() { drawCurveTo(curveCanvas, state, { tag: $("#gammaTag") }); }
function drawCurveSide() { drawCurveTo(curveSide, state); }

/* ================= 直方图 ================= */
const histoCanvas = $("#histoCanvas");
let histoSeed = 0;
function drawHisto() {
  const dpr = window.devicePixelRatio || 1;
  const w = histoCanvas.clientWidth, h = +histoCanvas.getAttribute("height");
  if (!w) return;
  histoCanvas.width = w * dpr; histoCanvas.height = h * dpr;
  const ctx = histoCanvas.getContext("2d");
  ctx.scale(dpr, dpr);
  ctx.clearRect(0, 0, w, h);
  const bins = 64, bw = w / bins;
  for (let i = 0; i < bins; i++) {
    const x = i / bins;
    // 模拟夜间室内分布：暗部集中 + 高光小峰
    const dark = Math.exp(-Math.pow((x - 0.12 - histoSeed * 0.02) / 0.13, 2)) * 0.9;
    const mid = Math.exp(-Math.pow((x - 0.42) / 0.2, 2)) * 0.35;
    const hi = Math.exp(-Math.pow((x - 0.9) / 0.05, 2)) * 0.28;
    const v = Math.min(1, dark + mid + hi + 0.02);
    ctx.fillStyle = x < 0.25 ? "rgba(78,156,255,.55)" : x > 0.8 ? "rgba(240,160,60,.6)" : "rgba(157,169,182,.4)";
    ctx.fillRect(i * bw + 0.5, h - v * (h - 6), bw - 1.5, v * (h - 6));
  }
}

/* ================= 分析模拟 ================= */
function clamp(v, a, b) { return Math.max(a, Math.min(b, v)); }
function analyze(auto = false) {
  histoSeed = Math.random() * 2 - 1;
  const p10 = Math.round(clamp(42 + histoSeed * 14 + (Math.random() * 8 - 4), 12, 90));
  const p50 = Math.round(clamp(96 + histoSeed * 26 + (Math.random() * 10 - 5), 40, 170));
  const p95 = Math.round(clamp(231 - histoSeed * 8 + (Math.random() * 6 - 3), 180, 252));
  const dr = p95 - p10;
  const set = (id, v, max = 255) => { $(id).textContent = v; };
  set("#rP10", p10); set("#rP50", p50); set("#rP95", p95); set("#rDR", dr);
  $("#bP10").style.width = (p10 / 255) * 100 + "%";
  $("#bP50").style.width = (p50 / 255) * 100 + "%";
  $("#bP95").style.width = (p95 / 255) * 100 + "%";
  $("#bDR").style.width = (dr / 255) * 100 + "%";
  const scene = p50 < 80 ? "夜间室内" : p50 < 130 ? "室内" : "明亮室外";
  $("#sceneTag").textContent = "场景：" + scene;
  drawHisto();
  updateMetrics();
  addLog("info", `分析完成 <b>${scene}</b> · P10 ${p10} / P50 ${p50} / P95 ${p95} · 动态范围 ${dr}`);
  if (!filterOn) { filterOn = true; renderFilterState(); addLog("ok", "<b>检测到新截图，滤镜自动重新开启</b>"); }
  toast(auto ? "检测到新截图，已自动分析并应用" : "分析完成，曲线已平滑应用", "ok");
}
$("#analyzeBtn").addEventListener("click", () => analyze(false));

function updateMetrics() {
  const gamma = 1 + (state.shadow / 100) * 0.9 + (state.exposure / 20) * 0.2;
  $("#mGamma").textContent = gamma.toFixed(2);
  $("#mBri").textContent = (state.exposure >= 0 ? "+" : "") + Math.round(state.exposure + state.shadow * 0.4);
  $("#mCon").textContent = (state.contrast >= 0 ? "+" : "") + Math.round(state.contrast + (state.black - 50) * 0.24);
}

/* ================= 设备页 ================= */
$("#dispModeSeg").addEventListener("click", e => {
  const b = e.target.closest("button"); if (!b) return;
  $$("#dispModeSeg button").forEach(x => x.classList.toggle("on", x === b));
  const multi = b.dataset.mode === "multi";
  $("#singlePick").style.display = multi ? "none" : "";
  $("#multiPick").style.display = multi ? "" : "none";
  $("#ddcState").className = "hw-state " + (multi ? "off" : "ok");
  $("#ddcState").textContent = multi
    ? "⚠ 多屏模式下硬件调整不可用，请分别调整各显示器"
    : "✓ 主屏支持 DDC/CI，可直接调整硬件亮度与对比度";
  $("#hwBri").disabled = $("#hwCon").disabled = multi;
  toast(multi ? "已切换到多屏模式" : "已切换到单屏模式");
});
$$(".monitor-card input").forEach(c => c.addEventListener("change", () => {
  c.closest(".monitor-card").classList.toggle("checked", c.checked);
  const any = $$(".monitor-card input").some(x => x.checked);
  if (!any) { c.checked = true; c.closest(".monitor-card").classList.add("checked"); toast("至少勾选一个显示器", "warn"); }
}));
$("#refreshMonBtn").addEventListener("click", () => { toast("已重新扫描显示器（2 台）", "ok"); addLog("info", "显示器列表已刷新：检测到 <b>2</b> 台显示器"); });

[["#hwBri", "#hwBriVal"], ["#hwCon", "#hwConVal"]].forEach(([s, v]) => {
  const el = $(s); syncSliderFill(el);
  el.addEventListener("input", () => { $(v).textContent = el.value; syncSliderFill(el); });
});

/* 目录 */
$("#addFolderBtn").addEventListener("click", () => {
  const el = document.createElement("div");
  el.className = "folder-item";
  el.innerHTML = `<span class="tag">自定义</span><span class="path">D:\\Games\\EFT\\Screenshots</span><button class="x" title="移除">×</button>`;
  $("#folderList").appendChild(el);
  toast("已添加监听目录", "ok");
});
$("#folderList").addEventListener("click", e => {
  const x = e.target.closest(".x"); if (!x) return;
  x.closest(".folder-item").remove();
  toast("已移除监听目录", "warn");
});
$("#rediscoverBtn").addEventListener("click", () => { toast("已重新自动发现原版 / 竞技场目录", "ok"); addLog("info", "截图目录重新发现完成：原版 + 竞技场"); });

/* 进程 */
$("#procSelect").addEventListener("change", e => {
  const v = e.target.value; if (!v) return;
  const chip = document.createElement("span");
  chip.className = "proc-chip";
  chip.innerHTML = `${v} <button class="x">×</button>`;
  $("#procChips").appendChild(chip);
  e.target.value = "";
  toast(`已添加侦听进程 ${v}`, "ok");
});
$("#procChips").addEventListener("click", e => {
  const x = e.target.closest(".x"); if (!x) return;
  x.closest(".proc-chip").remove();
});
$("#procWatchChk").addEventListener("change", e => {
  toast(e.target.checked ? "已启用：切出游戏自动关闭滤镜" : "已停用自动启停", e.target.checked ? "ok" : "warn");
});

/* ================= 热键捕获 ================= */
let listening = false;
const hotkeyBtn = $("#hotkeyBtn");
hotkeyBtn.addEventListener("click", () => {
  listening = true;
  hotkeyBtn.classList.add("listening");
  hotkeyBtn.textContent = "按下新热键…";
});
window.addEventListener("keydown", e => {
  if (!listening) return;
  e.preventDefault();
  listening = false;
  hotkeyBtn.classList.remove("listening");
  if (e.key === "Escape") { hotkeyBtn.textContent = hotkey; return; }
  const parts = [];
  if (e.ctrlKey) parts.push("Ctrl");
  if (e.altKey) parts.push("Alt");
  if (e.shiftKey) parts.push("Shift");
  if (!["Control", "Alt", "Shift", "Meta"].includes(e.key)) parts.push(e.key.toUpperCase());
  if (parts.length) {
    hotkey = parts.join("+");
    hotkeyBtn.textContent = hotkey;
    $("#topHotkey").textContent = hotkey;
    toast(`全局热键已设置为 ${hotkey}`, "ok");
    addLog("info", `全局热键修改为 <b>${hotkey}</b>`);
  } else hotkeyBtn.textContent = hotkey;
}, true);

/* ================= 关于弹窗 ================= */
const aboutModal = $("#aboutModal");
$("#aboutBtn").addEventListener("click", () => aboutModal.classList.add("open"));
$("#aboutBtn2").addEventListener("click", () => aboutModal.classList.add("open"));
$("#aboutClose").addEventListener("click", () => aboutModal.classList.remove("open"));
aboutModal.addEventListener("click", e => { if (e.target === aboutModal) aboutModal.classList.remove("open"); });

/* ================= 时钟 ================= */
function tick() {
  const d = new Date(), p = n => String(n).padStart(2, "0");
  $("#clock").textContent = `${d.getFullYear()}.${p(d.getMonth() + 1)}.${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}
setInterval(tick, 1000); tick();

/* ================= 初始化 ================= */
window.addEventListener("resize", () => { drawCurve(); drawCurveSide(); drawHisto(); });
applyParams(PRESETS.auto.v);
drawHisto();
addLog("ok", "<b>监听已启动</b> · 原版 + 竞技场截图目录");
addLog("info", "自动分析完成 <b>夜间室内</b> · 曲线已应用");
