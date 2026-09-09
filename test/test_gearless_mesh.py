import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("mesh_filter", ROOT / "tools/filter-kit-mesh.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

SOURCE = '''<!-- dmx encoding keyvalues2_flat 1 format model 22 -->
"DmeFaceSet"
{
"id" "elementid" "face-id"
"faces" "int_array" ["0", "1", "2", "-1", "3", "4", "5", "-1"]
}
"DmeVertexData"
{
"id" "elementid" "vertex-id"
"rig-preservation-sentinel" "string" "DO NOT CHANGE"
}
'''
COMPONENTS = [
    {"id": 0, "face_set": "face-id", "face_indices": [0], "face_count": 1,
     "triangles": np.array([[[0.0, 0.0, 1.0], [1.0, 0.0, 1.0], [0.0, 1.0, 1.0]]])},
    {"id": 1, "face_set": "face-id", "face_indices": [1], "face_count": 1,
     "triangles": np.array([[[0.0, 0.0, 2.0], [1.0, 0.0, 2.0], [0.0, 1.0, 2.0]]])},
]


class GearlessMeshChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.source = Path(self.temp.name) / "source.dmx"
        self.output = Path(self.temp.name) / "candidate.dmx"
        self.source.write_bytes(SOURCE.replace("\n", "\r\n").encode())
        self.manifest = {"source_sha256": hashlib.sha256(self.source.read_bytes()).hexdigest(), "keep_components": [0]}
        self.mock = patch.object(module.inspection, "inspect", return_value=COMPONENTS)
        self.mock.start()
        self.addCleanup(self.mock.stop)

    def test_only_faces_change_and_source_is_untouched(self):
        before = self.source.read_bytes()
        report = module.filter_mesh(self.source, self.manifest, self.output)
        self.assertEqual(report["removed_triangles"], 1)
        self.assertEqual(report["retained_triangles"], 1)
        self.assertTrue(report["unchanged_non_face_data"])
        self.assertEqual(self.source.read_bytes(), before)
        self.assertIn('"0",\n"1",\n"2",\n"-1"', self.output.read_text())
        self.assertIn('"rig-preservation-sentinel" "string" "DO NOT CHANGE"', self.output.read_text())

    def test_wrong_source_hash_rejected(self):
        self.manifest["source_sha256"] = "bad"
        with self.assertRaisesRegex(ValueError, "hash"):
            module.filter_mesh(self.source, self.manifest, self.output)
        self.assertFalse(self.output.exists())

    def test_existing_output_rejected(self):
        self.output.write_text("keep me")
        with self.assertRaisesRegex(ValueError, "overwrite"):
            module.filter_mesh(self.source, self.manifest, self.output)
        self.assertEqual(self.output.read_text(), "keep me")

    def test_invalid_component_rejected(self):
        self.manifest["keep_components"] = [500]
        with self.assertRaisesRegex(ValueError, "Invalid"):
            module.filter_mesh(self.source, self.manifest, self.output)

    def test_noop_rejected(self):
        self.manifest["keep_components"] = [0, 1]
        with self.assertRaisesRegex(ValueError, "nonempty"):
            module.filter_mesh(self.source, self.manifest, self.output)

    def test_face_rule_removes_only_selected_triangle(self):
        self.manifest["remove_face_rules"] = [{"component_id": 0, "centroid_z_min": 0.0}]
        report = module.filter_mesh(self.source, self.manifest, self.output)
        self.assertEqual(report["removed_triangles"], 2)
        self.assertEqual(report["retained_triangles"], 0)
        self.assertIn('"rig-preservation-sentinel" "string" "DO NOT CHANGE"', self.output.read_text())


if __name__ == "__main__":
    unittest.main()
