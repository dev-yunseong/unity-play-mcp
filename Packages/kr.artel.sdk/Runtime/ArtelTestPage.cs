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
  <main id=""scene""></main>
  <pre id=""log""></pre>
  <script>
    const wsUrl = '__WS_URL__';
    let ws;
    let actionId = 1;
    let liveSceneId = null;
    let scanMode = 'default';
    const status = document.getElementById('status');
    const sceneRoot = document.getElementById('scene');
    const log = document.getElementById('log');
    const keyCode = document.getElementById('key-code');
    const keyDuration = document.getElementById('key-duration');
    const snapshot = document.getElementById('snapshot');
    const snapshotLabel = document.getElementById('snapshot-label');
    const snapshotScene = document.getElementById('snapshot-scene');
    const snapshotJson = document.getElementById('snapshot-json');

    document.getElementById('connect').onclick = connect;
    document.getElementById('scan').onclick = scan;
    document.getElementById('scan-all').onclick = () => scanAllScenes();
    document.getElementById('scan-all-full').onclick = () => scanAllScenes('full');
    document.getElementById('snapshot-clear').onclick = clearSnapshot;
    document.getElementById('key-click').onclick = clickKey;

    function connect() {
      ws = new WebSocket(wsUrl);
      ws.onopen = () => { status.textContent = 'connected'; scan(); };
      ws.onclose = () => status.textContent = 'closed';
      ws.onerror = () => status.textContent = 'error';
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
      if (!ws || ws.readyState !== WebSocket.OPEN) {
        status.textContent = 'connect first';
        return;
      }

      ws.send(JSON.stringify({
        type: 'ACTION',
        id: actionId++,
        actions: [{ id: actionId++, jsonrpc: '2.0', method, params }]
      }));
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

    function handleMessage(message) {
      log.textContent = JSON.stringify(message, null, 2);
      if (message.type === 'GAME_STATE') renderScene(message.scene);
      if (message.type === 'ALL_SCENES') renderAllScenes(message.scenes, message);
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
