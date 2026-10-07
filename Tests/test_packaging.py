import json
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / 'Tools'))
import project


class DistributionShaders(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.build = pathlib.Path(self.temp.name)
        self.scope = patch.object(project, 'BUILD', self.build)
        self.scope.start()
        self.digest = patch.object(project, 'sources_digest', return_value='current')
        self.digest.start()

    def tearDown(self):
        self.digest.stop()
        self.scope.stop()
        self.temp.cleanup()

    def bundle(self, platform='windows', **changes):
        directory = self.build / 'shader-bundles' / platform
        directory.mkdir(parents=True)
        content = b'UnityFS' + b'fixture'
        (directory / ('vefx-' + platform + '.unity3d')).write_bytes(content)
        receipt = dict(source_sha256='current', unity='2019.4.18f1',
                       target=platform, bundle_built=True, host_render_probe=True,
                       graphics_api='Direct3D11' if platform == 'windows' else 'OpenGLCore',
                       sha256=project.hashlib.sha256(content).hexdigest())
        receipt.update(changes)
        (directory / 'receipt.json').write_text(json.dumps(receipt))

    def test_missing_bundle_is_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'Missing windows shaders'):
            project.require_distribution_bundle('windows')

    def test_stale_shader_source_is_rejected(self):
        self.bundle(source_sha256='old')
        with self.assertRaisesRegex(RuntimeError, 'Rebuild current'):
            project.require_distribution_bundle('windows')

    def test_modified_bundle_is_rejected(self):
        self.bundle(sha256='wrong')
        with self.assertRaisesRegex(RuntimeError, 'checksum'):
            project.require_distribution_bundle('windows')

    def test_cross_build_without_host_probe_is_rejected(self):
        self.bundle(host_render_probe=False)
        with self.assertRaisesRegex(RuntimeError, 'Direct3D11 host'):
            project.require_distribution_bundle('windows')

    def test_candidate_accepts_current_cross_build(self):
        self.bundle(host_render_probe=False, graphics_api='OpenGLCore')
        project.require_distribution_bundle('windows', require_host_probe=False)

    def test_candidate_still_requires_bundle(self):
        with self.assertRaisesRegex(RuntimeError, 'Missing windows shaders'):
            project.require_distribution_bundle('windows', require_host_probe=False)

    def test_candidate_rejects_stale_bundle(self):
        self.bundle(source_sha256='old', host_render_probe=False)
        with self.assertRaisesRegex(RuntimeError, 'Rebuild current'):
            project.require_distribution_bundle('windows', require_host_probe=False)

    def test_wrong_windows_api_is_rejected(self):
        self.bundle(graphics_api='OpenGLCore')
        with self.assertRaisesRegex(RuntimeError, 'Direct3D11 host'):
            project.require_distribution_bundle('windows')

    def test_valid_windows_bundle_is_accepted(self):
        self.bundle()
        project.require_distribution_bundle('windows')

    def test_mac_must_match_ksp_opengl(self):
        self.bundle('mac', graphics_api='Metal')
        with self.assertRaisesRegex(RuntimeError, 'OpenGLCore host'):
            project.require_distribution_bundle('mac')

    def test_valid_mac_bundle_is_accepted(self):
        self.bundle('mac')
        project.require_distribution_bundle('mac')


if __name__ == '__main__':
    unittest.main()
