import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "prepare_jersey_attachments",
    ROOT / "tools" / "prepare-jersey-attachments.py",
)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


MODEL = '''{\n\trootNode = {\n\t\t_class = "Skeleton"\n\t\tchildren = [\n\t\t\t{\n\t\t\t\tname = "spine_2"\n\t\t\t}\n\t\t]\n\t}\n\t{\n\t\t_class = "AttachmentList"\n\t\tchildren = [\n\t\t\t{\n\t\t\t\t_class = "Attachment"\n\t\t\t\tname = "weapon"\n\t\t\t\tignore_rotation = false\n\t\t\t\tparent_bone = "wpn"\n\t\t\t\trelative_origin = [ 0.0, 0.0, 0.0 ]\n\t\t\t\trelative_angles = [ 0.0, 0.0, 0.0 ]\n\t\t\t\tweight = 1.0\n\t\t\t},\n\t\t]\n\t}\n}\n'''


def calibration():
    def socket(name, origin):
        return {
            "name": name,
            "parent_bone": "spine_2",
            "relative_origin": origin,
            "relative_angles": [0.0, 180.0, 0.0],
        }

    return {
        "torso_bone": "spine_2",
        "attachments": {
            "name": socket(MODULE.NAME_SOCKET, [5.965696, 0.510076, -16.38]),
            "number": socket(MODULE.NUMBER_SOCKET, [1.051142, -0.410333, -16.4]),
        },
    }


class JerseyAttachmentTests(unittest.TestCase):
    def test_add_is_idempotent_and_preserves_weapon(self):
        config = calibration()
        first = MODULE.add_jersey_attachments(MODEL, config)
        second = MODULE.add_jersey_attachments(first, config)
        self.assertEqual(first, second)
        self.assertEqual(first.count('name = "soccermod_jersey_name"'), 1)
        self.assertEqual(first.count('name = "soccermod_jersey_number"'), 1)
        self.assertIn('name = "weapon"', first)
        self.assertIn('parent_bone = "wpn"', first)
        self.assertLess(first.index('name = "soccermod_jersey_name"'), first.index('name = "soccermod_jersey_number"'))

    def test_rejects_partial_duplicate_or_changed_inputs(self):
        config = calibration()
        generated = MODULE.add_jersey_attachments(MODEL, config)
        partial = generated.replace('name = "soccermod_jersey_number"', 'name = "number_changed"')
        with self.assertRaises(ValueError):
            MODULE.add_jersey_attachments(partial, config)
        duplicate = generated.replace(
            'name = "soccermod_jersey_number"',
            'name = "soccermod_jersey_name"',
            1,
        )
        with self.assertRaises(ValueError):
            MODULE.add_jersey_attachments(duplicate, config)
        changed = generated.replace(
            'relative_origin = [ 5.965696, 0.510076, -16.380000 ]',
            'relative_origin = [ 5.000000, 0.000000, -16.000000 ]',
        )
        with self.assertRaises(ValueError):
            MODULE.add_jersey_attachments(changed, config)

    def test_rejects_missing_torso_or_weapon_contract(self):
        config = calibration()
        with self.assertRaises(ValueError):
            MODULE.add_jersey_attachments(MODEL.replace('name = "spine_2"', 'name = "spine_1"'), config)
        with self.assertRaises(ValueError):
            MODULE.add_jersey_attachments(MODEL.replace('parent_bone = "wpn"', 'parent_bone = "weapon"'), config)

    def test_candidate_copy_checks_hashes_and_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            temp_path = Path(temp)
            source = temp_path / "source"
            source_model = source / "models/soccermod/kits/kit_home.vmdl"
            source_model.parent.mkdir(parents=True)
            source_model.write_text(MODEL, encoding="utf-8", newline="")
            manifest = temp_path / "manifest.json"
            model_config = calibration()
            model_config.update({
                "source_relative": "models/soccermod/kits/kit_home.vmdl",
                "source_sha256": MODULE._sha256(source_model),
            })
            manifest.write_text(json.dumps({"models": [model_config]}), encoding="utf-8")
            output = temp_path / "output"
            report = MODULE.prepare_candidate(source, output, manifest)
            self.assertTrue((output / "models/soccermod/kits/kit_home.vmdl").is_file())
            self.assertIn("models/soccermod/kits/kit_home.vmdl", report["models"])
            with self.assertRaises(ValueError):
                MODULE.prepare_candidate(source, output, manifest)


if __name__ == "__main__":
    unittest.main()
