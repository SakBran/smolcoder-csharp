#!/usr/bin/env bash
# Publishes a release build and installs the "smallcode" command.
# Re-run after pulling or changing the code.
set -euo pipefail

repo="$(cd "$(dirname "$0")" && pwd)"
app_dir="${SMALLCODE_APP_DIR:-$HOME/.smolcoder/app}"
bin_dir="${SMALLCODE_BIN_DIR:-/usr/local/bin}"

dotnet publish "$repo/src/Smolcoder.Cli" -c Release -o "$app_dir" --nologo

cat > "$bin_dir/smallcode" <<EOF
#!/usr/bin/env bash
exec "$app_dir/smol" "\$@"
EOF
chmod +x "$bin_dir/smallcode"

echo "Installed: $bin_dir/smallcode -> $app_dir/smol"
