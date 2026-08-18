#!/usr/bin/env python3
"""라이브 판독을 1초마다 다시 읽는 페이지로 낸다.

이것은 소비자이고, 일부러 멍청한 소비자다. 채널은 씬이 바뀔 때 전량 판독을, 그 외에는 차이를
보내므로 그것을 읽는 쪽은 마지막 전량 판독을 쥐고 그 위에 차이를 얹어야 한다. 그 일을 여기서
하는 것이 요점이다: 페이지가 도착한 것만으로 화면의 현재 상태를 보일 수 있으면 채널이 충분한
것이고, 못 보이면 부족한 자리가 논쟁이 아니라 눈에 보인다.

프로토타입 저장소에서 가져왔다. 거기서는 이것이 pulse 를 읽는 유일한 수단이었다. SDK 자신의
테스트 페이지는 GAME_STATE 를 그리는데 그것은 다른 물음에 답하는 다른 채널이라, 한 화면으로
합치지 않고 별개의 탭으로 둔다.

성능 탭은 파일이 아니라 SDK 의 로컬 소켓을 직접 읽는다. 프레임 시간은 pulse 에 없고 앞으로도
없을 것이다 — 그것은 게임이 아니라 프로세스에 관한 값이고 — 소켓이 이미 듣는 쪽에 그것을
나르고 있다.

    tools/watch-readings.py [--port 8770] [--file <path to artel-pulse.jsonl>]
                            [--socket ws://127.0.0.1:17311]

그리고 http://localhost:8770 을 열고 에디터에서 게임을 플레이한다.
"""

import argparse
import http.server
import json
import os
import socketserver

DEFAULT_FILE = os.path.expanduser(
    "~/Library/Application Support/Team6203/WordVenture/artel-pulse.jsonl"
)

# ArtelTestPageManager 의 기본 websocketPort. 그 컴포넌트가 켜져 있을 때만 열려 있다.
#
# 경로 /ws 가 붙는다. ArtelWebSocketServer 가 AddWebSocketService("/ws", ...) 로 거기에만
# 서비스를 매단다. 루트로 붙으면 포트는 열려 있고 핸드셰이크만 조용히 거절당하므로, 게임이
# 돌고 있는데도 버튼이 살아나지 않는 모습이 된다.
DEFAULT_SOCKET = "ws://127.0.0.1:17311/ws"

PAGE = """<!doctype html>
<meta charset="utf-8">
<title>Artel — live readings</title>
<style>
  :root { color-scheme: light dark; }
  body { font: 13px/1.5 ui-monospace, SFMono-Regular, Menlo, monospace; margin: 0; padding: 16px; }
  h1 { font-size: 14px; margin: 0 0 4px; font-weight: 600; }
  .bar { display: flex; gap: 20px; flex-wrap: wrap; padding: 8px 0 14px; opacity: .75; }
  .cols { display: grid; grid-template-columns: 1fr 1fr; gap: 22px; align-items: start; }
  @media (max-width: 900px) { .cols { grid-template-columns: 1fr; } }
  h2 { font-size: 12px; text-transform: uppercase; letter-spacing: .07em; opacity: .6;
       margin: 0 0 8px; font-weight: 600; }
  .obj { border-top: 1px solid rgba(128,128,128,.28); padding: 7px 0; }
  .path { font-weight: 600; }
  .sel { opacity: .5; font-size: 11px; }
  .off { opacity: .45; }
  table { border-collapse: collapse; width: 100%; margin-top: 3px; }
  td { padding: 1px 8px 1px 0; vertical-align: top; }
  td.m { opacity: .7; white-space: nowrap; }
  td.v { word-break: break-all; }
  .extra td.m { opacity: .38; }
  .tag { display: inline-block; padding: 0 5px; border-radius: 3px; font-size: 11px;
         background: rgba(128,128,128,.2); margin-right: 4px; }
  .k { background: rgba(90,160,255,.28); }
  .c { background: rgba(120,200,120,.3); }
  .p { background: rgba(220,170,90,.3); }
  .none { opacity: .45; padding: 10px 0; }
  .xy { opacity: .55; font-size: 11px; }
  .n { display: flex; gap: 8px; padding: 1px 0; }
  .n b { min-width: 34px; text-align: right; opacity: .8; font-weight: 600; }
  .n span { opacity: .65; word-break: break-all; }
  .on { opacity: .45; }
  .tabs { display: flex; gap: 2px; margin: 0 0 10px; }
  .tabs button { font: inherit; padding: 5px 14px; cursor: pointer; border: 0;
                 border-radius: 4px 4px 0 0; background: rgba(128,128,128,.14); opacity: .6; }
  .tabs button.sel { background: rgba(128,128,128,.3); opacity: 1; font-weight: 600; }
  .tab { display: none; }
  .tab.sel { display: block; }
  .big { display: flex; gap: 26px; flex-wrap: wrap; padding: 6px 0 16px; }
  .big div { min-width: 92px; }
  .big b { display: block; font-size: 20px; font-weight: 600; }
  .big span { opacity: .55; font-size: 11px; }
  .warn { color: rgb(220,120,90); }
  .raw { border-top: 1px solid rgba(128,128,128,.28); padding: 8px 0; }
  .raw pre { margin: 5px 0 0; padding: 9px 11px; overflow-x: auto;
             background: rgba(128,128,128,.1); border-radius: 4px; }
  .raw .head { display: flex; gap: 14px; flex-wrap: wrap; }
  .raw .head b { font-weight: 600; }
  .whole { background: rgba(120,200,120,.3); padding: 0 5px; border-radius: 3px; }
  .bar button { font: inherit; padding: 4px 12px; cursor: pointer; border-radius: 4px;
                border: 1px solid rgba(128,128,128,.4); background: transparent; }
  .bar button:disabled { opacity: .4; cursor: default; }
</style>
<h1>Artel — live readings</h1>
<div class="bar">
  <span>씬 <b id="scene">—</b></span>
  <span>판독 <b id="reading">—</b></span>
  <span>받은 문서 <b id="docs">0</b> (전량 <b id="whole">0</b>)</span>
  <span>오브젝트 <b id="count">0</b> (꺼짐 <b id="off">0</b>)</span>
  <span id="stale"></span>
</div>
<div class="bar">
  <button id="go">판독 시작</button>
  <button id="halt">판독 종료</button>
  <span id="run">판독 <b>모름</b></span>
  <span id="quiet"></span>
  <span id="said"></span>
</div>
<div class="tabs">
  <button id="tab-pulse" class="sel" onclick="show('pulse')">판독</button>
  <button id="tab-perf" onclick="show('perf')">성능</button>
  <button id="tab-raw" onclick="show('raw')">원문</button>
</div>

<div id="pane-pulse" class="tab sel">
<div class="cols">
  <div>
    <h2>정적 값 <span style="opacity:.5;font-weight:400">— 오브젝트에 붙지 않은 것</span></h2>
    <div id="statics"></div>
    <h2 style="margin-top:22px">상태</h2><div id="state"></div>
  </div>
  <div>
    <h2>할 수 있는 것</h2><div id="acts"></div>
    <h2 style="margin-top:22px">가장 자주 움직인 것</h2><div id="noisy"></div>
  </div>
</div>
</div>

<div id="pane-perf" class="tab">
  <div class="bar"><span id="sock">소켓 —</span></div>
  <h2>프레임</h2>
  <div class="big">
    <div><b id="fps">—</b><span>fps (평균)</span></div>
    <div><b id="ms">—</b><span>ms 평균</span></div>
    <div><b id="p95">—</b><span>ms p95</span></div>
    <div><b id="worst">—</b><span>ms 최악</span></div>
    <div><b id="over">—</b><span>예산 초과</span></div>
  </div>
  <h2>CPU · GPU</h2>
  <div class="big">
    <div><b id="cpuMain">—</b><span>ms main</span></div>
    <div><b id="cpuRender">—</b><span>ms render</span></div>
    <div><b id="gpu">—</b><span>ms gpu</span></div>
    <div><b id="proc">—</b><span>% process</span></div>
    <div><b id="mem">—</b><span>MB</span></div>
  </div>
  <h2>기기</h2><div id="device"></div>
  <h2 style="margin-top:22px">받은 보고 <span style="opacity:.5;font-weight:400">— 최근 것부터</span></h2>
  <div id="perflog"></div>
</div>

<div id="pane-raw" class="tab">
  <div class="bar">
    <span>보관 <b id="rawkept">0</b> / 50</span>
    <span>성능 보고는 없습니다 — 저것은 소켓이 나르는 다른 채널입니다</span>
  </div>
  <div id="raws"></div>
</div>

<script>
function show(which) {
  for (const one of ["pulse", "perf", "raw"]) {
    document.getElementById("pane-" + one).classList.toggle("sel", one === which);
    document.getElementById("tab-" + one).classList.toggle("sel", one === which);
  }
}
// 마지막 전량 판독에 그 뒤의 모든 차이를 얹은 것. scene+selector 로 키를 잡는다. 경로는
// 정체가 아니기 때문이다 — 만들어진 적 다섯이 하나를 공유한다.
let held = new Map();
// static 은 매달릴 객체가 없다 — 그것들이 따로 구워지는 이유 전체가 그것이다. 화면의 전제는
// 화면 위의 무엇 못지않게 이것들 중 하나일 수 있다: 샘플 게임은 스테이지 번호를 그런 것 하나에
// 두고, 명세 스물여섯 줄이 그것을 검사한다.
let statics = new Map();
let seen = 0, docs = 0, whole = 0, scene = "—", reading = "—";

// 페이지가 열린 뒤로 각 키가 몇 번이나 움직였는지. 문서가 거의 다 나가는 실행은 어떤 명세 줄도
// 언급하지 않는 무언가가 움직이고 있는 것이고, 그것의 이름을 대 주는 것이 이것이다.
const moved = new Map();

// 원문 그대로. 이 페이지가 접어서 그린 것과 채널이 실제로 보낸 것을 나란히 댈 수 있어야
// 접는 과정에서 잃은 것이 보인다. 바운드를 두는 이유는 한 시간 열어 둔 페이지가 그 시간의
// 모든 판독을 붙들고 있을 이유가 없기 때문이다.
const raws = [];

function drawRaw() {
  document.getElementById("rawkept").textContent = raws.length;
  document.getElementById("raws").innerHTML = raws.length
    ? raws.map(d => {
        const n = (d.changed ?? []).length;
        return `<div class="raw"><div class="head">`
          + `<span>판독 <b>${d.reading ?? "?"}</b></span>`
          + `<span>프레임 ${d.frame ?? "?"}</span>`
          + `<span>${d.scene ?? "—"}</span>`
          + (d.whole ? `<span class="whole">전량</span>` : `<span>델타</span>`)
          + `<span>바뀐 것 ${n}</span>`
          + `<span style="opacity:.5">${JSON.stringify(d).length} 바이트</span>`
          + `</div><pre>${JSON.stringify(d, null, 2)
              .replace(/&/g, "&amp;").replace(/</g, "&lt;")}</pre></div>`;
      }).join("")
    : `<div class="none">아직 없음</div>`;
}

function apply(doc) {
  docs++;

  raws.unshift(doc);
  if (raws.length > 50) { raws.length = 50; }
  if (doc.whole) { whole++; held = new Map(); statics = new Map(); }
  scene = doc.scene ?? "—";
  reading = doc.reading ?? "—";
  for (const k of doc.changed ?? []) moved.set(k, (moved.get(k) ?? 0) + 1);

  for (const st of doc.statics ?? []) {
    statics.set((st.declaring ?? "") + "::" + (st.member ?? ""), st);
  }

  // 객체가 어느 목록으로 도착하는지가 그것이 켜져 있는지를 말한다. 이번 판독에 아무 말도 하지
  // 않는 객체는 마지막으로 있던 목록에 그대로 남고, 그것이 옳다 — 그것이 바뀌는 일 자체가
  // 차이이고 그러면 그 객체를 여기로 데려왔을 것이기 때문이다.
  for (const [live, list] of [[true, doc.active], [false, doc.deactive]]) {
    for (const o of list ?? []) {
      const id = (o.scene ?? "") + "/" + (o.selector ?? o.path ?? "");
      const was = held.get(id) ?? { members: new Map() };
      const now = {
        path: o.path ?? was.path,
        selector: o.selector ?? was.selector,
        scene: o.scene ?? was.scene,
        active: live,
        offers: o.offers ?? was.offers,
        world: o.world ?? was.world,
        members: was.members,
      };
      for (const m of o.members ?? []) {
        now.members.set((m.on ?? "") + "::" + (m.member ?? "") + "#" + (m.among ?? 0), m);
      }
      held.set(id, now);
    }
  }
}

// 무언가가 어디 있는지, 소수점 아래 네 자리까지. "커서가 battle2 에 도착했다" 같은 줄은 이것
// 둘을 겹쳐 놓아 확인하므로, 숫자를 버리고 이름만 남기는 것은 — 처음에 이것이 그랬다 — 그에
// 답하는 절반을 버리는 일이다. 그 둘은 게임 안에서 서로에게서 대입되므로 거의가 아니라 정확히
// 일치한다.
function place(w) {
  if (!w) return "";
  const n = x => (Math.round((x ?? 0) * 10000) / 10000);
  return `<span class="xy">${n(w.x)}, ${n(w.y)}${w.z ? ", " + n(w.z) : ""}</span>`;
}

function value(v) {
  if (v === null || v === undefined) return "null";
  if (typeof v !== "object") return String(v);
  if (v.label !== undefined) return JSON.stringify(v.label);
  if (v.sprite !== undefined) return "🖼 " + v.sprite;
  if (v.state !== undefined) return "▶ " + v.state;
  if (v.path !== undefined) {
    return "→ " + v.path + (v.active === false ? " (꺼짐)" : "") + " " + place(v.world);
  }
  if (v.name !== undefined) return v.name;
  if (v.is !== undefined) return "⟨" + v.is.split(".").pop() + "⟩";
  if (v.count !== undefined) return "n=" + v.count;
  return JSON.stringify(v);
}

function draw() {
  document.getElementById("scene").textContent = scene;
  document.getElementById("reading").textContent = reading;
  document.getElementById("docs").textContent = docs;
  document.getElementById("whole").textContent = whole;

  // 지금 화면에 있는 것과, 씬보다 오래 사는 것만.
  const here = [...held.values()].filter(o => o.scene === scene || o.scene === "DontDestroyOnLoad");
  document.getElementById("count").textContent = here.length;
  document.getElementById("off").textContent = here.filter(o => o.active === false).length;

  const state = [], acts = [];
  const order = (a, b) =>
    (a.active === false) - (b.active === false) || (a.path ?? "").localeCompare(b.path ?? "");
  for (const o of here.sort(order)) {
    const head = `<div class="path ${o.active === false ? "off" : ""}">${o.path ?? ""}`
      + `${o.active === false ? " · 꺼짐" : ""}</div>`
      + `<div class="sel">${o.selector ?? ""} ${place(o.world)}</div>`;

    const asked = [...o.members.values()].filter(m => m.asked !== false);
    const extra = [...o.members.values()].filter(m => m.asked === false);
    const rows = ms => ms.map(m =>
      `<tr><td class="m"><span class="on">${(m.on ?? "").split(".").pop()}.</span>`
      + `${m.member}${m.among ? "#" + m.among : ""}</td>`
      + `<td class="v">${value(m.value)}</td></tr>`).join("");

    if (o.members.size) {
      state.push(`<div class="obj">${head}<table>${rows(asked)}`
        + `${extra.length ? `<tbody class="extra">${rows(extra)}</tbody>` : ""}</table></div>`);
    }

    if (o.offers) {
      const f = o.offers;
      const bits = [
        ...(f.clicks ?? []).map(c => `<span class="tag c">click ${c.event} → ${c.method}</span>`),
        ...(f.keys ?? []).map(k => `<span class="tag k">${k}</span>`),
        ...(f.pointers ?? []).map(p => `<span class="tag p">${p}</span>`),
      ];
      acts.push(`<div class="obj">${head}<div>${bits.join(" ")}</div></div>`);
    }
  }

  const st = [...statics.values()].sort((a, b) =>
    (a.declaring ?? "").localeCompare(b.declaring ?? "") || (a.member ?? "").localeCompare(b.member ?? ""));
  document.getElementById("statics").innerHTML = st.length
    ? `<table>${st.map(m => `<tr><td class="m">${(m.declaring ?? "").split(".").pop()}.${m.member}</td>`
        + `<td class="v">${value(m.value)}</td></tr>`).join("")}</table>`
    : `<div class="none">아직 없음</div>`;

  document.getElementById("state").innerHTML = state.join("") || `<div class="none">아직 없음</div>`;
  document.getElementById("acts").innerHTML = acts.join("") || `<div class="none">아직 없음</div>`;

  const top = [...moved.entries()].sort((a, b) => b[1] - a[1]).slice(0, 12);
  document.getElementById("noisy").innerHTML = top.length
    ? top.map(([k, n]) => `<div class="n"><b>${n}</b><span>${k}</span></div>`).join("")
    : `<div class="none">아직 없음</div>`;
}

// 성능 보고는 pulse 로 나르지 않고 그래서도 안 된다: 프레임 시간은 게임이 아니라 게임을 돌리는
// 프로세스에 관한 것이고, 둘은 서로 다른 시계 위에서 서로 다른 물음에 답한다. 그것은 SDK 자신의
// 소켓으로 도착하고 테스트 페이지 매니저가 이미 그것을 내고 있다 — 그래서 두 번째 파일을 청하는
// 대신 그쪽을 직접 읽는다.
const SOCKET = "__SOCKET__";
let perf = [];

// 성능 탭이 읽는 것과 같은 소켓이다. 판독은 연결이 아니라 세션이므로 — 연결은 도구가 봐도
// 된다는 말이고 시작은 실행이 시작됐다는 말이다 — 그것을 말할 자리가 있어야 하고, 판독을
// 지켜보는 자리가 이 페이지다.
let socket = null;
let actionId = 1;

// 판독이 도는지, 마지막 문서가 언제였는지. 정지 화면은 아무것도 내보내지 않는 것이 옳은
// 동작이라, 그 침묵과 "아직 시작 안 함" 이 화면에서 같아 보이면 안 된다.
let running = null;
let lastDoc = null;
let pending = null;

function drawRun() {
  const el = document.getElementById("run");
  el.innerHTML = "판독 <b>"
    + (running === true ? "도는 중" : running === false ? "멈춤" : "모름") + "</b>";
  el.className = running === false ? "warn" : "";

  const quiet = document.getElementById("quiet");
  if (running !== true) { quiet.textContent = ""; return; }
  quiet.textContent = lastDoc === null
    ? "아직 한 건도 안 옴"
    : "마지막 판독 " + Math.round((Date.now() - lastDoc) / 1000) + "초 전 "
      + "— 화면이 그대로면 이것이 맞는 동작입니다";
}
setInterval(drawRun, 1000);

function ready() {
  const live = socket && socket.readyState === WebSocket.OPEN;
  document.getElementById("go").disabled = !live;
  document.getElementById("halt").disabled = !live;
  return live;
}

function ask(method) {
  if (!ready()) { return; }
  const said = document.getElementById("said");
  said.textContent = method + " 보냄…";
  said.className = "";
  pending = method;
  socket.send(JSON.stringify({
    type: "ACTION",
    id: actionId++,
    actions: [{ id: actionId++, jsonrpc: "2.0", method: method, params: [] }],
  }));
}

function connectSocket() {
  const mark = (text, bad) => {
    const el = document.getElementById("sock");
    el.textContent = "소켓 " + text;
    el.className = bad ? "warn" : "";
    ready();
  };

  try { socket = new WebSocket(SOCKET); }
  catch (e) { socket = null; mark("열 수 없음 — " + SOCKET, true); return; }

  socket.onopen = () => mark("붙음 — " + SOCKET);
  socket.onclose = () => {
    // 게임이 멈추면 세션도 끝난다. 다시 붙어도 판독은 꺼진 채이므로 그렇게 말한다.
    running = null; drawRun();
    mark("끊김. 5초 뒤 다시", true);
    setTimeout(connectSocket, 5000);
  };
  socket.onerror = () => mark("오류 — 게임이 실행 중이고 ArtelTestPageManager 가 켜져 있어야 합니다", true);

  socket.onmessage = event => {
    let doc;
    try { doc = JSON.parse(event.data); } catch (e) { return; }

    if (doc.type === "ACTION_RESULT") {
      // 체크 표시가 아니라 온전한 문장으로 말한다. 거절은 제 이유의 이름을 댄다 — 출시 빌드는
      // 판독을 하지 않는다 — 그것을 감추면 죽은 버튼이 살아 있는 것처럼 보인다.
      const r = (doc.results ?? [])[0] ?? {};
      const said = document.getElementById("said");
      said.textContent = r.success ? "됨" : ("거절 — " + (r.error || "이유 없음"));
      said.className = r.success ? "" : "warn";

      if (r.success && pending === "start_readings") { running = true; lastDoc = null; }
      if (r.success && pending === "stop_readings") { running = false; }
      pending = null;
      drawRun();
      return;
    }

    if (doc.type === "DEVICE_CONTEXT") { drawDevice(doc.device); return; }
    if (doc.type !== "PERFORMANCE") return;

    // 새것부터, 그리고 유계다: 한 시간 열어 둔 페이지는 그러지 않으면 아무도 읽지 않을 보고를
    // 전부 쥐고 있게 된다.
    perf.unshift(doc);
    if (perf.length > 120) perf.length = 120;
    drawPerf();
  };
}

const ms = v => (v === null || v === undefined) ? "—" : (Math.round(v * 100) / 100).toFixed(2);
const mb = v => (v === null || v === undefined) ? "—" : Math.round(v / 1048576);

function drawPerf() {
  const now = perf[0];
  if (!now) return;
  const f = now.frameTimes ?? {};
  const t = now.frameTiming ?? {};
  const pr = now.process ?? {};

  const put = (id, text) => { document.getElementById(id).textContent = text; };

  put("fps", f.meanMs ? Math.round(1000 / f.meanMs) : "—");
  put("ms", ms(f.meanMs));
  put("p95", ms(f.p95Ms));
  put("worst", ms(f.maxMs));

  // 프레임 시간이 무슨 뜻이든 가지는 이유 전체가 예산이다: 같은 33ms 가 30fps 상한에서는
  // 멀쩡하고 144Hz 에서는 끊김이다. 날숫자가 아니라 그 예산에 대고 센 값으로 말한다.
  put("over", f.hitchCount === undefined ? "—"
      : f.hitchCount + (f.budgetMs ? " / " + ms(f.budgetMs) + "ms" : ""));

  put("cpuMain", ms(t.cpuMainThreadMs));
  put("cpuRender", ms(t.cpuRenderThreadMs));
  put("gpu", ms(t.gpuMs));
  put("proc", pr.cpuPercent === undefined ? "—" : Math.round(pr.cpuPercent) );
  put("mem", mb(pr.workingSetBytes));

  document.getElementById("perflog").innerHTML = perf.slice(0, 40).map(d => {
    const ft = d.frameTimes ?? {};
    const tm = d.frameTiming ?? {};
    return `<div class="obj"><div class="sel">#${d.id} · ${ft.frameCount ?? "?"} 프레임`
      + `${tm.bottleneck ? " · 병목 " + tm.bottleneck : ""}`
      + `${d.status && d.status.isFocused === false ? " · 포커스 없음" : ""}</div>`
      + `<div>평균 ${ms(ft.meanMs)}ms · p95 ${ms(ft.p95Ms)} · 최악 ${ms(ft.maxMs)}`
      + `${ft.hitchCount ? ` · <b>초과 ${ft.hitchCount}</b>` : ""}</div></div>`;
  }).join("");
}

function drawDevice(d) {
  if (!d) return;
  const row = (k, v) => v === undefined || v === null ? ""
    : `<tr><td class="m">${k}</td><td class="v">${v}</td></tr>`;
  document.getElementById("device").innerHTML = "<table>"
    + row("기기", d.deviceModel) + row("OS", d.operatingSystem)
    + row("CPU", (d.processorType ?? "") + (d.processorCount ? ` × ${d.processorCount}` : ""))
    + row("메모리", d.systemMemoryMb ? d.systemMemoryMb + " MB" : null)
    + row("GPU", d.graphicsDeviceName) + row("그래픽 메모리", d.graphicsMemoryMb ? d.graphicsMemoryMb + " MB" : null)
    + row("해상도", d.resolutionWidth ? `${d.resolutionWidth}×${d.resolutionHeight} @ ${d.refreshRateHz ?? "?"}Hz` : null)
    + row("목표 프레임", d.targetFrameRate) + row("vSync", d.vSyncCount)
    + row("스크립팅", d.scriptingBackend) + row("SDK", d.sdkVersion)
    + "</table>";
}

document.getElementById("go").onclick = () => ask("start_readings");
document.getElementById("halt").onclick = () => ask("stop_readings");
connectSocket();

// 1초에 한 번. 채널이 그 박자로 읽히도록 설계됐다.
async function poll() {
  try {
    const r = await fetch("/readings?from=" + seen);
    const body = await r.json();
    if (body.reset) { held = new Map(); seen = 0; docs = 0; whole = 0; raws.length = 0; }
    if (body.docs.length) { lastDoc = Date.now(); }
    for (const doc of body.docs) apply(doc);
    if (body.docs.length) { drawRaw(); }
    seen = body.next;
    document.getElementById("stale").textContent = body.missing ? "파일 없음" : "";
    draw();
  } catch (e) {
    document.getElementById("stale").textContent = "서버 없음";
  }
}
poll();
setInterval(poll, 1000);
</script>
"""


class Server(socketserver.ThreadingTCPServer):
    """연결당 스레드 하나. 브라우저가 하나를 열어 둔 채여도 나머지가 멎지 않도록."""


class Reader(http.server.BaseHTTPRequestHandler):
    path_to_readings = DEFAULT_FILE
    socket = DEFAULT_SOCKET

    # 응답마다 닫는다. 1초에 한 번 하는 폴링은 연결을 유지해서 얻을 것이 없고, 유지된 연결
    # 하나는 함께 붙잡힌 스레드 하나다.
    protocol_version = "HTTP/1.0"

    def do_GET(self):
        if self.path == "/":
            page = PAGE.replace("__SOCKET__", self.socket)
            return self.send(page.encode("utf-8"), "text/html; charset=utf-8")

        if not self.path.startswith("/readings"):
            self.send_error(404)
            return

        start = 0
        if "from=" in self.path:
            try:
                start = int(self.path.split("from=")[1].split("&")[0])
            except ValueError:
                start = 0

        docs, reset, missing = [], False, False

        try:
            with open(self.path_to_readings, "r", encoding="utf-8") as handle:
                lines = handle.readlines()
        except FileNotFoundError:
            lines, missing = [], True

        # 지난번보다 짧은 파일은 되감기가 아니라 새 실행이다. 페이지는 죽은 실행의 상태 위에
        # 새 차이를 얹는 대신 쥐고 있던 것을 버린다.
        if start > len(lines):
            start, reset = 0, True

        for line in lines[start:]:
            line = line.strip()
            if not line:
                continue
            try:
                docs.append(json.loads(line))
            except json.JSONDecodeError:
                # 반쯤 쓰인 마지막 줄. 다음 폴링에서 온전해진다.
                break

        payload = {"docs": docs, "next": start + len(docs), "reset": reset, "missing": missing}
        self.send(json.dumps(payload).encode("utf-8"), "application/json")

    def send(self, body, kind):
        self.send_response(200)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8770)
    parser.add_argument("--file", default=DEFAULT_FILE)
    parser.add_argument("--socket", default=DEFAULT_SOCKET,
                        help="SDK 로컬 소켓. PERFORMANCE 와 DEVICE_CONTEXT 가 여기로 온다")
    args = parser.parse_args()

    Reader.path_to_readings = args.file
    Reader.socket = args.socket

    print("readings: " + args.file)
    print("socket:   " + args.socket)
    print("open:     http://localhost:%d" % args.port)

    # 스레드로 돈다. 그것은 취향이 아니다. 단일 스레드 서버는 쥐고 있는 연결이 무엇이든 그것
    # 위에서 accept 루프에 막히는데, 브라우저는 아직 요청을 보내기로 정하지도 않은 연결을 연다 —
    # 투기적 preconnect 하나면 그 소켓이 타임아웃될 때까지 이후의 모든 폴링이 그 뒤에 줄을 선다.
    # 증상은 한 번 로드되고 나서 갱신을 멈추는 페이지이고, 그동안 서버는 curl 에는 완벽하게
    # 답한다.
    Server.allow_reuse_address = True
    Server.daemon_threads = True

    with Server(("127.0.0.1", args.port), Reader) as server:
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
