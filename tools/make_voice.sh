#!/usr/bin/env bash
# Generates the Sinistar's voice into ../Sounds/Voice*.wav with the Piper text-to-speech engine, then
# pitches it down and runs it through an emulation of the cabinet's CVSD speech chip (tools/sinistar_voice.py).
# The scream is not speech: tools/make_sounds.py synthesises it.
#
# Needs a Piper binary and the en_GB-vctk-medium voice (VCTK corpus, CC BY 4.0), speaker 75 (VCTK p345).
# Override the paths with
# PIPER=/path/to/piper and VOICE=/path/to/model.onnx.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out="$here/../Sounds"
piper="${PIPER:-$HOME/.local/share/piper/bin/piper}"
voice="${VOICE:-$HOME/.local/share/piper/voices/en_GB-vctk-medium.onnx}"
speaker="${SPEAKER:-75}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

say() {
  local name="$1" text="$2"
  echo "$text" | "$piper" --model "$voice" --speaker "$speaker" --length_scale 1.15 \
    --noise_scale 0.4 --noise_w 0.5 --output_file "$tmp/$name.wav" >/dev/null 2>&1
  python3 "$here/sinistar_voice.py" "$tmp/$name.wav" "$out/Voice$name.wav"
}

mkdir -p "$out"
say BewareILive   "Beware, I live!"
say IHunger       "I hunger!"
say RunCoward     "Run, coward!"
say IAmSinistar   "I am Sinistar!"
say BewareCoward  "Beware, coward!"
say RunRunRun     "Run! Run! Run!"
say IHungerCoward "I hunger, coward!"
echo "Wrote voices to $(cd "$out" && pwd)"
