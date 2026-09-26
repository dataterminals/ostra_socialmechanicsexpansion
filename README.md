# Social Mechanics Expansion

A BepInEx plugin for [Ostranauts](https://store.steampowered.com/app/1022980/Ostranauts/). It makes relationships move: enemies can make peace, feelings fade with time apart, and friends pass on what they think of people. It builds on the relationship numbers the game already keeps and saves, so it needs no save data of its own.

Status: thaw, drift and gossip are in. Shared history and favors are planned; see [DESIGN.md](DESIGN.md).

## How the base game does it

(dataterminals note: Rachel is the player character for the save/world that this mod is built of)

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

**Gossip.** When an exchange affects a character (the listener), the other person (the speaker) may bring up someone they have feelings about (the subject). Each pair gets one chance per `CooldownHours` (24).

- **Who:** someone the speaker knows at familiarity 50 or more. The pick is weighted by how strongly they lean, and doubled for people the listener knows too.
- **Whether:** `BaseChance` (0.35) × how hot the opinion is (lukewarm comes up half as often) × talkativeness. It never happens without some trust.
- **How much lands**, as a share of the speaker's feeling:

  > `MaxShare` (0.2) × trust × (1 − experience) × credibility × exaggeration

  - **Trust** is the listener's regard for the speaker, from 0 to 1. It grows with a kind share above half and with familiarity up to 100, with floors for a friend (0.6), family (0.5) and a crush (0.8). Enemies count for nothing.
  - **Experience** is the listener's own familiarity with the subject, as f / (f + 100). Hearsay barely moves someone who knows the subject well. A stranger's familiarity is itself hearsay, so it counts as none.
  - **Credibility:** Charismatic speakers get ×1.25. Honest gets ×1.25 and Liar ×0.5, but only once the listener has learned that trait. An Observant listener applies ×0.8, an Obtuse one ×1.25.
  - **Exaggeration:** a Liar or Treacherous speaker passes on 1.5× what they really feel.
- **Then the listener reacts:**
  - **Pushed back:** they know the subject firsthand (familiarity 50+) and lean clearly the other way. They don't budge, and the same formula runs in reverse, softening the speaker's view instead. A Loyal listener whose friend was run down also loses a little respect for the speaker.
  - **Shrugged:** under half a point of familiarity would land, so nothing happens.
  - **Agreed:** their tally on the subject moves by that share, capped at `Cap` (10) familiarity. Someone they'd never heard of is added as a stranger, with the history line "Heard about them from <speaker>".
- The player character never takes on gossip unless `ReachesPlayer` is on. The player is told when someone in their room talks about them, or sticks up for them.

**Personality.** The trait effects above can be turned off as a group. Talkativeness is Gregarious ×1.5 and Shy ×0.5. The set also includes Forgiving: a Forgiving character's grudges drift away twice as fast.

## Patches

| Target | Kind | Why |
| --- | --- | --- |
| `Relationship.StoreIAConds` | postfix | Thaw: the label step, sooner and both ways |
| `Relationship.StoreIAConds` | postfix | Gossip: roll for the listener and speaker; applied on the next frame |
| `BaseUnityPlugin.Update` | — | Drift's hourly tick and applying queued rumors |

These members are public for tools:
- `Thaw.Next(Relationship)` says what the next exchange would do to a label.
- `Thaw.VerdictAt` is the familiarity at which friend or enemy is decided.
- `Gossip.Trust(Relationship)` is how much someone regards a person, from 0 to 1.
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
| `Gossip.BaseChance` | `0.35` | Chance per pair per cooldown, before heat and talkativeness |
| `Gossip.MaxShare` | `0.2` | The most of the speaker's feeling a rumor can carry |
| `Gossip.Cap` | `10` | Most familiarity one rumor can move |
| `Gossip.CooldownHours` | `24` | Game hours between a pair's rolls |
| `Gossip.PushBack` | `true` | Listeners who know better argue back |
| `Gossip.ReachesPlayer` | `false` | Let gossip change the player character's feelings |
| `Gossip.LogAboutPlayer` | `true` | Log when someone in the player's room talks about them |
| `Personality.Enabled` | `true` | Traits shape gossip and drift |

## Build

```bash
dotnet build -c Release -p:Deploy=true -p:GameDir="<game folder>"
```

Requires BepInEx 5 in the game folder. The DLL is deployed to `BepInEx/plugins/SocialMechanicsExpansion/`.
