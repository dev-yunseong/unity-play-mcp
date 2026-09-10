/// `initialize` 응답에 실려 agent 의 context 에 들어가는 server 사용 안내.
///
/// tool 설명은 tool 하나가 무엇을 하는지 말하지만, tool 사이의 순서와 전제는 어디에도 없다. 그래서
/// agent 는 `start_readings` 없이 `get_scene_state` 를 부르고 빈 응답을 받은 뒤에야 순서를 배운다.
/// 여기 적는 것은 그 순서와 전제뿐이다. 이 문자열은 모든 대화에 들어가므로 tool 설명을 옮겨 적지 않는다.
export const serverInstructions = [
  "Unity Play MCP controls a game in Play Mode in the local Unity editor.",
  "Call get_unity_status first. If Unity is not running, ask the user to enter Play Mode instead of retrying.",
  "",
  "Reading the scene:",
  "- Call start_readings once before the first get_scene_state.",
  "- Readings arrive about once a second. After an action, compare reading numbers or call wait_for_condition for a scene or member value.",
  "- Narrow large scenes with selector, or use root and depth for a hierarchy.",
  "- Use search_targets to find compact ids/selectors by name, text, component, or actionability, then call get_scene_state.",
  "- Call stop_readings when done.",
  "",
  "Acting on the scene:",
  "- click, enter_text, and capture_screen take an instance id from get_scene_state. Read state first; never guess an id.",
  "- click is for UI Buttons. Use pointer_click for colliders and pointer handlers, or pointer_drag between ids.",
  "- Otherwise use move_mouse, mouse_button, press_key, set_axis, and set_button.",
  "- perform_actions sends actions together in one frame-aligned batch.",
  "",
  "Seeing the result:",
  "- capture_screen shows player-visible layout; scene state only reports values.",
  "- pause_game freezes time, resume_game continues, and reset_game starts over.",
].join("\n");
