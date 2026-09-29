# Signal maps: expanded collection groups

Follows [collections on signal maps](../../../../docs/content/guide/signal-maps.md#collections), which drew a projection
or lookup as one node. Decisions below were delegated by the user ("go with your recommendations end to end").

## Decisions

| Question | Decision |
| --- | --- |
| Grouping shape | A labelled box: the collection's node as its header, one row node per key beneath it |
| Reader edges | A reader of one row (`Get key`) attaches to that row; a reader of `Keys`, `Count` or `AnyPending` attaches to the header |
| Factory-created nodes | Drawn inside the box, left of their row, with their edge into the row |
| Default | Expanded; `groups=collapse` on a fence keeps the one-node drawing |
| Row styling | Each row and member takes the node state classes (flight, waiting, failed, fresh) on its own |

## Trace

`Tracer.PartScope (graph, owner, host, key)` records `Part` with `Flag = 1` and `Node` = the owner id of a
`createProjectionWith` key scope. `Conditional`, like every hook.

## Model (`MapModel`)

- `Scene` gains `Scopes` (owner id → part) and `Grouping` (`Expand` | `Collapse`, set on `start`).
- `placeOf scene id` classifies a node: `Top`, `Row host`, `Member (host, row)`, `Inside other` (drawn as another
  node) or `Hidden` (beacon, row watch: its edges are dropped).
  - Expand: an item signal is inside its host; another keyed part is a row; a keyless part is inside its host; a
    node owned (transitively) by a key scope is a member of that key's row.
  - Collapse: every part and member is inside its host, as before.
- `drawnAs` follows `Inside`; `edges` and every cue go through it.
- `rows scene host` lists (row id, members) by id; `laidOutAs` lifts a row or member to its host for `Layout`.

## Layout and drawing

- `Layout.placeSpanned` takes each node's span in rows; a node's row is the running total of spans above it.
- `SignalMap` draws a box per expanded host (span = 1 + rows), rows beneath the header, members left of their row.
  A row shows its key as its name; the log keeps `rows[tea]`.
- `SignalMap` gains a `grouping` argument; `MapFence` parses `groups=expand|collapse`.

## Docs

The Collections section shows the expanded projection, a `createProjectionWith` per-key quote whose rows go pending
one by one, and one `groups=collapse` map.

## Tasks

1. Trace hook and call site; core suite green.
2. Model: grouping, `placeOf`, `drawnAs`, `rows`, edges and cues; model tests for expand and collapse.
3. Layout spans; layout test.
4. `SignalMap` boxes, rows and members; CSS.
5. `MapFence` flag; authoring test.
6. Docs section; docs build.
