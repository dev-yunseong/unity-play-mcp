import { z } from "zod";
import type { UnityConnection } from "./connection.js";

const exactVersion = z.string().regex(/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/);
const metadata = z.object({
  runtimeVersion: exactVersion,
  requiredServerVersion: exactVersion,
});

export interface VersionCompatibility {
  status: "matched" | "mismatch" | "unknown";
  cliVersion: string;
  sdkVersion?: string;
  requiredCliVersion?: string;
  message: string;
}

export function compareVersions(cliVersion: string, raw: unknown): VersionCompatibility {
  const parsed = metadata.safeParse(raw);
  if (!parsed.success || !exactVersion.safeParse(cliVersion).success) {
    return unknownCompatibility(cliVersion);
  }
  const { runtimeVersion, requiredServerVersion } = parsed.data;
  const matched = cliVersion === requiredServerVersion;
  return {
    status: matched ? "matched" : "mismatch",
    cliVersion,
    sdkVersion: runtimeVersion,
    requiredCliVersion: requiredServerVersion,
    message: matched
      ? `CLI ${cliVersion} matches the version required by SDK ${runtimeVersion}.`
      : `Update required: running CLI ${cliVersion}; SDK ${runtimeVersion} requires CLI ${requiredServerVersion}. `
        + "In Unity, open Edit > Project Settings > Unity Play MCP, select the configured scope, and click Update CLI. "
        + `This pins unity-play-mcp@${requiredServerVersion} (which may be older). Restart the agent so npx installs/runs it. `
        + "Local or custom launches must be rebuilt or updated manually; changing configuration does not update this running process.",
  };
}

function unknownCompatibility(cliVersion: string): VersionCompatibility {
  return {
    status: "unknown",
    cliVersion,
    message: `CLI ${cliVersion}; SDK compatibility is unknown. The SDK may be older, or the version query failed. `
      + "Check the Unity Play MCP package and its compatible CLI in Unity Project Settings. Existing tools remain available.",
  };
}

/// 기존 connection을 짧게 조회한다. 오래된 SDK의 미지원 응답이 접속 상태 조회를 실패시키지 않는다.
export async function probeCompatibility(
  connection: Pick<UnityConnection, "sendActions" | "isConnected">,
  cliVersion: string,
  requestId: number,
): Promise<VersionCompatibility> {
  try {
    const results = await connection.sendActions(
      [{ id: requestId, method: "play_capabilities", params: [] }],
      { timeoutMilliseconds: 1_500 },
    );
    if (!connection.isConnected()) return unknownCompatibility(cliVersion);
    const result = results.find((item) => item.id === requestId);
    return compareVersions(cliVersion, result?.success ? result.returnValue : undefined);
  } catch {
    return unknownCompatibility(cliVersion);
  }
}
