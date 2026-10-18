# Narrative runtime contract

The host owns money, tasks, training, memory and save transactions. This module owns dialogue choices and emits stable effects. It never submits requests, grants resources or changes memory directly.

```gherkin
Scenario: Opening reward survives a save between choice and application
  Given a new zero-cell run at the doctor's opening question
  When the player chooses "喵"
  Then one Writing corpus effect and one center-cell reveal effect are pending
  And restoring the captured state preserves those same event IDs
  And acknowledging an effect twice does not create another reward

Scenario: Tutorial orders follow real completion
  Given the opening choice has been made
  When the host observes a completed first training job
  Then exactly one one-cell doctor order is requested
  And the ordinary order remains unavailable until the doctor order settles

Scenario: An upgrade purchase alone has no narrative effect
  Given actual installed memory count is 2
  When no successful installation event arrives
  Then the narrative remains at stage 2

Scenario: An optional romance does not override the reputation ending
  Given seven companion meetings have reached a mutually accepted invitation
  When the final policy node arrives
  Then the player explicitly chooses the invitation or the reputation ending
  And the chosen ending is locked across save and restore

Scenario: The erased memory choice cannot be selected twice
  Given the low reputation ending is open
  When the player chooses "我好像听过..."
  Then a "你没有" notice appears
  And that choice is absent from the frame and keyboard focus is cleared
```

`NarrativeSession.CreateNew()` begins the zero-cell tutorial. `AdoptLegacy(memoryCount)` marks an old run as already guided and emits nothing. The initial center compute cell is separate from physical RAM count: after revealing it call `ObserveCenterCellRevealed()` and keep physical memory count at 0. Call `ObserveMemoryCount` only after a successful committed physical installation, never when opening or purchasing the minigame. Stages 1 through 5 correspond to actual installed physical RAM count 1 through 5. Architecture introductions occur at physical counts 1, 2 and 4. The baseline model is available to the one-cell tutorial before any physical upgrade.

Apply `PendingEffects` through normal host commands and acknowledge only after success. `NarrativeEffect.EventId` is also the Training scripted reward ledger key. The initial request IDs are constants on `NarrativeSession`; all emitted tutorial tasks have `RequiredCells == 1`. Persist host state, Training state and `NarrativeSaveData` in the same save transaction. An unapplied effect must stay pending; an applied resource reward also has its own host ledger for recovery.

`RequestPolicyEnding(reputation, lowThreshold, highThreshold)` freezes the reputation band once: reputation at or below the low threshold is low, at or above the high threshold is high, and values strictly between are ordinary. With the policy defaults these are `≤ -15`, `≥ 15`, and `-14..14`. A completed companion invitation offers an explicit fourth-route choice. `EndRun` is emitted only after the chosen ending's closure has been read and the player selects the final curtain. The host should preserve the narrative panel throughout this sequence and use `EndingReason` rather than treating all endings as bankruptcy.

`NarrativePanel.Create(parent, font, session)` is a reusable UGUI overlay with wrapping, scrolling, focus reset and a separate blocking notice. Host contact rows can call `OpenContact(DoctorId/CompanionId/StageContactId)`. `PinnedContacts` contains the currently available fixed contacts; ordinary task contacts remain owned by the existing conversation workspace. `RequiresPause` is a separate pause claim and must not rewrite the player's manual pause or playback speed.

Companion meetings are separate visits. `UnlockCompanionOpportunity()` opens meeting 1; after each visit is completed, a fresh real non-training request settlement opens the next visit. Report each actual settlement through `ObserveRequestSettled(requestId)`. Previously observed IDs cannot open another visit. There are no affection points, gift purchases or resource rewards in this route.

## Stable content mapping

| Runtime ID | Creative outline ID | Meaning |
|---|---|---|
| `narrative-doctor` | `USER_DR_TOP` | Pinned doctor contact |
| `narrative-lin` | `USER_LIN_07` | Lin Qi companion contact |
| `doctor.opening` | `EVT_TUTORIAL_ZERO` | Zero physical RAM, zero compute cells before reveal |
| `stage.1` .. `stage.5` | `EVT_STAGE_1_CAT` .. `EVT_STAGE_5_CAT` | Actual installed physical RAM count 1 .. 5 |
| `companion.1` .. `companion.7` | `EVT_LOVE_01_MEET` .. `EVT_LOVE_07_INVITE` | Seven separately gated meetings |
| `ending.low` | `END_01_LOW_REP` | Host end reason `policy.bad` |
| `ending.normal` | `END_02_MID_REP` | Host end reason `policy.normal` |
| `ending.good` | `END_03_HIGH_REP` | Host end reason `policy.good` |
| `ending.companion` | `END_04_ROMANCE` | Host end reason `emotion` |

`Memories` retains the actual opening/stage/companion choices. Stage 4 dialogue recalls the selected stage 3 response; the companion ending recalls the selected habit and promise. The Argon outline is an edited creative source, not a source of new gameplay buffs. Its option-dependent customer modifiers and forced boundary violations were not adopted.
