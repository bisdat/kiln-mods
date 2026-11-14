"""Static integration checks for the NoClip hotkey wrapper.

Actual compilation against the published Kiln SDK is performed by CI.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "NoClipToggle/NoClipToggleMod.cs").read_text()
PROJECT = (ROOT / "NoClipToggle/NoClipToggle.csproj").read_text()

class NoClipToggleContractTests(unittest.TestCase):
    def test_mod_version_and_sdk(self):
        self.assertIn('Kiln.Sdk/0.2.0-dev.27', PROJECT)
        self.assertIn('<KilnSide>both</KilnSide>', PROJECT)
        self.assertIn('<KilnClients>none</KilnClients>', PROJECT)

    def test_key_schedules_to_server_tick(self):
        self.assertIn('"Backquote"', SOURCE)
        self.assertNotIn('"Ctrl+F10"', SOURCE)
        self.assertIn('KeyBindings.Register(', SOURCE)
        self.assertIn('Context.Scheduler.Post(', SOURCE)

    def test_server_controls_built_in_noclip(self):
        self.assertIn('NoClip.Of(player)', SOURCE)
        self.assertIn('NoClip.Set(player, !before.On)', SOURCE)
        self.assertIn('NoClip.Available', SOURCE)

    def test_authenticated_remote_request(self):
        self.assertIn('Toggle(context.Player, isHost: false)', SOURCE)
        self.assertIn('Permissions.Has(player, NoClip.PermissionNode)', SOURCE)
        self.assertNotIn('Toggle(request.Player', SOURCE)

    def test_no_direct_collision_hooks(self):
        self.assertNotIn('Natives.Hook(', SOURCE)
        self.assertNotIn('MovementCollider', SOURCE)
        self.assertNotIn('Rules.Apply(', SOURCE)

if __name__ == '__main__':
    unittest.main()
