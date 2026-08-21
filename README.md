# Bronzemanmode for SPT 4.1.3

> **Status:** personal development version of Bronzemanmode for SPT 4.1.3.

This repository is a continuation of **Bronzemanmode**, currently maintained and developed primarily for my own SPT setup.

The project is public so other users can inspect the source, follow development, test it, or use the builds if they find them useful. However, this is first and foremost a personal project rather than an official release for The Forge / SP-Mod.

## Important: LLM-assisted development

This project is developed with **substantial LLM/AI assistance, primarily ChatGPT**.

I define the desired behaviour, features, requirements and testing direction. ChatGPT is used to analyse the existing codebase and SPT APIs, suggest implementation approaches, and write a significant part of the implementation. I then compile the mod, test it in a live SPT installation, reproduce issues, provide logs and feedback, choose between implementation approaches, and validate the resulting behaviour.

Because substantial parts of the new compatibility work and features are written with LLM assistance, this project **does not meet The Forge / SP-Mod AI-generated content policy and will not be submitted there** under the current development workflow.

The relevant policy can be found here:

https://sp-mod.com/content-guidelines#ai-generated-content-policy

This disclosure is intentionally kept public so there is no ambiguity about how the project is developed.

## Project history

Bronzemanmode was originally created by **KcY / KeiranY**.

Original project:
https://github.com/KeiranY/tarkov-bronzeman

The mod was later ported and maintained for newer SPT versions by **Randek003**.

Randek003's repository:
https://github.com/Randek003/Bronzemanmode

SP-Mod / The Forge page for Randek003's port:
https://sp-mod.com/mod/1623/bronzemanmode-by-kcy-39-port

The SPT 4.1+ development work in this repository is maintained by **PainRUS** with permission from Randek003.

Current repository:
https://github.com/PainRUS/Bronzemanmode-SPT-4.1.3-Fix

## What Bronzemanmode does

Bronzemanmode changes progression so items generally need to be earned before they can be freely purchased from traders or the flea market.

Depending on configuration, items can be unlocked through raids, inventory ownership, quest rewards and other supported sources. The goal is to keep the original Bronzeman progression idea while improving compatibility and quality of life for newer SPT versions.

## SPT 4.1.3 work

Current compatibility work includes:

- updated raid-end handling for the SPT 4.1.x local raid flow;
- wishlist handling updated for the current SPT profile model;
- corrected trader filtering;
- updated quest reward handling for current SPT APIs;
- separated physical-item FIR checks from template-based unlocks;
- Gunsmith quest-ID fallback for the Part 1 data mismatch;
- persistent Bronzeman unlock state in the player profile;
- mail attachment unlock support;
- assembled weapons received through mail can unlock the weapon and attached components;
- immediate client-side wishlist synchronization through an optional BepInEx companion plugin.

## Version

Current development build: **2.0.3**  
Target: **SPT 4.1.3**

## Validation

The current build has been manually compiled and tested on SPT 4.1.3 with an existing profile and a multi-mod setup.

Validated scenarios include:

- server mod loading successfully;
- existing Bronzeman unlock data surviving profile reloads;
- wishlist initialization;
- successful raid unlocks;
- attached weapon component unlocks;
- raid-death configuration behaviour;
- inventory unlock configuration behaviour;
- immediate mail attachment unlocks;
- assembled weapon mail rewards;
- flea/trader availability after mail unlocks;
- client wishlist synchronization after closing the mail transfer screen.

Not every configuration or third-party mod combination has been tested.

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

## Installation for testing

### Server

Replace the Bronzeman server DLL in the installed server-mod directory with:

```text
Bronzeman.dll
```

Keep the existing configuration files unless intentionally changing settings.

### Client

Place:

```text
Bronzeman.Client.dll
```

under a BepInEx plugins directory, for example:

```text
SPT\BepInEx\plugins\Bronzeman\Bronzeman.Client.dll
```

Then restart the SPT server and EFT client.

## License

This repository uses **The Unlicense**, the same license used by the original Bronzeman Mode project.

See [LICENSE](LICENSE).

## Credits

- **KcY / KeiranY** — original creator of Bronzeman Mode.
- **Randek003** — porting and maintenance of Bronzemanmode for later SPT versions before the SPT 4.1+ continuation.
- **PainRUS** — SPT 4.1+ project direction, testing, integration, maintenance and validation.
- **ChatGPT / LLM tooling** — substantial assistance with code analysis and implementation for this development branch.
