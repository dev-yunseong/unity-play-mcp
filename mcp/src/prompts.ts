import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";

/// 사용자가 골라서 부르는 정형 작업 하나.
///
/// `instructions` 와 나누는 기준은 누가 고르느냐다. agent 가 스스로 판단해야 하는 전제와 순서는
/// `instructions` 에 있고, 여기 있는 것은 사용자가 slash command 로 직접 고르는 작업이다.
export interface UnityPrompt {
  name: string;
  title: string;
  description: string;
  /// MCP 의 prompt 인자는 문자열만 받는다. 숫자나 boolean 을 받고 싶으면 문자열로 받아 render 가 읽는다.
  argsSchema: Record<string, z.ZodType<string | undefined>>;
  render(args: Record<string, string | undefined>): string;
}

function lines(...parts: string[]): string {
  return parts.join("\n");
}

export const unityPrompts: readonly UnityPrompt[] = [
  {
    name: "inspect_scene",
    title: "Inspect the running scene",
    description:
      "Read the current Unity scene and report what is on it and which objects can be acted on.",
    argsSchema: {
      selector: z
        .string()
        .min(1)
        .optional()
        .describe("Narrow the reading to objects whose selector contains this text."),
    },
    render: ({ selector }) =>
      lines(
        "Inspect the Unity scene that is running right now.",
        "",
        "1. Call start_readings, then get_scene_state.",
        selector === undefined
          ? "2. If the scene is large, narrow the next reading with a selector or walk it with root and depth rather than dumping everything."
          : `2. Pass selector "${selector}" so the reading covers only the objects you were asked about.`,
        "3. Report what the scene holds: the objects a player can act on, each one's instance id, and what each appears to do.",
        "4. Say which objects you could not classify and why, rather than guessing.",
        "",
        "Do not act on the scene. This is a reading only.",
      ),
  },
  {
    name: "review_screen",
    title: "Review what the player sees",
    description:
      "Capture the game screen and review the layout, readability, and anything that looks wrong.",
    argsSchema: {
      focus: z
        .string()
        .min(1)
        .optional()
        .describe("What to pay attention to, such as a screen name or a suspected problem."),
    },
    render: ({ focus }) =>
      lines(
        "Review what the player sees in the Unity game running right now.",
        "",
        "1. Call capture_screen for the whole screen.",
        focus === undefined
          ? "2. Describe what is on screen, then review it: text that is too small or clipped, elements that overlap, contrast that is hard to read, and controls whose purpose is not obvious."
          : `2. Describe what is on screen, then review it with this in focus: ${focus}`,
        "3. For anything you suspect but cannot confirm from a still image, call get_scene_state and check the values behind it.",
        "4. Report each finding with what you saw and what you expected. Say plainly which findings the capture alone cannot settle.",
      ),
  },
  {
    name: "run_steps",
    title: "Run steps and report where they diverge",
    description:
      "Perform a described sequence of player actions and report the first step whose result differed from the expectation.",
    argsSchema: {
      steps: z
        .string()
        .min(1)
        .describe("The steps to perform, one per line, with what each is expected to do."),
      expectation: z
        .string()
        .min(1)
        .optional()
        .describe("What the whole sequence should end with."),
    },
    render: ({ steps, expectation }) =>
      lines(
        "Run these steps in the Unity game that is running right now.",
        "",
        steps ?? "",
        "",
        "How to run them:",
        "1. Call start_readings, then get_scene_state, and find the instance id for each object a step names. Never guess an id.",
        "2. Perform one step at a time. After each, read the state again and confirm the reading number moved before judging the result.",
        "3. Call capture_screen at any step whose result is visual.",
        "4. Stop at the first step whose result differs from what the step said it would do, and report that step with what you saw.",
        ...(expectation === undefined
          ? []
          : ["", `The sequence should end with: ${expectation}`]),
        "",
        "If every step matched, say so and report the final state.",
      ),
  },
  {
    name: "track_value",
    title: "Track how a value moves",
    description:
      "Watch how the members of an object change across readings, optionally while an action runs.",
    argsSchema: {
      selector: z
        .string()
        .min(1)
        .describe("The object to watch, matched against the selector reported by get_scene_state."),
      action: z
        .string()
        .min(1)
        .optional()
        .describe("What to do while watching, such as clicking a button."),
    },
    render: ({ selector, action }) =>
      lines(
        `Track how the values on "${selector ?? ""}" move in the Unity game running right now.`,
        "",
        "1. Call start_readings, then get_scene_state with includeHistory set and this selector.",
        "2. Note each member's current value and the reading it last moved on.",
        ...(action === undefined
          ? ["3. Wait for several readings, then read again with includeHistory."]
          : [`3. Do this: ${action}`, "4. Wait for the reading number to move, then read again with includeHistory."]),
        "",
        "Report which members moved, what they moved from and to, and on which readings. A member that did not move is worth reporting too when the action was expected to move it.",
      ),
  },
];

export function registerPrompts(server: McpServer): void {
  for (const prompt of unityPrompts) {
    server.registerPrompt(
      prompt.name,
      {
        title: prompt.title,
        description: prompt.description,
        argsSchema: prompt.argsSchema,
      },
      (args) => ({
        messages: [{
          role: "user",
          content: { type: "text", text: prompt.render(args as Record<string, string | undefined>) },
        }],
      }),
    );
  }
}
