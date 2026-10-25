#!/bin/zsh
# The real thing on a Mac: zsh tests/game.zsh [Lotus folder]
# The plugin is installed (from $RESURF_INSTALL – a git URL – or this folder), lotus surf get takes the Mac build of
# the game's newest GitHub release and makes it run on macOS, and the game starts without a window
# (-batchmode -nographics) for a while: does Unity come up, does it keep running? It needs the internet and changes
# nothing of yours (an empty home) – CI runs it on GitHub's macOS runners (.github/workflows/test.yml).
# $RESURF_RELEASES: a release list to use when GitHub's API says no (CI fetches it with its token).
# Exit status: 0 it runs, 1 it does not, 3 no release has a Mac build (nothing to start).
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

say "Installing the plugin from ${RESURF_INSTALL:-this folder}"
(sleep 2; print -rn -- y; sleep 1) | script -q /dev/null zsh -fc "$ROOT/bin/lotus plugin install ${(q)${RESURF_INSTALL:-$PLUGIN}}" 2>&1 | plain | tail -2
[[ -r $HOME/.config/lotus/plugins/resurferd/plugin.conf ]] || { print -r -- "The plugin did not install."; exit 1 }

say "lotus surf get"
mkdir -p $D
print -r -- "GET=release" >| $D/settings
out=$(lotus surf get); print -r -- $out
if [[ $out == *"GitHub could not be asked"* && -r $RESURF_RELEASES ]]; then
  say "GitHub's API said no – again with the release list from CI"
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
local -i bad=0
[[ -f $exe && -x $exe ]] && print -r -- "✓ its program is executable: ${exe:t}" || { print -r -- "✗ its program is not executable: $exe"; bad=1 }
print -r -- "  $(file -b $exe)"
xattr -p com.apple.quarantine $A >/dev/null 2>&1 && { print -r -- "✗ still in quarantine"; bad=1 } || print -r -- "✓ not in quarantine"
if codesign --verify --deep --strict $A 2>$OUT/codesign.txt; then print -r -- "✓ signed for this Mac: $(codesign -dv $A 2>&1 | grep -m1 '^Signature=')"
else print -r -- "✗ the signature does not hold:"; sed 's/^/    /' $OUT/codesign.txt; bad=1; fi

# An Intel build on Apple silicon needs Rosetta 2 (GitHub's runners may not have it yet)
if [[ $(sysctl -n hw.optional.arm64 2>/dev/null) == 1 && $(file -b $exe) != *arm64* ]] && ! arch -x86_64 /usr/bin/true 2>/dev/null; then
  say "Installing Rosetta 2"
  sudo softwareupdate --install-rosetta --agree-to-license >/dev/null 2>&1 || softwareupdate --install-rosetta --agree-to-license
fi

say "Starting the game without a window"
local log=$OUT/Player.log
local -i secs=${RESURF_SECONDS:-60} pid i code=0
$exe -batchmode -nographics -logFile $log </dev/null >/dev/null 2>&1 &
pid=$!
for (( i = 0; i < secs; i++ )); do kill -0 $pid 2>/dev/null || break; sleep 1; done
if kill -0 $pid 2>/dev/null; then
  print -r -- "✓ still running after $secs seconds"
  kill -TERM $pid 2>/dev/null; sleep 2; kill -KILL $pid 2>/dev/null
else
  wait $pid; code=$?
  print -r -- "✗ it ended after $i seconds (status $code)"
  bad=1
fi
if [[ -s $log ]]; then
  grep -m1 'Initialize engine version' $log && print -r -- "✓ Unity came up" || { print -r -- "✗ Unity did not say it came up"; bad=1 }
  grep -qE 'Crash!!!|Received signal SIG' $log && { print -r -- "✗ it crashed:"; grep -m5 -E 'Crash!!!|Received signal' $log; bad=1 }
  local -i ex=$(grep -c 'Exception' $log)
  if (( ex )); then
    print -r -- "  $ex lines with an exception in its log – the first ones:"
    grep -m3 'Exception' $log | cut -c1-200 | sed 's/^/    /'
  else
    print -r -- "  no exception in its log"
  fi
  print -r -- "  the end of its log:"; tail -n 15 $log | cut -c1-200 | sed 's/^/    /'
else
  print -r -- "✗ it wrote no log"; bad=1
fi
(( bad )) && exit 1
print -r -- "The game runs on this Mac."
