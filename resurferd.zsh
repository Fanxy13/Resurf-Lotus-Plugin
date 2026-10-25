# Subway Surfers Resurferd on this Mac – a plugin for Lotus with full access (plugin.conf).
#   lotus surf                      play – the first time it gets the game (it asks first)
#   lotus surf play --fullscreen    or --window
#   lotus surf get                  the game now, without the question – for scripts too
#   lotus surf update               a newer release or a newer source: the game again
#   lotus surf build                built from the source with Unity, even when a release has a Mac build
#   lotus surf install <zip|app>    a Mac build you downloaded yourself
#   lotus surf status               where it is, where it came from, what it needs
#   lotus surf log                  what Unity said in the last build
#   lotus surf remove               the game and its source go (asks) – your progress in the game stays
#   lotus surf coins [<n>|set <n>]  coins for the game (the Resurf mod hands them over) – without a number: how many
#   lotus surf stats                coins, high score, best distance … as the game saved them
#   lotus surf fps [on|off]         the FPS counter in the game · glow fix|original · mod on|off
# The Resurf mod (mod/ResurfMod.dll, its source in mod/src) goes into the app with the rest of _rs_fix: the game
# loads it at start. It fixes the coins' glow, saves the game reliably, writes a readable save, shows the FPS and
# takes coins from Lotus – the files it shares with Lotus are in <the game's folder>/resurf (_rs_gamedata).
# The game is a Unity 2019.4 project. Its Mac build either comes from its GitHub releases (made on Linux by its CI:
# not executable, not signed) or Unity builds it here. Either way Lotus makes it run on macOS: the program
# executable, out of quarantine, signed for this Mac. Unity 2019 builds for Intel Macs – Apple silicon runs it
# with Rosetta 2.
# In the data folder ($LOTUS_PLUGIN_DATA):
#   Subway Surfers Resurferd.app    the game
#   source/                         the Unity project (git), with Unity's Library – a second build is much quicker
#   game.info                       where the game came from: FROM=release|source|file, VERSION, DATE
#   build.log                       what Unity said in the last build
#   settings                        yours, KEY=value: REPO=owner/name (a fork), GET=auto|release|build (how the
#                                   game comes the first time), UNITY=<Unity.app> (one Unity Hub does not know),
#                                   GIT=<git URL>, RELEASES=<GitHub API link> and WEB=<GitHub> (the source and the
#                                   releases from elsewhere – WEB's release pages when the API says no)
source $LOTUS_ROOT/lib/core.zsh && source $LOTUS_ROOT/lib/ui.zsh && lotus_load || exit 1
setopt extendedglob
zmodload -F zsh/stat b:zstat 2>/dev/null
zmodload zsh/datetime 2>/dev/null
LOTUS_LOG_COMP=resurferd
[[ $LOTUS_PLUGIN_DATA == /?* ]] || { print -u2 -r -- "lotus: resurferd runs as a Lotus plugin: lotus surf"; exit 1 }

# KEY=value lines of a file → KV (read line by line, never sourced)
_rs_kv() {
  typeset -gA KV=()
  local line
  [[ -r $1 ]] || return 0
  for line in "${(@f)$(<$1)}"; do
    line=${line//$'\r'/}
    [[ $line == [A-Z][A-Z0-9_]#=* ]] && KV[${line%%=*}]=${line#*=}
  done
  return 0
}

typeset -g D=$LOTUS_PLUGIN_DATA NAME="Subway Surfers Resurferd"
typeset -g APP="$D/$NAME.app" SRC=$D/source LOG=$D/build.log INFO=$D/game.info
typeset -gA cfg=() game=()
_rs_kv $D/settings; cfg=("${(@kv)KV}")
typeset -g REPO=${cfg[REPO]:-cyberillcbutsmarter/subway-surfers-resurferd}
[[ $REPO == [A-Za-z0-9_.-]##/[A-Za-z0-9_.-]## ]] || REPO=cyberillcbutsmarter/subway-surfers-resurferd
typeset -g GIT=${cfg[GIT]:-https://github.com/$REPO.git}
[[ $GIT == -* ]] && GIT=https://github.com/$REPO.git
typeset -g RELEASES=${cfg[RELEASES]:-https://api.github.com/repos/$REPO/releases?per_page=30}
typeset -g WEB=${cfg[WEB]:-https://github.com}
[[ $WEB == (https|file)://* ]] || WEB=https://github.com
typeset -g GET=${cfg[GET]:-auto} UVER= UCS=
[[ $GET == (auto|release|build) ]] || GET=auto

_rs_save_info() {   # <release|source|file> <version>
  print -r -- "FROM=$1"$'\n'"VERSION=$2"$'\n'"DATE=$(strftime %Y-%m-%d $EPOCHSECONDS)" >| $INFO
}

_rs_apple_silicon() { [[ $(sysctl -n hw.optional.arm64 2>/dev/null) == 1 ]] }   # also when zsh runs as Intel
_rs_rosetta() { arch -x86_64 /usr/bin/true 2>/dev/null }

# Downloads and installs only with a yes – without a keyboard (scripts) it is a no
_rs_ask() {   # <question>
  ui_has_tty || { ui_dim "  $1 – no keyboard to ask, so no."; return 1 }
  ui_confirm "$1" y
}

# ── The app ───────────────────────────────────────────────────

# The program of a Mac app → REPLY ("" and status 1 when it is no Mac app)
_rs_exe() {   # <app>
  local app=$1 exe
  REPLY=
  [[ -r $app/Contents/Info.plist ]] || return 1
  exe=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' $app/Contents/Info.plist 2>/dev/null)
  if [[ -z $exe || $exe == */* || ! -f $app/Contents/MacOS/$exe ]]; then
    local -a f=($app/Contents/MacOS/*(.N))
    exe=${f[1]:t}
  fi
  [[ -n $exe ]] || return 1
  REPLY=$app/Contents/MacOS/$exe
}

# What a program is made for → REPLY: universal, apple, intel or ""
_rs_arch() {   # <program>
  case $(file -b $1 2>/dev/null) in
    *arm64*x86_64*|*x86_64*arm64*) REPLY=universal ;;
    *arm64*)                       REPLY=apple ;;
    *x86_64*)                      REPLY=intel ;;
    *)                             REPLY= ;;
  esac
}

# What a Mac build from Linux (the game's CI) or from a download needs before macOS opens it: its program
# executable, no quarantine (and no Finder data, which breaks a signature), the Resurf mod, an ad-hoc signature for
# this Mac – last, it covers the mod too
_rs_fix() {   # <app>
  _rs_exe $1 || return 1
  chmod -R u+rwX $1 2>/dev/null
  chmod +x $1/Contents/MacOS/*(.N) 2>/dev/null
  xattr -cr $1 2>/dev/null
  _rs_mod $1 || lotus_log WARN resurferd "the Resurf mod could not be put into ${1:t}"
  codesign --force --deep --sign - $1 >/dev/null 2>&1 || lotus_log WARN resurferd "codesign could not sign ${1:t}"
  return 0
}

# ── The Resurf mod (mod/): coins from Lotus, a save that holds, an FPS counter, the coins' glow ──

# What the app should have: the mod (mod/ResurfMod.dll) and Unity Doorstop, which loads it
# (mod/doorstop/libdoorstop.dylib) – MOD=<file> and DOORSTOP=<file> in the settings other ones, MOD=off neither
# → reply (mod doorstop); status 1 when there should be none, or the plugin has not both
_rs_mod_want() {
  reply=()
  [[ $cfg[MOD] == off ]] && return 1
  local m=${${cfg[MOD]:-$LOTUS_PLUGIN_DIR/mod/ResurfMod.dll}/#\~/$HOME}
  local d=${${cfg[DOORSTOP]:-$LOTUS_PLUGIN_DIR/mod/doorstop/libdoorstop.dylib}/#\~/$HOME}
  [[ -r $m && -r $d ]] || return 1
  reply=($m $d)
}

# The game's real program: the app's own – or, with the mod in, the one the launcher starts (<program>.game) → REPLY
_rs_bin() {   # <app>
  _rs_exe $1 || return 1
  [[ -f $REPLY.game ]] && REPLY=$REPLY.game
  return 0
}

# Has the app the mod it should have – or none, when it should have none? (A build without Mono: yes.)
_rs_mod_ok() {   # <app>
  local r=$1/Contents/Resources/Resurf exe
  [[ -d $1/Contents/Resources/Data/Managed ]] || return 0
  _rs_exe $1 || return 1
  exe=$REPLY
  if _rs_mod_want; then
    [[ -f $exe.game ]] && grep -q DOORSTOP_TARGET_ASSEMBLY $exe 2>/dev/null \
      && cmp -s -- $reply[1] $r/ResurfMod.dll && cmp -s -- $reply[2] $r/libdoorstop.dylib
  else
    [[ ! -e $exe.game && ! -e $r ]]
  fi
}

# The mod into a Unity Mono build, loaded by Unity Doorstop: the app's program becomes a small launcher that sets
# Doorstop up and starts the real one (<program>.game) – so the Finder, the Dock and lotus surf start it as before.
# The mod and Doorstop go into Contents/Resources/Resurf; of the game's own files only the program's name changes.
# When there should be no mod, it all comes out again and the program gets its name back. A build without Mono
# (IL2CPP) has nothing for Doorstop and stays as it is.
_rs_mod() {   # <app>
  local r=$1/Contents/Resources/Resurf exe
  [[ -n $1 && -d $1/Contents/Resources/Data/Managed ]] || return 0
  _rs_exe $1 || return 1
  exe=$REPLY
  [[ ${exe:t} == [A-Za-z0-9\ ._-]## ]] || return 1        # the launcher names it in quotes
  if _rs_mod_want; then
    mkdir -p -- $r && cp -f -- $reply[1] $r/ResurfMod.dll && cp -f -- $reply[2] $r/libdoorstop.dylib || return 1
    [[ -f $exe.game ]] || mv -- $exe $exe.game || return 1
    cat >| $exe <<EOF || return 1
#!/bin/sh
# $NAME with the Resurf mod: Unity Doorstop loads it, then the game (${exe:t}.game) starts.
# Put here by the Lotus plugin resurferd – lotus surf mod off gives the game its own program back.
here=\$(cd "\$(dirname "\$0")" && pwd)
mod="\$here/../Resources/Resurf"
export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY="\$mod/ResurfMod.dll"
export DYLD_LIBRARY_PATH="\$mod\${DYLD_LIBRARY_PATH:+:\$DYLD_LIBRARY_PATH}"
export DYLD_INSERT_LIBRARIES="\$mod/libdoorstop.dylib\${DYLD_INSERT_LIBRARIES:+:\$DYLD_INSERT_LIBRARIES}"
exec "\$here/${exe:t}.game" "\$@"
EOF
    chmod +x -- $exe $exe.game
    codesign --force --sign - $r/libdoorstop.dylib >/dev/null 2>&1
  else
    [[ -f $exe.game ]] && { mv -f -- $exe.game $exe || return 1 }
    rm -rf -- $r
  fi
  return 0
}

# Where the game keeps its save, and the mod its files: ~/Library/Application Support/<the app's bundle id> → REPLY
_rs_gamedata() {
  local id=
  [[ -r $APP/Contents/Info.plist ]] && id=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' $APP/Contents/Info.plist 2>/dev/null)
  [[ -n $id && $id != */* && $id != .* ]] || id="unity.Kiloo Games.Subway Surf"
  REPLY="$HOME/Library/Application Support/$id"
}

_rs_running() { pgrep -f -- "$NAME.app/Contents/MacOS/" >/dev/null 2>&1 }

# One KEY=value line of a settings file – the other lines stay; without a value the line goes
_rs_set_line() {   # <file> <KEY> [value]
  local f=$1 line
  local -a keep=()
  [[ -r $f ]] && for line in "${(@f)$(<$f)}"; do [[ -n $line && $line != $2=* ]] && keep+=($line); done
  (( $# > 2 )) && keep+=("$2=$3")
  mkdir -p -- ${f:h} && print -rl -- $keep >| $f
}

# 12345 → 12,345 → REPLY
_rs_num() {
  local s=$1 out=
  while (( ${#s} > 3 )); do out=,${s[-3,-1]}$out; s=${s[1,-4]}; done
  REPLY=$s$out
}

# How much room a file or folder takes, like 312M → REPLY
_rs_size() {   # <path>
  local s=$(du -sh $1 2>/dev/null)
  REPLY=${s%%[[:space:]]*}
}

# A new app takes the old one's place – the old one comes back when that fails
_rs_place() {   # <new app>
  local old=$D/.old.app
  rm -rf -- $old
  [[ -d $APP ]] && mv -- $APP $old
  if mv -- $1 $APP; then rm -rf -- $old; return 0; fi
  [[ -d $old ]] && mv -- $old $APP
  return 1
}

# The Mac app in an unpacked folder, the one closest to the top → REPLY
_rs_find_app() {   # <folder>
  local a
  local -a apps=($1/{,*/,*/*/}*.app(/N))
  for a in ${apps:#*/__MACOSX/*}; do
    _rs_exe $a && { REPLY=$a; return 0 }
  done
  REPLY=
  return 1
}

# Unpacks a zip (ditto keeps what macOS needs; unzip when ditto cannot) → REPLY = the Mac app in it
_rs_unpack() {   # <zip> <folder>
  rm -rf -- $2; mkdir -p $2
  ditto -x -k $1 $2 2>/dev/null || unzip -qo $1 -d $2 >/dev/null 2>&1 || return 1
  _rs_find_app $2
}

# ── Ready Mac builds: the game's GitHub releases ─────────────

# The newest release with a Mac build (an asset like …-StandaloneOSX.zip) → REL (tag link size digest name)
# Status 1: no release has one, 2: GitHub could not be asked
_rs_release() {
  typeset -ga REL=()
  local json line
  if json=$(curl -fsSL -m 20 -H 'Accept: application/vnd.github+json' -- "$RELEASES" 2>/dev/null); then
    line=$(print -r -- $json | lotus_jq -r '
      [ .[]? | select(.draft | not) | .tag_name as $t | .assets[]?
        | select((.name | test("\\.zip$"; "i")) and (.name | test("osx|macos|[^a-z]mac[^a-z]"; "i")))
        | [$t, .browser_download_url, (.size | tostring), (.digest // ""), .name] | join("\t") ]
      | first // empty') || return 2
    [[ -n $line ]] || return 1
    REL=("${(@ps:\t:)line}")
  else
    _rs_release_web || return
  fi
  # only from where the releases are: GitHub over https (file:// lists, for tests, may point to files)
  [[ $REL[2] == https://* || ( $REL[2] == file://* && ( $RELEASES == file://* || $WEB == file://* ) ) ]] || return 1
  [[ $REL[3] == <-> ]] || REL[3]=0
  return 0
}

# The same from GitHub's own pages, when its API says no – it answers 60 questions an hour without an account, and
# a shared network (a school, an office) runs out. The release feed has the newest 10 tags, the release pages after
# it 10 more each, each release's list of files the Mac build; there is no SHA-256 there, so that download is not
# checked. → REL, status as _rs_release
_rs_release_web() {
  local page href tag line url
  local -a tags=() hrefs=()
  local -i before
  for url in "$WEB/$REPO/releases.atom" "$WEB/$REPO/releases?page=2" "$WEB/$REPO/releases?page=3"; do
    page=$(curl -fsSL -m 20 -- "$url" 2>/dev/null) || break
    before=${#tags}
    for line in ${(f)"$(print -r -- $page | grep -oE 'href="[^"]*/releases/tag/[^"#?]+"')"}; do
      tag=${${line%\"}##*/releases/tag/}
      (( ${tags[(Ie)$tag]} )) || tags+=($tag)
    done
    (( ${#tags} > before )) || break
  done
  (( ${#tags} )) || return 2
  for tag in $tags; do
    page=$(curl -fsSL -m 20 -- "$WEB/$REPO/releases/expanded_assets/$tag" 2>/dev/null) || continue
    hrefs=(${(f)"$(print -r -- $page | grep -oE 'href="[^"]*/releases/download/[^"]+\.zip"')"})
    for href in $hrefs; do
      href=${${href#href=\"}%\"}
      [[ ${href:t} == (#i)*(osx|macos|[^a-z]mac[^a-z])*.zip ]] || continue
      [[ $href == /* ]] && href=$WEB$href
      local -i size=$(curl -fsIL -m 15 -- $href 2>/dev/null | awk 'tolower($1) == "content-length:" { n = $2 } END { print n + 0 }')
      REL=($tag $href $size "" ${href:t})
      return 0
    done
  done
  return 1
}

# The Mac build of the release in REL, while the lotus blooms: downloaded (real bytes), checked against the SHA-256
# GitHub gives, unpacked and made to run on macOS. Status 1 = failed, 2 = stopped
_rs_download() {
  local tag=$REL[1] url=$REL[2] digest=${REL[4]#sha256:} asset=$REL[5] sum
  local part=$D/download.zip.part work=$D/.unpack why=
  local -i total=$REL[3] pid rc=1
  local -a st
  setopt localoptions localtraps
  source $LOTUS_ROOT/lib/bg/progress.zsh
  lotus_lang_group bg
  typeset -gi RS_STOP=0
  trap 'RS_STOP=1' INT
  _bg_cancel() { RS_STOP=1 BG_CANCEL=0 }
  BG_CANCEL=0
  bg_ui_begin Resurferd "download|Downloading the Mac build|Download" "check|Checking it|Checksum" \
    "unpack|Unpacking the app|Unpack" "macos|Making it run on macOS|macOS"
  bg_ui_sub "github.com/$REPO · $tag"
  bg_ui_info file $asset
  bg_ui_stage download
  rm -f -- $part
  curl -fsSL -m 1800 -o $part -- "$url" 2>/dev/null &
  pid=$!
  while kill -0 $pid 2>/dev/null && (( ! RS_STOP )); do
    zstat -A st +size -- $part 2>/dev/null && (( total > 0 )) && bg_ui_progress $st[1] $total
    bg_ui_frame
    bg_ui_tick 8
  done
  if (( RS_STOP )); then
    kill -TERM $pid 2>/dev/null; wait $pid 2>/dev/null
    bg_ui_end; rm -f -- $part
    ui_info "Stopped."
    return 2
  fi
  wait $pid && rc=0 || why="The download did not finish."
  if (( rc == 0 )) && [[ -n $digest ]]; then
    bg_ui_stage check; bg_ui_progress 0 0; bg_ui_frame
    sum=$(shasum -a 256 $part 2>/dev/null)
    [[ ${sum%% *} == ${(L)digest} ]] || { rc=1 why="It is not the file GitHub promised (its SHA-256 differs)." }
  fi
  local new=
  if (( rc == 0 )); then
    bg_ui_stage unpack; bg_ui_frame
    _rs_unpack $part $work && new=$REPLY || { rc=1 why="There is no Mac app in $asset." }
  fi
  if (( rc == 0 )); then
    bg_ui_stage macos; bg_ui_frame
    _rs_fix $new && _rs_place $new || { rc=1 why="The app could not be put in place." }
  fi
  rm -rf -- $part $work
  if (( rc == 0 )); then
    _rs_save_info release $tag
    lotus_log INFO resurferd "Mac build of $tag installed"
    bg_ui_complete; bg_ui_end
    ui_success "Ready: $NAME, $tag"
    [[ -n $digest ]] || ui_dim "  GitHub gave no SHA-256 for it, so it could not be checked."
    return 0
  fi
  bg_ui_end
  ui_error "The Mac build of $tag did not work out" "$why" "lotus surf build builds the game from the source instead"
  return 1
}

# ── Built here: the source and Unity ──────────────────────────

# The game's source in source/ (git, only the newest commit): cloned the first time, brought up to date later –
# Unity's Library in it stays, so the next build is quick → REPLY = the commit
_rs_source() {
  if ! git --version >/dev/null 2>&1; then
    ui_error "git is missing" "" "xcode-select --install installs it, with Apple's Command Line Tools"
    return 1
  fi
  if [[ -d $SRC/.git ]]; then
    ui_step "Bringing the source up to date – github.com/$REPO …"
    git -C $SRC remote set-url origin $GIT 2>/dev/null
    git -C $SRC fetch --depth 1 --progress origin HEAD && git -C $SRC reset -q --hard FETCH_HEAD \
      || { ui_error "git could not bring the source up to date" "$GIT" "Are you online?"; return 1 }
  else
    ui_step "Getting the source – github.com/$REPO, about 120 MB …"
    rm -rf -- $SRC.part
    if ! git clone --depth 1 --progress -- $GIT $SRC.part; then
      rm -rf -- $SRC.part
      ui_error "git could not get the source" "$GIT" "Are you online?"
      return 1
    fi
    mv -- $SRC.part $SRC
  fi
  REPLY=$(git -C $SRC rev-parse --short HEAD 2>/dev/null)
}

# The Unity version the project is made with → UVER, UCS (its changeset: Unity Hub needs it for older versions)
_rs_project_version() {
  local f=$SRC/ProjectSettings/ProjectVersion.txt line
  UVER= UCS=
  [[ -r $f ]] || return 1
  for line in "${(@f)$(<$f)}"; do
    case $line in
      m_EditorVersion:\ *)                 UVER=${${line#*: }//[[:space:]]/} ;;
      m_EditorVersionWithRevision:*\(*\)*) UCS=${${line##*\(}%%\)*} ;;
    esac
  done
  [[ $UVER == <->.<->.<->[abfp]<-> ]]
}

# Unity <version> on this Mac: UNITY in the settings, else where Unity Hub puts it (also a folder of your
# choice in the Hub) → REPLY = the Unity program
_rs_unity() {   # <version>
  local p more="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
  local -a apps=()
  [[ -n $cfg[UNITY] ]] && apps+=(${cfg[UNITY]/#\~/$HOME})
  apps+=(/Applications/Unity/Hub/Editor/$1/Unity.app)
  if [[ -r $more ]]; then
    p=$(<$more); p=${${p//[\"\\]/}//[[:space:]]##(#e)/}     # a JSON string: "/Volumes/Disk/Unity"
    [[ $p == /* ]] && apps+=($p/$1/Unity.app)
  fi
  for p in $apps; do
    p=${p%/Contents/MacOS/Unity}
    [[ -x $p/Contents/MacOS/Unity ]] && { REPLY=$p/Contents/MacOS/Unity; return 0 }
  done
  REPLY=
  return 1
}

_rs_hub() {   # Unity Hub's program → REPLY
  local p
  for p in "/Applications/Unity Hub.app" "$HOME/Applications/Unity Hub.app"; do
    [[ -x "$p/Contents/MacOS/Unity Hub" ]] && { REPLY="$p/Contents/MacOS/Unity Hub"; return 0 }
  done
  REPLY=
  return 1
}

# Is there a Unity license on this Mac? Unity Hub writes one when you sign in and add the free personal license.
_rs_license() {
  local -a f=("/Library/Application Support/Unity/Unity_lic.ulf"(N)
              "/Library/Application Support/Unity/licenses/"*(N) $HOME/Library/Unity/licenses/*(N))
  (( ${#f} ))
}

# Unity UVER with Unity Hub, the Hub from Homebrew when it is missing – asks first → REPLY = the Unity program
_rs_get_unity() {
  ui_info "The game is built with Unity $UVER – it is not on this Mac yet."
  ui_dim "  Unity Hub installs it: free with a Unity account (Personal), several GB."
  if ! _rs_hub; then
    if (( $+commands[brew] )) && _rs_ask "Install Unity Hub with Homebrew (brew install --cask unity-hub)?"; then
      ui_step "Installing Unity Hub …"
      if ! brew install --cask unity-hub >| $D/hub.log 2>&1 || ! _rs_hub; then
        ui_error "Homebrew could not install Unity Hub" "" "Its log: $D/hub.log" "Or get it from unity.com/download – then: lotus surf build"
        return 1
      fi
      ui_success "Unity Hub is installed"
    else
      ui_error "Unity Hub is needed for Unity $UVER" "" "brew install --cask unity-hub, or unity.com/download – then: lotus surf build"
      return 1
    fi
  fi
  local hub=$REPLY line
  _rs_ask "Install Unity $UVER with Unity Hub now?" || return 1
  local -a args=(-- --headless install --version $UVER)
  [[ -n $UCS ]] && args+=(--changeset $UCS)
  [[ $UVER == 20(1[0-9]|20|21.1)* ]] && _rs_apple_silicon && args+=(--architecture x86_64)   # no Apple silicon Unity before 2021.2
  ui_step "Unity Hub installs Unity $UVER – this takes a while …"
  : >| $D/hub.log
  "$hub" $args 2>&1 </dev/null | while IFS= read -r line; do
    print -r -- $line >> $D/hub.log
    [[ -t 1 && -n ${line//[[:space:]]/} ]] && print -rn -- $'\r\e[K'"    ${${line//[[:cntrl:]]/}[1,COLUMNS-8]}"
  done
  [[ -t 1 ]] && print -rn -- $'\r\e[K'
  if ! _rs_unity $UVER; then
    # older Hubs know no --architecture, some want you signed in first: then the Hub's own window does it
    open "unityhub://$UVER/$UCS" 2>/dev/null
    ui_error "Unity $UVER is not installed yet" "Unity Hub opened with it – install it there, then: lotus surf build" "What the Hub said: $D/hub.log"
    return 1
  fi
  ui_success "Unity $UVER is installed"
}

# Moves the build screen to a later step (never back – Unity compiles again while it builds the player)
typeset -gi RS_AT=1
typeset -ga RS_KEYS=(open import compile player macos)
_rs_step() {
  local -i i=${RS_KEYS[(i)$1]}
  (( i > RS_AT )) || return 0
  RS_AT=i
  bg_ui_stage $1; bg_ui_progress 0 0; bg_ui_note ""
}

# Unity builds the Mac app, while the lotus blooms. Its log tells the steps: the assets it imports (real bytes of
# the project's files), the scripts it compiles, the player it builds. Status 1 = failed, 2 = stopped
_rs_unity_build() {   # <unity program> <commit>
  local unity=$1 commit=$2 out=$D/.build line f
  setopt localoptions localtraps
  source $LOTUS_ROOT/lib/bg/progress.zsh
  lotus_lang_group bg
  typeset -gi RS_STOP=0 RS_PID=0
  typeset -ga RS_ERRS=()
  typeset -g RS_WHY=
  trap 'RS_STOP=1; (( RS_PID )) && kill -TERM $RS_PID 2>/dev/null' INT
  _bg_cancel() { RS_STOP=1 BG_CANCEL=0; (( RS_PID )) && kill -TERM $RS_PID 2>/dev/null }
  BG_CANCEL=0
  # the project's files and their sizes: what the import has done, in bytes
  local -A size=()
  local -a st files=($SRC/Assets/**/*~*.meta(.ND))
  local -i total=0 got=0 imported=0 rc=1 fd more i
  for f in $files; do
    zstat -A st +size -- $f 2>/dev/null || continue
    size[${f#$SRC/}]=$st[1]; (( total += st[1] ))
  done
  rm -rf -- $out; mkdir -p $out
  : >| $LOG
  RS_AT=1
  bg_ui_begin Resurferd "open|Opening the project in Unity|Unity" "import|Importing the assets|Assets" \
    "compile|Compiling the scripts|Scripts" "player|Building the Mac app|Mac app" "macos|Making it run on macOS|macOS"
  bg_ui_sub "github.com/$REPO · $commit"
  bg_ui_info unity "Unity $UVER"
  bg_ui_info assets "${#files} assets"
  bg_ui_stage open
  bg_ui_note "Unity starts without a window …"
  _BG_BUF=
  exec {fd}< <(
    "$unity" -batchmode -quit -projectPath $SRC -buildTarget OSXUniversal \
      -buildOSXUniversalPlayer "$out/$NAME.app" -logFile - 2>&1 </dev/null &
    print -r -- "LOTUS-PID $!"
    wait $!
    print -r -- "LOTUS-EXIT $?"
  )
  while :; do
    bg_read_lines $fd 0.1; more=$?
    for line in $reply; do
      case $line in
        "LOTUS-PID "<->)  RS_PID=${line#* }; continue ;;
        "LOTUS-EXIT "<->) rc=${line#* }; continue ;;
      esac
      print -r -- $line >> $LOG
      case $line in
        *"Start importing "*" using Guid"*)
          f=${${line#*Start importing }%% using Guid*}
          _rs_step import
          (( imported++, got += ${size[$f]:-0}, got > total ? (got = total) : 0 ))
          (( total > 0 )) && bg_ui_progress $got $total
          bg_ui_note "$imported assets · ${f:t}" ;;
        (#i)*starting\ compile*|(#i)*compiling\ scripts*)
          _rs_step compile
          bg_ui_note "${${${line##*/}%%[[:space:]]#}[1,60]}" ;;
        *": error CS"<->*)
          [[ $line == *Assets/* ]] && line=Assets/${line#*Assets/}
          (( ${#RS_ERRS} < 4 )) && RS_ERRS+=("${line[1,150]}") ;;
        *"DisplayProgressbar: "*)
          [[ $line == (#i)*(build|player|shader|scene|resources)* ]] && _rs_step player
          bg_ui_note "${${line#*DisplayProgressbar: }[1,60]}" ;;
        *"Compiling shader \""*)
          _rs_step player
          bg_ui_note "Shader ${${line#*Compiling shader \"}%%\"*}" ;;
        (#i)*(no\ valid\ unity*licen|not\ been\ activated|editor.headless|failed\ to\ activate|licen[cs]e\ is\ not\ active|invalid\ licen)*)
          RS_WHY=license ;;
        (#i)*another\ unity\ instance\ is\ running*)
          RS_WHY=open ;;
      esac
    done
    bg_ui_frame
    (( more == 0 && ! RS_STOP )) || break
  done
  exec {fd}<&-
  if (( RS_STOP )); then
    for (( i = 0; i < 100 && RS_PID; i++ )); do kill -0 $RS_PID 2>/dev/null || break; sleep 0.1; done
    bg_ui_end; rm -rf -- $out
    ui_info "Stopped – the next build goes on from what Unity has imported so far."
    return 2
  fi
  local new="$out/$NAME.app"
  if (( rc == 0 )) && _rs_exe $new; then
    _rs_step macos; bg_ui_frame
    _rs_fix $new && _rs_place $new || rc=1
  else
    rc=1
  fi
  rm -rf -- $out
  if (( rc == 0 )); then
    _rs_save_info source $commit
    lotus_log INFO resurferd "Built $commit with Unity $UVER"
    bg_ui_complete; bg_ui_end
    ui_success "Ready: $NAME, built from $commit"
    return 0
  fi
  bg_ui_end
  _rs_build_failed
  return 1
}

# Why Unity did not build it, in words – from its log
_rs_build_failed() {
  local -i w=$(( ${COLUMNS:-80} > 40 ? ${COLUMNS:-80} - 6 : 74 ))
  case $RS_WHY in
    license)
      ui_error "Unity has no license on this Mac" "Unity needs one even to build – the free Personal license is enough." \
        "Open Unity Hub, sign in, then Preferences → Licenses → Add → Get a free personal license" "Then: lotus surf build" ;;
    open)
      ui_error "Another Unity has the project open" "Close it, then: lotus surf build" ;;
    *)
      local -a said=() l
      if (( ${#RS_ERRS} )); then
        for l in $RS_ERRS; do said+=(${l[1,w]}); done
        ui_error "The game's scripts did not compile with Unity $UVER" "Unity says:" $said "All of it: lotus surf log"
      else
        for l in ${(f)"$(grep -iE 'error|failed|exception|abort' $LOG 2>/dev/null | tail -n 4)"}; do said+=(${l[1,w]}); done
        ui_error "Unity could not build the Mac app" "Its last words:" ${said:-"(nothing)"} "All of it: lotus surf log"
      fi ;;
  esac
  lotus_log ERROR resurferd "Unity build failed${RS_WHY:+ ($RS_WHY)}"
}

# ── The commands ──────────────────────────────────────────────

rs_build() {
  _rs_source || return 1
  local commit=$REPLY
  _rs_project_version || { ui_error "The source is no Unity project" "ProjectSettings/ProjectVersion.txt is missing" "Is REPO in $D/settings right?"; return 1 }
  _rs_unity $UVER || _rs_get_unity || return 1
  local unity=$REPLY
  _rs_arch $unity
  if [[ $REPLY == intel ]] && _rs_apple_silicon && ! _rs_rosetta; then
    ui_info "Unity $UVER is made for Intel Macs – this Mac runs it with Rosetta 2, Apple's translator."
    _rs_ask "Install Rosetta 2 now?" || return 1
    if ! softwareupdate --install-rosetta --agree-to-license >| $D/rosetta.log 2>&1 || ! _rs_rosetta; then
      ui_error "Rosetta 2 could not be installed" "" "sudo softwareupdate --install-rosetta --agree-to-license"
      return 1
    fi
    ui_success "Rosetta 2 is installed"
  fi
  _rs_license || ui_warn "No Unity license on this Mac yet – sign in to Unity Hub and add the free personal license (Preferences → Licenses), or Unity cannot build."
  ui_dim "  The first build takes a while: Unity imports every asset once. Later builds are much quicker."
  _rs_unity_build $unity $commit
}

# A ready Mac build from the releases, or built from the source – as GET says
_rs_get_any() {
  local -i r=1
  if [[ $GET != build ]]; then
    ui_step "Looking for a Mac build in the releases of github.com/$REPO …"
    _rs_release; r=$?
    (( r == 0 )) && { _rs_download; return }
    if [[ $GET == release ]]; then
      if (( r == 2 )); then ui_error "GitHub could not be asked for the releases" "" "Are you online? GitHub also limits how often – try again later"
      else ui_error "No release has a Mac build" "github.com/$REPO/releases" "lotus surf build builds one from the source"; fi
      return 1
    fi
    if (( r == 2 )); then ui_info "GitHub could not be asked for the releases – Lotus builds the game from the source."
    else ui_info "No release has a Mac build – Lotus builds one from the source with Unity."; fi
  fi
  rs_build
}

# The first time: what happens, asked once
rs_get() {
  ui_header "$NAME" "github.com/$REPO"
  ui_text "The game is not on this Mac yet. Lotus gets it for you:"
  case $GET in
    release) ui_dim "  the Mac build of its newest GitHub release" ;;
    build)   ui_dim "  built from its source with Unity" ;;
    *)       ui_dim "  the Mac build of its newest GitHub release – or, when there is none, built from its source with Unity" ;;
  esac
  ui_dim "  Then it makes the app run on macOS, and starts it."
  ui_blank
  _rs_ask "Get it now?" || return 1
  _rs_get_any
}

_rs_launch() {   # [arguments for the game]
  _rs_exe $APP || return 1
  local exe=$REPLY
  [[ -x $exe ]] && _rs_mod_ok $APP || _rs_fix $APP     # moved or unpacked by hand, or the plugin brought a newer mod
  _rs_bin $APP
  _rs_arch $REPLY
  [[ $REPLY == intel ]] && _rs_apple_silicon && ! _rs_rosetta \
    && ui_info "It is an Intel app: macOS asks to install Rosetta 2 the first time – say yes."
  if (( $# )); then open $APP --args "$@"; else open $APP; fi || return 1
  lotus_log INFO resurferd "Started"
}

rs_play() {   # [--fullscreen | --window]
  local -a args=()
  case $1 in
    -f|--fullscreen) args=(-screen-fullscreen 1) ;;
    -w|--window)     args=(-screen-fullscreen 0) ;;
    '')              ;;
    *)               ui_error "Unknown: lotus surf play $1" "" "lotus surf play --fullscreen, or --window"; return 2 ;;
  esac
  if ! _rs_exe $APP; then
    rs_get || return 1
    _rs_exe $APP || return 1
  fi
  _rs_launch $args || { ui_error "macOS could not open the game" "$APP" "lotus surf status says more"; return 1 }
  ui_success "$NAME is starting"
  ui_kv Lanes "← → or A D"
  ui_kv Jump "↑ or W"
  ui_kv Roll "↓ or S"
  ui_kv Hoverboard "Space or a click, twice"
}

rs_update() {
  _rs_kv $INFO; game=("${(@kv)KV}")
  _rs_exe $APP || { rs_get; return }
  local -i r
  case $game[FROM] in
    release)
      ui_step "Looking for a newer Mac build …"
      _rs_release; r=$?
      if (( r == 2 )); then ui_error "GitHub could not be asked for the releases" "" "Are you online? GitHub also limits how often – try again later"; return 1; fi
      if (( r == 1 )); then ui_info "No release has a Mac build any more – yours ($game[VERSION]) stays. lotus surf build builds the newest source."; return 0; fi
      [[ $REL[1] == $game[VERSION] ]] && { ui_success "Up to date – $REL[1] is the newest release with a Mac build"; return 0 }
      ui_info "$REL[1] is out – you have $game[VERSION]"
      _rs_download ;;
    source)
      local head=$(git ls-remote $GIT HEAD 2>/dev/null)
      head=${head[1,7]}
      [[ -n $head ]] || { ui_error "git could not ask github.com/$REPO" "" "Are you online?"; return 1 }
      [[ $head == ${game[VERSION][1,7]} ]] && { ui_success "Up to date – $head is the newest commit"; return 0 }
      ui_info "The source changed: $game[VERSION] → $head"
      rs_build ;;
    *)
      ui_info "This one came from your file${game[VERSION]:+ ($game[VERSION])} – Lotus gets the newest instead."
      _rs_get_any ;;
  esac
}

rs_install_file() {   # <zip|app>
  local p=${1/#\~/$HOME} work=$D/.unpack
  [[ -n $p ]] || { ui_error "Which Mac build?" "" "lotus surf install ~/Downloads/Subway-Surfers-Resurferd-StandaloneOSX.zip"; return 2 }
  [[ $p == /* ]] || p=${LOTUS_PWD:-$PWD}/$p
  p=${p%/}
  [[ -e $p ]] || { ui_error "There is no $1"; return 1 }
  case $p in
    (#i)*.zip) _rs_unpack $p $work ;;
    (#i)*.app) rm -rf -- $work; mkdir -p $work && { ditto $p $work/${p:t} 2>/dev/null || cp -R $p $work/ } && _rs_find_app $work ;;
    *)         ui_error "A Mac build is a .zip or an .app" "${p:t}"; return 2 ;;
  esac
  local -i bad=$?
  local new=$REPLY
  if (( bad )) || ! _rs_fix $new || ! _rs_place $new; then
    rm -rf -- $work
    ui_error "There is no Mac app in it" "${p:t}" "A Mac build of a Unity game has a .app (StandaloneOSX)"
    return 1
  fi
  rm -rf -- $work
  _rs_save_info file ${p:t}
  lotus_log INFO resurferd "Installed from ${p:t}"
  ui_success "Ready: $NAME, from ${p:t} – lotus surf starts it"
}

rs_status() {
  _rs_kv $INFO; game=("${(@kv)KV}")
  ui_header "$NAME" "github.com/$REPO"
  if _rs_bin $APP; then
    local kind from
    _rs_arch $REPLY
    case $REPLY in
      universal) kind="Universal – Apple silicon and Intel" ;;
      apple)     kind="Apple silicon" ;;
      intel)     kind=Intel
                 _rs_apple_silicon && { _rs_rosetta && kind+=" – runs with Rosetta 2" || kind+=" – needs Rosetta 2 (macOS offers it)" } ;;
      *)         kind= ;;
    esac
    case $game[FROM] in
      release) from="the release $game[VERSION]" ;;
      source)  from="built from the source, $game[VERSION]" ;;
      file)    from="your file $game[VERSION]" ;;
      *)       from="?" ;;
    esac
    ui_kv Game "ready – lotus surf starts it"
    ui_kv From "$from${game[DATE]:+ · $game[DATE]}"
    ui_path $APP; ui_kv Where "$REPLY"
    _rs_size $APP; ui_kv Size "$REPLY"
    [[ -n $kind ]] && ui_kv Kind "$kind"
    if [[ ! -d $APP/Contents/Resources/Data/Managed ]]; then ui_kv Mod "this build has no Mono folder – no Resurf mod"
    elif _rs_mod_want; then _rs_mod_ok $APP && ui_kv Mod "Resurf – coins, the save, the FPS counter, the glow" || ui_kv Mod "not in it yet – lotus surf puts it in"
    elif [[ $cfg[MOD] == off ]]; then ui_kv Mod "off – the game as it is (lotus surf mod on)"
    else ui_kv Mod "missing from the plugin – lotus plugin update resurferd"; fi
  else
    ui_kv Game "not on this Mac yet – lotus surf gets it"
  fi
  if [[ -d $SRC/.git ]]; then
    _rs_size $SRC
    ui_kv Source "$(git -C $SRC rev-parse --short HEAD 2>/dev/null) · $REPLY with Unity's Library"
    if _rs_project_version; then
      if _rs_unity $UVER; then ui_path ${REPLY%/Contents/MacOS/Unity}; ui_kv Unity "$UVER · $REPLY"
      else ui_kv Unity "$UVER – not on this Mac (lotus surf build installs it)"; fi
      _rs_license && ui_kv License "there is one" || ui_kv License "none yet – sign in to Unity Hub, add the free personal license"
    fi
  fi
  ui_blank
}

rs_log() {
  [[ -s $LOG ]] || { ui_info "No build yet – lotus surf build"; return 0 }
  ui_path $LOG; ui_dim "$REPLY"
  tail -n 40 $LOG
}

rs_remove() {
  local -a what=()
  [[ -d $APP ]] && what+=($APP)
  [[ -d $SRC ]] && what+=($SRC)
  (( ${#what} )) || { ui_info "There is nothing to remove – the game is not on this Mac."; return 0 }
  ui_header "$NAME" "remove"
  local p
  if [[ -d $APP ]]; then ui_path $APP; p=$REPLY; _rs_size $APP; ui_kv Game "$REPLY · $p"; fi
  if [[ -d $SRC ]]; then ui_path $SRC; p=$REPLY; _rs_size $SRC; ui_kv Source "$REPLY · $p, with Unity's Library"; fi
  ui_dim "  Your progress in the game stays – macOS keeps it apart from the app. Unity and Unity Hub stay too."
  ui_blank
  ui_confirm "Remove them?" n || return 1
  rm -rf -- $what $INFO $LOG $D/.build $D/.unpack $D/download.zip.part
  lotus_log INFO resurferd "Removed"
  ui_success "Removed – lotus surf gets the game again"
}

# ── Coins, the FPS counter, the glow, the save: through the Resurf mod ──

# The mod has to be in the game for these – put in now when it is missing (a running game loads it when it starts)
# → status 1 when it cannot be; RS_FRESH=1 when it was put in just now
_rs_mod_ready() {
  typeset -gi RS_FRESH=0
  _rs_exe $APP || { ui_error "The game is not on this Mac yet" "" "lotus surf gets it"; return 1 }
  if ! _rs_mod_want; then
    if [[ $cfg[MOD] == off ]]; then ui_error "The Resurf mod is off" "" "lotus surf mod on"
    else ui_error "The Resurf mod is missing from the plugin" "mod/ResurfMod.dll and mod/doorstop/libdoorstop.dylib" "lotus plugin update resurferd"; fi
    return 1
  fi
  _rs_mod_ok $APP && return 0
  _rs_fix $APP && _rs_mod_ok $APP || { ui_error "The Resurf mod could not be put into the game" "$APP" "This build has no Mono folder – lotus surf status"; return 1 }
  RS_FRESH=1
}

# lotus surf coins [<n> | set <n>]: coins for the game – a running game takes them within a second, else the game
# takes them when it starts. Without a number: how many there are.
rs_coins() {
  _rs_gamedata
  local dir=$REPLY/resurf how=+ n=$1 id
  [[ $1 == set ]] && { how='=' n=$2 }
  if [[ -z $n ]]; then
    _rs_kv $dir/save.txt
    if [[ $KV[COINS] == <-> ]]; then
      _rs_num $KV[COINS]; ui_success "$REPLY coins${KV[SAVED]:+ – saved $KV[SAVED]}"
      ui_dim "  More: lotus surf coins 5000 · exactly so many: lotus surf coins set 100000"
    else
      ui_info "Lotus knows your coins once the game has run with the Resurf mod – lotus surf"
    fi
    return 0
  fi
  n=${${n#+}//[,_.\']/}
  if [[ $n != <-> ]] || (( ${#n} > 10 )) || (( n > 2147483647 )); then
    ui_error "How many coins?" "$n" "lotus surf coins 5000 · lotus surf coins set 100000 – at most 2,147,483,647"
    return 2
  fi
  _rs_mod_ready || return 1
  id=$(strftime %Y%m%d-%H%M%S $EPOCHSECONDS)-$RANDOM
  mkdir -p -- $dir/inbox && print -r -- "coins $how$(( n ))" >| $dir/inbox/.$id.tmp && mv -f -- $dir/inbox/.$id.tmp $dir/inbox/$id.req \
    || { ui_error "The coins could not be handed to the game" "$dir/inbox"; return 1 }
  lotus_log INFO resurferd "coins $how$n"
  _rs_num $n; local amount=$REPLY
  if _rs_running && (( ! RS_FRESH )); then
    local -i i
    for (( i = 0; i < 40; i++ )); do
      _rs_kv $dir/save.txt
      if [[ $KV[LAST] == $id ]]; then
        _rs_num $KV[COINS]
        if [[ $how == + ]]; then ui_success "+$amount coins – you have $REPLY now"
        else ui_success "You have $REPLY coins now"; fi
        return 0
      fi
      sleep 0.1
    done
    ui_info "The game has not taken them yet – it does as soon as it runs with the Resurf mod (start it again if it was open before)."
    return 0
  fi
  if [[ $how == + ]]; then ui_success "+$amount coins are waiting – the game takes them when it starts: lotus surf"
  else ui_success "$amount coins are waiting – the game takes them when it starts: lotus surf"; fi
  (( RS_FRESH )) && _rs_running && ui_dim "  The open game has no mod yet – quit it (⌘Q) and start it again."
  return 0
}

# A setting of the mod, in <the game's folder>/resurf/settings – the game reads it within a second
_rs_mod_setting() {   # <KEY> <value>
  _rs_gamedata
  _rs_set_line "$REPLY/resurf/settings" $1 $2
}

# lotus surf fps [on|off]: the FPS counter, bottom left in the game – without a word it switches
rs_fps() {
  _rs_gamedata
  _rs_kv "$REPLY/resurf/settings"
  local want=$1
  [[ -n $want ]] || { [[ $KV[FPS] == on ]] && want=off || want=on }
  [[ $want == (on|off) ]] || { ui_error "Unknown: lotus surf fps $1" "" "lotus surf fps on, or off"; return 2 }
  _rs_mod_ready || return 1
  _rs_mod_setting FPS $want
  if [[ $want == on ]]; then ui_success "The FPS counter is on – bottom left in the game; F switches it there too"
  else ui_success "The FPS counter is off – F in the game, or lotus surf fps on"; fi
}

# lotus surf glow [fix|original]: the coins' glow drawn as a glow (fix), or as the project draws it, black edge and all
rs_glow() {
  _rs_gamedata
  _rs_kv "$REPLY/resurf/settings"
  [[ -n $1 ]] || { ui_kv Glow "${KV[GLOW]:-fix} – lotus surf glow fix, or original"; return 0 }
  [[ $1 == (fix|original) ]] || { ui_error "Unknown: lotus surf glow $1" "" "lotus surf glow fix, or original"; return 2 }
  _rs_mod_ready || return 1
  _rs_mod_setting GLOW $1
  [[ $1 == fix ]] && ui_success "The coins glow without the black edge – from the next start of the game" \
                  || ui_success "The glow is drawn as the project draws it – from the next start of the game"
}

# lotus surf stats: what the game saved – the mod writes it readable next to the game's own save
rs_stats() {
  _rs_gamedata
  local gd=$REPLY k label
  _rs_kv $gd/resurf/save.txt
  ui_header "$NAME" "your game"
  if [[ -z $KV[COINS] ]]; then
    ui_info "No save from the Resurf mod yet – play once: lotus surf"
    ui_blank
    return 0
  fi
  for k label in COINS Coins HIGHSCORE "High score" BEST_METERS "Best distance" MYSTERY_BOXES "Mystery boxes" \
      SUPER_MYSTERY_BOXES "Super boxes" MISSIONS Missions MULTIPLIER Multiplier DAILY_WORDS_IN_A_ROW "Words in a row"; do
    [[ $KV[$k] == <-> ]] || continue
    _rs_num $KV[$k]
    case $k in
      BEST_METERS) REPLY+=" m" ;;
      MULTIPLIER)  REPLY="×$REPLY" ;;
    esac
    ui_kv $label $REPLY
  done
  ui_kv Saved "${KV[SAVED]:-?}${KV[SAVED]:+ – every 20 seconds, when the game loses focus and when it quits}"
  ui_path $gd/playerdata; ui_kv "Game's save" $REPLY
  ui_path $gd/resurf/save.txt; ui_kv Readable $REPLY
  ui_blank
}

# lotus surf mod [on|off]: the Resurf mod in the game, or the game as it is
rs_mod() {
  case $1 in
    on)  [[ $cfg[MOD] == off ]] && { _rs_set_line $D/settings MOD; cfg[MOD]= } ;;
    off) _rs_set_line $D/settings MOD off; cfg[MOD]=off ;;
    '')  ;;
    *)   ui_error "Unknown: lotus surf mod $1" "" "lotus surf mod on, or off"; return 2 ;;
  esac
  if [[ -n $1 ]] && _rs_exe $APP; then
    _rs_fix $APP
    _rs_running && ui_dim "  The open game keeps what it had – quit it (⌘Q) and start it again."
  fi
  if _rs_mod_want; then
    ui_kv Mod "on – coins from Lotus, a save that holds, an FPS counter, the coins' glow"
    if _rs_exe $APP; then
      _rs_mod_ok $APP && ui_kv Game "has it" || ui_kv Game "has it not yet – lotus surf puts it in when it starts the game"
    fi
  elif [[ $cfg[MOD] == off ]]; then
    ui_kv Mod "off – the game as it is (lotus surf mod on)"
  else
    ui_kv Mod "missing from the plugin – lotus plugin update resurferd"
  fi
}

# For the AI (tool play): plain words, status 3 when the user has to get the game first
rs_tool_play() {
  if ! _rs_exe $APP; then
    print -r -- "$NAME is not on this Mac yet. The user gets it with: lotus surf – it downloads or builds the game and asks first."
    return 3
  fi
  _rs_launch >/dev/null || { print -r -- "macOS could not open the game ($APP)."; return 1 }
  print -r -- "Started $NAME. Controls: arrow keys or WASD, Space or a click twice for the hoverboard."
}

rs_help() {
  ui_header "$NAME" "on this Mac"
  print -r -- "  lotus surf                      play – the first time it gets the game (it asks first)
  lotus surf play --fullscreen    or --window
  lotus surf get                  the game now, without the question – for scripts too
  lotus surf update               a newer release or a newer source: the game again
  lotus surf build                build it from the source with Unity, even when a release has a Mac build
  lotus surf install <zip|app>    a Mac build you downloaded yourself
  lotus surf status               where it is, where it came from, what it needs
  lotus surf log                  what Unity said in the last build
  lotus surf remove               the game and its source go – your progress stays

  lotus surf coins 5000           coins for the game – lotus surf coins shows them, coins set 100000 sets them
  lotus surf stats                coins, high score, best distance … as the game saved them
  lotus surf fps                  the FPS counter on or off (F in the game too)
  lotus surf glow fix|original    the coins' glow without the black edge (fix), or as the project draws it
  lotus surf mod on|off           the Resurf mod in the game, or the game as it is"
  ui_blank
  ui_path $D/settings
  ui_dim "  Settings in $REPLY: REPO=owner/name (a fork), GET=auto|release|build, UNITY=<Unity.app>, MOD=off|<dll>"
  ui_blank
}

mkdir -p $D
case ${1:-play} in
  play|start)                  rs_play "${@:2}" ;;
  -f|--fullscreen|-w|--window) rs_play "$@" ;;
  get|download)                _rs_get_any ;;
  update|upgrade)              rs_update ;;
  build|rebuild)               rs_build ;;
  install|add)                 rs_install_file "$2" ;;
  status|info)                 rs_status ;;
  log|logs)                    rs_log ;;
  remove|uninstall|rm)         rs_remove ;;
  coins|coin)                  rs_coins "${@:2}" ;;
  fps)                         rs_fps "$2" ;;
  glow)                        rs_glow "$2" ;;
  stats|save|score)            rs_stats ;;
  mod)                         rs_mod "$2" ;;
  tool-play)                   rs_tool_play ;;
  help|-h|--help)              rs_help ;;
  *)                           ui_error "Unknown: lotus surf $1" "" "lotus surf help shows what it can do"; exit 2 ;;
esac
