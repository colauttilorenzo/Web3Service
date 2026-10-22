"""Applies the Algorand / multi-chain changes to a web3-service checkout.

Usage: python3 apply_changes.py /path/to/web3-service

Reads sync-manifest.json and web3-service-changes.tar.gz from this script's folder. Every file the changes touch must
still match the version they were made from; otherwise nothing is written and the differing files are listed.
The README is the exception: only its section between the badges and "## Continuous integration" is replaced.
Files that cannot be deleted are moved to _to_delete/.
"""
import hashlib
import json
import os
import shutil
import sys
import tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
README = 'README.md'
README_START = 'Notarization of document hashes'
README_END = '## Continuous integration'


def blob_sha(path):
    data = open(path, 'rb').read()
    return hashlib.sha1(b'blob %d\0' % len(data) + data).hexdigest()


def new_content(archive, path):
    return archive.extractfile(path).read()


def conflicts(repo, manifest, archive):
    found = []
    for entry in manifest['modified'] + manifest['deleted'] + [{'path': r['from'], 'base': r['base']} for r in manifest['renamed']]:
        target = os.path.join(repo, entry['path'])
        if not os.path.exists(target):
            found += [] if already_moved(repo, manifest, entry['path']) else [f"missing: {entry['path']}"]
        elif entry['path'] != README and blob_sha(target) != entry['base'] and not already_applied(target, archive, entry['path']):
            found.append(f"changed since the base version: {entry['path']}")
    for path in manifest['added']:
        target = os.path.join(repo, path)
        if os.path.exists(target) and open(target, 'rb').read() != new_content(archive, path):
            found.append(f"already exists with other content: {path}")
    return found


def already_applied(target, archive, path):
    """A modified file that a previous run already wrote."""
    return path in archive.getnames() and open(target, 'rb').read() == new_content(archive, path)


def already_moved(repo, manifest, path):
    """A renamed or deleted file that a previous run already handled."""
    targets = [r['to'] for r in manifest['renamed'] if r['from'] == path]
    deleted = any(d['path'] == path for d in manifest['deleted'])
    return deleted or any(os.path.exists(os.path.join(repo, target)) for target in targets)


def merged_readme(repo, archive):
    current = open(os.path.join(repo, README), encoding='utf-8').read()
    mine = new_content(archive, README).decode('utf-8')
    section = mine[mine.index(README_START):mine.index(README_END)]
    return current[:current.index(README_START)] + section + current[current.index(README_END):]


def remove(repo, path):
    target = os.path.join(repo, path)
    if not os.path.exists(target):
        return
    try:
        os.remove(target)
    except PermissionError:
        parked = os.path.join(repo, '_to_delete', path)
        os.makedirs(os.path.dirname(parked), exist_ok=True)
        shutil.move(target, parked)
        print(f'moved to _to_delete/: {path}')


def rename(repo, old, new):
    source, target = os.path.join(repo, old), os.path.join(repo, new)
    if os.path.exists(source):
        os.makedirs(os.path.dirname(target), exist_ok=True)
        os.replace(source, target)


def main(repo):
    manifest = json.load(open(os.path.join(HERE, 'sync-manifest.json')))
    with tarfile.open(os.path.join(HERE, 'web3-service-changes.tar.gz')) as archive:
        problems = conflicts(repo, manifest, archive)
        if problems:
            sys.exit('Nothing was changed. These files differ from the expected version:\n  ' + '\n  '.join(problems))
        readme = merged_readme(repo, archive)
        for entry in manifest['renamed']:
            rename(repo, entry['from'], entry['to'])
        for entry in manifest['deleted']:
            remove(repo, entry['path'])
        archive.extractall(repo, filter='data') if hasattr(tarfile, 'data_filter') else archive.extractall(repo)
    open(os.path.join(repo, README), 'w', encoding='utf-8').write(readme)
    print(f"Applied: {len(manifest['added'])} added, {len(manifest['modified'])} modified, "
          f"{len(manifest['renamed'])} moved, {len(manifest['deleted'])} removed.")


if __name__ == '__main__':
    main(os.path.abspath(sys.argv[1]))
