#!/usr/bin/env bash
#
# Applies interface-scoped egress rules for the sandbox bridge; see design Decision 14.
#
# Usage: sandbox-egress.sh [--apply|--remove|--status|--check|--serve]
#   SANDBOX_BRIDGE   bridge interface to guard (default: br-sandbox)
#   SANDBOX_IPTABLES iptables binary to use (default: iptables)
#
# Idempotent. Safe to run before the network exists: the rules are keyed by
# interface name, not by network id.
set -euo pipefail

BRIDGE="${SANDBOX_BRIDGE:-br-sandbox}"
# Override for hosts whose docker daemon uses the legacy backend (Alpine only
# ships the nft binary; on such hosts use the host script or a legacy image).
IPT="${SANDBOX_IPTABLES:-iptables}"

# RFC1918 / CGNAT / loopback / link-local / multicast / reserved. Anything
# private or host-local a sandbox should not reach.
DESTINATIONS=(
    0.0.0.0/8
    10.0.0.0/8
    100.64.0.0/10
    127.0.0.0/8
    169.254.0.0/16
    172.16.0.0/12
    192.0.0.0/24
    192.168.0.0/16
    224.0.0.0/4
    240.0.0.0/4
)

log() { printf 'sandbox-egress: %s\n' "$*" >&2; }

require_root() {
    if [[ ${EUID} -ne 0 ]]; then
        log "must be run as root"
        exit 1
    fi
}

validate_bridge() {
    # Interface names are limited to 15 characters and this value is passed to
    # iptables, so refuse anything that is not a plain interface name.
    if [[ ! ${BRIDGE} =~ ^[A-Za-z0-9_.-]{1,15}$ ]]; then
        log "invalid SANDBOX_BRIDGE: ${BRIDGE}"
        exit 1
    fi
}

has_rule() { "${IPT}" -C "$@" 2>/dev/null; }

wait_for_docker_chains() {
    local attempts="${1:-30}"
    local i
    for ((i = 0; i < attempts; i++)); do
        if "${IPT}" -S DOCKER-USER >/dev/null 2>&1; then
            return 0
        fi
        sleep 1
    done
    return 1
}

apply_rules() {
    # Insert in reverse so the final order matches DESTINATIONS and the
    # same-bridge RETURN stays first; DOCKER-USER may contain a trailing RETURN
    # added by some Docker versions, and appending would land after it.
    local i
    for ((i = ${#DESTINATIONS[@]} - 1; i >= 0; i--)); do
        local dest="${DESTINATIONS[i]}"
        if ! has_rule DOCKER-USER -i "${BRIDGE}" -d "${dest}" -j DROP; then
            "${IPT}" -I DOCKER-USER 1 -i "${BRIDGE}" -d "${dest}" -j DROP
        fi
    done

    # Same-bridge traffic must reach Docker's own "-i br-x -o br-x ACCEPT" rule.
    if ! has_rule DOCKER-USER -i "${BRIDGE}" -o "${BRIDGE}" -j RETURN; then
        "${IPT}" -I DOCKER-USER 1 -i "${BRIDGE}" -o "${BRIDGE}" -j RETURN
    fi

    if ! has_rule INPUT -i "${BRIDGE}" -m conntrack --ctstate NEW -j DROP; then
        "${IPT}" -I INPUT 1 -i "${BRIDGE}" -m conntrack --ctstate NEW -j DROP
    fi
}

remove_rules() {
    while has_rule DOCKER-USER -i "${BRIDGE}" -o "${BRIDGE}" -j RETURN; do
        "${IPT}" -D DOCKER-USER -i "${BRIDGE}" -o "${BRIDGE}" -j RETURN
    done

    local i
    for ((i = 0; i < ${#DESTINATIONS[@]}; i++)); do
        local dest="${DESTINATIONS[i]}"
        while has_rule DOCKER-USER -i "${BRIDGE}" -d "${dest}" -j DROP; do
            "${IPT}" -D DOCKER-USER -i "${BRIDGE}" -d "${dest}" -j DROP
        done
    done

    while has_rule INPUT -i "${BRIDGE}" -m conntrack --ctstate NEW -j DROP; do
        "${IPT}" -D INPUT -i "${BRIDGE}" -m conntrack --ctstate NEW -j DROP
    done
}

show_status() {
    "${IPT}" -S DOCKER-USER | grep -- "-i ${BRIDGE} " || true
    "${IPT}" -S INPUT | grep -- "-i ${BRIDGE} " || true
}

require_root
validate_bridge

case "${1:---apply}" in
    --apply)
        if ! wait_for_docker_chains 10; then
            log "error: DOCKER-USER chain not found: is docker running, and is '${IPT}' the same backend as the daemon? Set SANDBOX_IPTABLES=iptables-legacy if the host uses legacy iptables."
            exit 1
        fi
        if ! ip link show "${BRIDGE}" >/dev/null 2>&1; then
            log "warning: ${BRIDGE} does not exist yet; rules will apply once the network is created"
        fi
        apply_rules
        log "egress containment active on ${BRIDGE}"
        ;;
    --remove)
        remove_rules
        log "egress containment removed from ${BRIDGE}"
        ;;
    --status)
        show_status
        ;;
    --check)
        # Used as the sidecar healthcheck: exit 0 only if every rule is present.
        ok=0
        has_rule DOCKER-USER -i "${BRIDGE}" -o "${BRIDGE}" -j RETURN || ok=1
        for dest in "${DESTINATIONS[@]}"; do
            has_rule DOCKER-USER -i "${BRIDGE}" -d "${dest}" -j DROP || ok=1
        done
        has_rule INPUT -i "${BRIDGE}" -m conntrack --ctstate NEW -j DROP || ok=1
        if [[ ${ok} -eq 0 ]]; then
            log "rules present on ${BRIDGE}"
            exit 0
        fi
        log "rules missing on ${BRIDGE}"
        exit 1
        ;;
    --serve)
        # Long-running mode for the compose sidecar: apply once, stay alive so
        # `docker compose down` can remove the rules again.
        trap 'remove_rules || true; log "stopped; rules removed"; exit 0' TERM INT
        if ! wait_for_docker_chains 30; then
            log "DOCKER-USER chain not found: is docker running, and is '${IPT}' the same backend as the daemon? Set SANDBOX_IPTABLES=iptables-legacy if the host uses legacy iptables."
            exit 1
        fi
        apply_rules
        log "egress containment active on ${BRIDGE}"
        while :; do
            sleep 3600 &
            wait $! || true
        done
        ;;
    *)
        log "usage: $0 [--apply|--remove|--status|--check|--serve]"
        exit 2
        ;;
esac
