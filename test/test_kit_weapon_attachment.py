import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("attachment_fix", Path(__file__).resolve().parents[1] / "tools/fix-kit-weapon-attachment.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

WEAPON = '{ _class = "Attachment" name = "weapon" parent_bone = "wpn" relative_origin = [0,0,0] }'
HAND = '{ _class = "Attachment" name = "weapon_hand_r" parent_bone = "hand_R" relative_origin = [-2.6,-1.4,0] relative_angles = [0,180,0] }'


class AttachmentChecks(unittest.TestCase):
    def test_only_weapon_socket_is_replaced(self):
        source = "prefix\n" + WEAPON + "\n" + HAND + "\nskeleton and geometry unchanged"
        fixed = module.fix_attachment(source)
        self.assertEqual(fixed, source.replace(WEAPON, HAND.replace('name = "weapon_hand_r"', 'name = "weapon"')))

    def test_missing_or_duplicate_socket_rejected(self):
        for source in (WEAPON, HAND, WEAPON + WEAPON + HAND):
            with self.assertRaises(ValueError):
                module.fix_attachment(source)

    def test_unknown_parent_rejected(self):
        with self.assertRaises(ValueError):
            module.fix_attachment(WEAPON.replace('"wpn"', '"other"') + HAND)

    def test_reapplying_fix_rejected(self):
        with self.assertRaises(ValueError):
            module.fix_attachment(module.fix_attachment(WEAPON + HAND))


if __name__ == "__main__":
    unittest.main()
