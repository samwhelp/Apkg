# GPG verification fails with ASCII-armored keyrings

## Symptom

CI: `firefox-anduinos` build fails.  `firmware-sof-anduinos` succeeds.

```
gpgv: invalid packet (ctb=2d)
gpgv: Can't check signature: No public key
```

## Root cause

`AptGpgVerifier.VerifyFileAsync()` passes the keyring file directly to
`gpgv --keyring <path>`.  `gpgv` does **not** support ASCII-armored
keyrings — it requires binary (`.gpg`) format.

`firmware-sof-anduinos` works because it has no `UpstreamSignedBy` →
`allowInsecure=true` → skips verification entirely.

## Reproduction

```bash
# Fails: ASCII keyring
gpgv --keyring firefox-anduinos/assets/mozilla-keyring.asc /tmp/inrelease
# → invalid packet (ctb=2d), NO_PUBKEY

# Works: binary keyring
gpg --dearmor < firefox-anduinos/assets/mozilla-keyring.asc > /tmp/keyring.gpg
gpgv --keyring /tmp/keyring.gpg /tmp/inrelease
# → GOODSIG
```

## Fix location

`src/Aiursoft.AptClient/AptGpgVerifier.cs` → `VerifyFileAsync()`

Before passing `keyringPath` to gpgv, detect ASCII format and convert:

```
if keyring starts with "-----BEGIN PGP"
  → gpg --dearmor → temp binary file → use that
else
  → use as-is
```

## Acceptance criteria

- [ ] `firefox-anduinos` builds with `apkg publish` against live Mozilla APT repo
- [ ] Existing `UpstreamSignedBy` packages (any with binary `.gpg` keyring) still work
- [ ] `gpgv --keyring <path>` never receives an ASCII-armored file

## Test requirements

- [ ] Unit test: `VerifyFileAsync` with `.asc` (ASCII) keyring → converts and succeeds
- [ ] Unit test: `VerifyFileAsync` with `.gpg` (binary) keyring → works unchanged
- [ ] Unit test: `VerifyFileAsync` with missing keyring → original error behavior preserved
