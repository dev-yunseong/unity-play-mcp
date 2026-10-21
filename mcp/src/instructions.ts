/// `initialize` 응답에 실려 agent 의 context 에 들어가는 server 사용 안내.
///
/// tool 설명에 없는 tool 사이의 순서와 전제만 적는다. 모든 대화에 들어가므로 tool 설명을 반복하지 않는다.
export const serverInstructions = [
  "Unity Play MCP controls a game in Play Mode in the local Unity editor.",
  "Call get_unity_status first; resolve version warnings and request Play Mode if needed.",
  "",
  "Playing (observe, decide, act, check):",
  "- Call get_play_capabilities; on unsupported_capability use the legacy tools below.",
  '- Before each live step, observe with freshness: "current"; use its refs and observationId.',
  "- Call act_and_observe with basedOnObservationId and expect predicates for the intended effect, based on observed facts or inspect_action. Do not invent missing values.",
  "- New input needs a new operationId; retry only the identical request with the same id.",
  "- execution accepts input; outcome confirms effects. Missing evidence is unknown, never success. Re-observe before retrying an unconfirmed effect. watch_events waits for changes.",
  "",
  "Legacy readings and input:",
  "- Queries resume readings after 15s idle; updates arrive about once a second. Use wait_for_condition to check effects, not just a newer reading number.",
  "- A stale result holds old ids: call start_readings and read again. Call stop_readings when done.",
  "- search_targets finds ids/selectors; get_scene_state reads values. Narrow with selector or root/depth.",
  "- targetId is an instance id from scene state; never guess an id. Re-read after reload or object recreation.",
  "- click, hover and drag use normal input with targetId, selector or x/y; drag takes from/to. enter_text takes targetId or selector.",
  "- Other input: move_mouse, mouse_button, press_key, set_axis, set_button. perform_actions batches inputs.",
  "",
  "Seeing the result:",
  "- capture_screen shows player-visible layout; values alone do not prove visual results.",
  "- set_overlay shows input UI; 15s idle hides it.",
  "- pause_game freezes time, resume_game continues, reset_game starts over.",
].join("\n");
