#!/bin/bash
#
# Does the download server start, and does it serve what it is there for?
# Run by ci.yml and nightly.yml on the Debian leg, after a Release build.
#
# The server started with a configuration file of its own, on ports nobody
# else has, and asked:
#
#  * "/" must come back as the embedded web interface - that it is inside the
#    assembly rather than merely to have been built;
#  * the JSON API must answer 401: it is nobody's business until somebody has
#    signed in, and a 200 would mean the door had come off;
#  * the SSH server - the command line, and SFTP - must say its version;
#  * an HTTP download without credentials must be refused with 401;
#  * signed in as the account of the first start, a file is published over the
#    JSON API and a downloader is made; the downloader's GET must come back
#    200 with the file as it was published, a GET from a byte on 206 with the
#    rest of it, and both downloads must be recorded.
#
# Everything it writes goes to a temporary directory: a first start makes an
# account store, a certificate store, a log and the areas, and none of that
# belongs in a workspace whose cleanliness the build stamps into every assembly.

set -u

tmp="$(mktemp -d)"

cat > "$tmp/configuration.json" <<'JSON'
{ "downloadServer": { "httpListen": "127.0.0.1" } }
JSON

dotnet DownloadServerCLI/bin/Release/net11.0/DownloadServerCLI.dll \
    --port           18365 \
    --ssh-port       22465 \
    --download-port  18865 \
    --accounts       "$tmp/accounts" \
    --certificates   "$tmp/certificates" \
    --config         "$tmp/configuration.json" \
    --log-file       "$tmp/logs" \
    < /dev/null > "$tmp/console.log" 2>&1 &
pid=$!

web=http://127.0.0.1:18365
get=http://127.0.0.1:18865

# curl -w writes "000" itself when it could not connect; "|| true" keeps its
# exit code from ending the script, and ${:-000} is for no curl at all.
root=000
for _ in $(seq 1 60); do
    root="$(curl -sS -o "$tmp/index.html" -w '%{http_code}' $web/ 2>/dev/null || true)"
    root="${root:-000}"
    [ "$root" != 000 ] && break
    # Gone rather than slow: stop waiting for it.
    kill -0 "$pid" 2>/dev/null || break
    sleep 1
done

api="$(curl -sS -o /dev/null -w '%{http_code}' $web/api/v1/downloadServer 2>/dev/null || true)"
api="${api:-000}"

# The first line the SSH server says: its version, "SSH-2.0-...".
ssh_banner="$(timeout 5 bash -c 'exec 3<>/dev/tcp/127.0.0.1/22465 && head -n 1 <&3' 2>/dev/null | tr -d '\r' || true)"

anonymous="$(curl -sS -o /dev/null -w '%{http_code}' $get/firmware/station-4.2.1.bin 2>/dev/null || true)"
anonymous="${anonymous:-000}"

# The password of the first start, as the banner shows it once.
password="$(sed -n 's/^.*password  *\([^ ]*\)$/\1/p' "$tmp/console.log" | head -n 1)"

login="$(curl -sS -o /dev/null -w '%{http_code}' -c "$tmp/cookies" -H "Origin: $web" \
              --data-urlencode "login=root" --data-urlencode "password=$password" $web/ext/login 2>/dev/null || true)"

# A firmware image of 64 KiB, of bytes nobody could guess.
head -c 65536 /dev/urandom > "$tmp/station-4.2.1.bin"

published="$(curl -sS -o /dev/null -w '%{http_code}' -b "$tmp/cookies" -H "Origin: $web" -X PUT \
                  -H 'Content-Type: application/octet-stream' --data-binary "@$tmp/station-4.2.1.bin" \
                  $web/api/v1/downloadServer/files/firmware/station-4.2.1.bin 2>/dev/null || true)"
published="${published:-000}"

made="$(curl -sS -b "$tmp/cookies" -H "Origin: $web" -H 'Content-Type: application/json' \
             -d '{ "id": "CS001" }' $web/api/v1/downloadServer/downloaders 2>/dev/null || true)"
downloader_password="$(printf '%s' "$made" | sed -n 's/.*"password":"\([^"]*\)".*/\1/p')"

downloaded="$(curl -sS -o "$tmp/downloaded.bin" -w '%{http_code}' -u "CS001:$downloader_password" \
                   $get/firmware/station-4.2.1.bin 2>/dev/null || true)"
downloaded="${downloaded:-000}"

# The rest of it, from byte 40000 on: what a download that stopped there asks.
ranged="$(curl -sS -o "$tmp/rest.bin" -w '%{http_code}' -u "CS001:$downloader_password" -H 'Range: bytes=40000-' \
               $get/firmware/station-4.2.1.bin 2>/dev/null || true)"
ranged="${ranged:-000}"
tail -c +40001 "$tmp/station-4.2.1.bin" > "$tmp/expected-rest.bin"

recorded="$(curl -sS -b "$tmp/cookies" "$web/api/v1/downloadServer/downloads?downloader=CS001" 2>/dev/null || true)"

kill "$pid" 2>/dev/null || true
wait "$pid" 2>/dev/null || true

echo "GET /                             -> $root"
echo "GET /api/v1/downloadServer        -> $api"
echo "SSH 22465                         -> ${ssh_banner:-nothing}"
echo "GET without credentials           -> $anonymous"
echo "Signed in as root                 -> ${login:-000}"
echo "Published over the JSON API       -> $published"
echo "Downloader CS001 made             -> $([ -n "$downloader_password" ] && echo yes || echo no)"
echo "GET as CS001                      -> $downloaded"
echo "GET as CS001 from byte 40000 on   -> $ranged"
echo "Recorded of CS001                 -> $(printf '%s' "$recorded" | grep -o '"total":[0-9]*' || echo nothing)"

# What the banner says this was built from - in every run, not only a red one.
awk '/^  built from/ { f = 1 } f && !/^  built from/ && !/^                 / { f = 0 } f' "$tmp/console.log" || true

fault=0

if [ "$root" != 200 ]; then
    echo "The web interface did not answer."
    fault=1
elif ! grep -q "<title>Download Server</title>" "$tmp/index.html"; then
    echo "Something answered, but it is not the embedded web interface."
    fault=1
fi

[ "$api" = 401 ]        || { echo "The JSON API answered $api where 401 was expected: it is supposed to be shut to anyone not signed in."; fault=1; }

case "$ssh_banner" in
    SSH-2.0-*) ;;
    *) echo "The SSH server did not answer on 22465."; fault=1 ;;
esac

[ "$anonymous" = 401 ]  || { echo "An HTTP download without credentials answered $anonymous where 401 was expected."; fault=1; }
[ "$login" = 201 ]      || { echo "The account of the first start could not sign in."; fault=1; }
[ "$published" = 200 ]  || { echo "Publishing a file over the JSON API answered $published where 200 was expected."; fault=1; }
[ -n "$downloader_password" ] || { echo "No downloader could be made over the JSON API."; fault=1; }
[ "$downloaded" = 200 ] || { echo "The downloader's HTTP download answered $downloaded where 200 was expected."; fault=1; }
[ "$ranged" = 206 ]     || { echo "The downloader's HTTP download from a byte on answered $ranged where 206 was expected."; fault=1; }

cmp -s "$tmp/station-4.2.1.bin" "$tmp/downloaded.bin" || { echo "The file downloaded is not the file published."; fault=1; }
cmp -s "$tmp/expected-rest.bin" "$tmp/rest.bin"       || { echo "What was downloaded from byte 40000 on is not the rest of the file."; fault=1; }

printf '%s' "$recorded" | grep -q '"total":2' || { echo "The two downloads of CS001 are not recorded."; fault=1; }

if [ "$fault" != 0 ]; then
    echo
    echo "What it said, without the first-start password:"
    grep -v -i password "$tmp/console.log" || echo "(nothing at all)"
    exit 1
fi
