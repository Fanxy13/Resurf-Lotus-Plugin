#!/bin/zsh
# The plugin's tests: zsh tests/run.zsh [Lotus folder] [name …]
# They need Lotus – the folder you give, $LOTUS_ROOT, or the lotus on your PATH (brew install Fanxy13/lotus/lotus).
# Every tests/test-*.zsh runs in its own empty home (nothing of yours is read or changed), installs the plugin from
# this folder there and reports with check / check_eq / check_has. Exit status 1 when a check failed.
emulate -L zsh
setopt extendedglob
typeset -g PLUGIN=${0:A:h:h} ROOT=
typeset -gi PASSED=0 FAILED=0

if [[ -n $1 && -r $1/lib/core.zsh ]]; then ROOT=${1:A}; shift
elif [[ -n $LOTUS_ROOT && -r $LOTUS_ROOT/lib/core.zsh ]]; then ROOT=${LOTUS_ROOT:A}
elif (( $+commands[lotus] )); then ROOT=${commands[lotus]:A:h:h}
fi
if [[ ! -x $ROOT/bin/lotus ]]; then
  print -u2 -r -- "The tests need Lotus: zsh tests/run.zsh <a Lotus folder>, or brew install Fanxy13/lotus/lotus"
  exit 2
fi

check() {   # <what> <command…> – passes when the command succeeds
  local what=$1; shift
  if "$@" >/dev/null 2>&1; then (( PASSED++ )); print -r -- "  ✓ $what"
  else (( FAILED++ )); print -r -- "  ✗ $what"; fi
}
check_eq() {   # <what> <got> <want>
  if [[ $2 == $3 ]]; then (( PASSED++ )); print -r -- "  ✓ $1"
  else (( FAILED++ )); print -r -- "  ✗ $1"$'\n'"      got:  ${2[1,300]}"$'\n'"      want: ${3[1,300]}"; fi
}
check_has() {   # <what> <text> <part>
  if [[ $2 == *"$3"* ]]; then (( PASSED++ )); print -r -- "  ✓ $1"
  else (( FAILED++ )); print -r -- "  ✗ $1"$'\n'"      missing: $3"$'\n'"      in: ${2[1,400]}"; fi
}

local ver=${${(M)${(f)"$(<$ROOT/lib/core.zsh)"}:#typeset -g LOTUS_VERSION=*}#*=}
print -r -- "Lotus ${ver:-?} in ${ROOT/#$HOME/~}"
local file
local -a files=($PLUGIN/tests/test-*.zsh(N))
(( $# )) && files=(${^@:/(#m)*/$PLUGIN/tests/test-$MATCH.zsh})
for file in $files; do
  print -r -- "${file:t:r}"
  local home=$(mktemp -d "${TMPDIR:-/tmp}/resurf-test.XXXXXX")
  (
    export LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8
    export HOME=$home XDG_CONFIG_HOME= XDG_CACHE_HOME= XDG_STATE_HOME= XDG_DATA_HOME= LOTUS_ROOT=$ROOT PLUGIN=$PLUGIN
    PASSED=0 FAILED=0          # this file's own count
    source $file
    print -r -- "$PASSED $FAILED" >| $home/.result
  )
  if [[ -r $home/.result ]]; then
    local -a r=($(<$home/.result))
    (( PASSED += r[1], FAILED += r[2] ))
  else
    (( FAILED++ )); print -r -- "  ✗ the test stopped before its end (an error in the test file?)"
  fi
  rm -rf -- "${home:?}"
done
print
print -r -- "$PASSED passed, $FAILED failed"
(( FAILED == 0 ))
