#!/usr/bin/env node

import net from "node:net";

// Minimal Source 2 VConsole2 client. The current 12-byte header and uint32
// total-frame length at byte 6 were verified against a live August 2026 CS2
// client and the open-source Dota 2 VConsole relay implementation:
// https://github.com/Demon673/dota2-mcp/blob/master/src/tools/vcon-relay.ts
// Common older layouts that treat bytes 4..7 as a uint32 version can send
// small commands by accident but misparse today's large CVRB handshake frame.

const HEADER_SIZE = 12;
// This is current-build evidence, not a negotiated protocol version. Reverify
// it after a Valve update if VConsole stops accepting commands.
const COMMAND_VERSION = 0x00d4;

const args = process.argv.slice(2);
const debugIndex = args.indexOf("--debug");
const debug = debugIndex >= 0;
if (debug) args.splice(debugIndex, 1);
function takeNumberOption(name, fallback, minimum = 1) {
  const index = args.indexOf(name);
  if (index < 0) return fallback;
  const value = Number(args[index + 1]);
  args.splice(index, 2);
  if (!Number.isInteger(value) || value < minimum) {
    throw new Error(`invalid ${name}: ${value}`);
  }
  return value;
}

const port = takeNumberOption("--port", 29000);
const timeoutMs = takeNumberOption("--timeout-ms", 8000);
const settleMs = takeNumberOption("--settle-ms", 0, 0);
const commands = args.length > 0 ? args : ["status"];
const marker = `SM2_VCON_${process.pid}_${Date.now()}`;

function commandPacket(command) {
  const body = Buffer.from(`${command}\0`, "utf8");
  const length = HEADER_SIZE + body.length;
  if (length > 0xffffffff) throw new Error("VConsole command is too long");

  const header = Buffer.alloc(HEADER_SIZE);
  header.write("CMND", 0, 4, "ascii");
  header.writeUInt16BE(COMMAND_VERSION, 4);
  header.writeUInt32BE(length, 6);
  header.writeUInt16BE(0, 10);
  return Buffer.concat([header, body]);
}

function printableMessage(body) {
  if (body.length < 28) return "";
  const raw = body.subarray(28);
  const nul = raw.indexOf(0);
  const message = nul >= 0 ? raw.subarray(0, nul) : raw;
  return Buffer.from([...message].filter((byte) => byte <= 127)).toString("ascii");
}

const socket = net.createConnection({ host: "127.0.0.1", port });
let pending = Buffer.alloc(0);
let capture = false;
let sawEnd = false;
let packetCount = 0;
let settleTimer;
let endRequested = false;

function requestEnd(commandsBeforeEnd = []) {
  if (socket.destroyed || endRequested) return;
  endRequested = true;
  const framed = [
    ...commandsBeforeEnd.map(commandPacket),
    commandPacket(`echoln ${marker}_END`),
  ];
  socket.write(Buffer.concat(framed));
}

socket.on("connect", () => {
  const framed = [
    commandPacket(`echoln ${marker}_BEGIN`),
    ...commands.map(commandPacket),
  ];
  socket.write(Buffer.concat(framed));
  settleTimer = setTimeout(() => {
    requestEnd();
  }, settleMs);
});

process.on("message", (message) => {
  if (message?.type === "commands") {
    const commands = Array.isArray(message.commands)
      ? message.commands.filter((command) => typeof command === "string" && command.length > 0)
      : [];
    if (commands.length > 0 && !socket.destroyed) {
      socket.write(Buffer.concat(commands.map(commandPacket)));
    }
    return;
  }
  if (message?.type !== "stop") return;
  const commandsBeforeEnd = Array.isArray(message.commands)
    ? message.commands.filter((command) => typeof command === "string" && command.length > 0)
    : [];
  requestEnd(commandsBeforeEnd);
});

socket.on("data", (chunk) => {
  pending = Buffer.concat([pending, chunk]);
  while (pending.length >= HEADER_SIZE) {
    const type = pending.toString("ascii", 0, 4);
    const length = pending.readUInt32BE(6);
    if (length < HEADER_SIZE || length > 16 * 1024 * 1024) {
      throw new Error(
        `invalid VConsole packet length ${length}; header=${pending.subarray(0, HEADER_SIZE).toString("hex")}`,
      );
    }
    if (pending.length < length) return;
    packetCount += 1;
    if (debug) {
      console.error(`#${packetCount} type=${JSON.stringify(type)} length=${length} header=${pending.subarray(0, HEADER_SIZE).toString("hex")}`);
    }

    const body = pending.subarray(HEADER_SIZE, length);
    pending = pending.subarray(length);
    if (type !== "PRNT") continue;

    const message = printableMessage(body);
    if (message.includes(`${marker}_BEGIN`)) {
      capture = true;
      continue;
    }
    if (message.includes(`${marker}_END`)) {
      sawEnd = true;
      capture = false;
      setTimeout(() => socket.end(), 100);
      continue;
    }
    if (capture) process.stdout.write(message);
  }
});

socket.on("error", (error) => {
  console.error(`VConsole error: ${error.message}`);
  process.exitCode = 1;
});

socket.on("close", () => {
  clearTimeout(settleTimer);
  clearTimeout(timeout);
  if (!sawEnd && !commands.includes("quit")) {
    console.error("VConsole closed before the end marker was observed");
    process.exitCode = 1;
  }
});

const timeout = setTimeout(() => {
  if (socket.destroyed) return;
  console.error(`VConsole timed out after ${timeoutMs} ms`);
  process.exitCode = 1;
  socket.destroy();
}, timeoutMs);
