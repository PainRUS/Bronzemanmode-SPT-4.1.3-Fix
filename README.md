# Bronzemanmode — SPT 4.1.3 Compatibility Work

> **Status:** unofficial compatibility/fix branch for Bronzemanmode 2.0.0. This is not an official upstream release.

This repository contains compatibility fixes and additional integration work for **Bronzemanmode** on **SPT 4.1.3**.

The work is based directly on the original author's `Bronzemanmode_2.0.0` branch at commit `4ee3da8591dde73a1cec1c79f89a3c01580cccf2`.

Original author / upstream repository: **Randek003**  
https://github.com/Randek003/Bronzemanmode

Current compatibility repository:  
https://github.com/PainRUS/Bronzemanmode-SPT-4.1.3-Fix

## What was fixed or added

### SPT 4.1.3 compatibility

- Updated raid-end handling to the SPT 4.1.x local raid flow (`/client/match/local/end`).
- Fixed wishlist handling for the current SPT profile model where wishlist data is dictionary-based.
- Corrected trader filtering so `allTraders` and configured trader IDs are respected.
- Updated quest reward handling for current SPT APIs.
- Separated physical-item FIR checks from template-based unlocks so quest/mail rewards are not incorrectly blocked by `foundInRaidOnly`.
- Added a Gunsmith quest-ID fallback for the Part 1 data mismatch.
- Kept Bronzeman unlock state persistent in the player profile.

### Mail attachment unlocks

Items received through in-game mail can now unlock immediately.

The mail handler:

- runs after SPT's native item-move processing;
- supports the relevant mail move/split/merge/transfer actions;
- unlocks the received root item and attached child templates;
- saves the profile immediately;
- removes Bronzeman-managed wishlist entries for newly unlocked templates.

This includes assembled weapons: receiving a weapon through mail unlocks the weapon and its attached components.

### Immediate client wishlist synchronization

SPT's item-event response does not automatically update EFT's in-memory `WishlistManager` after Bronzeman changes the authoritative server profile.

To solve that, this repository adds an optional BepInEx client companion:

- `Bronzeman.Client.dll`

After the mail transfer screen closes, the client:

1. waits for the EFT/SPT inventory operation queue to finish;
2. requests the authoritative wishlist from the Bronzeman server mod;
3. reconciles EFT's local explicit wishlist entries;
4. calls the native wishlist manager methods so the UI updates without restarting the game.

The synchronization intentionally operates on explicit `UserItems` rather than the full generated wishlist so client-generated QoL/hideout entries are not removed.

## Version

Current compatibility build: **2.0.3**  
Target: **SPT 4.1.3**

## Live validation

The 2.0.3 compatibility build has been manually compiled and live-tested on SPT 4.1.3 with an existing test profile and a 24-mod setup.

Validated scenarios include:

- server mod loads successfully on SPT 4.1.3;
- existing Bronzeman unlock data persists across profile reloads;
- wishlist initialization works without the previous runtime binder crash;
- successful raid unlocks new items and attached weapon components;
- death with `raidDeath: false` does not trigger raid-based unlocks;
- inventory scanning remains independent and follows `inventory: true/false`;
- mail attachments unlock immediately;
- assembled weapons received through mail unlock root + attached component templates;
- flea/trader availability reflects mail unlocks immediately;
- client wishlist state updates immediately after closing the transfer screen;
- tested alongside UI Fixes and ReceiveAllChats in the validated mail scenario.

Not every possible configuration combination has been tested. In particular, treat untested combinations and third-party mod interactions as needing their own validation.

## Build

### Server mod

Requires a .NET SDK capable of building the server project.

```powershell
dotnet build .\Bronzeman.csproj -c Release
```

Expected output:

```text
bin\Release\net10.0\Bronzeman.dll
```

### Client companion

The client project must be built against the DLLs from the local SPT 4.1.3 installation.

Example:

```powershell
dotnet build .\Bronzeman.Client\Bronzeman.Client.csproj -c Release -p:SptGamePath="C:\Games\SPT"
```

Expected output:

```text
Bronzeman.Client\bin\Release\netstandard2.1\Bronzeman.Client.dll
```

## Installation for testing

### Server

Replace the Bronzeman server DLL in the installed server-mod directory with the newly built:

```text
Bronzeman.dll
```

Keep the existing user configuration files unless intentionally changing settings.

### Client

Place:

```text
Bronzeman.Client.dll
```

under a BepInEx plugins directory, for example:

```text
SPT\BepInEx\plugins\Bronzeman\Bronzeman.Client.dll
```

Then fully restart the SPT server and EFT client.

A successful client load should log:

```text
Bronzeman client wishlist synchronization enabled.
```

## Configuration compatibility

The mail unlock option has a code default of enabled. Existing configs that do not yet contain an explicit `mail` property continue to work with the default behavior.

No configuration migration was required for the live-tested profile.

## Upstream contribution

The intention of this repository is to make the fixes reviewable and easy to contribute back to the original Bronzemanmode project.

It is **not intended to replace the original author's project or claim authorship of Bronzemanmode**.

A clean upstream pull request can be prepared against the original `Bronzemanmode_2.0.0` branch if the author wants to integrate these changes.

## AI-assisted development disclosure

Substantial LLM/AI assistance was used during analysis and implementation of this compatibility work, including investigation of SPT 4.1.3 API changes and development of the mail/client wishlist synchronization changes.

The resulting code was manually compiled and live-tested in SPT 4.1.3 before being marked as validated here.

This disclosure is included intentionally so the development history is transparent to the original author, reviewers, and any mod-distribution platform considering the work.

## Credits

- **Randek003** — current Bronzemanmode author/maintainer and source of the `Bronzemanmode_2.0.0` codebase used as the compatibility baseline.
- Previous Bronzemanmode authors/contributors remain credited through the upstream project history.
- **PainRUS** — SPT 4.1.3 compatibility testing, integration work, repository maintenance and live validation.
