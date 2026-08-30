import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const RUNTIME_MODULE = "cs_script/point_script";
const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]*$/;
const IMPORT_PATTERN = /^\s*import\s*\{([\s\S]*?)\}\s*from\s*["']([^"']+)["'];[ \t]*(?:\r?\n|$)/gm;
const EXPORT_PATTERN = /^export\s+(?:const|function)\s+([A-Za-z_$][A-Za-z0-9_$]*)/gm;

const MODULES = Object.freeze([
  Object.freeze({
    id: "vector",
    file: "src/ball-lab/core/vector.js",
    namespace: "__sm2Vector",
  }),
  Object.freeze({
    id: "goal",
    file: "src/ball-lab/core/goal.js",
    namespace: "__sm2Goal",
  }),
  Object.freeze({
    id: "kick",
    file: "src/ball-lab/core/kick.js",
    namespace: "__sm2Kick",
  }),
  Object.freeze({
    id: "reset",
    file: "src/ball-lab/core/reset.js",
    namespace: "__sm2Reset",
  }),
  Object.freeze({
    id: "cap",
    file: "src/ball-lab/core/cap.js",
    namespace: "__sm2Cap",
  }),
  Object.freeze({
    id: "match",
    file: "src/ball-lab/core/match.js",
    namespace: "__sm2Match",
  }),
  Object.freeze({
    id: "layout",
    file: "src/ball-lab/layout.js",
    namespace: "__sm2Layout",
  }),
  Object.freeze({
    id: "physics-diagnostics",
    file: "src/ball-lab/physics-diagnostics.js",
    namespace: "__sm2PhysicsDiagnostics",
  }),
]);

const ADAPTER_FILE = "src/ball-lab/engine/adapter.js";

function normalizeSource(source) {
  return source.replaceAll("\r\n", "\n").replaceAll("\r", "\n");
}

function parseImportedNames(text, sourceLabel) {
  const names = text.split(",").map((name) => name.trim()).filter(Boolean);
  if (names.length === 0 || names.some((name) => !IDENTIFIER.test(name))) {
    throw new Error(`${sourceLabel} has an unsupported named import`);
  }
  if (new Set(names).size !== names.length) {
    throw new Error(`${sourceLabel} imports the same name more than once`);
  }
  return names;
}

function extractImports(source, sourceLabel) {
  const imports = [];
  const body = source.replace(
    IMPORT_PATTERN,
    (_, namesText, specifier) => {
      imports.push({
        names: parseImportedNames(namesText, sourceLabel),
        specifier,
      });
      return "";
    },
  );
  if (/^\s*import\b/m.test(body)) {
    throw new Error(`${sourceLabel} contains an unsupported import form`);
  }
  return { imports, body: body.trimStart() };
}

function collectExports(source, sourceLabel) {
  const names = [...source.matchAll(EXPORT_PATTERN)].map((match) => match[1]);
  if (names.length === 0) {
    throw new Error(`${sourceLabel} exports no supported declarations`);
  }
  if (new Set(names).size !== names.length) {
    throw new Error(`${sourceLabel} exports the same name more than once`);
  }
  const body = source.replace(/^export\s+(?=(?:const|function)\b)/gm, "");
  if (/^\s*export\b/m.test(body)) {
    throw new Error(`${sourceLabel} contains an unsupported export form`);
  }
  return { names, body };
}

function indent(source, spaces) {
  const prefix = " ".repeat(spaces);
  return source.split("\n").map((line) => `${prefix}${line}`).join("\n");
}

function moduleByResolvedPath(projectRoot) {
  return new Map(MODULES.map((module) => [
    path.resolve(projectRoot, module.file).toLowerCase(),
    module,
  ]));
}

function resolveLocalImport(importerPath, specifier, moduleLookup, sourceLabel) {
  if (!specifier.startsWith(".")) {
    throw new Error(`${sourceLabel} imports unsupported module '${specifier}'`);
  }
  const resolved = path.resolve(path.dirname(importerPath), specifier).toLowerCase();
  const dependency = moduleLookup.get(resolved);
  if (!dependency) {
    throw new Error(`${sourceLabel} imports unmanaged local module '${specifier}'`);
  }
  return dependency;
}

function renderModule(projectRoot, module, moduleLookup, sourceRecords) {
  const absolutePath = path.resolve(projectRoot, module.file);
  const source = normalizeSource(fs.readFileSync(absolutePath, "utf8"));
  sourceRecords.push({ file: module.file, source });
  const parsed = extractImports(source, module.file);
  const exported = collectExports(parsed.body, module.file);
  const importBindings = parsed.imports.map((entry) => {
    const dependency = resolveLocalImport(
      absolutePath,
      entry.specifier,
      moduleLookup,
      module.file,
    );
    return `const { ${entry.names.join(", ")} } = ${dependency.namespace};`;
  });
  const sections = [];
  if (importBindings.length > 0) sections.push(importBindings.join("\n"));
  sections.push(exported.body.trim());
  sections.push(`return Object.freeze({ ${exported.names.join(", ")} });`);
  return `const ${module.namespace} = (() => {\n${indent(sections.join("\n\n"), 2)}\n})();`;
}

function renderAdapter(projectRoot, moduleLookup, sourceRecords) {
  const absolutePath = path.resolve(projectRoot, ADAPTER_FILE);
  const source = normalizeSource(fs.readFileSync(absolutePath, "utf8"));
  sourceRecords.push({ file: ADAPTER_FILE, source });
  const parsed = extractImports(source, ADAPTER_FILE);
  const runtimeImports = parsed.imports.filter(({ specifier }) => specifier === RUNTIME_MODULE);
  const localImports = parsed.imports.filter(({ specifier }) => specifier !== RUNTIME_MODULE);
  if (runtimeImports.length !== 1) {
    throw new Error(`${ADAPTER_FILE} must import '${RUNTIME_MODULE}' exactly once`);
  }
  const runtimeNames = runtimeImports[0].names;
  const localBindings = localImports.map((entry) => {
    const dependency = resolveLocalImport(
      absolutePath,
      entry.specifier,
      moduleLookup,
      ADAPTER_FILE,
    );
    return `const { ${entry.names.join(", ")} } = ${dependency.namespace};`;
  });
  if (/^\s*export\b/m.test(parsed.body)) {
    throw new Error(`${ADAPTER_FILE} must not export runtime declarations`);
  }
  return {
    runtimeImport: `import { ${runtimeNames.join(", ")} } from "${RUNTIME_MODULE}";`,
    body: `${localBindings.join("\n")}\n\n${parsed.body.trim()}`,
  };
}

export function bundlePhase1Adapter(projectRoot) {
  if (typeof projectRoot !== "string" || projectRoot.length === 0) {
    throw new Error("project root is required");
  }
  const resolvedRoot = path.resolve(projectRoot);
  const moduleLookup = moduleByResolvedPath(resolvedRoot);
  const sourceRecords = [];
  const renderedModules = MODULES.map((module) => renderModule(
    resolvedRoot,
    module,
    moduleLookup,
    sourceRecords,
  ));
  const adapter = renderAdapter(resolvedRoot, moduleLookup, sourceRecords);
  const sourceManifest = sourceRecords
    .map(({ file, source }) => `${file}\0${source.length}\0${source}`)
    .join("\0");
  const sourceSha256 = crypto.createHash("sha256")
    .update(sourceManifest, "utf8")
    .digest("hex");
  const code = [
    "// Generated by tools/bundle-phase1-adapter.mjs; edit source modules instead.",
    `// Source manifest SHA-256: ${sourceSha256}`,
    adapter.runtimeImport,
    "",
    ...renderedModules.flatMap((module) => [module, ""]),
    adapter.body,
    "",
  ].join("\n");
  if (/\bfrom\s*["']\./.test(code)) {
    throw new Error("generated adapter still contains a relative module specifier");
  }
  return Object.freeze({
    code,
    sourceSha256,
    sources: Object.freeze(sourceRecords.map(({ file }) => file)),
  });
}

function runCli() {
  const [, , projectRoot, outputPath] = process.argv;
  if (!projectRoot || !outputPath) {
    throw new Error("usage: node bundle-phase1-adapter.mjs <project-root> <output.js>");
  }
  const bundle = bundlePhase1Adapter(projectRoot);
  const resolvedOutput = path.resolve(outputPath);
  fs.mkdirSync(path.dirname(resolvedOutput), { recursive: true });
  fs.writeFileSync(resolvedOutput, bundle.code, "utf8");
  process.stdout.write(`${JSON.stringify({
    output: resolvedOutput,
    sourceSha256: bundle.sourceSha256,
    sources: bundle.sources,
  })}\n`);
}

const invokedPath = process.argv[1] ? path.resolve(process.argv[1]) : "";
if (invokedPath === fileURLToPath(import.meta.url)) runCli();
