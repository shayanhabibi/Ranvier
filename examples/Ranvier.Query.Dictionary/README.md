# Dictionary navigation model

`Navigation.fs` is a compiled, UI-independent Elmish-style example for Ranvier.Query.
It covers partial loading, page ownership, independent drafts, adding words,
Back, Home, and saves completing after an editor closes.

Build with `dotnet build examples/Ranvier.Query.Dictionary -c Release`.
Supply a `Graph` and the four asynchronous functions in `Api`, then use `init`
and `update` on the graph thread. Their command lists use Elmish's dispatch
subscription shape. The view reads the current page's query `State` and draft.
Dispose the model's session when the application closes.

On `ReconciliationFailed`, the server has already saved the word. Show a refresh
action for stale queries rather than resubmitting that write to repair the cache.
See `docs/content/guide/query-navigation.md` for the ownership and command flow.
