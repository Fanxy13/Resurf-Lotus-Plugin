#!/bin/zsh
# The real thing on a Mac: zsh tests/game.zsh [Lotus folder]
# The plugin is installed (from $RESURF_INSTALL – a git URL – or this folder), lotus surf get takes the Mac build of
# the game's newest GitHub release and makes it run on macOS. The Resurf mod is built against that build's own
# libraries (mod/build.zsh) and put in with lotus surf mod on. Then the game starts twice without a window
# (-batchmode -nographics): the first time coins come from Lotus – waiting ones when it starts, more while it runs –
# and the coins' glow is fixed; the second time the coins must still be there, from the game's own save.
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

say "The Resurf mod, built against the game's own libraries"
local data=$A/Contents/Resources/Data
print -r -- "  ScriptingAssemblies.json: $(head -c 400 $data/ScriptingAssemblies.json)"
print -r -- "  RuntimeInitializeOnLoads.json: $(head -c 400 $data/RuntimeInitializeOnLoads.json 2>/dev/null || print '(none)')"
zsh -f $PLUGIN/mod/build.zsh $A $OUT/ResurfMod.dll || { print -r -- "✗ the mod did not build"; exit 1 }
print -r -- "MOD=$OUT/ResurfMod.dll" >> $D/settings
lotus surf mod on
cmp -s $OUT/ResurfMod.dll $data/Managed/ResurfMod.dll && ok "the mod is in the game" || fail "the mod is not in the game"
grep -q '"ResurfMod.dll"' $data/ScriptingAssemblies.json && ok "Unity is told to load it" || fail "ScriptingAssemblies.json does not name it"
grep -q '"ResurfMod"' $data/RuntimeInitializeOnLoads.json && ok "and to start it" || fail "RuntimeInitializeOnLoads.json does not name it"
if codesign --verify --deep --strict $A 2>$OUT/codesign.txt; then ok "signed for this Mac, with the mod: $(codesign -dv $A 2>&1 | grep -m1 '^Signature=')"
else fail "the signature does not hold:"; sed 's/^/    /' $OUT/codesign.txt; fi

say "Coins and the FPS counter, while the game is closed"
lotus surf coins 5000
lotus surf fps on

# An Intel build on Apple silicon needs Rosetta 2 (GitHub's runners may not have it yet)
if [[ $(sysctl -n hw.optional.arm64 2>/dev/null) == 1 && $(file -b $exe) != *arm64* ]] && ! arch -x86_64 /usr/bin/true 2>/dev/null; then
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
sleep 8
stop $pid 1 30
local saved=$(grep -m1 '^COINS=' "$HOME/Library/Application Support"/*/resurf/save.txt 2>/dev/null)
[[ $saved == "COINS=$(( start + 5250 ))" ]] && ok "save.txt: $saved" || fail "save.txt says ${saved:-nothing}"
[[ -s $(print -r -- "$HOME/Library/Application Support"/*/playerdata(N[1])) ]] && ok "the game's own save is there" || fail "the game wrote no playerdata"

say "The second start – are the coins still there?"
log=$OUT/Player-2.log
run 2; pid=$REPLY
waitlog $log "[Resurf] coins $(( start + 5250 )) (from the game's save)" 60 \
  && ok "the game loaded its save: $(( start + 5250 )) coins" || fail "not from the save: $(grep -m1 -F '[Resurf] coins' $log)"
sleep 10
stop $pid 2 15
lotus surf stats

say "The game's logs"
for log in $OUT/Player-*.log(N); do
  local -i ex=$(grep -c 'Exception' $log)
  print -r -- "  ${log:t}: $(grep -c -F '[Resurf]' $log) lines of the mod, $ex with an exception"
  grep -F '[Resurf]' $log | head -12 | cut -c1-160 | sed 's/^/    /'
  grep -E -m5 'Crash!!!|Received signal SIG' $log && fail "it crashed"
done
(( bad )) && exit 1
print -r -- "The game runs on this Mac, with the Resurf mod."
