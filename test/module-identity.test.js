import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("plugin exposes the current CS2 SoccerMod release identity", async () => {
  const source = await readFile(
    new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs", import.meta.url),
    "utf8",
  );

  assert.match(source, /ModuleName => "CS2 SoccerMod"/);
  assert.match(source, /ModuleVersion => "1\.4\.9-dev"/);
  assert.doesNotMatch(source, /CS2 SoccerMod Ball|4\.0\.0-alpha/);
});
