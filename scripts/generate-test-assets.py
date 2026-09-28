#!/usr/bin/env python3
"""Regenerates the WebM clips under tests/assets with ffmpeg.

The clips are synthetic:
- alpha-vp9.webm and alpha-vp8.webm use a moving vertical edge between alpha 255 and alpha 64
- opaque-vp9.webm uses the ffmpeg testsrc2 pattern without alpha

ffmpeg must be able to encode VP8 and VP9, which means a build with the 'libvpx' encoders,
for example a full build from https://www.gyan.dev/ffmpeg/builds/.
"""

import argparse
import pathlib
import shutil
import subprocess
import sys

ALPHA_FILTER = "format=yuva420p,geq=r='128+64*sin(X/40)':g='64+96*sin(Y/30)':b='200':a='if(lt(mod(X+T*120,320),160),255,64)'"

ALPHA_INPUT = ["-f", "lavfi", "-i", "color=c=black:s=320x240:r=30:d=2"]

ALPHA_CLIPS = (
    ("alpha-vp9.webm", "libvpx-vp9"),
    ("alpha-vp8.webm", "libvpx"),
)


def run_ffmpeg(ffmpeg_path: str, arguments: list[str]) -> None:
    command = [ffmpeg_path, "-v", "error", "-y", *arguments]
    print(" ".join(command))
    subprocess.run(command, check=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default=shutil.which("ffmpeg"), help="path to an ffmpeg build with the libvpx encoders")
    parser.add_argument("--out", default=str(pathlib.Path(__file__).resolve().parent.parent / "tests" / "assets"), help="output directory")
    options = parser.parse_args()

    if not options.ffmpeg:
        print("ffmpeg was not found. Install one or pass --ffmpeg.", file=sys.stderr)
        return 1

    output_directory = pathlib.Path(options.out)
    output_directory.mkdir(parents=True, exist_ok=True)

    for name, encoder in ALPHA_CLIPS:
        run_ffmpeg(options.ffmpeg, [*ALPHA_INPUT, "-vf", ALPHA_FILTER, "-c:v", encoder, "-pix_fmt", "yuva420p", "-auto-alt-ref", "0", "-b:v", "300k", str(output_directory / name)])

    run_ffmpeg(options.ffmpeg, ["-f", "lavfi", "-i", "testsrc2=s=320x240:r=30:d=2", "-c:v", "libvpx-vp9", "-pix_fmt", "yuv420p", "-b:v", "400k", str(output_directory / "opaque-vp9.webm")])

    for clip in sorted(output_directory.glob("*.webm")):
        print(f"{clip.name}: {clip.stat().st_size} bytes")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
