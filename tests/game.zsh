#!/bin/zsh
# The real thing on a Mac: zsh tests/game.zsh [Lotus folder]
# The plugin is installed (from $RESURF_INSTALL – a git URL – or this folder), lotus surf get takes the Mac build of
# the game's newest GitHub release and makes it run on macOS. The Resurf mod is built against that build's own
# libraries (mod/build.zsh) and put in with lotus surf mod on, with Unity Doorstop ($RESURF_DOORSTOP, else the
# plugin's). Then the game starts without a window (-batchmode -nographics): the first time coins come from Lotus –
# waiting ones when it starts, more while it runs – and the coins' glow is fixed; the second time the coins must
# still be there, from the game's own save; the third time macOS starts it (open), as the Finder and the Dock do.
# It needs the internet and changes nothing of yours (an empty home) – CI runs it on GitHub's macOS runners.
# $RESURF_RELEASES: a release list to use when GitHub's API says no (CI fetches it with its token).
# $RESURF_OUT: where the logs and the mod go (the mod as ResurfMod.dll – CI keeps it).
# Exit status: 0 it all works, 1 something does not, 3 no release has a Mac build (nothing to start).
emulate -L zsh
setopt extendedglob
typeset -g PLUGIN=${0:A:h:h} ROOT=
if [[ -n $1 && -r $1/lib/core.zsh ]]; then ROOT=${1:A}
elif [[ -n $LOTUS_ROOT && -r $LOTUS_ROOT/lib/core.zsh ]]; then ROOT=${LOTUS_ROOT:A}
elif (( $+commands[lotus] )); then ROOT=${commands[lotus]:A:h:h}
fi
[[ -x $ROOT/bin/lotus ]] || { print -u2 -r -- "Lotus is needed: zsh tests/game.zsh <a Lotus folder>"; exit 2 }
export HOME=$(mktemp -d "${TMPDIR:-/tmp}/resurf-game.XXXXXX") XDG_CONFIG_HOME= XDG_CACHE_HOME= XDG_STATE_HOME= XDG_DATA_HOME=
export LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8 LOTUS_ROOT=$ROOT
plain() { sed $'s/\e\\[[0-9;?]*[a-zA-Z]//g' | tr -d '\r' }
say() { print -r -- "── $*" }
lotus() { $ROOT/bin/lotus "$@" 2>&1 | plain }
D=$HOME/.local/share/lotus/plugins/resurferd A="$HOME/.local/share/lotus/plugins/resurferd/Subway Surfers Resurferd.app"
OUT=${RESURF_OUT:-$HOME/out}
mkdir -p $OUT
# The game keeps its save in the home folder macOS has for you, whatever $HOME says: a save of yours goes aside
# while the test runs, and comes back after it
local realhome=${~:-~$USERNAME}           # ~user asks macOS – userdirs knows only /etc/passwd
[[ $realhome == /?* && -d $realhome ]] || realhome=$HOME
GDATA="$realhome/Library/Application Support/unity.Kiloo Games.Subway Surf"
if [[ -e $GDATA ]]; then
  mv -- $GDATA $GDATA.resurf-test-$$ || { print -r -- "Your save could not be put aside: $GDATA"; exit 1 }
  TRAPEXIT() { rm -rf -- $GDATA; mv -- $GDATA.resurf-test-$$ $GDATA }
fi
typeset -gi bad=0
ok()   { print -r -- "✓ $*" }
fail() { print -r -- "✗ $*"; bad=1 }
waitlog() {   # <log> <pattern> <seconds> – the pattern in the log within so many seconds?
  local -i i
  for (( i = 0; i < $3 * 2; i++ )); do grep -qF -- "$2" $1 2>/dev/null && return 0; sleep 0.5; done
  return 1
}

say "Installing the plugin from ${RESURF_INSTALL:-this folder}"
(sleep 2; print -rn -- y; sleep 1) | script -q /dev/null zsh -fc "$ROOT/bin/lotus plugin install ${(q)${RESURF_INSTALL:-$PLUGIN}}" 2>&1 | plain | tail -2
[[ -r $HOME/.config/lotus/plugins/resurferd/plugin.conf ]] || { print -r -- "The plugin did not install."; exit 1 }

say "lotus surf get – the game as it is first (MOD=off)"
mkdir -p $D
print -r -- $'GET=release\nMOD=off' >| $D/settings
out=$(lotus surf get); print -r -- $out
if [[ $out == *"GitHub could not be asked"* && -r $RESURF_RELEASES ]]; then
  say "GitHub said no – again with the release list from CI"
  print -r -- "RELEASES=file://${RESURF_RELEASES:A}" >> $D/settings
  out=$(lotus surf get); print -r -- $out
fi
if [[ $out == *"No release has a Mac build"* ]]; then
  print -r -- "No release of the game has a Mac build – on a Mac, lotus surf builds one with Unity."
  exit 3
fi
[[ -d $A ]] || { print -r -- "There is no game after lotus surf get."; exit 1 }

say "What macOS sees"
lotus surf status
local exe=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' $A/Contents/Info.plist 2>/dev/null)
exe=$A/Contents/MacOS/$exe
[[ -f $exe && -x $exe ]] && ok "its program is executable: ${exe:t}" || fail "its program is not executable: $exe"
print -r -- "  $(file -b $exe)"
xattr -p com.apple.quarantine $A >/dev/null 2>&1 && fail "still in quarantine" || ok "not in quarantine"

say "Inside the app"
(cd $A/Contents && find . -maxdepth 4 ! -path '*/Managed/*' ! -path '*/MonoBleedingEdge/*' ! -path '*/_CodeSignature/*' | sort | head -60 | sed 's/^/  /')
print -r -- "  every .json and boot.config: $(cd $A && find . \( -name '*.json' -o -name boot.config \) | paste -sd' ' -)"
local managed=$(cd $A && find . -type d -name Managed | head -1)
print -r -- "  Managed: ${managed:-none} – $(ls $A/$managed 2>/dev/null | paste -sd' ' - | cut -c1-600)"
[[ -n $managed ]] && for f in $(cd $A && find . -name boot.config); do print -r -- "  $f:"; sed 's/^/    /' $A/$f; done

say "The Resurf mod, built against the game's own libraries"
local res=$A/Contents/Resources/Resurf
zsh -f $PLUGIN/mod/build.zsh $A $OUT/ResurfMod.dll || { print -r -- "✗ the mod did not build"; exit 1 }
print -r -- "MOD=$OUT/ResurfMod.dll" >> $D/settings
[[ -r $RESURF_DOORSTOP ]] && print -r -- "DOORSTOP=${RESURF_DOORSTOP:A}" >> $D/settings
lotus surf mod on
cmp -s $OUT/ResurfMod.dll $res/ResurfMod.dll && ok "the mod is in the game" || fail "the mod is not in the game"
[[ -s $res/libdoorstop.dylib ]] && ok "Unity Doorstop too: $(file -b $res/libdoorstop.dylib | cut -c1-80)" || fail "Doorstop is not in the game"
grep -q DOORSTOP_TARGET_ASSEMBLY $exe && [[ -x $exe.game ]] && ok "the launcher starts ${exe:t}.game with Doorstop" || fail "no launcher in the game"
print -r -- "  the launcher:"; sed 's/^/    /' $exe
if codesign --verify --deep --strict $A 2>$OUT/codesign.txt; then ok "signed for this Mac, with the mod: $(codesign -dv $A 2>&1 | grep -m1 '^Signature=')"
else fail "the signature does not hold:"; sed 's/^/    /' $OUT/codesign.txt; fi

say "Coins, the FPS counter, a sky and a look, while the game is closed"
lotus surf coins 5000
lotus surf fps on
lotus surf sky sunset
lotus surf look pretty
# a panorama for later: 256 × 128, a gradient – PNG by hand (zlib), so nothing but python3 is needed
cat >| $OUT/make-panorama.py <<'EOF'
import struct, sys, zlib
w, h = 256, 128
rows = b"".join(b"\0" + b"".join(bytes([x, 64 + y // 2, 255 - y]) for x in range(w)) for y in range(h))
chunk = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")
open(sys.argv[1], "wb").write(png)
EOF
python3 -I $OUT/make-panorama.py $OUT/panorama.png && print -r -- "  a panorama to give it later: $(file -b $OUT/panorama.png)"

# An Intel build on Apple silicon needs Rosetta 2 (GitHub's runners may not have it yet)
if [[ $(sysctl -n hw.optional.arm64 2>/dev/null) == 1 && $(file -b $exe.game) != *arm64* ]] && ! arch -x86_64 /usr/bin/true 2>/dev/null; then
  say "Installing Rosetta 2"
  sudo softwareupdate --install-rosetta --agree-to-license >/dev/null 2>&1 || softwareupdate --install-rosetta --agree-to-license
fi

run() {   # <n> <seconds> – the game without a window, its log in $OUT/Player-<n>.log; REPLY = its pid
  $exe -batchmode -nographics -logFile $OUT/Player-$1.log </dev/null >/dev/null 2>&1 &
  REPLY=$!
}
stop() {   # <pid> <n> <seconds it should have run>
  if kill -0 $1 2>/dev/null; then ok "the game still ran after $3 seconds"
  else fail "the game ended by itself (run $2)"; fi
  kill -TERM $1 2>/dev/null; sleep 3; kill -KILL $1 2>/dev/null
}

say "The first start"
local log=$OUT/Player-1.log
local -i pid start=0
run 1; pid=$REPLY
if waitlog $log "[Resurf] mod" 60; then ok "$(grep -m1 -F '[Resurf] mod' $log)"
else fail "the mod did not load – the end of the game's log:"; tail -n 25 $log 2>/dev/null | cut -c1-200 | sed 's/^/    /'; fi
grep -m1 -F 'Initialize engine version' $log >/dev/null && ok "Unity came up" || fail "Unity did not say it came up"
local had=$(grep -m1 -oE '\[Resurf\] coins [0-9]+ \(from' $log | grep -oE '[0-9]+')
start=${had:-0}
print -r -- "  coins in the game's save: $start"
waitlog $log "[Resurf] coins +5000 → $(( start + 5000 ))" 15 && ok "the waiting coins came: +5000 → $(( start + 5000 ))" || fail "the waiting coins did not come"
sleep 3
out=$(lotus surf coins 250); print -r -- "  lotus surf coins 250: ${out## #}"
[[ ${out//,/} == *"you have $(( start + 5250 )) now"* ]] && ok "coins while it runs: there within a second" \
  || fail "coins while it runs did not come back"
waitlog $log "[Resurf] coins +250 → $(( start + 5250 ))" 5 && ok "the game took them: +250 → $(( start + 5250 ))" || fail "the game did not take the 250"
waitlog $log "[Resurf] glow: glow gold" 20 && ok "$(grep -m1 -F '[Resurf] glow: glow gold' $log)" || fail "the coins' glow was not fixed: $(grep -F '[Resurf] glow' $log | head -2)"
grep -F '[Resurf] glow: EFX' $log | head -1 | sed 's/^/  /'
grep -m1 -F '[Resurf] sky: found' $log | cut -c1-200 | sed 's/^/  /'
waitlog $log "[Resurf] sky: sunset – 512 × 512" 20 && ok "the sky Sunset is painted" || fail "no sunset: $(grep -F '[Resurf] sky' $log | head -3 | tr '\n' ' ')"
waitlog $log " shows sunset" 10 && ok "$(grep -m1 -F ' shows sunset' $log | cut -c1-120)" || fail "no sky material shows the sunset"
out=$(lotus surf sky add $OUT/panorama.png); print -r -- "  lotus surf sky add panorama.png: ${out## #}"
waitlog $log "[Resurf] sky: file:panorama.png – 256 × 128" 10 && ok "a picture of yours, while it runs: read" || fail "the panorama was not read: $(grep -F '[Resurf] sky' $log | tail -2 | tr '\n' ' ')"
waitlog $log " shows file:panorama.png" 6 && ok "  and the sky shows it" || fail "no sky material shows the panorama"
# the look: the shaders it builds the finish from, sharper, the finish on the world camera (no picture here – no GPU)
waitlog $log "[Resurf] look: Hidden/Internal-Colored" 10 && print -r -- "  $(grep -m1 -F '[Resurf] look: Hidden' $log | cut -c1-120)"
grep -q -F '[Resurf] look: Hidden/Internal-Colored yes' $log && ok "the look's shaders are in this build" || fail "no Hidden/Internal-Colored for the look"
waitlog $log "[Resurf] look: sharper" 5 && ok "$(grep -m1 -F '[Resurf] look: sharper' $log | cut -c1-140)" || fail "not sharper: $(grep -F '[Resurf] look' $log | head -3 | tr '\n' ' ')"
waitlog $log "[Resurf] look: pretty on the camera" 10 && ok "$(grep -m1 -F '[Resurf] look: pretty on' $log | cut -c1-120)" || fail "the look found no camera: $(grep -F '[Resurf] look' $log | head -3 | tr '\n' ' ')"
out=$(lotus surf look sharp off); print -r -- "  lotus surf look sharp off: ${out## #}"
waitlog $log "[Resurf] look: the game's own quality again" 6 && ok "  sharper off, while it runs" || fail "sharper did not go off: $(grep -F '[Resurf] look' $log | tail -2 | tr '\n' ' ') – settings: $(tr '\n' ' ' <$GDATA/resurf/settings)"
sleep 5
stop $pid 1 30
local saved=$(grep -m1 "^COINS=" $GDATA/resurf/save.txt 2>/dev/null)
[[ $saved == "COINS=$(( start + 5250 ))" ]] && ok "save.txt: $saved" || fail "save.txt says ${saved:-nothing}"
[[ -s $GDATA/playerdata ]] && ok "the game's own save is there: $GDATA/playerdata" || fail "the game wrote no playerdata in $GDATA"

say "The second start – are the coins still there?"
log=$OUT/Player-2.log
run 2; pid=$REPLY
waitlog $log "[Resurf] coins $(( start + 5250 )) (from the game's save)" 60 \
  && ok "the game loaded its save: $(( start + 5250 )) coins" || fail "not from the save: $(grep -m1 -F '[Resurf] coins' $log)"
waitlog $log " shows file:panorama.png" 20 && ok "the sky kept: your panorama again" || fail "the sky was not kept: $(grep -F '[Resurf] sky' $log | head -2 | tr '\n' ' ')"
waitlog $log "[Resurf] look: pretty on the camera" 10 && ok "the look kept: pretty" || fail "the look was not kept: $(grep -F '[Resurf] look' $log | head -2 | tr '\n' ' ')"
grep -q -F '[Resurf] look: sharper' $log && fail "sharper came back" || ok "  sharper stayed off"
sleep 10
stop $pid 2 15
lotus surf stats

say "The third start – the way the Finder and the Dock start it (open, Launch Services)"
log=$OUT/Player-3.log
open -n $A --args -batchmode -nographics -logFile $log
waitlog $log "[Resurf] mod" 60 && ok "started by macOS, with the mod: $(grep -m1 -F '[Resurf] mod' $log | cut -c1-80)" \
  || fail "started by macOS, the mod did not load: $(tail -n 5 $log 2>/dev/null | tr '\n' ' ' | cut -c1-200)"
pkill -TERM -f "$A/Contents/MacOS/" 2>/dev/null; sleep 3; pkill -KILL -f "$A/Contents/MacOS/" 2>/dev/null

say "The game's logs"
for log in $OUT/Player-*.log(N); do
  local -i ex=$(grep -c 'Exception' $log)
  print -r -- "  ${log:t}: $(grep -c -F '[Resurf]' $log) lines of the mod, $ex with an exception"
  grep -F '[Resurf]' $log | head -24 | cut -c1-160 | sed 's/^/    /'
  (( ex )) && grep -m3 -A2 'Exception' $log | cut -c1-200 | sed 's/^/    ! /'
  grep -E -m5 'Crash!!!|Received signal SIG' $log && fail "it crashed"
done
(( bad )) && exit 1
print -r -- "The game runs on this Mac, with the Resurf mod."
