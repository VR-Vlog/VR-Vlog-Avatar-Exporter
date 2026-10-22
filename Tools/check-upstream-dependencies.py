"""Read stable upstream releases for a manual compatibility review; never update packages."""
import argparse
import json
import re
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import liltoon_catalogue  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
REPOSITORIES = {'lilToon': 'lilxyzw/lilToon', 'UniVRM': 'vrm-c/UniVRM',
                'NDMF': 'bdunderscore/ndmf', 'Modular Avatar': 'bdunderscore/modular-avatar'}


def release_version(tag):
    value = tag[1:] if tag.startswith('v') else tag
    if not re.fullmatch(r'\d+\.\d+\.\d+', value):
        raise ValueError('Unrecognized stable release tag')
    return tuple(map(int, value.split('.')))


def compare(config, releases):
    known = {'lilToon': config['lilToon']['verified']['maximum'],
             'UniVRM': max(config['uniVrm']['versions'], key=release_version),
             'NDMF': config['authoring']['ndmfReference'],
             'Modular Avatar': config['authoring']['modularAvatarReference']}
    result = []
    for name in REPOSITORIES:
        release = releases[name]
        if release.get('draft') or release.get('prerelease'):
            raise ValueError('Expected a published stable release: ' + name)
        tag = release['tag_name']
        result.append({'package': name, 'reference': known[name], 'upstream': tag,
                       'needsCompatibilityReview': release_version(tag) > release_version(known[name])})
    return result


def add_liltoon_structure(result, catalogue_text, load=liltoon_catalogue.catalogue_for_tag):
    """For a newer lilToon tag, add whether its shader structure matches the committed catalogue."""
    row = next(item for item in result if item['package'] == 'lilToon')
    if not row['needsCompatibilityReview']:
        return row
    reference = liltoon_catalogue.parse_catalogue(catalogue_text)
    try:
        upstream = load(row['upstream'])
    except Exception as error:  # Report the failure; the release comparison stays useful.
        row['structureError'] = type(error).__name__ + ': ' + str(error)
        return row
    row['structureHash'] = liltoon_catalogue.structure_hash(upstream)
    row['structureMatchesReference'] = row['structureHash'] == liltoon_catalogue.structure_hash(reference)
    row['structureDifferences'] = liltoon_catalogue.structure_difference(reference, upstream)[:50]
    return row


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    config = json.loads((ROOT / 'Compatibility/dependencies.json').read_text(encoding='utf-8'))
    releases = {}
    for name, repository in REPOSITORIES.items():
        request = urllib.request.Request('https://api.github.com/repos/' + repository + '/releases/latest',
                                         headers={'Accept': 'application/vnd.github+json', 'User-Agent': 'VRVlog-compatibility'})
        with urllib.request.urlopen(request, timeout=20) as response:
            data = response.read(2 * 1024 * 1024 + 1)
            if len(data) > 2 * 1024 * 1024:
                raise ValueError('Release response exceeds limit')
            releases[name] = json.loads(data)
    result = compare(config, releases)
    add_liltoon_structure(result, (ROOT / liltoon_catalogue.CATALOGUE_PATH).read_text(encoding='ascii'))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    for item in result:
        state = 'REVIEW NEEDED' if item['needsCompatibilityReview'] else 'no newer release'
        print(f"{item['package']}: {item['reference']} -> {item['upstream']} ({state})")
        if 'structureError' in item:
            print('  structure hash could not be computed: ' + item['structureError'])
        elif 'structureHash' in item:
            same = 'same as' if item['structureMatchesReference'] else 'DIFFERS from'
            print(f"  shader structure {item['structureHash'][:16]} {same} the {liltoon_catalogue.REFERENCE_VERSION} catalogue")
            for line in item['structureDifferences']:
                print('    ' + line)
    print('Release availability is not proof of compatibility. No dependency or support policy was changed.')


if __name__ == '__main__':
    main()
