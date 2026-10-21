import { z } from "zod";
import type { UnityConnection } from "./connection.js";
import type { PulseStore } from "./pulse.js";

const activitySchema = z.object({ reading: z.boolean(), resumed: z.boolean(), run: z.string().nullable() });
type ActivityConnection = Pick<UnityConnection, "sendActions" | "onDisconnect" | "isConnected">;

/// 캐시를 쓰는 요청도 Unity에 전달한다. 응답 run의 whole 상태가 없으면 이전 cache를 반환하지 않는다.
export async function requestRuntimeActivity(
  connection: ActivityConnection, store: PulseStore, id: number, intent: "status" | "readings",
  signal: AbortSignal = new AbortController().signal, timeoutMs = 2_000,
): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  if (signal.aborted) throw new Error("Request cancelled.");
  const controller = new AbortController();
  const cancel = () => controller.abort();
  signal.addEventListener("abort", cancel, { once: true });
  const timeout = setTimeout(cancel, Math.max(1, timeoutMs));
  try {
    const results = await connection.sendActions([{ id, method: "runtime_activity", params: [intent] }],
      { timeoutMilliseconds: Math.max(1, timeoutMs), signal: controller.signal });
    const result = results.find((item) => item.id === id);
    const parsed = activitySchema.safeParse(result?.returnValue);
    if (!result?.success || !parsed.success)
      throw new Error("Runtime activity is unavailable. Update the Unity Play MCP SDK to match this CLI.");
    if (signal.aborted) throw new Error("Request cancelled.");
    if (!connection.isConnected()) throw new Error("Unity disconnected.");
    if (!parsed.data.reading) store.markInterrupted("stopped");
    if (intent === "status") return;
    const run = parsed.data.run;
    if (!parsed.data.reading || run === null) throw new Error("Unity did not start scene readings.");
    const fresh = () => store.getState()?.run === run && store.getStaleness() === undefined;
    if (fresh()) return;
    store.markInterrupted("stopped");
    await new Promise<void>((resolve, reject) => {
      const finish = (error?: Error) => {
        clearTimeout(timer);
        offReading(); offDisconnect();
        signal.removeEventListener("abort", abort);
        error ? reject(error) : resolve();
      };
      const abort = () => finish(new Error("Request cancelled."));
      const offReading = store.onReading(() => { if (fresh()) finish(); });
      const offDisconnect = connection.onDisconnect(() => finish(new Error("Unity disconnected.")));
      const timer = setTimeout(() => finish(new Error("No fresh scene reading arrived after resuming Unity.")),
        Math.max(1, deadline - Date.now()));
      signal.addEventListener("abort", abort, { once: true });
    });
    } finally { clearTimeout(timeout); signal.removeEventListener("abort", cancel); }
}

/// 대기 중인 tool만 activity를 갱신한다. 요청을 겹치지 않고 종료 후 새 timer를 만들지 않는다.
export function keepReadingsActive(refresh: () => Promise<void>, signal: AbortSignal, intervalMs = 5_000): () => void {
  let stopped = false;
  let timer: ReturnType<typeof setTimeout>;
  const stop = () => { stopped = true; clearTimeout(timer); signal.removeEventListener("abort", stop); };
  const schedule = () => {
    if (stopped) return;
    timer = setTimeout(async () => {
      if (stopped) return;
      try { await refresh(); } catch { stop(); }
      schedule();
    }, intervalMs);
  };
  signal.addEventListener("abort", stop, { once: true });
  if (signal.aborted) stop(); else schedule();
  return stop;
}
