# GSBT Agent Notebook

GSBT embeds a machine-facing field manual in `gsbt.exe`. An agent reads it from
the top-level `agentNotebook` object returned by:

```bat
gsbt help --ai
```

The notebook is not shown in ordinary human help and is not shipped as a loose
JSON file. It gives an agent product-specific context before it operates GSBT or
answers questions about known behavior.

## Product Scope

GSBT is optimized for game saves, but `gsbt add custom` can register any existing
user-selected folder. Useful candidates include custom maps, campaigns, mods,
presets, emulator saves, editor projects, application profiles, templates, and
other durable personal data.

```bat
gsbt add custom "Descriptive Name" "C:\verified\folder" --ai
```

The folder must exist. An agent should explain the proposed scope, obtain the
user's approval, register the narrowest useful folder, create a targeted first
backup, and verify the result.

GSBT is a folder snapshot tool, not an application-aware database exporter or a
replacement for source control. Active databases, virtual machines, credential
stores, browser profiles, system directories, and whole drives need a more
specialized workflow and must not be selected by default.

## Missing Data Discovery

When a game is not found, the agent should first run `scan --full` and inspect
`list all`. If useful local data is still missing, research sources in this order:

1. PCGamingWiki for edition-aware save, configuration, and cloud information.
2. Official game, publisher, or support documentation.
3. Store/platform documentation and well-maintained community guides.
4. Forums and Reddit as leads that require corroboration.

Published paths are candidates, not proof. The agent must identify the edition,
launcher, Windows account, and possible Documents/OneDrive redirection, then
confirm that the folder exists and contains plausible durable data locally.
It must never invent or create a guessed path merely to make registration pass.

Public research should use the game title, edition, and data type. It should not
include the user's Windows name, account IDs, private paths, or file contents.
GSBT itself does not browse these sites; browsing is an optional agent action.

## Sourced Example: Warcraft III

An online-focused Warcraft III player may have no campaign saves while still
having valuable downloaded maps, authored maps, custom campaigns, or persistent
map data.

- [Blizzard's Warcraft III Editor guide](https://news.blizzard.com/en-us/article/23395649/revisiting-the-warcraft-iii-editor)
  places user-created Windows maps under `Documents\Warcraft III\Maps`.
- [PCGamingWiki's Reforged page](https://www.pcgamingwiki.com/wiki/Warcraft_III:_Reforged)
  documents campaign and `CustomMapData` locations separately.
- [PCGamingWiki's classic page](https://www.pcgamingwiki.com/wiki/Warcraft_III:_Reign_of_Chaos)
  documents version-dependent migration of classic save locations.

The notebook tells the agent to confirm Classic versus Reforged, inspect the
user's real Documents location, and propose separate entries only for folders the
user values. It does not assume that one path fits every installation.

## Knowledge Maintenance

The notebook should remain a compact field manual plus a small set of useful,
sourced examples. It should not duplicate the full Ludusavi or PCGamingWiki path
databases.

Every game-specific card should include:

- a stable knowledge ID;
- exact edition and platform scope;
- source URLs and source type;
- the date the fact was verified;
- agent guidance and a clear do-not-assume boundary.

Runtime research wins when a path, launcher, or game version may have changed.
When reliable sources disagree, the agent should present the candidates and
inspect the machine rather than choosing silently.

## Release Validation

Tests verify that the embedded notebook exposes the custom-folder scope, ranks
PCGamingWiki first for missing-save research, retains safety rules, and includes
an official source for the Warcraft III example. CLI integration tests also run
`gsbt add custom ... --ai` against an isolated test catalog.
