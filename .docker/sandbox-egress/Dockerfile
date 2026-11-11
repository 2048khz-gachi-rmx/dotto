# Sidecar that applies the sandbox egress rules to the host's iptables.
FROM alpine:3.20@sha256:d9e853e87e55526f6b2917df91a2115c36dd7c696a35be12163d44e6e2a4b6bc

RUN apk add --no-cache iptables bash

COPY sandbox-egress.sh /usr/local/bin/sandbox-egress
RUN chmod 0755 /usr/local/bin/sandbox-egress

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD ["/usr/local/bin/sandbox-egress", "--check"]

ENTRYPOINT ["/usr/local/bin/sandbox-egress"]
CMD ["--serve"]
