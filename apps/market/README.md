# Codex.Market

A self-hosted e-commerce suite providing a product catalog, shopping cart, checkout, admin panel, and multi-vendor marketplace as a single bare-metal HTTP application.

## Modules

- **MarketTypes** -- Domain model: Money, Product, Variant, Cart, Order, User, Session, Coupon, Review, Wishlist, Auction, Merchant, Supplier, Creator, Affiliate, Payout, Subscription, SEO, Inventory
- **MarketDb** -- Relational schema and CRUD on Codex.Data
- **MarketAuth** -- Registration, HMAC-SHA256 password hashing, session tokens
- **MarketCatalog** -- Product creation, option/variant management, slug generation, publish/archive lifecycle
- **MarketCart** -- Add/remove/update quantities, cart subtotal, full checkout flow
- **MarketTax** -- Basis-point tax engine; US state and EU VAT rule presets
- **MarketPayment** -- Authorize/capture/refund abstraction with test-mode gateway
- **MarketShipping** -- Zone-based shipping quotes; flat-rate, weight-based, free-over-threshold methods
- **MarketCoupon** -- Percent-off, fixed-off, free-shipping, buy-X-get-Y with expiry/usage-limit validation
- **MarketReview** -- Star ratings, verified-purchase badges, rating breakdown
- **MarketAuction** -- English, Dutch, sealed-bid, Vickrey auction types; proxy bidding; snipe protection
- **MarketMerchant** -- Multi-vendor onboarding, commission splits, payouts, drop-ship, content creator profiles, affiliate tracking
- **MarketFields** -- Custom field templates per product category
- **MarketHtml** -- Server-side HTML generation: product grid/detail, cart, admin, auction cards, merchant storefront
- **MarketMaui** -- XAML/C# generation for .NET MAUI cross-platform app
- **MarketWeb** -- HTTP server: route dispatch, page rendering, API endpoints, demo store seed
- **tests/TestMarket** -- Unit tests for money, tax, cart, coupon, checkout

## Completeness

65% -- Domain model, business-logic engines (tax, shipping, coupons, auctions, merchant/affiliate/creator), and HTML rendering are fully implemented. Key gaps: auth HTTP wiring is stubbed (login/register log but don't parse forms or issue cookies), DB persistence returns empty arrays unconditionally, payment gateway is a stub, only three JSON endpoints exist, no `opening` entry point.

## Codex Conformance

Partial -- All files are valid Codex. Backend persistence and payment I/O are intended to go through plugs. Fully conformant in structure and intent; partially conformant in execution because the DB read/write path and auth wiring are not yet connected.
