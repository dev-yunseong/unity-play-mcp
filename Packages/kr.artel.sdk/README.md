# Artel SDK

## Local PoC

Add `ArtelManager` to a scene to start the SDK WebSocket server:

- WebSocket URL: `ws://127.0.0.1:17311/ws`
- Scan request: `{ "jsonrpc": "2.0", "id": 1, "method": "scan_scene", "params": [] }`
- Action message: `ACTION` with `button_click` and `enter_text`

Add `ArtelTestPageServer` to a scene when you want the browser test page:

- HTTP URL: `http://127.0.0.1:17310/`
- The test page connects to the `ArtelManager` WebSocket server.

## Included dependencies

The SDK vendors `websocket-sharp` under `Runtime/Plugins`, so projects that install `kr.artel.sdk` also get the WebSocket server dependency.
