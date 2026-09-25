#!/bin/bash
# CI only. Grants Fluent (and the screenshot tool) the privacy permissions a person would click
# through in System Settings, by writing TCC database rows. This works only where System
# Integrity Protection lets root edit the databases; the output says what happened either way.
set -uo pipefail
APP="${1:-/Applications/Fluent.app}"
BIN="$APP/Contents/MacOS/Fluent"
SYS_DB="/Library/Application Support/com.apple.TCC/TCC.db"
USER_DB="$HOME/Library/Application Support/com.apple.TCC/TCC.db"

echo "SIP: $(csrutil status 2>&1)"
codesign -d -r- "$APP" 2>&1 | sed -n 's/^designated => //p' > /tmp/fluent-req.txt
echo "Designated requirement: $(cat /tmp/fluent-req.txt)"
if csreq -r /tmp/fluent-req.txt -b /tmp/fluent-req.bin 2>/dev/null; then
    REQ="X'$(xxd -p /tmp/fluent-req.bin | tr -d '\n')'"
else
    REQ="NULL"
fi

grant() {  # db service client client_type csreq
    local db="$1" service="$2" client="$3" type="$4" req="$5"
    if sudo sqlite3 "$db" "INSERT OR REPLACE INTO access (service, client, client_type, auth_value, auth_reason, auth_version, csreq, flags) VALUES ('$service', '$client', $type, 2, 4, 1, $req, 0);" 2>/tmp/tcc-err.txt; then
        echo "granted $service to $client ($(basename "$db"))"
    else
        echo "COULD NOT grant $service to $client: $(cat /tmp/tcc-err.txt)"
    fi
}

for service in kTCCServiceAccessibility kTCCServicePostEvent kTCCServiceListenEvent kTCCServiceScreenCapture; do
    grant "$SYS_DB" "$service" "com.hammaad.fluent.mac" 0 "$REQ"
    grant "$SYS_DB" "$service" "$BIN" 1 "$REQ"
done
for client in /usr/sbin/screencapture /bin/bash /bin/zsh; do
    grant "$SYS_DB" kTCCServiceScreenCapture "$client" 1 NULL
done
grant "$USER_DB" kTCCServiceMicrophone "com.hammaad.fluent.mac" 0 "$REQ"

sudo killall -9 tccd 2>/dev/null || true
killall -9 tccd 2>/dev/null || true
sleep 2
echo "Rows for Fluent now:"
sudo sqlite3 "$SYS_DB" "SELECT service, client, auth_value FROM access WHERE client LIKE '%fluent%' OR client LIKE '%Fluent%';" 2>&1 || true
