# Mini GTA — Phase 6: Ads & Monetisation

Builds on [PHASE1](PHASE1.md)–[PHASE5](PHASE5.md).

**Runs on a mock ad network by default.** No AdMob account is needed to playtest the whole
flow — the mock shows a real full-screen overlay with a real countdown and a real skip button.

---

## What Phase 6 delivers

| Brief item | Status | Where |
|---|---|---|
| Modular `AdService` interface, provider swappable | Done | `IAdService`, `AdService` |
| Mock implementation with fake countdown + reward | Done | `MockAdService`, `AdOverlayUI` |
| Rewarded: double cash | Done | `AdRewards.OnMissionEnded` |
| Rewarded: revive after death | Done | `AdRewards.OnGameStateChanged` → `GameStateManager.ReviveInPlace` |
| Rewarded: reduce wanted level | Done | `AdRewards.OfferClearWanted`, `ClearWantedButton` |
| Interstitials at natural breakpoints | Done | Mission end, game over, app launch |
| Frequency cap | Done | `AdService.TryShowInterstitial` — 1 per 180 s + grace |
| Banner, menus only | Done | `AdOverlayUI.SetBannerVisible`, shown by `StorePanel` |
| Real provider wiring | Stub, documented | `GoogleMobileAdsService` |
| Remove Ads toggle with stubbed IAP hook | Done | `IapService`, `StorePanel` |

---

## Swapping the provider

One field: `AdService.Provider` — `Mock`, `GoogleMobileAds`, or `None`.

Game code never names a provider. It calls `AdService.Instance.ShowRewarded(...)` or
`TryShowInterstitial(...)` and gets a callback. That is the whole point of the interface: the
mission system does not know advertising exists, and the ad code does not know missions exist —
`AdRewards` is the only file that has heard of both.

**To go live with AdMob:**
1. Install the Google Mobile Ads Unity plugin.
2. Add `MINIGTA_ADMOB` to Scripting Define Symbols.
3. Replace the test unit ids in `GoogleMobileAdsService` with your real ones.
4. Set `AdService.Provider` to `GoogleMobileAds`.
5. Enter your App ID under Assets > Google Mobile Ads > Settings.

Until step 2, that file compiles to a stub that reports "unavailable", so **the project always
builds whether or not the SDK is present**.

> The brief asked for `capacitor-community/admob`. That is a web/Capacitor plugin and this is a
> Unity project, so the equivalent is the official Google Mobile Ads **Unity** plugin — same
> AdMob account, same unit ids, same consent requirements, different binding.

---

## Frequency rules

Capping lives in `AdService`, not in the provider, so it applies identically to any network:

- **1 interstitial per 180 seconds.**
- **First opportunity is skipped** (`GraceInterstitials = 1`), so a new player is not hit with
  an ad before they have played anything.
- **Ads never stack.** A request while one is showing is refused, not queued.
- **Rewarded ads ignore both the cap and the Remove Ads purchase** — the player asked for them
  and they pay out.
- **Declining a rewarded offer is what triggers the interstitial**, not accepting one. The
  player is never shown two ads back to back.

`AdOverlayUI` sets `Time.timeScale = 0` while an ad is up, because a real full-screen ad
suspends the game. Testing against an overlay that let the world keep running would hide every
bug the real thing causes — police still shooting during an ad, mission timers still ticking.

---

## Remove Ads is a stub, on purpose

`IapService` is **not** a real store integration and does not pretend to be. Real billing needs
a Play Console entry, a signed upload, a configured product id and server-side receipt
validation — none of which exist in this project.

- **In the editor** it simulates a purchase after a delay so the entitlement flow is testable.
- **In a real build** it refuses and logs a warning rather than granting anything.

Shipping something that *looks* like it charges money would be worse than an obvious stub. The
shape is what transfers: `Purchase` and `Restore` are the two calls any store needs, the UI is
already wired to them, and swapping in Unity IAP means replacing the body of that one class.

`Restore` currently reads the save file, which is why a reinstall would lose the entitlement —
a real implementation must query the store. That is called out in the code.

---

## Verified in Play mode

- Interstitial cap: request 1 suppressed by grace, request 2 shown, requests 3–4 suppressed;
  `Time.timeScale` went to 0 and back to 1
- Rewarded ad watched to the end: **$8,950 → $9,450**
- Rewarded ad requested while another was showing: refused, **paid nothing**
- Remove Ads purchased → `IsInterstitialReady` false, `TryShowInterstitial` returned false,
  **rewarded still paid ($9,450 → $9,550)**
- Entitlement persisted to the save as `"AdsRemoved": true` (save format v3)

---

## Known rough edges

- The mock's "ad" is a coloured panel with a countdown, not video playback. It tests the flow,
  not rendering performance during an ad.
- No consent/UMP flow (GDPR/ATT). **Required before shipping to EU or iOS users** — it belongs
  in `GoogleMobileAdsService.Initialise` before the first request.
- No ad analytics or revenue reporting hooks.
- `AdService.GraceInterstitials` counts opportunities, not sessions; it resets on every launch.
- Banner only appears in the store panel. There is no other menu yet — Phase 7's main menu and
  pause menu are the natural homes for it.
- Still on Windows Standalone; the Android switch remains pending from Phase 1.

---

## Editor testing note

The Unity player loop freezes when the Game view is not being drawn (`runInBackground` is
false), which stops anything time-based — including ad countdowns. If ads appear stuck while
testing headlessly, set `Application.runInBackground = true`. This is an editor artifact, not
game behaviour; leave it false for a mobile build.
