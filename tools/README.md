# tools

The node half of `make gen-contracts`, and nothing else. It is not shipped, not referenced by
any project, and not part of the API contract — it exists because the REST types are generated
from `openapi.json` by [openapi-typescript](https://openapi-ts.dev), which is a node tool.

```bash
npm ci --prefix tools     # what gen-contracts runs for you
```

Both dependencies are pinned exactly and locked. `make gen-contracts` regenerates a directory
that is committed and will be diffed for drift, so a floating version would show up as a
contract change nobody made.

| Package | Why |
| --- | --- |
| `openapi-typescript` | `openapi.json` → `contracts/src/rest.ts` |
| `typescript` | type-checks the generated package, so a generator bug fails here rather than in a client repo |

It lives outside `src/` deliberately: a `node_modules` inside a C# project directory is
something MSBuild has to be told to ignore, forever.
