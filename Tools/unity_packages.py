"""Read the package versions a Unity project actually resolved, without opening Unity."""
import json
import re
from pathlib import Path

LILTOON = 'jp.lilxyzw.liltoon'
VERSION = re.compile(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)')


def parse_version(text):
    """Return a release version tuple; prereleases and build labels are rejected."""
    match = VERSION.fullmatch(text or '')
    if not match:
        raise ValueError('Not a release version: ' + repr(text))
    return tuple(int(part) for part in match.groups())


def univrm_support(policy, version):
    """Classify a UniVRM version against Compatibility/dependencies.json.

    `supported` matches DependencyPolicy.SupportsUniVrm: any release X.Y.Z of the
    accepted patch line `uniVrm.vpmRange` (X.Y.x). `listed` says whether the
    patch is in `uniVrm.versions`, i.e. already verified with its source commit.
    """
    line = re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.x', policy['uniVrm']['vpmRange'])
    if not line:
        raise ValueError('uniVrm.vpmRange is not an X.Y.x patch line: ' + repr(policy['uniVrm']['vpmRange']))
    try:
        parsed = parse_version(version)
    except ValueError:
        parsed = None
    supported = parsed is not None and parsed[:2] == (int(line.group(1)), int(line.group(2)))
    return {'supported': supported, 'listed': supported and version in policy['uniVrm']['versions']}


def _package_json(folder):
    data = json.loads((folder / 'package.json').read_text(encoding='utf-8-sig'))
    if not isinstance(data, dict) or not isinstance(data.get('version'), str):
        raise ValueError('package.json has no version: ' + str(folder))
    return data


def _local_path(project, reference):
    # Unity writes "file:<path>" relative to Packages/ or absolute, with forward
    # slashes and without percent-encoding.
    path = Path(reference[len('file:'):])
    return path if path.is_absolute() else project / 'Packages' / path


def installed_package(project, name):
    """Describe the resolved package from packages-lock.json and its package.json.

    Returns None when the lock has no entry. Raises ValueError when the lock
    exists but its package folder cannot prove the version, so a stale or
    partially imported project is not mistaken for the expected environment.
    """
    project = Path(project)
    lock_path = project / 'Packages' / 'packages-lock.json'
    if not lock_path.is_file():
        raise ValueError('packages-lock.json is missing; open the project in Unity once to resolve packages')
    lock = json.loads(lock_path.read_text(encoding='utf-8-sig'))
    dependencies = lock.get('dependencies') if isinstance(lock, dict) else None
    if not isinstance(dependencies, dict):
        raise ValueError('packages-lock.json has no dependencies object')
    entry = dependencies.get(name)
    if entry is None:
        return None
    if not isinstance(entry, dict):
        raise ValueError('packages-lock.json entry for ' + name + ' is not an object')
    source = entry.get('source')
    reference = entry.get('version', '')
    if source == 'embedded':
        folder = _local_path(project, reference) if reference.startswith('file:') else project / 'Packages' / name
    elif source == 'local':
        folder = _local_path(project, reference)
    elif source in ('registry', 'builtin'):
        folder = None
    elif source == 'git':
        # Unity 2022.3 caches a git package as Library/PackageCache/<name>@<first
        # 10 hash digits>; the lock holds only the URL. An unexpected layout fails.
        commit = entry.get('hash', '')
        matches = sorted((project / 'Library' / 'PackageCache').glob(name + '@' + commit[:10] + '*')) if commit else []
        if len(matches) != 1:
            raise ValueError('Git package ' + name + ' is not in Library/PackageCache; open the project in Unity once')
        folder = matches[0]
    else:
        raise ValueError('Unknown package source for ' + name + ': ' + repr(source))
    if folder is None:
        version = reference
    else:
        data = _package_json(folder)
        if data.get('name') != name:
            raise ValueError('Package folder for ' + name + ' contains ' + repr(data.get('name')))
        version = data['version']
    return {'name': name, 'version': version, 'source': source}


def editor_version(project):
    """Return (version, revision) from ProjectSettings/ProjectVersion.txt, or (None, None)."""
    path = Path(project) / 'ProjectSettings' / 'ProjectVersion.txt'
    if not path.is_file():
        return None, None
    text = path.read_text(encoding='utf-8-sig')
    version = re.search(r'^m_EditorVersion:\s*(\S+)', text, re.MULTILINE)
    revision = re.search(r'^m_EditorVersionWithRevision:\s*\S+\s*\(([0-9a-f]+)\)', text, re.MULTILINE)
    return (version.group(1) if version else None), (revision.group(1) if revision else None)
