# Spec Delta

## Purpose

How a running agent accepts new instructions from the channel it is working for: who may send them,
where they enter the model's context, how they are framed, and when they stop applying.

## ADDED Requirements

### Requirement: Steering Window

A run SHALL accept steering only while it is executing, and only from messages created after the run
started. Messages that already existed when the run began are history, not steering, and SHALL NOT be
injected a second time.

#### Scenario: Message during a run is delivered

- **WHEN** a message is posted in the channel while an agent run is executing there
- **THEN** it becomes eligible for delivery to that run's next model request

#### Scenario: Message posted before the run started is not steering

- **WHEN** a run starts after a message was already posted in the channel
- **THEN** that message is not injected as steering; it is only available through the run's initial history

#### Scenario: Message with no run is dropped

- **WHEN** a message arrives in a channel where no agent run is executing
- **THEN** nothing is queued and no error is produced

#### Scenario: Message after the answer changes nothing

- **WHEN** a message arrives after the run produced its final answer
- **THEN** the run's result is unchanged, the message is not delivered anywhere, and nothing is reported

### Requirement: Who May Steer

Any human participant of the run's channel SHALL be able to steer that run, whether or not they
invoked it. Messages authored by bots or applications and messages that invoke a command SHALL NOT
steer.

#### Scenario: Caller steers their own run

- **WHEN** the caller who started the run posts a message in the run's channel
- **THEN** the message is delivered to that run

#### Scenario: Another participant steers

- **WHEN** a different human posts in the same channel while a run executes there
- **THEN** that run receives the message

#### Scenario: Two runs in one channel both receive it

- **WHEN** two runs are executing in one channel and a human posts there
- **THEN** both runs receive the message

#### Scenario: Bot messages never steer

- **WHEN** a bot or application posts in the channel
- **THEN** no run receives it as steering

#### Scenario: Command invocations never steer

- **WHEN** a message carries an interaction or command payload
- **THEN** no run receives it as steering

### Requirement: Injection Point

Steering SHALL be attached to a model request that is already about to be made, after the previous
turn's tool results. It SHALL NOT cause an additional model request and SHALL NOT change the turn
count, because it only shapes a request the run was making anyway. The system prompt, the tool list
and the tool mode SHALL remain unchanged and in place.

#### Scenario: Steering follows the tool results

- **WHEN** a request would carry tool results from the previous turn and steering is pending
- **THEN** the model receives the tool results first and the steering message after them

#### Scenario: Steering adds no request and no turn

- **WHEN** a steer is delivered during a run
- **THEN** the number of model requests and the run's turn count are the same as without it

#### Scenario: Session shape is preserved

- **WHEN** a request carries steering
- **THEN** the system prompt remains the first message and the tool list is identical to earlier requests

#### Scenario: Steering is visible on every later request

- **WHEN** steering was delivered on an earlier turn of the same run
- **THEN** later requests in that run still carry it, so the model does not lose the instruction

#### Scenario: Steering after the turn limit

- **WHEN** a steer is delivered in a turn past the turn budget
- **THEN** the model receives it and may change its answer, while tool calls remain refused

### Requirement: Steering Message Shape

Each message received since the last request SHALL be delivered as its own user-role message holding
a `<system>` envelope with one `<message user="…"><content>…</content></message>` element, matching
the XML the run's initial history uses, in arrival order. Steering SHALL NOT be merged into a tool
result, an assistant message or the original prompt, and message content SHALL NOT be truncated.

#### Scenario: Several messages arrive

- **WHEN** three messages arrive between two model requests
- **THEN** the next request carries three steering messages, one per arrival, in arrival order

#### Scenario: Authors are identified

- **WHEN** the model receives steering
- **THEN** the envelope names the author of the message it carries

#### Scenario: Framing is explicit

- **WHEN** the model receives steering
- **THEN** it arrives as a user-role message holding a `<system>` envelope, separate from any tool result

#### Scenario: Content is verbatim

- **WHEN** a steering message is long
- **THEN** it is delivered in full, without trimming or summarising

### Requirement: Steering Disclosure in the System Prompt

When steering is enabled for a run, the system prompt SHALL state that channel messages may arrive
during the run, that they arrive in a `<system>` envelope after tool results, that they are user
input rather than tool output, and that the newest instruction takes precedence on conflict.

#### Scenario: Enabled run discloses steering

- **WHEN** a system prompt is rendered for a run with steering enabled
- **THEN** it explains that mid-run messages may arrive and how to treat them

#### Scenario: Disabled run says nothing about steering

- **WHEN** a system prompt is rendered for a run with steering disabled
- **THEN** it contains no steering instructions

### Requirement: Isolation Between Runs and Scopes

Steering SHALL reach only runs executing in the channel the message was posted in, and SHALL stop
being accepted for a run once that run ends.

#### Scenario: Runs in other channels are unaffected

- **WHEN** a message is posted in one channel
- **THEN** runs executing in other channels do not receive it

#### Scenario: Ended run accepts nothing

- **WHEN** a message arrives after a run was disposed
- **THEN** nothing is enqueued for it and no exception escapes

### Requirement: Failure Containment

A failure while collecting or delivering steering SHALL NOT fail the agent run, and a failing run
SHALL NOT leave steering state behind. An unreadable or closed inbox SHALL be treated as no steering.

#### Scenario: Delivery failure does not break the run

- **WHEN** reading or formatting pending steering throws
- **THEN** the model request proceeds without steering and the run still answers

#### Scenario: Listener failure is swallowed

- **WHEN** the message listener cannot enqueue a message
- **THEN** the failure is logged and other message processing is unaffected

#### Scenario: Run teardown removes the subscription

- **WHEN** a run ends, whether it succeeded or threw
- **THEN** its channel entry is removed and later messages find no subscriber

### Requirement: Steering Configuration

Steering SHALL be configurable per interaction scope with a single enabled switch, defaulting to
enabled.

#### Scenario: Defaults

- **WHEN** no steering configuration is supplied
- **THEN** a run accepts steering from any human in its channel

#### Scenario: Disabled switch

- **WHEN** steering is disabled for the interaction
- **THEN** no messages are collected and the prompt does not mention steering

#### Scenario: Scope override applies

- **WHEN** a guild or channel disables steering
- **THEN** runs invoked through that scope collect no steering
