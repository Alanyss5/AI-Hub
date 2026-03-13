#!/usr/bin/env node
import { createRequire } from "node:module";
var __create = Object.create;
var __getProtoOf = Object.getPrototypeOf;
var __defProp = Object.defineProperty;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __toESM = (mod, isNodeMode, target) => {
  target = mod != null ? __create(__getProtoOf(mod)) : {};
  const to = isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target;
  for (let key of __getOwnPropNames(mod))
    if (!__hasOwnProp.call(to, key))
      __defProp(to, key, {
        get: () => mod[key],
        enumerable: true
      });
  return to;
};
var __require = /* @__PURE__ */ createRequire(import.meta.url);

// src/integration-tests/mock-lsp-server.ts
import { createInterface } from "node:readline";

class MockLSPServer {
  config;
  requestCount = 0;
  initialized = false;
  openedFiles = new Set;
  fileVersions = new Map;
  constructor(config = {}) {
    this.config = config;
  }
  async start() {
    const rl = createInterface({
      input: process.stdin,
      output: process.stdout,
      terminal: false
    });
    let buffer = "";
    rl.on("line", (line) => {
      buffer += `${line}\r
`;
      const contentLengthMatch = buffer.match(/Content-Length: (\d+)\r\n/);
      if (!contentLengthMatch)
        return;
      const contentLength = Number.parseInt(contentLengthMatch[1], 10);
      const messageStart = buffer.indexOf(`\r
\r
`);
      if (messageStart === -1)
        return;
      const bodyStart = messageStart + 4;
      const bodyEnd = bodyStart + contentLength;
      if (buffer.length < bodyEnd)
        return;
      const body = buffer.slice(bodyStart, bodyEnd);
      buffer = buffer.slice(bodyEnd);
      try {
        const message = JSON.parse(body);
        this.handleMessage(message);
      } catch (error) {
        this.sendError(-32700, "Parse error", null);
      }
    });
  }
  handleMessage(message) {
    this.requestCount++;
    const behaviors = this.config.behaviors || {};
    if (behaviors.crashOnRequest === message.method) {
      process.exit(1);
    }
    if (behaviors.timeoutOnRequest === message.method) {
      return;
    }
    if (behaviors.invalidJsonAfterRequests && this.requestCount > behaviors.invalidJsonAfterRequests) {
      this.sendRaw('{"invalid": json}');
      return;
    }
    if (behaviors.delayMs) {
      setTimeout(() => this.processMessage(message), behaviors.delayMs);
    } else {
      this.processMessage(message);
    }
  }
  processMessage(message) {
    const { id, method, params } = message;
    if (this.config.behaviors?.returnErrorFor?.includes(method)) {
      this.sendError(-32603, `Mock error for ${method}`, id);
      return;
    }
    if (this.config.responses?.[method] !== undefined) {
      if (id !== undefined) {
        this.sendResponse(id, this.config.responses[method]);
      }
      return;
    }
    switch (method) {
      case "initialize":
        this.handleInitialize(id, params);
        break;
      case "initialized":
        this.initialized = true;
        break;
      case "shutdown":
        this.sendResponse(id, null);
        break;
      case "exit":
        process.exit(0);
        break;
      case "textDocument/didOpen":
        this.handleDidOpen(params);
        break;
      case "textDocument/didClose":
        this.handleDidClose(params);
        break;
      case "textDocument/didChange":
        this.handleDidChange(params);
        break;
      case "textDocument/documentSymbol":
        this.handleDocumentSymbol(id, params);
        break;
      case "textDocument/definition":
        this.handleDefinition(id, params);
        break;
      case "textDocument/references":
        this.handleReferences(id, params);
        break;
      case "textDocument/diagnostic":
        this.handleDiagnostic(id, params);
        break;
      case "textDocument/rename":
        this.handleRename(id, params);
        break;
      case "textDocument/prepareRename":
        this.handlePrepareRename(id, params);
        break;
      default:
        if (id !== undefined) {
          this.sendError(-32601, `Method not found: ${method}`, id);
        }
    }
  }
  handleInitialize(id, params) {
    const result = {
      capabilities: this.config.capabilities || {
        textDocumentSync: 1,
        definitionProvider: true,
        referencesProvider: true,
        documentSymbolProvider: true,
        renameProvider: {
          prepareProvider: true
        },
        diagnosticProvider: {
          interFileDependencies: false,
          workspaceDiagnostics: false
        }
      },
      serverInfo: this.config.serverInfo || {
        name: "mock-lsp-server",
        version: "1.0.0"
      }
    };
    this.sendResponse(id, result);
  }
  handleDidOpen(params) {
    const uri = params.textDocument.uri;
    this.openedFiles.add(uri);
    this.fileVersions.set(uri, params.textDocument.version);
  }
  handleDidClose(params) {
    const uri = params.textDocument.uri;
    this.openedFiles.delete(uri);
    this.fileVersions.delete(uri);
  }
  handleDidChange(params) {
    const uri = params.textDocument.uri;
    this.fileVersions.set(uri, params.textDocument.version);
  }
  handleDocumentSymbol(id, params) {
    const symbols = this.config.symbols || [];
    const documentSymbols = symbols.filter((s) => s.location.uri === params.textDocument.uri).map((s) => ({
      name: s.name,
      kind: s.kind,
      range: s.location.range,
      selectionRange: s.location.range,
      children: []
    }));
    this.sendResponse(id, documentSymbols);
  }
  handleDefinition(id, params) {
    const key = this.makePositionKey(params.textDocument.uri, params.position);
    const definitions = this.config.definitions?.[key] || [];
    this.sendResponse(id, definitions);
  }
  handleReferences(id, params) {
    const key = this.makePositionKey(params.textDocument.uri, params.position);
    const references = this.config.references?.[key] || [];
    this.sendResponse(id, references);
  }
  handleDiagnostic(id, params) {
    const diagnostics = this.config.diagnostics?.[params.textDocument.uri] || [];
    this.sendResponse(id, {
      kind: "full",
      items: diagnostics
    });
  }
  handleRename(id, params) {
    const key = this.makePositionKey(params.textDocument.uri, params.position);
    const edit = this.config.renameEdits?.[key] || { changes: {} };
    this.sendResponse(id, edit);
  }
  handlePrepareRename(id, params) {
    this.sendResponse(id, {
      range: {
        start: params.position,
        end: params.position
      },
      placeholder: "symbol"
    });
  }
  makePositionKey(uri, position) {
    return `${uri}:${position.line}:${position.character}`;
  }
  sendResponse(id, result) {
    const message = {
      jsonrpc: "2.0",
      id,
      result
    };
    this.send(message);
  }
  sendError(code, message, id) {
    const response = {
      jsonrpc: "2.0",
      id,
      error: {
        code,
        message
      }
    };
    this.send(response);
  }
  send(message) {
    const json = JSON.stringify(message);
    const headers = `Content-Length: ${Buffer.byteLength(json)}\r
\r
`;
    process.stdout.write(headers + json);
  }
  sendRaw(data) {
    const headers = `Content-Length: ${Buffer.byteLength(data)}\r
\r
`;
    process.stdout.write(headers + data);
  }
}
if (process.argv[1] === import.meta.url.replace("file://", "")) {
  const configArg = process.argv[2];
  let config = {};
  if (configArg) {
    try {
      config = JSON.parse(configArg);
    } catch {
      try {
        const fs = await import("node:fs");
        config = JSON.parse(fs.readFileSync(configArg, "utf-8"));
      } catch (error) {
        process.stderr.write(`Failed to load config: ${error}
`);
        process.exit(1);
      }
    }
  }
  const server = new MockLSPServer(config);
  server.start();
}
var mock_lsp_server_default = MockLSPServer;
export {
  mock_lsp_server_default as default,
  MockLSPServer
};
