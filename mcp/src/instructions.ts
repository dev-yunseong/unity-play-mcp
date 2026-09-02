/// `initialize` 응답에 실려 agent 의 context 에 들어가는 server 사용 안내.
///
/// tool 설명은 tool 하나가 무엇을 하는지 말하지만, tool 사이의 순서와 전제는 어디에도 없다. 그래서
/// agent 는 `start_readings` 없이 `get_scene_state` 를 부르고 빈 응답을 받은 뒤에야 순서를 배운다.
/// 여기 적는 것은 그 순서와 전제뿐이다. 이 문자열은 모든 대화에 들어가므로 tool 설명을 옮겨 적지 않는다.
export const serverInstructions = [
  "Unity Play MCP drives a Unity game running in the Unity editor on this machine.",
  "Every tool talks to that editor over a local WebSocket, so nothing works unless the user has the Unity project open and has entered Play Mode.",
  "Call get_unity_status first. It answers whether the game is running and whether readings have started, and it never fails just because Unity is off. Any other tool that reports Unity is not running means the same thing: ask the user to enter Play Mode instead of retrying.",
  "",
  "Reading the scene:",
  "- Call start_readings once before the first get_scene_state. Without it get_scene_state answers that no reading has arrived.",
  "- Readings arrive about once a second. After an action, give the game a moment before reading the state again, and compare the reading number to confirm you are not looking at the same reading twice.",
  "- A full scene is large. Narrow it with selector, or pass root and depth to walk the hierarchy instead of the flat list.",
  "- Call stop_readings when the user is done, so the game stops paying for readings it does not need.",
  "",
  "Acting on the scene:",
  "- click, enter_text, and capture_screen take the instance id that get_scene_state reports as each object's id. Read the state first; never guess an id.",
  "- For a game that does not use Unity UI, drive it with move_mouse, mouse_button, press_key, set_axis, and set_button instead.",
  "- perform_actions sends several of those in one frame-aligned batch. Use it when the actions must land together, such as holding a key while moving the mouse.",
  "",
  "Seeing the result:",
  "- capture_screen is the only tool that shows what the player sees. The scene state reports values, not layout, so verify anything visual with a capture.",
  "- pause_game freezes game time while you inspect, and resume_game continues. reset_game starts the game over.",
].join("\n");
