# Cache Timer

Terms for a companion timer that helps a person decide when to continue a long Codex task.

## Language

**Estimated warm window**:
An approximate period during which continuing a Codex task may reuse its prompt cache. It is an aid for timing a reply, not a guarantee that a particular request will hit the cache.
_Avoid_: Cache expiration time, guaranteed warm cache

**Tracked session**:
A single Codex task or conversation whose recent activity is considered independently when estimating its warm window.
_Avoid_: Project session, workspace session

**Estimated cold**:
The state of a tracked session after its estimated warm window has ended. It does not assert that the server has evicted every cached prefix.
_Avoid_: Cache miss, cache expired

**Preferred session**:
The task the person chose to show on the taskbar whenever it is estimated warm. Another warm task can appear temporarily after the preferred session becomes estimated cold.
_Avoid_: Permanently pinned session

**Current model**:
The most recently used model in a tracked session. A session has one visible row for this model; an estimate for an earlier model does not carry over after a model switch.
_Avoid_: All models in session
