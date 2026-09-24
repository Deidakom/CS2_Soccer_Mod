import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');
const elo = read('SoccerModMvpPlugin.Elo.cs');
const match = read('SoccerModMvpPlugin.Match.cs');
const cap = read('SoccerModMvpPlugin.Cap.cs');
const afk = read('SoccerModMvpPlugin.Afk.cs');
const main = read('SoccerModMvpPlugin.cs');
const menu = read('SoccerModMvpPlugin.Menu.cs');

test('ELO is loaded, persisted and reachable from !elo and the main menu', () => {
  assert.match(main, /EloOnLoad\(\);/);
  assert.match(elo, /EloFileName = "soccermod_elo\.json"/);
  assert.match(elo, /AddCommand\("css_elo"/);
  assert.match(menu, /menu\.Add\("ELO Ranking", OpenEloMenu\)/);
});

test('ratings follow the match lifecycle: kickoff roster, full time only, stop and AFK void it', () => {
  assert.match(match, /EloOnMatchStart\(\);/);
  assert.match(match, /EloOnMatchFinished\(forfeitWinner is not null\);\s*RestoreMatchOnlyTeamNames\(\);\s*StatsOnMatchFinished\(\);/);
  assert.match(match, /EloOnMatchStopped\(\);/);
  assert.match(afk, /if \(MatchRunning\) EloInvalidateMatch\("AFK kick"\);/);
  assert.match(elo, /if \(forfeit \|\| _scoreCt == _scoreT\)/);
});

test('scores and match team names follow the squads across the halftime swap', () => {
  assert.match(match, /_teamsSwapped = !_teamsSwapped;\s*(\/\/.*\s*)*\(_scoreCt, _scoreT\) = \(_scoreT, _scoreCt\);\s*\(_teamNameCt, _teamNameT\) = \(_teamNameT, _teamNameCt\);/);
  assert.match(elo, /var winningKickoffSide = _teamsSwapped \? OppositeTeam\(winningTeam\) : winningTeam;/);
});

test('cap: the lower-rated captain picks first past the gap, picks show ELO, first picks are protected', () => {
  assert.match(cap, /if \(EloDecideFirstPick\(fighters\) is \{ \} eloFirstPickTeam\)/);
  assert.match(elo, /var firstTeam = ratingCt < ratingT \? CsTeam\.CounterTerrorist : CsTeam\.Terrorist;/);
  assert.match(cap, /EloOnDraftStart\(\);/);
  assert.match(cap, /EloOnCapPick\(picker\.Team, targetId\);/);
  assert.match(cap, /EloPickLabel\(targetId\)/);
});

test('halftime vote runs at the period break and keeps the draft enforcement in step', () => {
  assert.match(match, /EloOnHalftime\(\(float\)_breakLengthSeconds\);/);
  assert.match(elo, /if \(_draftAssignments\.ContainsKey\(a\)\) _draftAssignments\[a\] = teamB;/);
  assert.match(elo, /EloMath\.TallySwapVote\(counts, keep\)/);
});

test('admin ELO tools are gated by the admin flag', () => {
  for (const method of ['OpenEloPlayerPicker', 'EloBeginRename', 'OpenEloAdjust', 'EloAdjust']) {
    const start = elo.indexOf(`private void ${method}(`);
    assert.ok(start > 0, method);
    assert.match(elo.slice(start, start + 200), /HasFlag\(SteamIdOf\((player|admin)\), "admin"\)/, method);
  }
});
