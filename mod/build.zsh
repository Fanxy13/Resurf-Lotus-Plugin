#!/bin/zsh
# Builds the Resurf mod against the game's own libraries: zsh mod/build.zsh <the app, or its Data/Managed> [out.dll]
# The compiler is .NET's csc (dotnet) – deterministic: the same source and libraries give the same bytes – or Mono's
# mcs. CI builds mod/ResurfMod.dll so, against the Mac build of the game's release, and tests it in the game.
emulate -L zsh
setopt extendedglob
local here=${0:A:h} m=${1:A} out=${2:-${0:A:h}/ResurfMod.dll} x csc= sdk
[[ -d $m/Contents/Resources/Data/Managed ]] && m=$m/Contents/Resources/Data/Managed
[[ -r $m/UnityEngine.CoreModule.dll ]] || { print -u2 -r -- "No Unity libraries in $m – give the app or its Data/Managed folder"; exit 2 }
local -a refs=()
for x in mscorlib System System.Core UnityEngine.CoreModule UnityEngine.IMGUIModule UnityEngine.InputLegacyModule UnityEngine.TextRenderingModule; do
  [[ -r $m/$x.dll ]] || { print -u2 -r -- "$x.dll is missing in $m"; exit 2 }
  refs+=(-r:$m/$x.dll)
done
local -a src=($here/src/*.cs(N))
mkdir -p -- ${out:h}
if (( $+commands[dotnet] )); then
  for sdk in ${(f)"$(dotnet --list-sdks 2>/dev/null)"}; do      # "8.0.404 [/usr/local/share/dotnet/sdk]" – the newest last
    x=${${sdk#*\[}%\]}/${sdk%% *}/Roslyn/bincore/csc.dll
    [[ -r $x ]] && csc=$x
  done
fi
if [[ -n $csc ]]; then
  print -r -- "csc ${${csc:h:h:h}:t}"
  dotnet $csc -nologo -noconfig -nostdlib+ -deterministic+ -optimize+ -target:library -out:$out $refs $src || exit 1
elif (( $+commands[mcs] )); then
  print -r -- "mcs $(mcs --version 2>/dev/null)"
  mcs -noconfig -nostdlib -optimize+ -target:library -out:$out $refs $src || exit 1
else
  print -u2 -r -- "No C# compiler: the .NET SDK (brew install --cask dotnet-sdk) or Mono's mcs"
  exit 2
fi
print -r -- "$out  $(shasum -a 256 $out | cut -d' ' -f1)"
