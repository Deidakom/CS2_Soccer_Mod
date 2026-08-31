import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const bodyImpactPath = new URL(
  "../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.BodyImpact.cs",
  import.meta.url,
);

test("ball impact feedback never sends camera shake", async () => {
  const source = await readFile(bodyImpactPath, "utf8");

  assert.doesNotMatch(source, /FromPartialName\("Shake"\)/);
  assert.doesNotMatch(source, /shakeMessage\.Send\(\)/);
  assert.match(source, /FromPartialName\("Damage"\)/);
  assert.match(source, /ApplyBallImpactKnockback/);
});
