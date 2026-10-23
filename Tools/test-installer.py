"""Exercise the installer config, the pinned creator check and the built package check."""
import importlib.util
import io
import json
import tarfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("build_installer", Path(__file__).with_name("build-installer.py"))
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)

PACKAGE = {"name": "com.vrvlog.liltoon-vrm-exporter", "version": "0.12.0", "unity": "2022.3"}


def unitypackage(files):
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode="w:gz") as archive:
        for guid, (pathname, asset) in files.items():
            for name, data in (("pathname", pathname.encode("utf-8")), ("asset", asset)):
                info = tarfile.TarInfo(guid + "/" + name)
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))
    return stream.getvalue()


class InstallerTests(unittest.TestCase):
    def test_stable_installer_adds_the_official_repository_and_compatible_exporter(self):
        self.assertEqual({
            "vpmRepositories": ["https://vrvlog.fun/exporter/index.json"],
            "vpmDependencies": {"com.vrvlog.liltoon-vrm-exporter": "^0.12.0"},
            "includePrerelease": False,
            "minimumUnity": "2022.3",
        }, installer.installer_config(PACKAGE))

    def test_beta_installer_can_resolve_its_prerelease(self):
        config = installer.installer_config(dict(PACKAGE, version="0.12.0-beta.8"))
        self.assertEqual({"com.vrvlog.liltoon-vrm-exporter": "^0.12.0-beta.8"}, config["vpmDependencies"])
        self.assertTrue(config["includePrerelease"])

    def test_only_the_pinned_creator_runs(self):
        with self.assertRaises(ValueError):
            installer.verified_creator(b"console.log('other creator')")

    def test_built_package_must_carry_exactly_the_requested_config(self):
        config = installer.installer_config(PACKAGE)
        script = ("Assets/installer/Installer.cs", b"class Installer {}")
        good = unitypackage({"a" * 32: ("Assets/installer/config.json", json.dumps(config).encode("utf-8")),
                             "b" * 32: script})
        installer.check_installer(good, config)
        other = dict(config, vpmRepositories=["https://example.invalid/index.json"])
        for data in (unitypackage({"b" * 32: script}),
                     unitypackage({"a" * 32: ("Assets/installer/config.json", json.dumps(other).encode("utf-8"))}),
                     unitypackage({"a" * 32: ("Assets/installer/config.json", json.dumps(config).encode("utf-8")),
                                   "c" * 32: ("Assets/other/config.json", json.dumps(config).encode("utf-8"))})):
            with self.subTest(data=data), self.assertRaises(ValueError):
                installer.check_installer(data, config)

    def test_download_retries_then_gives_up(self):
        calls = []

        def failing(url, timeout):
            calls.append(url)
            raise installer.urllib.error.URLError("offline")

        original = installer.urllib.request.urlopen
        installer.urllib.request.urlopen = failing
        try:
            with self.assertRaises(installer.urllib.error.URLError):
                installer.download_creator(wait=0)
        finally:
            installer.urllib.request.urlopen = original
        self.assertEqual([installer.CREATOR_URL] * installer.DOWNLOAD_ATTEMPTS, calls)


if __name__ == "__main__":
    unittest.main()
