# Spec Delta

## Purpose

Defines how the host reaches a sandbox (transport, authentication, socket lifecycle) and what a sandbox cannot reach (peers, host, LAN) while retaining public internet egress.

## ADDED Requirements

### Requirement: Production control channel uses a per-session Unix socket

When sandboxes run on a named Docker network, the sandbox API SHALL listen on a Unix-domain socket inside a per-session directory that is bind-mounted into the sandbox, and the container SHALL NOT publish a TCP port. When no network name is configured (development), the API MAY use a loopback-published TCP port.

#### Scenario: Production transport

- **WHEN** a sandbox starts with a configured network name
- **THEN** the container publishes no host port, a socket exists at the session's socket path, and the bot reaches `/ping` through that path

#### Scenario: Development transport

- **WHEN** no network name is configured
- **THEN** the API is published on `127.0.0.1` only and no Unix socket is required

### Requirement: Every control request is authenticated

Every request on either transport SHALL carry the per-container token in `X-Sandbox-Token`. A missing or invalid token SHALL return HTTP 401 and close the connection; the Unix socket SHALL NOT be treated as an authentication substitute.

#### Scenario: Missing token over the socket

- **WHEN** a request arrives without a token
- **THEN** the server responds 401 with `Connection: close` and does not process the request body

#### Scenario: Valid token over the socket

- **WHEN** a request carries the container's token
- **THEN** the request is processed normally

### Requirement: Socket lifecycle is per-session and self-cleaning

Each sandbox session SHALL use a unique socket path. The server SHALL unlink a stale socket file before binding, and the host SHALL remove the session's socket directory when the container is disposed.

#### Scenario: Stale socket file

- **WHEN** the server starts and the socket path already exists as a leftover file
- **THEN** it removes the file and binds successfully

#### Scenario: Teardown

- **WHEN** the sandbox container is stopped or the session ends
- **THEN** the session's socket directory no longer exists and a later session gets a fresh path

### Requirement: Sibling sandboxes cannot reach each other

The sandbox network SHALL have inter-container communication disabled, and the bot SHALL NOT be attached to that network. Sandboxes SHALL NOT be able to open connections to another sandbox's API or to the bot over the sandbox bridge.

#### Scenario: Sandbox-to-sandbox connection

- **WHEN** one sandbox attempts a TCP connection to another sandbox's bridge address
- **THEN** the connection is blocked

#### Scenario: Bot is not on the sandbox bridge

- **WHEN** the production stack is inspected
- **THEN** the bot container's networks do not include the sandbox network

### Requirement: Internet-only egress

Sandboxes SHALL be able to reach public internet destinations but SHALL NOT reach RFC1918, CGNAT, loopback, link-local, multicast or reserved destinations, host services, or ports published on the host. Domain names SHALL continue to resolve through the configured resolvers.

#### Scenario: Public destination

- **WHEN** a sandbox requests a public HTTPS URL
- **THEN** the request succeeds

#### Scenario: Host or LAN destination

- **WHEN** a sandbox requests a private-range address, a host loopback port, or a sibling container's published port
- **THEN** the connection is dropped
