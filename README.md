# Social Mechanics Expansion

A BepInEx plugin for [Ostranauts](https://store.steampowered.com/app/1022980/Ostranauts/). It makes relationships move: enemies can make peace, feelings fade with time apart, and friends pass on what they think of people. It builds on the relationship numbers the game already keeps and saves, so it needs no save data of its own.

Status: thaw, drift and gossip are in. Shared history and favors are planned; see [DESIGN.md](DESIGN.md).

## How the base game does it

Each character keeps one relationship per person they know. It's one-way: Rachel's view of Isabel and Isabel's view of Rachel are separate. A relationship holds a tally with one entry per social stat. After every exchange, `Relationship.StoreIAConds` adds half of what the exchange did to each stat. The entry goes negative when the other person eased that stat (kind) and positive when they worsened it (hostile).

The game reads the tally like this:

- Each entry is capped at ±25.
- **Familiarity** is the sum of the capped sizes.
- **Kindness** is the negative part and **animosity** the positive part.
- Labels flip right after the exchange:
  - stranger → acquaintance at familiarity 50;
  - from 200, over 55% kind → friend and over 55% hostile → enemy;
  - an intimacy entry of −50 or lower makes a crush.

Nothing else ever moves a label. Time doesn't touch the tally, and nobody learns anything about a person from anyone else.

While a character's attention is on someone (`CondOwner.strLastSocial`), the game adds that relationship's entries, capped at ±50, onto their live social stats. When attention moves, `Relationship.ApplyConds` takes them off again. So any change to a focused relationship's tally has to move the live stat by the same amount, or the stat stays off for good. `Tally.Add` does that for every change this mod makes.

## What the mod changes

**Thaw.** This runs right after the game's own label step on each exchange:

- At `VerdictAt` familiarity (120 instead of 200), friend or enemy is decided by the game's own 55% rule.
- From `ThawAt` (60), an enemy whose tally is now more than `ThawShare` kind softens to an acquaintance. The history line reads "Made peace".
- A friend whose tally is more than `ThawShare` hostile cools to an acquaintance the same way ("Fell out").
- Strangers, nemeses and family are left to the game.

**Drift.** About once a game hour, every loaded character's tally eases toward neutral:

- The hostile entries have a half-life of `HostileHalfLifeDays` (20) and the kind ones `KindHalfLifeDays` (40), so grudges cool first.
- Two people in the same room don't drift apart while they're there.
- Labels wait for the next exchange, where they're re-read.
- An NPC's feelings drift only while they're loaded, i.e. while the player is on their station or ship.

**Gossip.** When an exchange affects a character (the listener) who trusts the other person (the speaker), the pair gets one roll per `CooldownHours` (24) at `Chance` (0.25). Trust means family, a friend, a crush, or an acquaintance whose tally is more than 55% kind. On a hit:

- The speaker brings up one third person. The pick is weighted by how strongly they lean, and doubled for people the listener already knows.
- The listener's tally on that person moves by `Share` (10%) of the speaker's, capped at `Cap` (10) familiarity for the whole rumor.
- If the listener had never heard of them, they're added as a stranger, with the history line "Heard about them from <speaker>".
- The player character never takes on gossip unless `ReachesPlayer` is on.
- The player is told when someone in their room talks about them.

## Patches

| Target | Kind | Why |
| --- | --- | --- |
| `Relationship.StoreIAConds` | postfix | Thaw: the label step, sooner and both ways |
| `Relationship.StoreIAConds` | postfix | Gossip: roll for the listener and speaker; applied on the next frame |
| `BaseUnityPlugin.Update` | — | Drift's hourly tick and applying queued rumors |

Two members are public for tools:
- `Thaw.Next(Relationship)` says what the next exchange would do to a label.
- `Gossip.RecentRumors` lists this session's rumors.

[OstraScope](https://github.com/dataterminals/ostrascope)'s `/social` shows both.

## Config

`BepInEx/config/com.sylvia.socialmechanicsexpansion.cfg`, created on first launch:

| Setting | Default | |
| --- | --- | --- |
| `Thaw.Enabled` | `true` | |
| `Thaw.VerdictAt` | `120` | Familiarity for friend or enemy; the game uses 200 |
| `Thaw.ThawAt` | `60` | Familiarity from which enemies can soften and friends cool |
| `Thaw.ThawShare` | `0.55` | Kind share for an enemy to soften, hostile share for a friend to cool |
| `Drift.Enabled` | `true` | |
| `Drift.HostileHalfLifeDays` | `20` | Game days for hostility to fade by half |
| `Drift.KindHalfLifeDays` | `40` | Game days for kindness to fade by half |
| `Drift.PauseWhenTogether` | `true` | No drift between people in the same room |
| `Gossip.Enabled` | `true` | |
| `Gossip.Chance` | `0.25` | Chance per pair per cooldown |
| `Gossip.Share` | `0.10` | How much of the speaker's feeling rubs off |
| `Gossip.Cap` | `10` | Most familiarity one rumor can move |
| `Gossip.CooldownHours` | `24` | Game hours between a pair's rolls |
| `Gossip.ReachesPlayer` | `false` | Let gossip change the player character's feelings |
| `Gossip.LogAboutPlayer` | `true` | Log when someone in the player's room talks about them |

## Build

```bash
dotnet build -c Release -p:Deploy=true -p:GameDir="<game folder>"
```

Requires BepInEx 5 in the game folder. The DLL is deployed to `BepInEx/plugins/SocialMechanicsExpansion/`.
