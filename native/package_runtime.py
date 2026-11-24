"""Collect a relocatable UxPlay runtime and exact package provenance."""
from pathlib import Path
import hashlib, json, re, shutil, struct, urllib.request

root = Path(__file__).resolve().parent
msys = root / 'msys64'
prefix = msys / 'ucrt64'
build_src = next((root / 'source').glob('UxPlay-*'))
out = root.parent / 'receiver-runtime' / 'native'
out.mkdir(parents=True, exist_ok=True)
plugins_out = out / 'lib' / 'gstreamer-1.0'
plugins_out.mkdir(parents=True, exist_ok=True)

def imports(path):
    data = path.read_bytes()
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    nsec, optlen = struct.unpack_from('<H12xH', data, pe + 6)
    opt = pe + 24
    is64 = struct.unpack_from('<H', data, opt)[0] == 0x20b
    imp = struct.unpack_from('<I', data, opt + (120 if is64 else 104))[0]
    sections = []
    for i in range(nsec):
        pos = opt + optlen + i * 40
        virtual_size, virtual_start, raw_size, raw_start = struct.unpack_from('<IIII', data, pos + 8)
        sections.append((virtual_start, max(virtual_size, raw_size), raw_start))
    def offset(rva):
        for start, size, raw in sections:
            if start <= rva < start + size:
                return raw + rva - start
        return rva
    if not imp:
        return []
    pos = offset(imp)
    names = []
    while any(data[pos:pos+20]):
        name = offset(struct.unpack_from('<I', data, pos + 12)[0])
        names.append(data[name:data.index(b'\0', name)].decode('ascii'))
        pos += 20
    return names

bin_files = {p.name.lower(): p for p in (prefix / 'bin').glob('*') if p.is_file()}
selected = {}
queue = []
def add(path, destination):
    key = str(path).lower()
    if key in selected:
        return
    selected[key] = (path, destination)
    queue.append(path)

add(build_src / 'build' / 'uxplay.exe', out / 'uxplay.exe')
for exe in ['gst-inspect-1.0.exe', 'gst-launch-1.0.exe']:
    add(prefix / 'bin' / exe, out / exe)
scanner = prefix / 'libexec' / 'gstreamer-1.0' / 'gst-plugin-scanner.exe'
add(scanner, out / 'gst-plugin-scanner.exe')
plugin_names = [
    'app', 'audioconvert', 'audioresample', 'autodetect', 'audioparsers',
    'coreelements', 'playback', 'typefindfunctions', 'videoparsersbad',
    'libav', 'volume', 'videoconvertscale', 'd3d11', 'd3d12', 'wasapi',
    'wasapi2', 'directsound', 'isomp4', 'jpeg', 'videotestsrc', 'audiotestsrc',
    'videofilter', 'debugutilsbad', 'rtp', 'udp',
]
for name in plugin_names:
    path = prefix / 'lib' / 'gstreamer-1.0' / f'libgst{name}.dll'
    if not path.exists():
        raise FileNotFoundError(path)
    add(path, plugins_out / path.name)

missing = set()
while queue:
    path = queue.pop()
    for name in imports(path):
        dep = bin_files.get(name.lower())
        if dep:
            add(dep, out / dep.name)
        else:
            missing.add(name)

packages = {}
file_packages = {}
for entry in (msys / 'var' / 'lib' / 'pacman' / 'local').glob('*'):
    if not entry.is_dir():
        continue
    text = (entry / 'desc').read_text(encoding='utf-8')
    values = {}
    for block in text.strip().split('\n\n'):
        lines = block.splitlines()
        if lines[0].startswith('%'):
            values[lines[0].strip('%')] = lines[1:]
    name = values['NAME'][0]
    if not name.startswith('mingw-w64-ucrt-x86_64-'):
        continue
    packages[name] = values
    files_path = entry / 'files'
    if files_path.exists():
        for rel in files_path.read_text(encoding='utf-8').splitlines():
            file_packages[rel.lower()] = name

used = set()
manifest = []
for path, destination in selected.values():
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, destination)
    package = None
    if path.is_relative_to(msys):
        package = file_packages.get(path.relative_to(msys).as_posix().lower())
    if package:
        used.add(package)
    manifest.append({'file': destination.relative_to(out).as_posix(), 'package': package,
                     'sha256': hashlib.sha256(destination.read_bytes()).hexdigest()})

license_out = out / 'licenses'
license_out.mkdir(exist_ok=True)
shutil.copy2(build_src / 'LICENSE', license_out / 'UxPlay-GPL-3.0.txt')
source_packages = []
for name in sorted(used):
    values = packages[name]
    for rel, pkg in file_packages.items():
        if pkg == name and (rel.startswith('ucrt64/share/licenses/') or
                            rel.startswith('ucrt64/share/doc/') and re.search(r'copying|license|copyright', rel)):
            original = msys / rel
            if original.is_file():
                dest = license_out / name / Path(rel).relative_to('ucrt64/share')
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(original, dest)
    source_name = f"{values.get('BASE', [name.replace('mingw-w64-ucrt-x86_64-', 'mingw-w64-')])[0]}-{values['VERSION'][0]}.src.tar.zst"
    source_packages.append({'name': name, 'base': values.get('BASE', [None])[0],
                            'version': values['VERSION'][0], 'license': values.get('LICENSE', []),
                            'url': values.get('URL', []),
                            'source_url': 'https://repo.msys2.org/mingw/sources/' + source_name})

commit = json.loads((root / 'upstream-commit.json').read_text(encoding='utf-8'))['sha']
provenance = {'uxplay_commit': commit, 'upstream': 'https://github.com/FDH2/UxPlay',
              'source_archive': 'https://github.com/FDH2/UxPlay/archive/' + commit + '.zip',
              'build': 'cmake -G Ninja -DNO_MARCH_NATIVE=ON -DCMAKE_BUILD_TYPE=Release ..; cmake --build . --parallel',
              'discovery': 'internal mDNS, no Bonjour service', 'architecture': 'Windows x86_64/UCRT',
              'packages': source_packages, 'files': manifest,
              'system_imports': sorted(missing)}
(out / 'PROVENANCE.json').write_text(json.dumps(provenance, indent=2), encoding='utf-8')
print(json.dumps({'runtime': str(out), 'files': len(manifest), 'packages': len(used),
                  'size_mb': round(sum(p.stat().st_size for p in out.rglob('*') if p.is_file()) / 1024**2, 1),
                  'source_urls': [x['source_url'] for x in source_packages]}, indent=2))
