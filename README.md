# Bronzemanmode for SPT 4.1.3

> **Bronzeman Mode was originally created by KcY / KeiranY.** This repository continues that original mod for SPT 4.1.3, based on the later port and maintenance work by Randek003.

Current release: **2.0.1**  
Target: **SPT 4.1.3**

## Description

Bronzeman Mode is a challenge/progression mode inspired by Gudi's OSRS Bronzeman concept. The core rule is simple: **you must earn or unlock an item before you can freely purchase it from affected traders or the flea market.**

With the default configuration, locked items are hidden from the affected trader assortments and flea-market results. Once an item is unlocked through one of the enabled sources, it becomes available for normal purchase from the configured traders and/or flea market.

Items can currently be unlocked through:

- successful raid extracts;
- Run Through raids, when enabled;
- items already owned in the PMC inventory, when enabled;
- items received from in-game mail, including received item trees/attachments;
- quest rewards, when enabled.

The mod can also be configured to unlock items after death/MIA, require Found in Raid status for physical item unlocks, exclude whole item categories from Bronzeman restrictions, or permanently allow specific item templates.

Locked purchases are protected by a second purchase check. This means a locked item cannot be bought simply because it becomes visible through another search route or UI interaction. When such a purchase is blocked, the client companion shows a non-modal Bronzeman notification instead of allowing the transaction.

Bronzeman also integrates with the EFT wishlist. Locked items can be managed through Bronzeman-specific wishlist values, and Gunsmith-related items can use a separate wishlist marker.

### Original project and ports

Original Bronzeman Mode by **KcY / KeiranY**:

- https://github.com/KeiranY/tarkov-bronzeman
- https://sp-mod.com/mod/192/bronzeman-mode

Later SPT port and maintenance by **Randek003**:

- https://github.com/Randek003/Bronzemanmode
- https://sp-mod.com/mod/1623/bronzemanmode-by-kcy-39-port

The SPT 4.1+ continuation in this repository is maintained by **PainRUS** with permission from Randek003.

## Installation

Download the release archive and extract its contents directly into the **root folder of your SPT installation**.

The installed files should end up in these locations:

```text
SPT_Runtime\user\mods\Bronzeman\Bronzeman.dll
SPT_Runtime\user\mods\Bronzeman\config.json
SPT_Runtime\user\mods\Bronzeman\gunsmith.json

BepInEx\plugins\Bronzeman\Bronzeman.Client.dll
```

Restart both the SPT server and EFT after installation.

If you are updating an existing installation and have customized `config.json`, back it up before replacing files from the release archive.

## Configuration

Configuration is stored in:

```text
SPT_Runtime\user\mods\Bronzeman\config.json
```

### Unlock sources

| Setting | Default | Description |
| --- | --- | --- |
| `unlocks.raidRunThrough` | `true` | Allows items to be unlocked after a Run Through. If `foundInRaidOnly` is enabled, the FIR requirement still applies. |
| `unlocks.raidDeath` | `false` | Allows raid-carried items to unlock after death/MIA. |
| `unlocks.inventory` | `true` | Unlocks items already present in the PMC inventory/stash when the profile is processed. |
| `unlocks.mail` | `true` | Unlocks items received from in-game mail. Received item trees and attachments are processed together. |
| `unlocks.quests` | `true` | Unlocks item templates received as quest rewards. |
| `unlocks.foundInRaidOnly` | `false` | Requires physical raid/inventory items to have Found in Raid status before they unlock. Quest/mail template unlocks are handled independently. |

### Trader and flea restrictions

| Setting | Default | Description |
| --- | --- | --- |
| `hideItems` | `true` | Removes locked items from affected trader assortments. If `false`, locked trader items remain visible with stock set to `0`. |
| `allTraders` | `false` | Applies Bronzeman restrictions to every trader, including compatible modded traders. If `false`, only IDs listed in `traders` are affected. |
| `traders` | configured list | Trader IDs affected when `allTraders` is `false`. |
| `includeRagfair` | `true` | Applies Bronzeman restrictions to the flea market/ragfair. |
| `requireUnlockComponents` | `true` | Requires the root item and its included/attached item components to be unlocked before the complete offer can be purchased. |

### Always-available items

`ignoreCategories` controls categories that are always purchasable without being individually unlocked.

The default configuration ignores these categories:

- keys;
- special equipment;
- secure containers;
- maps;
- money;
- containers.

Other category switches are present in `config.json` and can be enabled individually.

`ignoreItems` is a list of individual template IDs that are always allowed. This is useful for items that should remain available regardless of Bronzeman progression.

### Wishlist and Gunsmith

These are advanced settings and normally do not need to be changed:

| Setting | Default | Description |
| --- | --- | --- |
| `wishlisttype` | `4` | Wishlist marker value used by Bronzeman for normal locked items. |
| `gunsmith` | `3` | Separate wishlist marker value used for Gunsmith-related items. |
| `gunsmithcount` | `25` | Maximum number of configured Gunsmith quests considered by the Gunsmith wishlist helper. |

### Debug options

| Setting | Default | Description |
| --- | --- | --- |
| `debug` | `false` | Enables additional Bronzeman diagnostic logging. |
| `debugShowLockedItems` | `false` | Shows locked trader/flea entries for testing while keeping the purchase guard active. Do not enable for normal gameplay unless you specifically want this debug behaviour. |

## Build

### Server mod

```powershell
dotnet build .\Bronzeman.csproj -c Release
```

Expected output:

```text
bin\Release\net10.0\Bronzeman.dll
```

### Client companion

The client project must be built against the DLLs from a local SPT 4.1.3 installation.

```powershell
dotnet build .\Bronzeman.Client\Bronzeman.Client.csproj -c Release -p:SptGamePath="C:\Games\SPT"
```

Expected output:

```text
Bronzeman.Client\bin\Release\netstandard2.1\Bronzeman.Client.dll
```

## Important: LLM-assisted development

This SPT 4.1+ continuation is developed with **substantial LLM/AI assistance, primarily ChatGPT**.

I define the desired behaviour, features, requirements and testing direction. ChatGPT is used to analyse the existing codebase and SPT APIs, suggest implementation approaches, and write a significant part of the implementation. I then compile the mod, test it in a live SPT installation, reproduce issues, provide logs and feedback, choose between implementation approaches, and validate the resulting behaviour.

Because substantial parts of the continuation are written with LLM assistance, this project is currently maintained on GitHub rather than being submitted to The Forge / SP-Mod under the current development workflow.

## License

This repository uses **The Unlicense**, the same license used by the original Bronzeman Mode project.

See [LICENSE](LICENSE).

## Credits

- **KcY / KeiranY** — original developer and creator of Bronzeman Mode for SPT.
- **Randek003** — porting and maintenance of Bronzemanmode for later SPT versions before this continuation.
- **PainRUS** — SPT 4.1+ project direction, testing, integration and maintenance.
- **ChatGPT / LLM tooling** — substantial assistance with code analysis and implementation for the SPT 4.1+ continuation.
