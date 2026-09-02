#!/usr/bin/env node
import { readFileSync } from "node:fs";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";

import { UnityConnection } from "./connection.js";
import { serverInstructions } from "./instructions.js";
import { PulseStore } from "./pulse.js";
import { registerTools } from "./tools.js";

const pulseStore = new PulseStore();
const connection = new UnityConnection({
  url: process.env.UNITY_PLAY_MCP_URL ?? "ws://127.0.0.1:17311/ws",
  timeoutMilliseconds: parseTimeout(process.env.UNITY_PLAY_MCP_TIMEOUT_MS),
  pulseStore,
});
const server = new McpServer(
  { name: "unity-play-mcp", version: packageVersion() },
  { instructions: serverInstructions },
);

registerTools(server, connection, pulseStore);
await server.connect(new StdioServerTransport());

function parseTimeout(rawValue: string | undefined): number {
  if (rawValue === undefined) return 15_000;
  const value = Number(rawValue);
  if (!Number.isInteger(value) || value <= 0) {
    throw new Error("UNITY_PLAY_MCP_TIMEOUT_MS must be a positive integer.");
  }
  return value;
}

function packageVersion(): string {
  const packageJsonUrl = new URL("../package.json", import.meta.url);
  const packageJson = JSON.parse(readFileSync(packageJsonUrl, "utf8")) as {
    version?: unknown;
  };

  if (typeof packageJson.version !== "string" || packageJson.version.length === 0) {
    throw new Error("package.json must contain a non-empty version.");
  }

  return packageJson.version;
}
