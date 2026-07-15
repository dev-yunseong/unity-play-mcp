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
    .node { border-left: 2px solid #d0d7de; margin: 8px 0 8px 16px; padding-left: 12px; }
    .label { font-size: 12px; color: #57606a; margin-bottom: 4px; }
    .block { padding: 8px; background: #f6f8fa; }
    pre { background: #f6f8fa; padding: 12px; overflow: auto; }
  </style>
</head>
<body>
  <header>
    <strong>Artel SDK PoC</strong>
    <button id=""connect"">Connect</button>
    <button id=""scan"">Scan</button>
    <span id=""status"">idle</span>
  </header>
  <main id=""scene""></main>
  <pre id=""log""></pre>
  <script>
    const wsUrl = '__WS_URL__';
    let ws;
    let actionId = 1;
    const status = document.getElementById('status');
    const sceneRoot = document.getElementById('scene');
    const log = document.getElementById('log');

    document.getElementById('connect').onclick = connect;
    document.getElementById('scan').onclick = scan;

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

    function sendAction(method, params) {
      ws.send(JSON.stringify({
        type: 'ACTION',
        id: actionId++,
        actions: [{ id: actionId++, jsonrpc: '2.0', method, params }]
      }));
    }

    function handleMessage(message) {
      log.textContent = JSON.stringify(message, null, 2);
      if (message.type === 'GAME_STATE') renderScene(message.scene);
    }

    function renderScene(scene) {
      sceneRoot.innerHTML = '';
      sceneRoot.appendChild(renderNode(scene));
    }

    function renderNode(node) {
      const wrap = document.createElement('div');
      wrap.className = 'node';
      const label = document.createElement('div');
      label.className = 'label';
      label.textContent = `${node.type} #${node.id} ${node.name || ''}`;
      wrap.appendChild(label);

      for (const component of node.components || []) {
        wrap.appendChild(renderComponent(node.id, component));
      }

      for (const child of node.children || []) wrap.appendChild(renderNode(child));
      return wrap;
    }

    function renderComponent(blockId, component) {
      const wrap = document.createElement('div');
      wrap.className = 'block';

      if (component.type === 'button') {
        const button = document.createElement('button');
        button.textContent = component.name || `Button ${blockId}`;
        button.onclick = () => sendAction('button_click', [blockId]);
        wrap.appendChild(button);
      } else if (component.type === 'editText') {
        const input = document.createElement('input');
        input.value = component.content || '';
        input.placeholder = component.placeholder || '';
        input.onchange = () => sendAction('enter_text', [blockId, input.value]);
        wrap.appendChild(input);
      } else if (component.type === 'text') {
        const text = document.createElement('div');
        text.textContent = component.content || component.name || '';
        wrap.appendChild(text);
      } else {
        wrap.textContent = component.name || component.type;
      }

      if ((component.states || []).length > 0 || (component.actions || []).length > 0) {
        const metadata = document.createElement('pre');
        metadata.textContent = JSON.stringify({ states: component.states, actions: component.actions }, null, 2);
        wrap.appendChild(metadata);
      }

      return wrap;
    }
  </script>
</body>
</html>";
    }
}
