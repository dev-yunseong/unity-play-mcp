import assert from "node:assert/strict";
import test from "node:test";

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

import type { UnityConnection } from "../src/connection.js";
import { PulseStore } from "../src/pulse.js";
import { registerTools } from "../src/tools.js";

const REGISTERED_TOOL_COUNT = 27;
