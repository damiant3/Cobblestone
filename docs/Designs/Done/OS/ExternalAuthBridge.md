# External Authentication Bridge

**Date**: 2026-06-22
**Status**: Active
**Depends on**: Identity (done), Trust lattice (done), HttpClient (done),
TLS 1.3 (done), Ed25519 (done), HMAC/HKDF (done), SessionStore (done),
Accounts (done)
**Unblocks**: Browser SSO login, email fetch via OAuth, calendar sync,
cloud file access during transition from legacy systems

---

## Purpose

Codex identity is a 32-byte Ed25519 public key. That's the end state.
But the world runs on Google and Microsoft accounts, and during the
transition from barbarian code to Codex, users need to:

1. Sign in to the Codex web UI with their Google/Microsoft account
2. Fetch email from Gmail/Outlook via OAuth tokens
3. Access cloud APIs (Drive, Calendar, Graph) using delegated credentials

This design describes an **I/O boundary adapter** -- external auth
lives at the edge, maps to native Codex identity, and never penetrates
the core trust model. The Identity design doc's "What NOT to build"
section remains valid: Codex does not use OAuth for internal identity.
External providers are treated as untrusted agents that vouch for
users through the trust lattice.

---

## Architecture

```
  External World              Boundary                  Codex Core
  ──────────────              ────────                  ──────────
  Google/MS auth  ──►  OAuthClient (token exchange)
  servers               JwtDecoder (claim extraction)
                              │
                        OAuthProvider (endpoint config)
                              │
                        TrustBridge ──► TrustLattice
                        (vouch from     (provider node,
                         provider to     score = 6000)
                         user identity)
                              │
                        Accounts ──► SessionStore
                        (link external   (Codex session
                         ID to account)   token)
                              │
                        HttpClient + bearer token
                              │
                        ImapClient ──► email fetch
                        GraphClient ──► calendar/files
```

### Key Principle

External providers are **TrustNode** entries in the lattice with a
configurable trust score. When a user authenticates via OAuth, the
provider "vouches" for the user's identity. The PolicyEngine gates
access based on trust thresholds:

| Identity Source | Trust Score | Access Level |
|----------------|-------------|--------------|
| Native Ed25519 (local key) | 10000 | Full |
| Native Ed25519 (remote peer) | Lattice-computed | Per-policy |
| Google OAuth | 6000 (default) | Standard user |
| Microsoft OAuth | 6000 (default) | Standard user |
| Unknown external | 0 | None |

Scores are configurable per-provider via PolicyFact entries.

---

## Modules

### 1. JWT Decoder (`codex/foreword/encode/Jwt.codex`)

Decode and validate JSON Web Tokens (RFC 7519). Does NOT verify
cryptographic signatures against provider JWKS (that requires
fetching and caching RSA/EC public keys from Google/Microsoft --
deferred to V2). V1 validates structure, expiry, issuer, and audience.

**Types:**
- `JwtHeader` -- alg, typ, kid
- `JwtPayload` -- iss, sub, aud, exp, iat, nonce, email, name
- `JwtToken` -- header, payload, signature-bytes, raw-text

**Functions:**
- `jwt-decode : Text -> JwtToken` -- split on `.`, base64-decode
  each segment, parse JSON header and payload
- `jwt-validate-claims : JwtToken, Text, Text, Integer -> Boolean`
  -- check issuer, audience, and expiry against current time
- `jwt-get-email / jwt-get-sub / jwt-get-name` -- claim accessors

### 2. OAuth Client (`codex/os/net/OAuthClient.codex`)

OAuth 2.0 Authorization Code flow with PKCE (RFC 7636). Handles
the three-legged dance: authorization URL, code exchange, token
refresh.

**Types:**
- `OAuthConfig` -- client-id, redirect-uri, scopes, auth-url,
  token-url, provider-name
- `OAuthState` -- state-nonce, code-verifier (PKCE), timestamp
- `OAuthTokens` -- access-token, refresh-token, id-token, expires-at
- `OAuthError` -- error code + description

**Functions:**
- `oauth-authorize-url : OAuthConfig, OAuthState -> Text` -- build
  authorization URL with state, PKCE challenge, scopes
- `oauth-exchange-code : OAuthConfig, Text, OAuthState -> OAuthTokens`
  -- POST to token endpoint with authorization code + PKCE verifier
- `oauth-refresh : OAuthConfig, Text -> OAuthTokens` -- POST with
  refresh token to get new access token
- `oauth-pkce-challenge : Text -> Text` -- SHA-256 + base64url of
  code verifier

### 3. OAuth Providers (`codex/os/net/OAuthProvider.codex`)

Pre-configured endpoint URLs, scopes, and claim mappings for
Google and Microsoft.

**Google:**
- Auth: `https://accounts.google.com/o/oauth2/v2/auth`
- Token: `https://oauth2.googleapis.com/token`
- Scopes: `openid email profile` + `https://mail.google.com/`
  (for IMAP) + `https://www.googleapis.com/auth/calendar.readonly`
- Issuer: `https://accounts.google.com`

**Microsoft (Entra ID / O365):**
- Auth: `https://login.microsoftonline.com/common/oauth2/v2.0/authorize`
- Token: `https://login.microsoftonline.com/common/oauth2/v2.0/token`
- Scopes: `openid email profile offline_access` +
  `https://outlook.office365.com/IMAP.AccessAsUser.All` +
  `https://graph.microsoft.com/Calendars.Read`
- Issuer: `https://login.microsoftonline.com/{tenant}/v2.0`

**Functions:**
- `google-oauth-config : Text -> OAuthConfig` -- takes client-id
- `microsoft-oauth-config : Text, Text -> OAuthConfig` -- takes
  client-id, tenant-id

### 4. Trust Bridge (`codex/os/trust/ExternalAuthBridge.codex`)

Maps external OAuth identity claims to Codex AgentIdentity via
the trust lattice.

**Flow:**
1. User authenticates via OAuth (gets id_token)
2. JWT decoded, email/sub extracted
3. Look up or create a synthetic AgentIdentity keyed by
   `provider:sub` (e.g., `google:1234567890`)
4. The provider's TrustNode vouches for this identity at
   the configured trust score
5. A Codex session token is issued via SessionStore
6. The OAuth access/refresh tokens are stored in the session
   for subsequent API calls (email fetch, etc.)

**Types:**
- `ExternalIdentity` -- provider, subject, email, display-name,
  access-token, refresh-token, token-expiry
- `ProviderTrust` -- provider-name, trust-score, enabled

**Functions:**
- `bridge-authenticate : OAuthTokens, OAuthConfig -> ExternalIdentity`
- `bridge-link-account : ExternalIdentity, SessionStore -> SessionStore`
- `bridge-get-token : SessionStore, Text -> Text` -- retrieve
  stored access token for a provider
- `bridge-refresh-if-needed : SessionStore, OAuthConfig -> SessionStore`

### 5. IMAP Client (`codex/os/net/ImapClient.codex`)

IMAP4rev1 (RFC 3501) client for email fetch. Uses XOAUTH2 (RFC
7628) for authentication with OAuth access tokens instead of
passwords.

**Types:**
- `ImapSession` -- connection state, tag counter, selected mailbox
- `ImapMailbox` -- name, message-count, recent, unseen
- `ImapMessage` -- uid, flags, date, from, to, subject, body-preview

**Functions:**
- `imap-connect : Text, Integer -> ImapSession` -- host, port (993)
- `imap-auth-xoauth2 : ImapSession, Text, Text -> ImapSession`
  -- email, access-token
- `imap-select : ImapSession, Text -> ImapMailbox` -- select INBOX
- `imap-fetch-headers : ImapSession, Integer, Integer -> List ImapMessage`
  -- fetch range of message headers
- `imap-fetch-body : ImapSession, Integer -> Text` -- fetch one
  message body
- `imap-logout : ImapSession -> ImapSession`

---

## Security Considerations

1. **OAuth tokens are session-scoped.** Access tokens live in the
   SessionStore with TTL matching the token's `expires_in`. Refresh
   tokens are stored encrypted (AES-256 with session-derived key).
   On session expiry, all tokens are wiped.

2. **No ambient authority.** OAuth tokens are not globally accessible.
   A process must hold the `ExternalAuth` capability to read tokens
   from the session store.

3. **Trust score is a ceiling, not a floor.** An OAuth-authenticated
   user starts at the provider's trust score (6000). The trust lattice
   can raise it (via local vouches) or lower it (via policy or decay).
   The score never exceeds 10000 (reserved for native Ed25519 identity).

4. **Provider compromise.** If Google or Microsoft's auth servers are
   compromised, the damage is bounded: compromised tokens grant access
   at score 6000, which policies can restrict to non-critical operations.
   Native identity operations (signing, capability grants, trust lattice
   modifications) require score > 8000 and are unaffected.

5. **Client secrets.** OAuth client-id is public. Client-secret (if
   used) is stored in the system's DiskFacts encrypted under the
   identity key. PKCE eliminates the need for client secrets in
   public-client flows (browsers, native apps).

---

## Commit Plan

1. `Jwt.codex` -- JWT decode + claim validation
2. `OAuthClient.codex` -- authorization code flow + PKCE + token exchange
3. `OAuthProvider.codex` -- Google + Microsoft configs
4. `ExternalAuthBridge.codex` -- trust bridge, identity mapping
5. `ImapClient.codex` -- IMAP4 + XOAUTH2

Each module is self-contained and testable independently.

---

## What This Is NOT

This is not a replacement for Codex identity. This is a bridge.

The long-term migration path:
1. User signs in with Google/Microsoft (this design)
2. User generates a native Codex Ed25519 identity
3. User links the external identity to the native identity
4. Over time, the user's trust score shifts from provider-derived
   (6000) to lattice-derived (growing toward 10000)
5. Eventually, the external auth is no longer needed -- the user's
   native identity is fully established in the trust lattice

The bridge is scaffolding. The building is Ed25519.
