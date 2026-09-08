#!/bin/sh
set -eu

[ "$(/usr/bin/id -u)" = 0 ] || { echo 'Use sudo to remove the system package.' >&2; exit 1; }
prefix='/Library/Application Support/Clone'
path_entry='/private/etc/paths.d/io.github.6a6f6a6f.clone'
for path in /Library '/Library/Application Support' "$prefix" "$prefix/bin" "$prefix/share" "$prefix/share/zsh" "$prefix/share/zsh/site-functions" /private /private/etc /private/etc/paths.d; do
  [ ! -L "$path" ] || { echo 'Refusing to uninstall through a symlink.' >&2; exit 1; }
  if [ -e "$path" ]; then
    [ -d "$path" ] && [ "$(/usr/bin/stat -f %u "$path")" = 0 ] || exit 1
    mode=$(/usr/bin/stat -f %Lp "$path")
    [ $((0$mode & 022)) -eq 0 ] || exit 1
  fi
done
/usr/sbin/pkgutil --pkg-info io.github.6a6f6a6f.clone >/dev/null 2>&1 || { echo 'Package receipt is missing; inspect the installation manually.' >&2; exit 1; }
# Check the complete fixed payload before removing any file. Preserve modified data.
for relative in bin/clone share/zsh/site-functions/_clone LICENSE uninstall.sh installed.sha256; do
  [ -f "$prefix/$relative" ] && [ ! -L "$prefix/$relative" ] || { echo 'Unexpected package contents; nothing removed.' >&2; exit 1; }
  [ "$(/usr/bin/stat -f %u "$prefix/$relative")" = 0 ] || exit 1
  mode=$(/usr/bin/stat -f %Lp "$prefix/$relative")
  [ $((0$mode & 022)) -eq 0 ] || exit 1
done
[ -f "$path_entry" ] && [ ! -L "$path_entry" ] || exit 1
[ "$(cat "$path_entry")" = "$prefix/bin" ] || { echo 'The PATH entry was modified; nothing removed.' >&2; exit 1; }
(cd "$prefix" && /usr/bin/shasum -a 256 -c installed.sha256) || { echo 'Installed files changed; inspect them before removal.' >&2; exit 1; }
for relative in bin/clone share/zsh/site-functions/_clone LICENSE uninstall.sh installed.sha256; do
  /bin/rm "$prefix/$relative"
done
/bin/rm "$path_entry"
for path in "$prefix/share/zsh/site-functions" "$prefix/share/zsh" "$prefix/share" "$prefix/bin" "$prefix"; do
  /bin/rmdir "$path" 2>/dev/null || true
done
/usr/sbin/pkgutil --forget io.github.6a6f6a6f.clone >/dev/null
printf '%s\n' 'Clone package removed. User configuration and cloned repositories were preserved.'
