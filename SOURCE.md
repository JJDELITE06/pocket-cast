# Complete corresponding source

Pocket Cast 1.1's complete source bundle is about **1.2 GB**. It contains the frontend, original pinned UxPlay source, the local USB-interface patch, native build/packaging scripts, license notices, and the exact source packages for the bundled native dependencies. You do **not** need it to run Pocket Cast.

The source ZIP is published as **19 smaller parts** in the [v1.1.0 release](https://github.com/JJDELITE06/pocket-cast/releases/tag/v1.1.0), avoiding unreliable large-file transfers. Joining the parts reconstructs the original ZIP byte for byte.

## Download and verify automatically

1. Download [download-source.py](https://github.com/JJDELITE06/pocket-cast/releases/download/v1.1.0/download-source.py).
2. Inspect the script if desired, then run it with Python 3.10 or newer:

```sh
python download-source.py
```

The script downloads each part from this GitHub release, verifies its embedded SHA-256, and joins the parts into `PocketCast-Source-1.1-download/PocketCast-Corresponding-Source-1.1.zip`. It retries failed transfers and skips already verified parts when run again. It does not execute or automatically extract downloaded content. Keep about **2.5 GB** of free disk space for both the parts and the completed ZIP.

To choose another folder:

```sh
python download-source.py --directory ./pocket-cast-source
```

## Download parts manually

Download all assets named `PocketCast-Corresponding-Source-1.1.zip.001` through `.019` from the [release](https://github.com/JJDELITE06/pocket-cast/releases/tag/v1.1.0). Keep their names unchanged and place them together in a folder. The [part manifest](https://github.com/JJDELITE06/pocket-cast/releases/download/v1.1.0/source-parts-1.1.json) lists exact sizes and SHA-256 values.

Use the helper to verify and join local files without making network requests:

```sh
python download-source.py --assemble-only --directory ./folder-containing-the-parts
```

Extract the reconstructed ZIP using an ordinary ZIP tool. Its SHA-256 must be:

```text
f6760c1e1ffc169e1d38876d0baa8b843c2430e4c39aeeb58f15581acc1b1136
```

This also matches the full source bundle entry in [SHA256SUMS-1.1.txt](https://github.com/JJDELITE06/pocket-cast/releases/download/v1.1.0/SHA256SUMS-1.1.txt). Build instructions are included in `BUILDING.txt` inside the reconstructed ZIP; the frontend and receiver patch are also browsable in this repository.
