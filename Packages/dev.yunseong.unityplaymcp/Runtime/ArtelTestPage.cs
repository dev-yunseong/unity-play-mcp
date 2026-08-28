namespace Artel
{
    internal static class ArtelTestPage
    {
        public const string Html = @"<!doctype html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>Artel SDK PoC</title>
  <style>
    body { font-family: system-ui, sans-serif; margin: 24px; color: #1f2328; }
    header { display: flex; gap: 8px; align-items: center; margin-bottom: 16px; }
    button, input { font: inherit; padding: 8px 10px; }
    .controls { display: flex; flex-wrap: wrap; gap: 8px; align-items: end; margin-bottom: 16px; padding: 12px; background: #f6f8fa; }
    .controls label { display: grid; gap: 4px; font-size: 12px; color: #57606a; }
    .controls input { color: #1f2328; }
    #key-duration { width: 96px; }
    #capture-target { width: 160px; }
    .split { display: flex; gap: 16px; align-items: flex-start; }
    .split main { flex: 1 1 auto; min-width: 0; }
    #capture { flex: 0 0 360px; position: sticky; top: 16px; border: 1px solid #d0d7de; padding: 12px; }
    #capture header { justify-content: space-between; margin-bottom: 8px; }
    #capture img { display: block; max-width: 100%; border: 1px solid #d0d7de; }
    #stream-viewer video { display: block; width: min(640px, 100%); background: #111; border: 1px solid #d0d7de; }
    .node { border-left: 2px solid #d0d7de; margin: 8px 0 8px 16px; padding-left: 12px; }
    .node.inactive { border-left-style: dashed; opacity: 0.6; }
    .label { font-size: 12px; color: #57606a; margin-bottom: 4px; }
    .block { padding: 8px; background: #f6f8fa; }
    pre { background: #f6f8fa; padding: 12px; overflow: auto; }
    #snapshot { border: 1px solid #d0d7de; padding: 12px; margin-bottom: 16px; }
    #snapshot header { justify-content: space-between; }
    #snapshot.empty { display: none; }
    summary { font-size: 12px; color: #57606a; cursor: pointer; }
  </style>
</head>
<body>
  <header>
    <strong>Artel SDK PoC</strong>
    <button id=""connect"">Connect</button>
    <button id=""scan"">Scan</button>
    <button id=""scan-all"">Scan all scenes</button>
    <button id=""scan-all-full"">Scan all scenes (full)</button>
    <button id=""readings-start"">Start readings</button>
    <button id=""readings-stop"">Stop readings</button>
    <span id=""status"">idle</span>
  </header>
  <section class=""controls"" aria-label=""Keyboard input"">
    <strong>Keyboard</strong>
    <label>
      KeyCode
      <input id=""key-code"" list=""key-codes"" value=""Space"" autocomplete=""off"">
    </label>
    <datalist id=""key-codes"">
      <option value=""Space""></option>
      <option value=""Return""></option>
      <option value=""Escape""></option>
      <option value=""UpArrow""></option>
      <option value=""DownArrow""></option>
      <option value=""LeftArrow""></option>
      <option value=""RightArrow""></option>
      <option value=""A""></option>
      <option value=""W""></option>
      <option value=""S""></option>
      <option value=""D""></option>
    </datalist>
    <label>
      Duration (seconds)
      <input id=""key-duration"" type=""number"" value=""0.5"" min=""0.01"" step=""0.05"">
    </label>
    <button id=""key-click"">Click key</button>
    <button id=""key-down"">Hold</button>
    <button id=""key-up"">Release</button>
  </section>
  <section id=""pointer-controls"" aria-label=""Pointer"">
    <strong>Pointer (px from top left)</strong>
    <label>
      X
      <input id=""pointer-x"" type=""number"" value=""400"" step=""10"">
    </label>
    <label>
      Y
      <input id=""pointer-y"" type=""number"" value=""300"" step=""10"">
    </label>
    <label>
      Button
      <select id=""pointer-button"">
        <option value=""0"">Left</option>
        <option value=""1"">Right</option>
        <option value=""2"">Middle</option>
      </select>
    </label>
    <button id=""pointer-move"">Move</button>
    <button id=""pointer-down"">Press</button>
    <button id=""pointer-up"">Release</button>
    <button id=""pointer-drag"">Drag to here</button>
  </section>
  <section class=""controls"" aria-label=""Capture"">
    <strong>Capture</strong>
    <label>
      Target block id
      <input id=""capture-target"" type=""number"" min=""1"" step=""1"" placeholder=""whole screen"">
    </label>
    <button id=""capture-screen"">Capture</button>
  </section>
  <section id=""stream-viewer"" class=""controls"" aria-label=""WebRTC stream"">
    <strong>WebRTC</strong>
    <button id=""stream-start"">Start stream</button>
    <button id=""stream-stop"">Stop stream</button>
    <span id=""stream-status"">idle</span>
    <video id=""stream-video"" autoplay playsinline muted></video>
  </section>
  <section id=""snapshot"" class=""empty"" aria-label=""Pinned scan"">
    <header>
      <span class=""label"" id=""snapshot-label""></span>
      <button id=""snapshot-clear"">Clear</button>
    </header>
    <div id=""snapshot-scene""></div>
    <details>
      <summary>raw ALL_SCENES</summary>
      <pre id=""snapshot-json""></pre>
    </details>
  </section>
  <div class=""split"">
    <main id=""scene""></main>
    <section id=""capture"" aria-label=""Latest capture"">
      <header>
        <span class=""label"" id=""capture-status"">no capture yet</span>
        <button id=""capture-clear"">Clear</button>
      </header>
      <img id=""capture-image"" alt=""Latest capture"" hidden>
      <details>
        <summary>raw capture result</summary>
        <pre id=""capture-json""></pre>
      </details>
    </section>
  </div>
  <pre id=""log""></pre>
  <script>
    const wsUrl = '__WS_URL__';
    let ws;
    let actionId = 1;
    let liveSceneId = null;
    let scanMode = 'default';
    // 어느 결과가 이 캡처의 것인지 가리는 값. capture_screen 의 성공은 결과에 액션 이름을 싣지 않으므로
    // 보낸 액션 id 로 짝을 맞춘다.
    let pendingCaptureId = null;
    let streamPeer = null;
    let streamId = null;
    let streamRenewTimer = null;
    let pendingRemoteIce = [];
    const status = document.getElementById('status');
    const sceneRoot = document.getElementById('scene');
    const log = document.getElementById('log');
    const keyCode = document.getElementById('key-code');
    const keyDuration = document.getElementById('key-duration');
    const pointerX = document.getElementById('pointer-x');
    const pointerY = document.getElementById('pointer-y');
    const pointerButton = document.getElementById('pointer-button');
    const snapshot = document.getElementById('snapshot');
    const snapshotLabel = document.getElementById('snapshot-label');
    const snapshotScene = document.getElementById('snapshot-scene');
    const snapshotJson = document.getElementById('snapshot-json');
    const captureTarget = document.getElementById('capture-target');
    const captureStatus = document.getElementById('capture-status');
    const captureImage = document.getElementById('capture-image');
    const captureJson = document.getElementById('capture-json');
    const captureButton = document.getElementById('capture-screen');
    const streamStatus = document.getElementById('stream-status');
    const streamVideo = document.getElementById('stream-video');

    document.getElementById('connect').onclick = connect;
    document.getElementById('scan').onclick = scan;
    document.getElementById('scan-all').onclick = () => scanAllScenes();
    document.getElementById('scan-all-full').onclick = () => scanAllScenes('full');
    // 판독은 연결이 아니라 세션이다. 연결은 도구가 봐도 된다고 말하고, 이것들은 실행이 시작됐다고 말한다. 갈라 둔 것은 모든
    // 씬을 도는 순회도 연결에서 시작하는데 그 동안 찍은 판독이 아무도 걸어가지 않은 화면을 서술하기 때문이다.
    document.getElementById('readings-start').onclick = () => sendAction('start_readings', []);
    document.getElementById('readings-stop').onclick = () => sendAction('stop_readings', []);
    document.getElementById('snapshot-clear').onclick = clearSnapshot;
    captureButton.onclick = captureScreen;
    document.getElementById('capture-clear').onclick = clearCapture;
    document.getElementById('stream-start').onclick = startStream;
    document.getElementById('stream-stop').onclick = () => stopStream(true, 'stopped');
    document.getElementById('key-click').onclick = clickKey;
    document.getElementById('key-down').onclick = () => holdKey('key_down');
    document.getElementById('key-up').onclick = () => holdKey('key_up');
    document.getElementById('pointer-move').onclick = movePointer;
    document.getElementById('pointer-down').onclick = () => pressPointer('mouse_down');
    document.getElementById('pointer-up').onclick = () => pressPointer('mouse_up');
    document.getElementById('pointer-drag').onclick = dragPointer;

    function connect() {
      ws = new WebSocket(wsUrl);
      ws.onopen = () => { status.textContent = 'connected'; scan(); };
      ws.onclose = () => { status.textContent = 'closed'; stopStream(false); };
      ws.onerror = () => { status.textContent = 'error'; stopStream(false); };
      ws.onmessage = event => handleMessage(JSON.parse(event.data));
    }

    function scan() {
      if (!ws || ws.readyState !== WebSocket.OPEN) return;
      ws.send(JSON.stringify({ jsonrpc: '2.0', id: actionId++, method: 'scan_scene', params: [] }));
    }

    function scanAllScenes(mode) {
      status.textContent = mode ? `scanning every scene (${mode})…` : 'scanning every scene…';
      scanMode = mode || 'default';
      sendAction('scan_all_scenes', mode ? [mode] : []);
    }

    function clearSnapshot() {
      snapshot.className = 'empty';
      snapshotScene.innerHTML = '';
      snapshotJson.textContent = '';
      snapshotLabel.textContent = '';
    }

    function sendAction(method, params) {
      return sendActions([[method, params]])[0];
    }

    // Returns the id of every action sent, so a caller that has to recognise its own result —
    // capture_screen does — can keep one. Undefined for a send that never left.
    function sendActions(steps) {
      if (!ws || ws.readyState !== WebSocket.OPEN) {
        status.textContent = 'connect first';
        return [];
      }

      const envelopeId = actionId++;
      const actions = steps.map(([method, params]) =>
        ({ id: actionId++, jsonrpc: '2.0', method, params }));
      ws.send(JSON.stringify({ type: 'ACTION', id: envelopeId, actions }));
      return actions.map(action => action.id);
    }

    function captureScreen() {
      const raw = captureTarget.value.trim();
      const target = Number(raw);
      if (raw !== '' && !Number.isInteger(target)) {
        captureStatus.textContent = 'invalid target id';
        return;
      }

      const id = sendAction('capture_screen', raw === '' ? [] : [target]);
      if (id === undefined) return;

      pendingCaptureId = id;
      captureButton.disabled = true;
      captureStatus.textContent = 'capturing…';
    }

    function clearCapture() {
      pendingCaptureId = null;
      captureButton.disabled = false;
      captureImage.hidden = true;
      captureImage.removeAttribute('src');
      captureJson.textContent = '';
      captureStatus.textContent = 'no capture yet';
    }

    function clickKey() {
      const key = keyCode.value.trim();
      const duration = Number(keyDuration.value);
      if (!key || !Number.isFinite(duration) || duration <= 0) {
        status.textContent = 'invalid key input';
        return;
      }

      sendAction('key_click', [key, duration]);
    }

    function holdKey(method) {
      const key = keyCode.value.trim();
      if (!key) {
        status.textContent = 'invalid key input';
        return;
      }

      sendAction(method, [key]);
    }

    function readPointerTarget() {
      const x = Number(pointerX.value);
      const y = Number(pointerY.value);
      if (!Number.isFinite(x) || !Number.isFinite(y)) {
        status.textContent = 'invalid pointer position';
        return null;
      }

      return [x, y];
    }

    function movePointer() {
      const target = readPointerTarget();
      if (target) sendAction('move_mouse', target);
    }

    function pressPointer(method) {
      sendAction(method, [Number(pointerButton.value)]);
    }

    // One batch, because the queue is what keeps the press, the travel, and the
    // release in order. Sent as three messages they could interleave with a scan.
    function dragPointer() {
      const target = readPointerTarget();
      if (!target) return;

      const button = Number(pointerButton.value);
      sendActions([
        ['mouse_down', [button]],
        ['move_mouse', target],
        ['mouse_up', [button]]
      ]);
    }

    function handleMessage(message) {
      log.textContent = JSON.stringify(message, null, 2);
      if (message.type === 'GAME_STATE') renderScene(message.scene);
      if (message.type === 'ALL_SCENES') renderAllScenes(message.scenes, message);
      if (message.type === 'ACTION_RESULT') renderCapture(message.results);
      if (message.type === 'WEBRTC_OFFER') acceptStreamOffer(message);
      if (message.type === 'WEBRTC_ICE') acceptRemoteIce(message);
      if (message.type === 'STREAM_STATE') renderStreamState(message);
    }

    function startStream() {
      if (!ws || ws.readyState !== WebSocket.OPEN) {
        streamStatus.textContent = 'connect first';
        return;
      }

      stopStream(true);
      streamId = `test-page-${Date.now()}`;
      pendingRemoteIce = [];
      streamPeer = new RTCPeerConnection({ iceServers: [] });
      streamPeer.ontrack = event => {
        // SDK는 track을 특정 MediaStream에 묶지 않는다. 브라우저가 빈 streams 배열을 주면
        // 수신 track으로 로컬 stream을 만들어야 협상 성공 뒤에도 video가 실제로 재생된다.
        streamVideo.srcObject = event.streams[0] || new MediaStream([event.track]);
      };
      streamPeer.onicecandidate = event => {
        if (!event.candidate || !streamId) return;
        sendStreamMessage({
          type: 'WEBRTC_ICE',
          streamId,
          candidate: event.candidate.toJSON()
        });
      };
      streamPeer.onconnectionstatechange = () => {
        if (!streamPeer) return;
        if (streamPeer.connectionState === 'failed' || streamPeer.connectionState === 'closed') {
          stopStream(true, 'failed');
        }
      };

      sendStreamMessage({
        type: 'STREAM_START',
        streamId,
        iceServers: [],
        video: { maxWidth: 640, maxFramerate: 10 },
        leaseSeconds: 30
      });
      streamRenewTimer = setInterval(() => {
        if (streamId) sendStreamMessage({ type: 'STREAM_RENEW', streamId });
      }, 10000);
      streamStatus.textContent = 'starting';
    }

    async function acceptStreamOffer(message) {
      if (!streamPeer || message.streamId !== streamId || !message.sdp) return;

      const peer = streamPeer;
      const offeredStreamId = streamId;
      try {
        await peer.setRemoteDescription({ type: 'offer', sdp: message.sdp });
        if (peer !== streamPeer || offeredStreamId !== streamId) return;
        for (const candidate of pendingRemoteIce) await peer.addIceCandidate(candidate);
        pendingRemoteIce = [];
        const answer = await peer.createAnswer();
        await peer.setLocalDescription(answer);
        if (peer !== streamPeer || offeredStreamId !== streamId) return;
        sendStreamMessage({ type: 'WEBRTC_ANSWER', streamId, sdp: answer.sdp });
      } catch (error) {
        streamStatus.textContent = `negotiation failed: ${error.message}`;
        stopStream(true);
      }
    }

    async function acceptRemoteIce(message) {
      if (!streamPeer || message.streamId !== streamId || !message.candidate ||
          !message.candidate.candidate) return;

      try {
        if (!streamPeer.remoteDescription) {
          pendingRemoteIce.push(message.candidate);
          return;
        }
        await streamPeer.addIceCandidate(message.candidate);
      } catch (error) {
        streamStatus.textContent = `ICE failed: ${error.message}`;
      }
    }

    function renderStreamState(message) {
      if (message.streamId !== streamId) return;
      streamStatus.textContent = message.error ? `${message.state}: ${message.error}` : message.state;
      if (message.state === 'FAILED' || message.state === 'STOPPED') stopStream(false);
    }

    function sendStreamMessage(message) {
      if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(message));
    }

    function stopStream(notifySdk, finalStatus) {
      const stoppedId = streamId;
      streamId = null;
      if (streamRenewTimer) clearInterval(streamRenewTimer);
      streamRenewTimer = null;
      pendingRemoteIce = [];
      if (streamPeer) {
        streamPeer.ontrack = null;
        streamPeer.onicecandidate = null;
        streamPeer.onconnectionstatechange = null;
        streamPeer.close();
      }
      streamPeer = null;
      streamVideo.srcObject = null;
      if (notifySdk && stoppedId) {
        sendStreamMessage({ type: 'STREAM_STOP', streamId: stoppedId });
      }
      if (finalStatus) streamStatus.textContent = finalStatus;
    }

    window.addEventListener('beforeunload', () => stopStream(true));

    // Drawn beside the scene, never into it. The poller pushes a GAME_STATE within a second of
    // any change and renderScene replaces that whole subtree, so a capture rendered there would
    // disappear before it could be looked at. This one stays until the next capture or Clear.
    function renderCapture(results) {
      const result = (results || []).find(entry => entry.id === pendingCaptureId);
      if (!result) return;

      pendingCaptureId = null;
      captureButton.disabled = false;

      if (!result.success) {
        // The upload is the half that needs a signed-in session, and it fails long after the
        // screen was read. Saying which half failed is the difference between a rendering bug
        // and a missing login.
        captureStatus.textContent = result.error || 'capture failed';
        captureJson.textContent = JSON.stringify(result, null, 2);
        return;
      }

      const capture = result.returnValue || {};
      const parts = [`${capture.width}×${capture.height}`];
      if (capture.mimeType) parts.push(capture.mimeType);
      if (capture.targetId != null) parts.push(`target #${capture.targetId}`);
      if (capture.clipped) parts.push('clipped');
      parts.push(new Date().toLocaleTimeString());

      captureStatus.textContent = parts.join(' · ');
      captureJson.textContent = JSON.stringify(capture, null, 2);
      captureImage.src = capture.url;
      captureImage.hidden = false;
    }

    function renderScene(scene) {
      liveSceneId = scene.id;
      sceneRoot.innerHTML = '';
      sceneRoot.appendChild(renderNode(scene, true));
    }

    // Drawn into its own pinned section rather than over the live scene: the poller
    // pushes a GAME_STATE within a second of any change, and a scan that took the
    // whole walk to produce would vanish under it. It stays until Clear.
    function renderAllScenes(scenes, message) {
      const entries = scenes || [];
      status.textContent = `${entries.length} scenes scanned`;
      snapshot.className = '';
      snapshotLabel.textContent =
        `${entries.length} scenes · ${scanMode} mode · ${new Date().toLocaleTimeString()}`;
      snapshotJson.textContent = JSON.stringify(message, null, 2);
      snapshotScene.innerHTML = '';

      for (const entry of entries) {
        const label = document.createElement('div');
        label.className = 'label';
        label.textContent = `build #${entry.buildIndex} — ${entry.path}`;
        snapshotScene.appendChild(label);

        // The walk unloads every scene it opened, so those block ids address objects
        // that no longer exist and their controls are rendered dead. The scene the
        // game already had open survived it and stays clickable.
        snapshotScene.appendChild(renderNode(entry.scene, entry.scene.id === liveSceneId));
      }
    }

    function renderNode(node, interactive) {
      const wrap = document.createElement('div');
      const inactive = node.active === false;
      wrap.className = inactive ? 'node inactive' : 'node';
      const label = document.createElement('div');
      label.className = 'label';
      label.textContent =
        `${node.type} #${node.id} ${node.name || ''}${inactive ? ' (inactive)' : ''}`;
      wrap.appendChild(label);

      for (const component of node.components || []) {
        wrap.appendChild(renderComponent(node.id, component, interactive));
      }

      for (const child of node.children || []) wrap.appendChild(renderNode(child, interactive));
      return wrap;
    }

    function renderComponent(blockId, component, interactive) {
      const wrap = document.createElement('div');
      wrap.className = 'block';

      if (component.type === 'button') {
        const button = document.createElement('button');
        button.textContent = component.name || `Button ${blockId}`;
        button.disabled = !interactive;
        if (interactive) button.onclick = () => sendAction('button_click', [blockId]);
        wrap.appendChild(button);

        // Only a full scan collects these, so a default scan renders nothing here.
        for (const handler of component.onClick || []) {
          const line = document.createElement('div');
          line.className = 'label';
          line.textContent = `onClick: ${handler.target || '(missing target)'} (${handler.targetType || 'unknown type'}) . ${handler.method || '(no method)'}`;
          wrap.appendChild(line);
        }
      } else if (component.type === 'editText') {
        const input = document.createElement('input');
        input.value = component.content || '';
        input.placeholder = component.placeholder || '';
        input.disabled = !interactive;
        if (interactive) input.onchange = () => sendAction('enter_text', [blockId, input.value]);
        wrap.appendChild(input);
      } else if (component.type === 'text') {
        const text = document.createElement('div');
        text.textContent = component.content || component.name || '';
        wrap.appendChild(text);
      } else {
        wrap.textContent = component.name || component.type;
      }

      const states = component.states || [];
      const actions = component.actions || [];
      if (states.length > 0 || actions.length > 0) {
        // Open by default so values are readable without a click, but foldable —
        // a full scan puts every serialized field of the component in here.
        const metadata = document.createElement('details');
        metadata.open = true;
        const summary = document.createElement('summary');
        summary.textContent = `${states.length} states · ${actions.length} actions`;
        const body = document.createElement('pre');
        body.textContent = JSON.stringify({ states, actions }, null, 2);
        metadata.appendChild(summary);
        metadata.appendChild(body);
        wrap.appendChild(metadata);
      }

      return wrap;
    }
  </script>
</body>
</html>";
    }
}
