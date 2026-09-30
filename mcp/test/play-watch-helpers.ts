import type { EntityRef } from "../src/play-types.js";
import { SESSION } from "./play-fake.js";

export function entityRefFor(id: number, generation = 1): EntityRef {
  return { sessionId: SESSION, id, generation };
}
