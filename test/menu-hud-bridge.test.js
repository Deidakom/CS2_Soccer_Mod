import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import test from 'node:test';

const source = await readFile(new URL('../src/workshop-addon/soccermod_classic_ui/maps/scripts/soccermod_classic_menu.js', import.meta.url), 'utf8');
function bridge() {
  const callbacks = new Map(), variables = new Map(), classes = new Map(), commands = [], capture = [];
  class Entity { IsValid() { return true; } }
  const layout = new Entity();
  layout.SetDialogVariableStringForPlayer = (slot, panel, key, value) => variables.set(`${slot}/${panel}/${key}`, value);
  layout.SetHasClassForPlayer = (slot, panel, key, value) => classes.set(`${slot}/${panel}/${key}`, value);
  layout.SetInputCaptureEnabled = (slot, enabled) => capture.push([slot, enabled]);
  let mounted = true;
  const Instance = {
    FindEntitiesByName: () => mounted ? [layout] : [],
    OnScriptInput: (key, action) => callbacks.set(key, action),
    ServerCommand: command => commands.push(command),
  };
  vm.runInNewContext(source.replace(/^import .*;\r?\n/, ''), { Entity, Instance });
  const apply = payload => callbacks.get('Apply')({ caller: { GetEntityName: () => `sm2h|${payload}` } });
  return { apply, variables, classes, commands, capture, callbacks, unmount: () => { mounted = false; } };
}
const hex = value => Buffer.from(value).toString('hex');

test('HUD waits for a real layout and probe before acknowledging readiness', () => {
  const b = bridge();
  assert.equal(b.commands.length, 0);
  b.unmount();
  b.callbacks.get('ReadyProbe')();
  assert.equal(b.commands.length, 0);
  const ready = bridge();
  ready.callbacks.get('ReadyProbe')();
  assert.deepEqual(ready.commands, ['css_sm2menu_classic_ready']);
});

test('HUD updates stay per player, preserve information rows and clear on close', () => {
  const b = bridge();
  b.apply(`begin|0|1|7|${hex('Ball – settings')}`);
  b.apply(`line|0|1|${hex('Power <1.0> & speed')}|0`);
  b.apply(`line|0|9|${hex('Next')}|1`);
  b.apply('show|0');
  b.apply(`begin|1|1|1|${hex('Help')}`);
  assert.equal(b.variables.get('0/menu/title'), 'Ball – settings');
  assert.equal(b.variables.get('1/menu/title'), 'Help');
  assert.equal(b.variables.get('0/menu/line1'), 'Power <1.0> & speed');
  assert.equal(b.classes.get('0/line_1/Disabled'), true);
  assert.equal(b.classes.get('0/line_9/Empty'), false);
  assert.equal(b.classes.get('0/menu/Hidden'), false);
  b.apply('close|0');
  assert.equal(b.classes.get('0/menu/Hidden'), true);
  assert.ok(b.capture.every(([, enabled]) => enabled === false));
  const before = b.variables.size;
  b.apply('begin|64|1|1|4142');
  b.apply('line|-1|1|4142');
  assert.equal(b.variables.size, before);
});

test('sprint HUD clamps percentages, changes refill colour, and clears per player', () => {
  const b = bridge();
  b.apply('sprint|0|52|1|1');
  assert.equal(b.variables.get('0/sprint/percent'), '52%');
  assert.equal(b.variables.get('0/sprint/left').length, 10);
  assert.equal(b.variables.get('0/sprint/right').length, 10);
  assert.equal(b.classes.get('0/sprint/Refilling'), false);
  b.apply('sprint|1|125|0|1');
  assert.equal(b.variables.get('1/sprint/percent'), '100%');
  b.apply('sprint|0|37|0|1');
  assert.equal(b.classes.get('0/sprint/Refilling'), true);
  b.apply('sprint|0|0|0|0');
  assert.equal(b.classes.get('0/sprint/Hidden'), true);
  assert.equal(b.classes.get('1/sprint/Hidden'), false);
  b.apply('sprint|1|NaN|0|1');
  assert.equal(b.classes.get('1/sprint/Hidden'), true);
});
