#!/usr/bin/env python3
"""Download or reassemble Pocket Cast 1.1 corresponding source. Python 3.10+."""
import argparse
import hashlib
from pathlib import Path
import re
import sys
import time
import urllib.error
import urllib.request

BASE_URL = 'https://github.com/JJDELITE06/pocket-cast/releases/download/v1.1.0/'
BUNDLE_NAME = 'PocketCast-Corresponding-Source-1.1.zip'
BUNDLE_SIZE = 1244657826
BUNDLE_SHA256 = 'f6760c1e1ffc169e1d38876d0baa8b843c2430e4c39aeeb58f15581acc1b1136'
PARTS = [('PocketCast-Corresponding-Source-1.1.zip.001', 67108864, '68b82dc43a68918b0e9e988582e3546b4bc8b9c4b5ecc618ea7f93578cbcc1f4'), ('PocketCast-Corresponding-Source-1.1.zip.002', 67108864, '46c74b10f54490321e4abbc472f83559420228d2571af0e15e74a6f8083b279d'), ('PocketCast-Corresponding-Source-1.1.zip.003', 67108864, 'e688e0174bf49fe7b3a510e0307f6970ecd25466d548ffaef720c4954544b13b'), ('PocketCast-Corresponding-Source-1.1.zip.004', 67108864, '5b331a03557a00e82c48ae1120a8f484ecdeaa4335e22fd6e9a2729e799e88cf'), ('PocketCast-Corresponding-Source-1.1.zip.005', 67108864, '4c965a98345e9957ede02e52ea52554e9adda2753079c1838e1169a8b2d77127'), ('PocketCast-Corresponding-Source-1.1.zip.006', 67108864, 'e58a3c5fc66107d73d987de842df5cf81995254dcc640c9d33fd2eb480c9dc36'), ('PocketCast-Corresponding-Source-1.1.zip.007', 67108864, '192023fe222c94584445b35c3053660a6f034de5684f4db437783312609a805e'), ('PocketCast-Corresponding-Source-1.1.zip.008', 67108864, '61f8991370c6d5691d23593f6c6bd3ca80fc6f8e03361fd00e214af2a64b7510'), ('PocketCast-Corresponding-Source-1.1.zip.009', 67108864, 'a46686d560c6d29ca9a9e61e2dc6a586384a320423cb01bb2eff01a463b56797'), ('PocketCast-Corresponding-Source-1.1.zip.010', 67108864, 'a602ab60ef5338a770557f440e6a3f9660dd256c6ddcf356a70d23ef5b695282'), ('PocketCast-Corresponding-Source-1.1.zip.011', 67108864, 'b630572099a92046ae1515d3c88e3864a3970fe0592c45458e208fbbd36036fb'), ('PocketCast-Corresponding-Source-1.1.zip.012', 67108864, 'd4ef9f3ca6eaf2bdfedb0e16045b3c25ec3207d122cc819463b5183746e238f0'), ('PocketCast-Corresponding-Source-1.1.zip.013', 67108864, '5292bca2f9dec804e2ecf166fd5c7186e1d5e5dc680ed865ee44152d5d8d0129'), ('PocketCast-Corresponding-Source-1.1.zip.014', 67108864, '008f9f314d8d93d05ef521ad44751ba2a661f8b150ba2d9aba57fd95ef215139'), ('PocketCast-Corresponding-Source-1.1.zip.015', 67108864, '5b20be6f4251b1ab822864aaabba2a4a363ecdcec4835d93033afa0f022de2f2'), ('PocketCast-Corresponding-Source-1.1.zip.016', 67108864, 'e54dce98c814f25a7e3bdf8ba0632b45ab344171856fbe304056e5548abaa92f'), ('PocketCast-Corresponding-Source-1.1.zip.017', 67108864, 'bdbd6e27ff476f9dc393e85b9f21ddfd8d2646c7fb98a39d76ec2cf11473b197'), ('PocketCast-Corresponding-Source-1.1.zip.018', 67108864, 'ee7364d0548b4cbfd20563cf8f5969619094bcf95c8d32e649640710c9b6f4de'), ('PocketCast-Corresponding-Source-1.1.zip.019', 36698274, '0f796d7962168c09c7ff15e76e0b850dc0f74d7e74f0afbde7ba6c3967ce9a20')]


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(data)
    return value.hexdigest()


def valid_file(path, size, checksum):
    return path.is_file() and path.stat().st_size == size and digest(path) == checksum


def download_part(directory, name, size, checksum):
    target = directory / name
    temporary = directory / (name + '.partial')
    for attempt in range(1, 4):
        try:
            request = urllib.request.Request(BASE_URL + name,
                headers={'User-Agent': 'PocketCast-source-downloader/1.1'})
            value = hashlib.sha256()
            received = 0
            with urllib.request.urlopen(request, timeout=60) as response, temporary.open('wb') as stream:
                while True:
                    data = response.read(1024 * 1024)
                    if not data:
                        break
                    received += len(data)
                    if received > size:
                        raise ValueError('Server returned more data than expected.')
                    value.update(data)
                    stream.write(data)
            if received != size or value.hexdigest() != checksum:
                raise ValueError('Downloaded part did not match its pinned size/SHA-256.')
            temporary.replace(target)
            return
        except (OSError, ValueError, urllib.error.URLError) as error:
            if attempt == 3:
                raise RuntimeError(f'{name}: download failed after 3 attempts: {error}') from error
            print(f'  Attempt {attempt} failed; retrying in {attempt * 2} seconds.', flush=True)
            time.sleep(attempt * 2)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path,
        default=Path.cwd() / 'PocketCast-Source-1.1-download', help='Folder for parts and reconstructed ZIP.')
    parser.add_argument('--assemble-only', action='store_true',
        help='Verify and join already downloaded parts without any network requests.')
    args = parser.parse_args()
    directory = args.directory.expanduser().resolve()
    directory.mkdir(parents=True, exist_ok=True)
    output = directory / BUNDLE_NAME
    if output.exists():
        if valid_file(output, BUNDLE_SIZE, BUNDLE_SHA256):
            print(f'Complete source ZIP is already verified: {output}')
            return 0
        raise RuntimeError(f'Existing {output.name} has a different checksum; move it before running again.')
    for number, (name, size, checksum) in enumerate(PARTS, 1):
        # Filenames and checksums are embedded, not loaded from remote instructions.
        if not re.fullmatch(re.escape(BUNDLE_NAME) + r'\.\d{3}', name) or Path(name).name != name:
            raise RuntimeError('Unsafe embedded part filename.')
        target = directory / name
        if valid_file(target, size, checksum):
            print(f'[{number}/{len(PARTS)}] Verified existing {name}', flush=True)
            continue
        if args.assemble_only:
            raise RuntimeError(f'Missing or damaged part: {name}. Download it before assembling.')
        print(f'[{number}/{len(PARTS)}] Downloading {name}', flush=True)
        download_part(directory, name, size, checksum)
    temporary = directory / (BUNDLE_NAME + '.partial')
    value = hashlib.sha256()
    total = 0
    with temporary.open('wb') as destination:
        for name, _, _ in PARTS:
            with (directory / name).open('rb') as stream:
                for data in iter(lambda: stream.read(1024 * 1024), b''):
                    value.update(data)
                    total += len(data)
                    destination.write(data)
    if total != BUNDLE_SIZE or value.hexdigest() != BUNDLE_SHA256:
        raise RuntimeError('Reconstructed ZIP did not match its pinned size/SHA-256.')
    temporary.replace(output)
    print(f'Complete source ZIP verified: {output}')
    print(f'SHA-256: {BUNDLE_SHA256}')
    print('Extract the ZIP to inspect/build the source. No downloaded content was executed.')
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError) as error:
        print(f'Error: {error}', file=sys.stderr)
        raise SystemExit(1)
