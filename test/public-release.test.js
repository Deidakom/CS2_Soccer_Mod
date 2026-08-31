import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("public build never grants a hard-coded Steam account root access", async () => {
  const source = await readFile(
    new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Admin.cs", import.meta.url),
    "utf8",
  );

  assert.doesNotMatch(source, /RootAdminSteamId64|self_healing_root_admin/);
  assert.match(source, /css_admin_add <steamid64> root/);
});
