"""Exercise the actual installer/rollback in an isolated fake systemd host."""
import hashlib
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'deploy/testserver/install-ball-handling.sh'


class BallDeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.plugin = self.root / 'plugin'
        self.plugin.mkdir()
        (self.plugin / 'SoccerModNativeHull.dll').write_text('old binary')
        (self.plugin / 'soccermod_settings.json').write_text('{"power":1602}')
        (self.plugin / 'soccermod_stats.json').write_text('ranks')
        self.source = self.root / 'new.dll'
        self.source.write_text('new binary')
        self.digest = hashlib.sha256(self.source.read_bytes()).hexdigest()
        self.script = self.root / 'install.sh'
        script = SCRIPT.read_text().replace('/home/gameserver/cs2/game/csgo/addons/counterstrikesharp/plugins/SoccerModNativeHull', str(self.plugin))
        script = script.replace('/home/gameserver/cs2-soccermod-backups', str(self.root / 'backups'))
        script = '\n'.join(line for line in script.splitlines() if not line.startswith('[[ $EUID'))
        script = script.replace('-o gameserver -g gameserver ', '').replace('chown gameserver:gameserver', ':')
        self.script.write_text(script)
        bin_dir = self.root / 'bin'
        bin_dir.mkdir()
        commands = {
            'systemctl': '''#!/bin/sh
if [ "$1" = start ] && [ -f "$BALL_TEST_ROOT/fail-start" ]; then
  rm "$BALL_TEST_ROOT/fail-start"
  exit 1
fi
exit 0
''',
            'sha256sum': '''#!/usr/bin/env python3
import hashlib,sys
print(hashlib.sha256(open(sys.argv[1],'rb').read()).hexdigest(), sys.argv[1])
''',
        }
        for name, content in commands.items():
            file = bin_dir / name
            file.write_text(content)
            file.chmod(0o755)
        self.env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ['PATH'], BALL_TEST_ROOT=str(self.root))

    def run_script(self, *args):
        return subprocess.run(['bash', str(self.script), str(self.source), *args], env=self.env, text=True, capture_output=True)

    def test_install_and_rollback_preserve_current_ranks(self):
        result = self.run_script(self.digest)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.plugin / 'SoccerModNativeHull.dll').read_text(), 'new binary')
        (self.plugin / 'soccermod_stats.json').write_text('new match ranks')
        backup = next((self.root / 'backups').iterdir())
        result = subprocess.run(['bash', str(backup / 'rollback.sh')], env=self.env, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.plugin / 'SoccerModNativeHull.dll').read_text(), 'old binary')
        self.assertEqual((self.plugin / 'soccermod_stats.json').read_text(), 'new match ranks')
        self.assertEqual((self.plugin / 'soccermod_settings.json').read_text(), '{"power":1602}')
        self.assertFalse((self.plugin / 'soccermod_ball_handling.json').exists())

    def test_failed_restart_restores_original_binary(self):
        (self.root / 'fail-start').touch()
        result = self.run_script(self.digest)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.plugin / 'SoccerModNativeHull.dll').read_text(), 'old binary')
        self.assertFalse((self.plugin / 'soccermod_ball_handling.json').exists())

    def test_wrong_checksum_makes_no_changes(self):
        self.assertNotEqual(self.run_script('0' * 64).returncode, 0)
        self.assertFalse((self.root / 'backups').exists())
        self.assertEqual((self.plugin / 'SoccerModNativeHull.dll').read_text(), 'old binary')


if __name__ == '__main__':
    unittest.main()
