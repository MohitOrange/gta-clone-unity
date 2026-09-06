# Release signing — Mini GTA (`com.minigta.city`)

**Read this before you publish, and back up what it points at.**

---

## ⚠️ The one thing that matters

**If `minigta-release.keystore` is lost, a published app can never be updated again.**

Not "hard to update". Cannot. Google Play identifies an app by the certificate it was signed
with; there is no recovery path, no support ticket, no appeal. The only remedy is to publish a
brand-new listing under a different package name and lose every install, rating and review.

The same is true of the password. A keystore you cannot open is a keystore you have lost.

---

## Where everything is

| What | Where |
|---|---|
| **Keystore file** | `C:\unity_games\minigta-keystore\minigta-release.keystore` |
| **Password** | `C:\unity_games\minigta-keystore\keystore-password.txt` |
| **Key alias** | `minigta-release` |
| Store password and key password | **the same value** — the single string in that file |

Both files are **outside the git repository** (the repo is `C:\unity_games\gta\gta`), so a
`git add -A` cannot sweep them in. `.gitignore` also blocks `*.keystore`, `*.jks`, `*.p12`,
`*.pfx` and `keystore-password.txt` as a second line of defence.

**The password is deliberately not written in this file**, because this file *is* committed.

## What you need to back up

Back up **the whole folder** `C:\unity_games\minigta-keystore\` — it contains both the keystore
and the password, so one copy captures everything needed.

Suggested, in rough order of usefulness:

1. A password manager entry containing the password **and** an attached copy of the `.keystore`
   file. This is the best option: it survives losing the machine entirely.
2. An encrypted archive on separate physical media, kept somewhere else.
3. A private (never public) backup repository or cloud folder.

**A note on the current arrangement.** The password sits in a plain text file next to the
keystore. That is deliberate — it means a single folder copy is a complete backup, and the
realistic risk here is *losing* the key, not somebody stealing it off this machine. If that
trade stops being right for you, move the password into a password manager and delete
`keystore-password.txt`; nothing in the project reads it.

## Certificate details

```
Alias        : minigta-release
Owner        : CN=Mini GTA, OU=Development, O=MiniGTA, L=Unknown, ST=Unknown, C=IN
Algorithm    : 2048-bit RSA, SHA256withRSA
Valid from   : 05 Sep 2026
Valid until  : 27 Aug 2061   (~35 years; Google Play requires validity past 2033)
SHA-256      : D1:CF:9E:38:23:1B:27:5B:E4:E4:A6:E3:F1:EB:3B:9B:
               CD:FD:87:46:8E:86:96:2D:1E:15:30:2F:42:75:F1:B2
```

Verify any APK against that fingerprint with:

```bash
apksigner verify --print-certs Builds/Android/MiniGTA-<version>.apk
```

A release build must show `CN=Mini GTA`. If it shows `CN=Android Debug`, it is **not**
publishable — that is the Android debug key, which every developer machine shares.

## How the project uses it

`ProjectSettings.asset` (committed) records only the **path** and the **alias**:

```
AndroidKeystoreName: C:/unity_games/minigta-keystore/minigta-release.keystore
AndroidKeyaliasName: minigta-release
androidUseCustomKeystore: 1
```

**Unity does not persist the passwords anywhere in the project** — verified, not assumed. They
live in the Editor session only. So:

- **After an Editor restart you must re-enter the password** in
  *Project Settings → Player → Publishing Settings* before a release build will sign.
- `Mini GTA ▸ Build ▸ Android APK (release)` **fails loudly** if the keystore is missing or the
  password is absent. It will never quietly fall back to the debug key — a release that
  silently ships debug-signed is worse than one that does not build.
- `Mini GTA ▸ Build ▸ Android APK (development)` deliberately uses the debug key and needs no
  password. Use it for device testing and benchmarks.

## History — why this file exists

The project was configured to sign with `D:/JoySmashProjects/keystore/bundle.keystore`, a path
belonging to an entirely different project, on a drive where it did not exist. That was BUG-026,
and it sat open as a release blocker.

The investigation found it was the harmless version of the problem: **every APK this project had
ever produced was debug-signed** (all seven, `CN=Android Debug`, matching this machine's
`~/.android/debug.keystore`), the passwords were never set, and no keystore was ever tracked in
git. Nothing had been signed for release, so nothing could have been published from here — the
setting was leftover from an asset-pack import, the same clobber that took the scene list
(BUG-025) and the app icon (BUG-028).

So this key is new, and nothing depends on the old one. That is the good case, and it only stays
good if this key is backed up **before** the first release.
