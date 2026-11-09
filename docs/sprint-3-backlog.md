# Sprint 3 Backlog Submission

**CECS 491B Senior Project II | Sprint Planning Deliverable**

| | |
| --- | --- |
| **Project Title** | NaviPet |
| **Team Name & Sec** | TBD (fill in team name and section) |
| **GitHub Org** | https://github.com/navipet-senior-project |
| **Issue Tracker** | https://github.com/navipet-senior-project/.github/issues (label `sprint-3`) |
| **Project Board** | https://github.com/orgs/navipet-senior-project/projects/5 |
| **Product Roadmap** | https://github.com/orgs/navipet-senior-project/projects/7 |
| **Sprint Window** | TBD |
| **Scheduled Demo** | TBD |
| **Present Engineers** | Khoi Do (@Ben2104), Jan Montemayor (@Kura-Yami), Jake Nomoto (@GlazedKrispy), Jomar Hernandez (@thejomar), Will Chhuor (@will-chhuor) |

## Sprint 3 Goal

Deliver the VEC indoor AR vertical slice (launch from the app, localize, and guide to VEC 404 across floors) with a polished Unity AR HUD and screens, finish the VEC stair, room and elevator pinpoints for floors 3–5, and ship the first gem rewards experience (balance, claim, and spend on the pet).

## Team Focus

| Engineer | Sprint 3 focus |
| --- | --- |
| Jake Nomoto (@GlazedKrispy) | Gem APIs: get gem balance, claim a gem, use gems on the pet. Also entrance and indoor-availability data. |
| Will Chhuor (@will-chhuor) | Gem user experience in the app: Achievements and rewards, pet customization. Also search results and arrival card. |
| Jomar Hernandez (@thejomar) | Unity pinpoints: left and right stairs on every floor, every room, and the elevators on floors 3, 4 and 5. |
| Khoi Do (@Ben2104) | Unity AR: HUD improvements and building coverage, with Jan. Also release-build AR fix and the typed API client. |
| Jan Montemayor (@Kura-Yami) | Unity AR: UI improvements and building coverage, with Khoi. Also seed data and routing to the entrance. |

## 1. Committed User Stories and Verification Checklist

Story points come from the issue's Roadmap table. Full Gherkin is in each issue.

### A. Gem rewards (Pet & Rewards)

#### #57: Gem balance and claim API — Assigned: Jake N.
**Narrative:** As a student, I want to see my gem balance and claim gems for completed class tasks so that I am rewarded for staying organized.
**Module:** Pet & Rewards | **Priority:** High | **Story Points:** 5 | **Issue:** #57 (parent #34)
- AC 1: Given I am signed in, when I request my balance, then I receive my current gem balance.
- AC 2: Given I completed today's task, when I claim the gem, then my balance increases and the task shows as claimed.
- AC 3: Given I already claimed that task today, when I claim it again, then the request is rejected and my balance is unchanged.
- AC 4 (Security): Given I am not signed in, when I request or claim gems, then the request is rejected as unauthenticated.

#### #58: Use gems on my pet API (buy and equip) — Assigned: Jake N.
**Narrative:** As a student, I want to spend gems on my pet so that I can customize it with what I have earned.
**Module:** Pet & Rewards | **Priority:** High | **Story Points:** 5 | **Issue:** #58 (parent #35)
- AC 1: Given I have enough gems, when I buy and equip an item, then gems are deducted once and the pet shows the item.
- AC 2: Given I do not have enough gems, when I try to buy, then I am told how many more are needed and nothing is charged.
- AC 3: Given two identical buy requests arrive together, then I am charged once.

#### #59: Gem rewards and Achievements screen — Assigned: Will C.
**Narrative:** As a student, I want to see my gems, claim them for finished tasks, and see my achievements so that completing tasks feels rewarding.
**Module:** Pet & Rewards | **Priority:** High | **Story Points:** 5 | **Issue:** #59 (parent #34)
- AC 1 (Empty State): Given I have no classes, when I open Achievements, then I see an empty state pointing to Add Class.
- AC 2: Given I completed today's task, when I tap Claim, then my gem balance increases and the achievement shows as active.
- AC 3: Given no network, when I tap Claim, then I see a clear message and the app keeps working.

#### #60: Customize my pet with gems screen — Assigned: Will C.
**Narrative:** As a student, I want to pick an outfit and spend my gems on it so that my pet feels personal.
**Module:** Pet & Rewards | **Priority:** Medium | **Story Points:** 5 | **Issue:** #60 (parent #35)
- AC 1: Given I have enough gems, when I buy and equip an outfit, then my pet shows it and a toast confirms it was saved.
- AC 2: Given I do not have enough gems, when I select an outfit, then I see how many more gems I need and nothing is charged.

### B. VEC pinpoints (Indoor AR)

#### #61: VEC stair pinpoints on all floors (left and right stairs) — Assigned: Jomar H.
**Narrative:** As the NaviPet team, we want pinpoints on every VEC stair so that AR guidance can route between floors.
**Module:** Indoor AR | **Priority:** High | **Story Points:** 5 | **Issue:** #61
- AC 1: Given the merged VEC scene, when the left and right stairs are checked on every floor, then each landing has a named, connected waypoint.
- AC 2: Given the stair waypoints, when a path is requested between two floors by stairs, then a connected path is returned.

#### #62: VEC room pinpoints on floors 3, 4 and 5 — Assigned: Jomar H.
**Narrative:** As the NaviPet team, we want a pinpoint at every room door on floors 3, 4 and 5 so that AR guidance can lead students to any room.
**Module:** Indoor AR | **Priority:** High | **Story Points:** 8 | **Issue:** #62
- AC 1: Given the merged VEC scene, when floors 3, 4 and 5 are checked, then every room door has a named waypoint inside a walkable doorway.
- AC 2: Given the floor graphs, when a path is requested from the stairs to any mapped room, then a connected path is returned.

#### #63: VEC elevator pinpoints on floors 3, 4 and 5 — Assigned: Jomar H.
**Narrative:** As the NaviPet team, we want pinpoints at every elevator on floors 3, 4 and 5 so that guidance can route students who avoid stairs.
**Module:** Indoor AR | **Priority:** High | **Story Points:** 3 | **Issue:** #63
- AC 1: Given the merged VEC scene, when floors 3, 4 and 5 are checked, then each floor has a named elevator waypoint linked to its corridor.

#### #67: Finish VEC floors 1–2 walkable waypoint graph (carried over from #16) — Assigned: Jomar H.
**Narrative:** As a student walking inside VEC, I want floors 1–2 mapped with connected waypoints so that the app can compute an indoor path to a room.
**Module:** Indoor AR | **Priority:** High | **Story Points:** 3 | **Issue:** #67
- AC 1: Given the merged VEC scene, when a path is requested from the entrance node to a mapped room node, then a connected path is returned.

### C. Unity AR experience (Indoor AR)

#### #64: Improve the Unity AR HUD — Assigned: Khoi D.
**Narrative:** As a student following AR guidance, I want a clear HUD so that I always know where to go next and how far I am.
**Module:** Indoor AR | **Priority:** High | **Story Points:** 5 | **Issue:** #64
- AC 1: Given I am following an AR path, when I walk, then the HUD shows distance to the next waypoint, remaining distance and current floor.
- AC 2: Given I reach the destination, when arrival is detected, then the HUD shows an arrival message.
- Note: exact HUD elements are proposed and need confirming against the design before work starts.

#### #65: Improve the Unity AR screens — Assigned: Jan M.
**Narrative:** As a student using indoor AR, I want clear localizing, failure, floor-change and arrival screens so that I understand what the app is doing.
**Module:** Indoor AR | **Priority:** High | **Story Points:** 5 | **Issue:** #65
- AC 1: Given localization fails, then I can Retry, view the floor map, or return to the app.
- AC 2: Given I am guided to another floor, when I reach the elevator or stairs, then I see a clear floor-change screen.

#### #66: Indoor AR coverage for the sprint-3 target buildings — Assigned: Khoi D., Jan M.
**Narrative:** As a student, I want indoor AR to work in the campus buildings the team targets this sprint so that I can reach more rooms with AR.
**Module:** Indoor AR | **Priority:** High | **Story Points:** 8 (provisional) | **Issue:** #66
- AC 1: Given a target building with a merged scan and waypoints, when a path is requested from its entrance to a mapped room, then a connected path is returned.
- Open question: the target building list is not fixed yet; points are provisional until it is.

#### #73: Fix black screen when opening Unity AR in the release build (carried over from #56) — Assigned: Khoi D.
**Narrative:** As a student, I want the AR button to open the camera view in the release build so that indoor AR works outside debug builds.
**Module:** Indoor AR | **Priority:** High | **Story Points:** 3 | **Issue:** #73
- AC 1: Given a release build on a physical Android device, when I tap the AR button, then the camera permission prompt appears and the AR camera view opens.

### D. VEC 404 vertical slice (existing Sprint 3 stories)

| Issue | Story | Module | Priority | SP |
| --- | --- | --- | --- | --- |
| #17 | Launch indoor AR from the app and return safely | Indoor AR | High | 8 |
| #18 | Entrance and indoor-availability data for campus destinations | Campus Search | High | 3 |
| #19 | Localize in VEC and follow AR guidance to VEC 404 | Indoor AR | High | 8 |
| #20 | Find a room by name, abbreviation or number | Campus Search | High | 5 |
| #21 | Route to the building entrance and hand off to indoor AR | Outdoor Navigation | High | 5 |
| #31 | Guide the student between floors (elevator or stairs) | Indoor AR | High | 5 |
| #22 | End-to-end VEC 404 walkthrough testing (technical task) | QA | Medium | 3 |

Acceptance criteria for these are in their issues. #17, #19, #31 and #22 absorb the unfinished Sprint 2 stories #46, #47, #48 and #49.

### E. Sprint 2 carry-over

| Issue | Story | Module | Priority | SP | Owner |
| --- | --- | --- | --- | --- | --- |
| #68 | Avatar upload (from #10) | Profile | Medium | 3 | Jake N., Jan M., Jomar H., Khoi D. |
| #69 | Bottom-bar active tab and UX polish list (from #14) | UX | Low | 3 | Will C., Khoi D., Jomar H., Jan M., Jake N. |
| #70 | Accessibility preferences settings API, E-03 (from #40) | Accessibility | Medium | 3 | **Needs an owner** |
| #71 | Finish change account email, E-02 (from #50) | Profile | Medium | 3 | **Needs an owner** |
| #72 | Typed backend API client generated from OpenAPI (from #51) | Foundation | Medium | 3 | Khoi D. |

## 2. Capacity and Velocity

| | Story points |
| --- | --- |
| Planned: existing Sprint 3 stories (#17–#22, #31) | 37 |
| Planned: gem rewards (#57–#60) | 20 |
| Planned: VEC pinpoints (#61–#63, #67) | 19 |
| Planned: Unity AR experience (#64–#66, #73) | 21 |
| Planned: Sprint 2 carry-over (#68–#72) | 15 |
| **Total committed** | **112** |

Sprint 2 completed velocity: fill in from the Sprint 2 demo.

**Risk:** 112 points is large for five engineers. Confirm against Sprint 2 velocity and decide what moves out if it does not fit. Lowest-risk candidates to defer: #69 (low priority), #60, and part of #66.

## 3. Risks and Open Questions

- Target building list for #66 ("all buildings") is not fixed; points are provisional.
- #70 and #71 have no owner.
- HUD and UI scope for #64 and #65 are proposed; confirm against the design.
- Sprint dates and demo date are not set.

## 4. Sprint 2 Retrospective Summary

**What went well:** (fill in)

**Key technical challenge:** The Unity AR library failed to open from the release build (#73, carried over). (fill in the rest)

**Process adjustment for next sprint:** (fill in)
