# Unity Play MCP

Unity Play MCP lets a coding agent inspect and control a running Unity game.
The package opens a WebSocket server at `ws://127.0.0.1:17311/ws`.

## Scene state

Call `start_readings` when a play session begins. The game then sends `PULSE`
readings containing the current scene, active and inactive objects, stable
instance identifiers, screen rectangles, available interactions, and watched
member values. Later readings are deltas; call `stop_readings` when the session
ends.

`PERFORMANCE` and `DEVICE_CONTEXT` are diagnostic pushes independent of scene
state.

## Actions

Send one or more actions in an `ACTION` message:

```json
{
  "type": "ACTION",
  "id": 1,
  "actions": [
    { "id": 1, "method": "button_click", "params": [12345] }
  ]
}
```

The game answers the batch with `ACTION_RESULT`. Match the response to the
request with `requestId`, not the response message `id`.

Supported methods are `button_click`, `enter_text`, `move_mouse`, `mouse_down`,
`mouse_up`, `key_click`, `key_down`, `key_up`, `set_axis`, `set_button`,
`pause_time`, `resume_time`, `reset_game`, `start_readings`, `stop_readings`,
and `capture_screen`.

`capture_screen` returns the encoded image inline in `returnValue.data` together
with its MIME type, dimensions, optional target identifier, and clipped flag.
It does not upload the image or return an expiring URL.
